using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using static OpenTK.Graphics.OpenGL.GL;

namespace CollidersLib.Items;


public readonly struct ItemColliderCollisionData
{
    public readonly int ColliderIndex;
    public readonly IImmutableDictionary<Entity, EntityWithCapsuleIntersectionData[]> EntityCollisions;
    public readonly IImmutableList<TerrainWithCapsuleIntersectionData> TerrainCollisions;

    public ItemColliderCollisionData(int colliderIndex, Dictionary<Entity, EntityWithCapsuleIntersectionData[]> entityCollisions, List<TerrainWithCapsuleIntersectionData> terrainCollisions)
    {
        ColliderIndex = colliderIndex;
        EntityCollisions = entityCollisions.ToImmutableDictionary();
        TerrainCollisions = terrainCollisions.ToImmutableList();
    }
}


public class ItemCollidersBehaviorServer : CollectibleBehavior
{
    public ItemCollidersBehaviorServer(CollectibleObject collObj) : base(collObj)
    {
    }

    public event Action<EntityPlayer, ItemSlot, List<ItemColliderCollisionData>>? OnCollision;

    public void OnCollisionPacket(EntityPlayer player, ItemSlot inSlot, List<ItemColliderCollisionData> collisions)
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

    public event Action<EntityPlayer, ItemSlot, List<ItemColliderCollisionData>>? OnCollision;

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

    public virtual void ResetColliders()
    {
        ResetColliderNextTick = true;
    }

    public virtual void CheckCollisions(EntityPlayer player, ItemSlot inSlot, out List<ItemColliderCollisionData> collisions, bool restColliders = false)
    {
        collisions = [];

        if (player.Api is not ICoreClientAPI api)
        {
            return;
        }

        if (api.World.Player.Entity.EntityId != player.EntityId)
        {
            return;
        }

        if (inSlot.Itemstack?.Item?.Id != collObj.Id)
        {
            return;
        }

        if (restColliders)
        {
            ResetColliderNextTick = true;
        }

        bool mainHand = inSlot == player.RightHandItemSlot;

        TryCollide(player, inSlot, api, mainHand, out collisions);
    }



    protected ItemCollisionsSynchroniserClient? CollisionsSychronizer;
    protected bool ResetColliderNextTick { get; set; } = false;
    protected readonly Item Item;


    protected virtual void TryCollide(EntityPlayer player, ItemSlot inSlot, ICoreClientAPI api, bool mainHand, out List<ItemColliderCollisionData> collisions)
    {
        foreach (ItemCapsuleCollider collier in Colliders.Values)
        {
            collier.TransformCollider(player, mainHand, ResetColliderNextTick);
        }
        ResetColliderNextTick = false;

        GatherCollisionData(player, api, out collisions);

        if (collisions.Count > 0)
        {
            if (SynchronizeCollisions)
            {
                CollisionsSychronizer?.SendCollisions(player, Item, mainHand, collisions);
            }

            OnCollision?.Invoke(player, inSlot, collisions);
        }
    }

    protected virtual void GatherCollisionData(EntityPlayer player, ICoreClientAPI api, out List<ItemColliderCollisionData> collisions)
    {
        Entity[] targets = api.World.GetEntitiesAround(player.Pos.XYZ, SearchRadius, SearchRadius);
        Dictionary<Entity, CollidersEntityBehavior?> targetsColliders = targets.ToDictionary(target => target, targets => targets.GetBehavior<CollidersEntityBehavior>());

        collisions = [];
        foreach ((int colliderIndex, ItemCapsuleCollider collider) in Colliders)
        {
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
}
