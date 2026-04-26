using OpenTK.Mathematics;
using OverhaulLib.Utils;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CollidersLib.Projectiles;

public abstract class ProjectileCollisionsSynchroniser
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ProjectileCollisionsPacket
    {
        public long ProjectileEntityId { get; set; }
        public Dictionary<long, ProjectileEntityCollisionPacketData[]> EntityCollisions { get; set; } = [];
        public List<ProjectileTerrainCollisionPacketData> TerrainCollisions { get; set; } = [];
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ProjectileUpdateCollisionsPacket
    {
        public long ProjectileEntityId { get; set; }
        public long TimeStamp { get; set; }
        public bool EnableCollisions { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double PositionZ { get; set; }
        public float Radius { get; set; }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ProjectileResetCollisionsPacket
    {
        public long ProjectileEntityId { get; set; }
        public long TimeStamp { get; set; }
        public double PositionX { get; set; }
        public double PositionY { get; set; }
        public double PositionZ { get; set; }
        public float Radius { get; set; }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public struct ProjectileEntityCollisionPacketData
    {
        public string ColliderElementName { get; set; }
        public double IntersectionPointX { get; set; }
        public double IntersectionPointY { get; set; }
        public double IntersectionPointZ { get; set; }
        public double PositionInTime { get; set; }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public struct ProjectileTerrainCollisionPacketData
    {
        public int BlockId { get; set; }
        public int FacingIndex { get; set; }
        public double NormalX { get; set; }
        public double NormalY { get; set; }
        public double NormalZ { get; set; }
        public double IntersectionPointX { get; set; }
        public double IntersectionPointY { get; set; }
        public double IntersectionPointZ { get; set; }
        public int BlockPositionX { get; set; }
        public int BlockPositionY { get; set; }
        public int BlockPositionZ { get; set; }
        public double PositionInTime { get; set; }
    }
}


public sealed class ProjectileCollisionsSynchroniserServer : ProjectileCollisionsSynchroniser
{
    public ProjectileCollisionsSynchroniserServer(ICoreServerAPI api)
    {
        _api = api;
        _collisionTestersManager = new(api, HandleUpdate);
        _serverChannel = api.Network.RegisterChannel("ProjectileCollisionsSynchroniserClient")
            .RegisterMessageType<ProjectileCollisionsPacket>()
            .RegisterMessageType<ProjectileUpdateCollisionsPacket>()
            .RegisterMessageType<ProjectileResetCollisionsPacket>()
            .SetMessageHandler<ProjectileCollisionsPacket>(HandlePacket);
    }

    public void ToggleProjectileCollisions(IServerPlayer projectileOwner, Entity projectile, bool enable, float radius)
    {
        long projectileId = projectile.EntityId;
        
        if (enable)
        {
            _projectileOwners[projectileId] = projectileOwner;
            _collisionTestersManager.StartUpdates(projectile, radius);
        }
        else
        {
            TurnOffProjectileCollisions(projectile);
        }
    }

    public void TurnOffProjectileCollisions(Entity projectile)
    {
        long projectileId = projectile.EntityId;
        TurnOffProjectileCollisions(projectileId);
    }

    public void TurnOffProjectileCollisions(long projectileId)
    {
        if (!_projectileOwners.TryGetValue(projectileId, out IServerPlayer? owner))
        {
            return;
        }

        _collisionTestersManager.StopUpdates(projectileId);
        ProjectileUpdateCollisionsPacket packet = new()
        {
            ProjectileEntityId = projectileId,
            EnableCollisions = false
        };
        _serverChannel.SendPacket(packet, owner);
        _projectileOwners.Remove(projectileId);
    }

    public void ResetPosition(long projectileId, Vector3d position, float radius)
    {
        long timeStamp = _api.World.ElapsedMilliseconds;
        ProjectileResetCollisionsPacket packet = new()
        {
            ProjectileEntityId = projectileId,
            TimeStamp = timeStamp,
            PositionX = position.X,
            PositionY = position.Y,
            PositionZ = position.Z,
            Radius = radius
        };
        _serverChannel.SendPacket(packet, _projectileOwners[projectileId]);
    }


    private readonly IServerNetworkChannel _serverChannel;
    private readonly ICoreServerAPI _api;
    private readonly ProjectileCollisionTestersManagerServer _collisionTestersManager;
    private readonly Dictionary<long, IServerPlayer> _projectileOwners = [];


    private void HandleUpdate(long projectileId, long timeStamp, Vector3d position, float radius)
    {
        ProjectileUpdateCollisionsPacket packet = new()
        {
            ProjectileEntityId = projectileId,
            TimeStamp = timeStamp,
            EnableCollisions = true,
            PositionX = position.X,
            PositionY = position.Y,
            PositionZ = position.Z,
            Radius = radius
        };
        _serverChannel.SendPacket(packet, _projectileOwners[projectileId]);
    }

    private void HandlePacket(IPlayer player, ProjectileCollisionsPacket packet)
    {
        Entity? projectile = _api.World.GetEntityById(packet.ProjectileEntityId);
        if (projectile == null)
        {
            Log.Dev(_api, this, $"Was not able to find entity by supplied entity id '{packet.ProjectileEntityId}' when receiving collisions data");
            TurnOffProjectileCollisions(packet.ProjectileEntityId);
            return;
        }

        ProjectileColliderServerBehavior? colliderBehavior = projectile.GetBehavior<ProjectileColliderServerBehavior>();
        if (colliderBehavior == null)
        {
            Log.Warn(_api, this, $"Received collisions data for projectile, but projectile '{projectile.Code}' does not have 'ProjectileColliderServerBehavior'");
            return;
        }

        Dictionary<Entity, EntityWithSphereIntersectionData[]> entityCollisions = [];
        foreach ((long targetEntityId, ProjectileEntityCollisionPacketData[] data) in packet.EntityCollisions)
        {
            Entity? target = _api.World.GetEntityById(targetEntityId);
            if (target == null)
            {
                Log.Warn(_api, this, $"Was not able to find target entity by supplied entity id '{targetEntityId}' when receiving collisions data");
                continue;
            }

            CollidersEntityBehavior? collidersBehavior = target.GetBehavior<CollidersEntityBehavior>();

            entityCollisions.Add(target, data.Select(collision => GenerateEntityCollisionData(collision, collidersBehavior)).ToArray());
        }

        List<TerrainWithShpereIntersectionData> terrainCollisions = packet.TerrainCollisions.Select(collision => GenerateTerrainCollisionData(_api, collision)).ToList();

        colliderBehavior.OnCollisionPacket(entityCollisions, terrainCollisions);
    }

    private static EntityWithSphereIntersectionData GenerateEntityCollisionData(ProjectileEntityCollisionPacketData collision, CollidersEntityBehavior? targetColliders)
    {
        ShapeElementCollider? collider = null;
        if (targetColliders != null)
        {
            collider = targetColliders.Colliders.Find(element => element.ShapeElementName == collision.ColliderElementName);
        }

        return new(collider, new(collision.IntersectionPointX, collision.IntersectionPointY, collision.IntersectionPointZ), collision.PositionInTime);
    }

    private static TerrainWithShpereIntersectionData GenerateTerrainCollisionData(ICoreAPI api, ProjectileTerrainCollisionPacketData collision)
    {
        Block? block = api.World.GetBlock(collision.BlockId);
        if (block == null)
        {
            string errorMessage = $"Enable to find block with id '{collision.BlockId}' when handling projectile collision packet";
            Log.Error(api, typeof(ProjectileCollisionsSynchroniserServer), errorMessage);
            throw new InvalidDataException(errorMessage);
        }

        BlockFacing facing = collision.FacingIndex switch
        {
            BlockFacing.indexNORTH => BlockFacing.NORTH,
            BlockFacing.indexEAST => BlockFacing.EAST,
            BlockFacing.indexSOUTH => BlockFacing.SOUTH,
            BlockFacing.indexWEST => BlockFacing.WEST,
            BlockFacing.indexUP => BlockFacing.UP,
            BlockFacing.indexDOWN => BlockFacing.DOWN,
            _ => throw new InvalidDataException($"Enable to find facing with index {collision.FacingIndex}")
        };

        return new(
            block,
            facing,
            new(collision.NormalX, collision.NormalY, collision.NormalZ),
            new(collision.IntersectionPointX, collision.IntersectionPointY, collision.IntersectionPointZ),
            new(collision.BlockPositionX, collision.BlockPositionY, collision.BlockPositionZ),
            collision.PositionInTime);
    }
}


public sealed class ProjectileCollisionsSynchroniserClient : ProjectileCollisionsSynchroniser
{
    public ProjectileCollisionsSynchroniserClient(ICoreClientAPI api)
    {
        _collisionTestersManager = new(api);
        _clientChannel = api.Network.RegisterChannel("ProjectileCollisionsSynchroniserClient")
            .RegisterMessageType<ProjectileCollisionsPacket>()
            .RegisterMessageType<ProjectileUpdateCollisionsPacket>()
            .RegisterMessageType<ProjectileResetCollisionsPacket>()
            .SetMessageHandler<ProjectileUpdateCollisionsPacket>(HandlePacket)
            .SetMessageHandler<ProjectileResetCollisionsPacket>(HandlePacket);
    }

    public void SendCollisions(long projectileEntityId, Dictionary<Entity, EntityWithSphereIntersectionData[]> entityCollisions, List<TerrainWithShpereIntersectionData> terrainCollisions)
    {
        ProjectileCollisionsPacket packet = new()
        {
            ProjectileEntityId = projectileEntityId,
            EntityCollisions = entityCollisions.ToDictionary(entry => entry.Key.EntityId, entry => entry.Value.Select(GenerateEntityCollisionData).ToArray()),
            TerrainCollisions = terrainCollisions.Select(GenerateTerrainCollisionData).ToList()
        };

        _clientChannel.SendPacket(packet);
    }



    private readonly IClientNetworkChannel _clientChannel;
    private readonly ProjectileCollisionTestersManagerClient _collisionTestersManager;


    private void HandlePacket(ProjectileUpdateCollisionsPacket packet)
    {
        if (packet.EnableCollisions)
        {
            Vector3d position = new(packet.PositionX, packet.PositionY, packet.PositionZ);
            _collisionTestersManager.QueueUpdate(packet.ProjectileEntityId, packet.TimeStamp, position, packet.Radius);
        }
        else
        {
            _collisionTestersManager.StopTester(packet.ProjectileEntityId);
        }
    }

    private void HandlePacket(ProjectileResetCollisionsPacket packet)
    {
        Vector3d position = new(packet.PositionX, packet.PositionY, packet.PositionZ);
        _collisionTestersManager.QueueUpdate(packet.ProjectileEntityId, packet.TimeStamp, position, packet.Radius, reset: true);
    }

    private static ProjectileEntityCollisionPacketData GenerateEntityCollisionData(EntityWithSphereIntersectionData collision)
    {
        return new()
        {
            ColliderElementName = collision.EntityCollider?.ShapeElementName ?? "",
            IntersectionPointX = collision.IntersectionPoint.X,
            IntersectionPointY = collision.IntersectionPoint.Y,
            IntersectionPointZ = collision.IntersectionPoint.Z,
            PositionInTime = collision.PositionInTime
        };
    }

    private static ProjectileTerrainCollisionPacketData GenerateTerrainCollisionData(TerrainWithShpereIntersectionData collision)
    {
        return new()
        {
            BlockId = collision.Block.Id,
            FacingIndex = collision.Facing.Index,
            NormalX = collision.Normal.X,
            NormalY = collision.Normal.Y,
            NormalZ = collision.Normal.Z,
            IntersectionPointX = collision.IntersectionPoint.X,
            IntersectionPointY = collision.IntersectionPoint.Y,
            IntersectionPointZ = collision.IntersectionPoint.Z,
            BlockPositionX = collision.BlockPosition.X,
            BlockPositionY = collision.BlockPosition.Y,
            BlockPositionZ = collision.BlockPosition.Z,
            PositionInTime = collision.PositionInTime
        };
    }
}
