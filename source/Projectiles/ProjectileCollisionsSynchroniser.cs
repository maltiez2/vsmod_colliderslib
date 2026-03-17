using CollidersLib.Utils;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CollidersLib.Projectiles;

public class ProjectileCollisionsSynchroniser
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ProjectileCollisionsPacket
    {
        public long ProjectileEntityId { get; set; }
        public Dictionary<long, ProjectileEntityCollisionPacketData[]> EntityCollisions { get; set; } = [];
        public List<ProjectileTerrainCollisionPacketData> TerrainCollisions { get; set; } = [];
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ProjectileEnableCollisionsPacket
    {
        public long ProjectileEntityId { get; set; }
        public bool EnableCollisions { get; set; }
    }

    public struct ProjectileEntityCollisionPacketData
    {
        public string ColliderElementName { get; set; }
        public double IntersectionPointX { get; set; }
        public double IntersectionPointY { get; set; }
        public double IntersectionPointZ { get; set; }
        public double PositionInTime { get; set; }
    }

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
        _serverChannel = api.Network.RegisterChannel("ProjectileCollisionsSynchroniserClient")
            .RegisterMessageType<ProjectileCollisionsPacket>()
            .RegisterMessageType<ProjectileEnableCollisionsPacket>()
            .SetMessageHandler<ProjectileCollisionsPacket>(HandlePacket);
    }

    public void ToggleProjectileCollisions(IServerPlayer projectileOwner, long projectileEntityId, bool enable)
    {
        _serverChannel.SendPacket(new ProjectileEnableCollisionsPacket() { EnableCollisions = enable, ProjectileEntityId = projectileEntityId }, projectileOwner);
    }



    private readonly IServerNetworkChannel _serverChannel;
    private readonly ICoreServerAPI _api;

    private void HandlePacket(IPlayer player, ProjectileCollisionsPacket packet)
    {
        Entity? projectile = _api.World.GetEntityById(packet.ProjectileEntityId);
        if (projectile == null)
        {
            LoggerUtil.Warn(_api, this, $"Was not able to find entity by supplied entity id '{packet.ProjectileEntityId}' when receiving collisions data");
            return;
        }

        ProjectileColliderServerBehavior? colliderBehavior = projectile.GetBehavior<ProjectileColliderServerBehavior>();
        if (colliderBehavior == null)
        {
            LoggerUtil.Warn(_api, this, $"Received collisions data for projectile, but projectile '{projectile.Code}' does not have 'ProjectileColliderServerBehavior'");
            return;
        }

        Dictionary<Entity, EntityWithSphereIntersectionData[]> entityCollisions = [];
        foreach ((long targetEntityId, ProjectileEntityCollisionPacketData[] data) in packet.EntityCollisions)
        {
            Entity? target = _api.World.GetEntityById(targetEntityId);
            if (target == null)
            {
                LoggerUtil.Warn(_api, this, $"Was not able to find target entity by supplied entity id '{targetEntityId}' when receiving collisions data");
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
            LoggerUtil.Error(api, typeof(ProjectileCollisionsSynchroniserServer), errorMessage);
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
            new(collision.IntersectionPointX, collision.IntersectionPointY, collision.IntersectionPointY),
            new(collision.BlockPositionX, collision.BlockPositionY, collision.BlockPositionZ),
            collision.PositionInTime);
    }
}

public sealed class ProjectileCollisionsSynchroniserClient : ProjectileCollisionsSynchroniser
{
    public ProjectileCollisionsSynchroniserClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel("ProjectileCollisionsSynchroniserClient")
            .RegisterMessageType<ProjectileCollisionsPacket>()
            .RegisterMessageType<ProjectileEnableCollisionsPacket>()
            .SetMessageHandler<ProjectileEnableCollisionsPacket>(HandlePacket);
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
    private readonly ICoreClientAPI _api;

    private void HandlePacket(ProjectileEnableCollisionsPacket packet)
    {
        Entity? projectile = _api.World.GetEntityById(packet.ProjectileEntityId);
        if (projectile == null)
        {
            LoggerUtil.Warn(_api, this, $"Was not able to find entity by supplied entity id '{packet.ProjectileEntityId}' when trying to enable/disable collisions for it");
            return;
        }

        ProjectileColliderClientBehavior? colliderBehavior = projectile.GetBehavior<ProjectileColliderClientBehavior>();
        if (colliderBehavior == null)
        {
            LoggerUtil.Warn(_api, this, $"Trying to enable/disable collisions for projectile, but projectile '{projectile.Code}' does not have 'ProjectileColliderClientBehavior'");
            return;
        }

        colliderBehavior.EnableCollisions = packet.EnableCollisions;
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
