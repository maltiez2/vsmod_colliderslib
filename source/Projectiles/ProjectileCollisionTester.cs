using OpenTK.Mathematics;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;

namespace CollidersLib.Projectiles;

public class ProjectileCollisionTester
{
    public ProjectileCollisionTester(ICoreClientAPI api, float radius, Vector3d position, long entityId)
    {
        CollisionsSycnronizer = api.ModLoader.GetModSystem<CollidersLibSystem>().ProjectileCollisionsSynchroniserClient ?? throw new InvalidOperationException($"Unable to get ProjectileCollisionsSynchroniserServer while intantiating ProjectileColliderServerBehavior");
        EntityId = entityId;
        Collider.Radius = radius;
        UpdateCollider(position, reset: true);
    }


    public EntitySphereCollider Collider { get; set; } = new();
    public float SearchRadius { get; set; } = 8;
    public long EntityId { get; }

    public event Action<Dictionary<Entity, EntityWithSphereIntersectionData[]>, List<TerrainWithShpereIntersectionData>>? OnCollision;


    public void OnGameTick(ICoreClientAPI api, Vector3d position, bool reset = false)
    {
        UpdateCollider(position, reset);

        Collider.CollideWithTerrain(api, out List<TerrainWithShpereIntersectionData> terrainCollisions);

        Dictionary<Entity, EntityWithSphereIntersectionData[]> entitiesCollisions = [];
        HashSet<Entity> targets = GetSurroundingEntities(api);
        foreach (Entity target in targets)
        {
            if (target.EntityId == EntityId)
            {
                continue;
            }
            
            CollidersEntityBehavior? targetColliders = target.GetBehavior<CollidersEntityBehavior>();
            if (Collider.CollideWithEntity(target, targetColliders, out List<EntityWithSphereIntersectionData> entityCollisions))
            {
                entitiesCollisions.Add(target, entityCollisions.ToArray());
            }
        }

        if (terrainCollisions.Count > 0 || entitiesCollisions.Count > 0)
        {
            CollisionsSycnronizer.SendCollisions(EntityId, entitiesCollisions, terrainCollisions);
            OnCollision?.Invoke(entitiesCollisions, terrainCollisions);
        }
    }



    protected readonly ProjectileCollisionsSynchroniserClient CollisionsSycnronizer;


    protected void UpdateCollider(Vector3d position, bool reset = false)
    {
        Collider.PreviousPosition = reset ? position : Collider.Position;
        Collider.Position = position;
    }

    protected virtual HashSet<Entity> GetSurroundingEntities(ICoreClientAPI api)
    {
        Vector3d direction = Collider.Position - Collider.PreviousPosition;
        if (direction.Length < SearchRadius / 4)
        {
            Vector3d searchPoint = Collider.PreviousPosition + direction / 2;
            return api.World.GetEntitiesAround(new(searchPoint.X, searchPoint.Y, searchPoint.Z), SearchRadius, SearchRadius).ToHashSet();
        }

        HashSet<Entity> result = [];
        int subdivisions = (int)Math.Ceiling((Collider.Position - Collider.PreviousPosition).Length / SearchRadius * 2);
        direction = direction.Length / subdivisions * direction.Normalized();

        for (int subdivision = 0; subdivision <= subdivisions; subdivision++)
        {
            Vector3d searchPoint = Collider.PreviousPosition + direction * subdivision;

            Entity[] entitiesAroundSearchPoint = api.World.GetEntitiesAround(new(searchPoint.X, searchPoint.Y, searchPoint.Z), SearchRadius, SearchRadius);

            foreach (Entity entity in entitiesAroundSearchPoint)
            {
                result.Add(entity);
            }
        }

        return result;
    }
}
