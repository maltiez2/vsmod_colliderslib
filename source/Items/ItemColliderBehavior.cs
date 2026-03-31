using CollidersLib.VectorsUtils;
using Newtonsoft.Json.Linq;
using OpenTK.Mathematics;
using System.Collections.Immutable;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace CollidersLib.Items;


public readonly struct ColliderItemCollisionData(int colliderIndex, Dictionary<Entity, EntityWithCapsuleIntersectionData[]> entityCollisions, List<TerrainWithCapsuleIntersectionData> terrainCollisions)
{
    public readonly int ColliderIndex = colliderIndex;
    public readonly IImmutableDictionary<Entity, EntityWithCapsuleIntersectionData[]> EntityCollisions = entityCollisions.ToImmutableDictionary();
    public readonly IImmutableList<TerrainWithCapsuleIntersectionData> TerrainCollisions = terrainCollisions.ToImmutableList();
}

public readonly struct SingleItemCollisionData(double normalizedPosition, int colliderIndex, Entity? target, EntityWithCapsuleIntersectionData? entityCollision, TerrainWithCapsuleIntersectionData? terrainCollision, bool behindTerrain, double positionInTime, double positionOnCollider, bool behindAttacker)
{
    public readonly int ColliderIndex = colliderIndex;
    public readonly double NormalizedPosition = normalizedPosition;
    public readonly double PositionInTime = positionInTime;
    public readonly double PositionOnCollider = positionOnCollider;
    public readonly bool BehindTerrain = behindTerrain;
    public readonly bool BehindAttacker = behindAttacker;
    
    public readonly Entity? Target = target;
    public readonly EntityWithCapsuleIntersectionData? EntityCollision = entityCollision;
    public readonly TerrainWithCapsuleIntersectionData? TerrainCollision = terrainCollision;
}


public class ItemCollidersBehaviorServer : CollectibleBehavior
{
    public ItemCollidersBehaviorServer(CollectibleObject collObj) : base(collObj)
    {
    }

    public event Action<EntityPlayer, ItemSlot, List<ColliderItemCollisionData>>? OnCollision;

    public void OnCollisionPacket(EntityPlayer player, ItemSlot inSlot, List<ColliderItemCollisionData> collisions)
    {
        OnCollision?.Invoke(player, inSlot, collisions);
    }

    public override void OnLoaded(ICoreAPI api)
    {
        CollisionsSychronizer = api.ModLoader.GetModSystem<CollidersLibSystem>()?.ItemCollisionsSynchroniserServer;
    }


    protected ItemCollisionsSynchroniserServer? CollisionsSychronizer;
}


public class ItemCollidersBehaviorClient : CollectibleBehavior
{
    public ItemCollidersBehaviorClient(CollectibleObject collObj) : base(collObj)
    {
        Item = collObj as Item ?? throw new InvalidOperationException($"'ItemCollidersBehaviorClient' should be attached to an Item");
    }

    public bool MainHandCollisionsEnabled { get; set; } = false;
    public bool OffHandCollisionsEnabled { get; set; } = false;
    public bool SynchronizeCollisions { get; set; } = false;
    public float SearchRadius { get; set; } = 8;

    public Dictionary<int, ItemCapsuleCollider> Colliders { get; } = [];

    public event Action<EntityPlayer, ItemSlot, List<ColliderItemCollisionData>>? OnCollision;

    public override void OnLoaded(ICoreAPI api)
    {
        CollisionsSychronizer = api.ModLoader.GetModSystem<CollidersLibSystem>()?.ItemCollisionsSynchroniserClient;

    }

    public override void Initialize(JsonObject properties)
    {
        base.Initialize(properties);

        SynchronizeCollisions = collObj.GetCollectibleBehavior<ItemCollidersBehaviorServer>(withInheritance: true) != null;

        if (properties.KeyExists("colliders") && properties["colliders"].Token is JArray jsonArray)
        {
            for (int colliderIndex = 0; colliderIndex < jsonArray.Count; colliderIndex++)
            {
                Colliders.Add(colliderIndex, ItemCapsuleCollider.FromJson(new(jsonArray[colliderIndex])));
            }
        }
    }

    public virtual void ResetColliders(EntityPlayer player, ItemSlot inSlot)
    {
        bool mainHand = inSlot == player.RightHandItemSlot;
        if (mainHand)
        {
            MainHandResetColliderNextTick = true;
        }
        else
        {
            OffHandResetColliderNextTick = true;
        }
    }

    public virtual List<ColliderItemCollisionData> CheckForCollisions(EntityPlayer player, ItemSlot inSlot, bool resetColliders = false, int[]? collidersToCheck = null)
    {
        List<ColliderItemCollisionData> collisions = [];

        if (player.Api is not ICoreClientAPI api)
        {
            return collisions;
        }

        if (api.World.Player.Entity.EntityId != player.EntityId)
        {
            return collisions;
        }

        if (inSlot.Itemstack?.Item?.Id != collObj.Id)
        {
            return collisions;
        }

        bool mainHand = inSlot == player.RightHandItemSlot;

        if (resetColliders)
        {
            if (mainHand)
            {
                MainHandResetColliderNextTick = true;
            }
            else
            {
                OffHandResetColliderNextTick = true;
            }
        }

        TryCollide(player, inSlot, api, mainHand, out collisions, collidersToCheck);

        return collisions;
    }

    public virtual List<SingleItemCollisionData> CheckForCollisionsInOrder(EntityPlayer player, ItemSlot inSlot, int[] collidersInOrder, bool ingoreTerrainBehindAttacker, bool resetColliders = false)
    {
        List<ColliderItemCollisionData> collisions = CheckForCollisions(player, inSlot, resetColliders);

        return SortCollisions(player, collisions, collidersInOrder, ingoreTerrainBehindAttacker);
    }

    public virtual List<SingleItemCollisionData> SortCollisions(EntityPlayer player, List<ColliderItemCollisionData> collisions, int[] collidersInOrder, bool ingoreTerrainBehindAttacker = false)
    {
        List<SingleItemCollisionData> result = FlattenCollisionsData(player, collisions, collidersInOrder);

        result = result.OrderBy(entry => entry.NormalizedPosition).ToList();

        result = CheckIfBehindTerrain(result, ingoreTerrainBehindAttacker);

        return result;
    }



    protected bool MainHandResetColliderNextTick { get; set; } = false;
    protected bool OffHandResetColliderNextTick { get; set; } = false;

    protected ItemCollisionsSynchroniserClient? CollisionsSychronizer;
    protected readonly Item Item;


    protected virtual void TryCollide(EntityPlayer player, ItemSlot inSlot, ICoreClientAPI api, bool mainHand, out List<ColliderItemCollisionData> collisions, int[]? collidersToCheck)
    {
        bool resetColliders = mainHand ? MainHandResetColliderNextTick : OffHandResetColliderNextTick;

        foreach (ItemCapsuleCollider collier in Colliders.Values)
        {
            collier.TransformCollider(player, mainHand, resetColliders);
        }

        if (mainHand)
        {
            MainHandResetColliderNextTick = false;
        }
        else
        {
            OffHandResetColliderNextTick = false;
        }


        GatherCollisionData(player, api, out collisions, collidersToCheck);

        if (collisions.Count > 0)
        {
            if (SynchronizeCollisions)
            {
                CollisionsSychronizer?.SendCollisions(player, Item, mainHand, collisions);
            }

            OnCollision?.Invoke(player, inSlot, collisions);
        }
    }

    protected virtual void GatherCollisionData(EntityPlayer player, ICoreClientAPI api, out List<ColliderItemCollisionData> collisions, int[]? collidersToCheck)
    {
        Entity[] targets = api.World.GetEntitiesAround(player.Pos.XYZ, SearchRadius, SearchRadius);
        Dictionary<Entity, CollidersEntityBehavior?> targetsColliders = targets.ToDictionary(target => target, targets => targets.GetBehavior<CollidersEntityBehavior>());

        collisions = [];
        foreach ((int colliderIndex, ItemCapsuleCollider collider) in Colliders)
        {
            if (collidersToCheck != null && !collidersToCheck.Contains(colliderIndex))
            {
                continue;
            }

            Dictionary<Entity, EntityWithCapsuleIntersectionData[]> entityCollisions = [];
            foreach ((Entity target, CollidersEntityBehavior? targetColliders) in targetsColliders)
            {
                if (collider.CollideWithEntity(target, targetColliders, out List<EntityWithCapsuleIntersectionData> entityIntersections))
                {
                    entityCollisions.Add(target, entityIntersections.ToArray());
                }
            }

            collider.CollideWithTerrain(api, out List<TerrainWithCapsuleIntersectionData> terrainCollisions);

            if (entityCollisions.Count > 0 || terrainCollisions.Count > 0)
            {
                collisions.Add(new(colliderIndex, entityCollisions, terrainCollisions));
            }
        }
    }

    protected virtual List<SingleItemCollisionData> FlattenCollisionsData(EntityPlayer player, List<ColliderItemCollisionData> collisions, int[] collidersInOrder)
    {
        Vector3d playerPosition = player.Pos.XYZ.ToOpenTK();
        Vector3d eyesPosition = player.LocalEyePos.ToOpenTK() + playerPosition;
        Vector3d viewDirection = player.Pos.GetViewVector().ToVec3d().ToOpenTK();
        double eyesProjection = Vector3d.Dot(viewDirection, eyesPosition);

        List<SingleItemCollisionData> result = [];
        foreach (ColliderItemCollisionData collision in collisions)
        {
            double colliderPriority = collidersInOrder.IndexOf(collision.ColliderIndex) / (double)(collidersInOrder.Length - 1);

            foreach ((Entity target, EntityWithCapsuleIntersectionData[] entityCollisions) in collision.EntityCollisions)
            {
                foreach (EntityWithCapsuleIntersectionData entityCollision in entityCollisions)
                {
                    double normalizedPosition = (colliderPriority * 10.0 + entityCollision.NormalizedPosition) / 11.0;
                    double positionOnCollider = (colliderPriority * 10.0 + entityCollision.PositionOnCollider) / 11.0;
                    double hitProjection = Vector3d.Dot(viewDirection, entityCollision.IntersectionPoint);
                    bool behindAttacker = hitProjection > eyesProjection;

                    SingleItemCollisionData collisionData = new(normalizedPosition, collision.ColliderIndex, target, entityCollision, null, false, entityCollision.PositionInTime, positionOnCollider, behindAttacker);
                    result.Add(collisionData);
                }
            }

            foreach (TerrainWithCapsuleIntersectionData terrainCollision in collision.TerrainCollisions)
            {
                double normalizedPosition = (colliderPriority * 10.0 + terrainCollision.NormalizedPosition) / 11.0;
                double positionOnCollider = (colliderPriority * 10.0 + terrainCollision.PositionOnCollider) / 11.0;
                double hitProjection = Vector3d.Dot(viewDirection, terrainCollision.IntersectionPoint);
                bool behindAttacker = hitProjection > eyesProjection;

                SingleItemCollisionData collisionData = new(normalizedPosition, collision.ColliderIndex, null, null, terrainCollision, false, terrainCollision.PositionInTime, positionOnCollider, behindAttacker);
                result.Add(collisionData);
            }
        }
        return result;
    }

    protected virtual List<SingleItemCollisionData> CheckIfBehindTerrain(List<SingleItemCollisionData> collisions, bool ingoreTerrainBehindAttacker)
    {
        List<(double from, double to, double distance)> terrainCollisions = [];

        foreach (SingleItemCollisionData collision in collisions)
        {
            if (collision.TerrainCollision == null)
            {
                continue;
            }

            if (ingoreTerrainBehindAttacker && collision.BehindAttacker)
            {
                continue;
            }

            double radiusInTime = 1.0 / collision.TerrainCollision.Value.SubdivisionsNumber / 0.5;

            terrainCollisions.Add((collision.PositionInTime - radiusInTime, collision.PositionInTime + radiusInTime, collision.PositionOnCollider));
        }

        List<SingleItemCollisionData> result = [];

        foreach (SingleItemCollisionData collision in collisions)
        {
            double positionOnCollider = collision.PositionOnCollider;
            double positionInTime = collision.PositionInTime;
            bool behindTerrain = false;

            foreach ((double from, double to, double distance) in terrainCollisions)
            {
                if (distance >= positionOnCollider)
                {
                    continue;
                }

                if (from > positionInTime || to < positionInTime)
                {
                    continue;
                }

                behindTerrain = true;
                break;
            }

            result.Add(new(collision.NormalizedPosition, collision.ColliderIndex, collision.Target, collision.EntityCollision, collision.TerrainCollision, behindTerrain, collision.PositionInTime, collision.PositionOnCollider, collision.BehindAttacker));
        }

        return result;
    }
}
