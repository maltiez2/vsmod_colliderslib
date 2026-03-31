using CollidersLib.Utils;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CollidersLib.Items;


public abstract class ItemCollisionsSynchroniser
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ItemCollisionsPacket
    {
        public int ItemId { get; set; }
        public bool MainHand { get; set; }
        public Dictionary<int, Dictionary<long, ItemEntityCollisionPacketData[]>> EntityCollisions { get; set; } = [];
        public Dictionary<int, List<ItemTerrainCollisionPacketData>> TerrainCollisions { get; set; } = [];
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class ItemEnableCollisionsPacket
    {
        public int ItemId { get; set; }
        public bool MainHand { get; set; }
        public bool EnableCollisions { get; set; }
    }

    public struct ItemEntityCollisionPacketData
    {
        public string ColliderElementName { get; set; }
        public double IntersectionPointX { get; set; }
        public double IntersectionPointY { get; set; }
        public double IntersectionPointZ { get; set; }
        public double PositionOnCollider { get; set; }
        public double PositionInTime { get; set; }
    }

    public struct ItemTerrainCollisionPacketData
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
        public double PositionOnCollider { get; set; }
        public double PositionInTime { get; set; }
        public int SubdivisionsNumber { get; set; }
    }
}


public sealed class ItemCollisionsSynchroniserServer : ItemCollisionsSynchroniser
{
    public ItemCollisionsSynchroniserServer(ICoreServerAPI api)
    {
        _api = api;
        _serverChannel = api.Network.RegisterChannel("ItemCollisionsSynchroniserClient")
            .RegisterMessageType<ItemCollisionsPacket>()
            .RegisterMessageType<ItemEnableCollisionsPacket>()
            .SetMessageHandler<ItemCollisionsPacket>(HandlePacket);
    }

    public void ToggleItemCollisions(Item item, IServerPlayer playerHoldingItem, bool mainHand, bool enable)
    {
        _serverChannel.SendPacket(new ItemEnableCollisionsPacket() { EnableCollisions = enable, MainHand = mainHand, ItemId = item.Id }, playerHoldingItem);
    }



    private readonly IServerNetworkChannel _serverChannel;
    private readonly ICoreServerAPI _api;


    private void HandlePacket(IPlayer player, ItemCollisionsPacket packet)
    {
        Item? item = _api.World.GetItem(packet.ItemId);
        if (item == null)
        {
            LoggerUtil.Warn(_api, this, $"Unable to find item with id {packet.ItemId} when receiving collisions packet");
            return;
        }

        ItemCollidersBehaviorServer? collidersBehavior = item.GetCollectibleBehavior<ItemCollidersBehaviorServer>(withInheritance: true);
        if (collidersBehavior == null)
        {
            LoggerUtil.Warn(_api, this, $"Item {item.Code} does not have 'ItemColliderBehaviorServer' behavior");
            return;
        }

        ItemSlot slot = packet.MainHand ? player.Entity.RightHandItemSlot : player.Entity.LeftHandItemSlot;
        if (slot.Itemstack?.Item?.Id != packet.ItemId)
        {
            LoggerUtil.Verbose(_api, this, $"Received collisions for item {item.Code} but it is no longer in the slot");
            return;
        }

        HashSet<int> colliders = [];
        foreach (int colliderIndex in packet.EntityCollisions.Keys)
        {
            colliders.Add(colliderIndex);
        }
        foreach (int colliderIndex in packet.TerrainCollisions.Keys)
        {
            colliders.Add(colliderIndex);
        }

        List<ColliderItemCollisionData> collisions = [];
        foreach (int colliderIndex in colliders)
        {
            GenerateCollisionsData(colliderIndex, packet, collisions);
        }

        if (collisions.Count > 0)
        {
            collidersBehavior.OnCollisionPacket(player.Entity, slot, collisions);
        }
    }

    private void GenerateCollisionsData(int colliderIndex, ItemCollisionsPacket packet, List<ColliderItemCollisionData> collisions)
    {
        Dictionary<Entity, EntityWithCapsuleIntersectionData[]> entityCollisions = [];
        if (packet.EntityCollisions.TryGetValue(colliderIndex, out Dictionary<long, ItemEntityCollisionPacketData[]>? packetEntityCollisions))
        {
            foreach ((long targetEntityId, ItemEntityCollisionPacketData[] data) in packetEntityCollisions)
            {
                Entity? target = _api.World.GetEntityById(targetEntityId);
                if (target == null)
                {
                    LoggerUtil.Warn(_api, this, $"Was not able to find target entity by supplied entity id '{targetEntityId}' when receiving collisions data");
                    continue;
                }

                CollidersEntityBehavior? entityCollidersBehavior = target.GetBehavior<CollidersEntityBehavior>();

                entityCollisions.Add(target, data.Select(collision => GenerateEntityCollisionData(collision, entityCollidersBehavior)).ToArray());
            }
        }

        List<TerrainWithCapsuleIntersectionData> terrainCollisions = [];
        if (packet.TerrainCollisions.TryGetValue(colliderIndex, out List<ItemTerrainCollisionPacketData>? packetTerrainCollisions))
        {
            terrainCollisions = packetTerrainCollisions.Select(collision => GenerateTerrainCollisionData(_api, collision)).ToList();
        }

        if (entityCollisions.Count > 0 && terrainCollisions.Count > 0)
        {
            collisions.Add(new(colliderIndex, entityCollisions, terrainCollisions));
        }
    }

    private static EntityWithCapsuleIntersectionData GenerateEntityCollisionData(ItemEntityCollisionPacketData collision, CollidersEntityBehavior? targetColliders)
    {
        ShapeElementCollider? collider = null;
        if (targetColliders != null)
        {
            collider = targetColliders.Colliders.Find(element => element.ShapeElementName == collision.ColliderElementName);
        }

        return new(collider, new(collision.IntersectionPointX, collision.IntersectionPointY, collision.IntersectionPointZ), collision.PositionOnCollider, collision.PositionInTime);
    }

    private static TerrainWithCapsuleIntersectionData GenerateTerrainCollisionData(ICoreAPI api, ItemTerrainCollisionPacketData collision)
    {
        Block? block = api.World.GetBlock(collision.BlockId);
        if (block == null)
        {
            string errorMessage = $"Enable to find block with id '{collision.BlockId}' when handling projectile collision packet";
            LoggerUtil.Error(api, typeof(ItemCollisionsSynchroniserServer), errorMessage);
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
            collision.PositionOnCollider,
            collision.PositionInTime,
            collision.SubdivisionsNumber);
    }
}


public sealed class ItemCollisionsSynchroniserClient : ItemCollisionsSynchroniser
{
    public ItemCollisionsSynchroniserClient(ICoreClientAPI api)
    {
        _api = api;
        _clientChannel = api.Network.RegisterChannel("ItemCollisionsSynchroniserClient")
            .RegisterMessageType<ItemCollisionsPacket>()
            .RegisterMessageType<ItemEnableCollisionsPacket>()
            .SetMessageHandler<ItemEnableCollisionsPacket>(HandlePacket);
    }

    public void SendCollisions(EntityPlayer playerHoldingItem, Item item, bool mainHand, List<ColliderItemCollisionData> collisions)
    {
        Dictionary<int, Dictionary<long, ItemEntityCollisionPacketData[]>> entityCollisions = [];
        Dictionary<int, List<ItemTerrainCollisionPacketData>> terrainCollisions = [];

        foreach (ColliderItemCollisionData collision in collisions)
        {
            entityCollisions.Add(collision.ColliderIndex, collision.EntityCollisions.ToDictionary(entry => entry.Key.EntityId, entry => entry.Value.Select(GenerateEntityCollisionData).ToArray()));
            terrainCollisions.Add(collision.ColliderIndex, collision.TerrainCollisions.Select(GenerateTerrainCollisionData).ToList());
        }

        ItemCollisionsPacket packet = new()
        {
            MainHand = mainHand,
            ItemId = item.Id,
            EntityCollisions = entityCollisions,
            TerrainCollisions = terrainCollisions
        };

        _clientChannel.SendPacket(packet);
    }



    private readonly IClientNetworkChannel _clientChannel;
    private readonly ICoreClientAPI _api;


    private void HandlePacket(ItemEnableCollisionsPacket packet)
    {
        Item? item = _api.World.GetItem(packet.ItemId);
        if (item == null)
        {
            LoggerUtil.Warn(_api, this, $"Unable to find item with id {packet.ItemId} when receiving collision toggle packet");
            return;
        }

        ItemCollidersBehaviorClient? colliderBehavior = item.GetCollectibleBehavior<ItemCollidersBehaviorClient>(withInheritance: true);
        if (colliderBehavior == null)
        {
            LoggerUtil.Warn(_api, this, $"Item {item.Code} does not have 'ItemColliderBehaviorClient' behavior");
            return;
        }

        if (packet.MainHand)
        {
            colliderBehavior.MainHandCollisionsEnabled = packet.EnableCollisions;
        }
        else
        {
            colliderBehavior.OffHandCollisionsEnabled = packet.EnableCollisions;
        }
    }

    private static ItemEntityCollisionPacketData GenerateEntityCollisionData(EntityWithCapsuleIntersectionData collision)
    {
        return new()
        {
            ColliderElementName = collision.EntityCollider?.ShapeElementName ?? "",
            IntersectionPointX = collision.IntersectionPoint.X,
            IntersectionPointY = collision.IntersectionPoint.Y,
            IntersectionPointZ = collision.IntersectionPoint.Z,
            PositionOnCollider = collision.PositionOnCollider,
            PositionInTime = collision.PositionInTime
        };
    }

    private static ItemTerrainCollisionPacketData GenerateTerrainCollisionData(TerrainWithCapsuleIntersectionData collision)
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
            PositionOnCollider = collision.PositionOnCollider,
            PositionInTime = collision.PositionInTime,
            SubdivisionsNumber = collision.SubdivisionsNumber
        };
    }
}
