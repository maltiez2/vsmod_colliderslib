using CollidersLib.VectorsUtils;
using OpenTK.Mathematics;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace CollidersLib.Projectiles;

public class ProjectileColliderConfig
{
    public float Radius { get; set; } = 0;
}

public class ProjectileColliderServerBehavior : EntityBehavior
{
    public ProjectileColliderServerBehavior(Entity entity) : base(entity)
    {
        if (entity.Api.Side != Vintagestory.API.Common.EnumAppSide.Server)
        {
            throw new InvalidOperationException($"ProjectileColliderServerBehavior should be a server side behavior");
        }

        CollisionsSycnronizer = entity.Api.ModLoader.GetModSystem<CollidersLibSystem>().ProjectileCollisionsSynchroniserServer ?? throw new InvalidOperationException($"Unable to get ProjectileCollisionsSynchroniserServer while intantiating ProjectileColliderServerBehavior");
    }


    public event Action<Dictionary<Entity, EntityWithSphereIntersectionData[]>, List<TerrainWithShpereIntersectionData>>? OnCollision;


    public void OnCollisionPacket(Dictionary<Entity, EntityWithSphereIntersectionData[]> entityCollisions, List<TerrainWithShpereIntersectionData> terrainCollisions)
    {
        OnCollision?.Invoke(entityCollisions, terrainCollisions);
    }

    public void ToggleCollisions(IServerPlayer owner, bool enable)
    {
        CollisionsSycnronizer.ToggleProjectileCollisions(owner, entity.EntityId, enable);
    }

    public override string PropertyName() => "ProjectileColliderBehavior";



    protected readonly ProjectileCollisionsSynchroniserServer CollisionsSycnronizer;
}

public class ProjectileColliderClientBehavior : EntityBehavior
{
    public ProjectileColliderClientBehavior(Entity entity) : base(entity)
    {
        if (entity.Api.Side != Vintagestory.API.Common.EnumAppSide.Client)
        {
            throw new InvalidOperationException($"ProjectileColliderClientBehavior should be a client side behavior");
        }

        CollisionsSycnronizer = entity.Api.ModLoader.GetModSystem<CollidersLibSystem>().ProjectileCollisionsSynchroniserClient ?? throw new InvalidOperationException($"Unable to get ProjectileCollisionsSynchroniserServer while intantiating ProjectileColliderServerBehavior");
    }


    public EntitySphereCollider Collider { get; set; } = new();
    public long ShooterEntityId { get; set; }
    public bool EnableCollisions { get; set; }
    public float SearchRadius { get; set; } = 8;

    public event Action<Dictionary<Entity, EntityWithSphereIntersectionData[]>, List<TerrainWithShpereIntersectionData>>? OnCollision;


    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        ProjectileColliderConfig config = attributes.AsObject<ProjectileColliderConfig>() ?? new();

        Collider.Radius = config.Radius;
        UpdateCollider();
    }

    public override void OnGameTick(float deltaTime)
    {
        UpdateCollider();

        if (!EnableCollisions) return;

        Collider.CollideWithTerrain(entity.Api, out List<TerrainWithShpereIntersectionData> terrainCollisions);

        Dictionary<Entity, EntityWithSphereIntersectionData[]> entitiesCollisions = [];
        HashSet<Entity> targets = GetSurroundingEntities();
        foreach (Entity target in targets)
        {
            CollidersEntityBehavior? targetColliders = target.GetBehavior<CollidersEntityBehavior>();
            if (Collider.CollideWithEntity(target, targetColliders, out List<EntityWithSphereIntersectionData> entityCollisions))
            {
                entitiesCollisions.Add(target, entityCollisions.ToArray());
            }
        }

        if (terrainCollisions.Count > 0 || entitiesCollisions.Count > 0)
        {
            CollisionsSycnronizer.SendCollisions(entity.EntityId, entitiesCollisions, terrainCollisions);
            OnCollision?.Invoke(entitiesCollisions, terrainCollisions);
        }
    }

    public override string PropertyName() => "ProjectileColliderBehavior";



    protected readonly ProjectileCollisionsSynchroniserClient CollisionsSycnronizer;

    protected virtual void UpdateCollider()
    {
        Collider.Position = entity.Pos.XYZ.ToOpenTK();
        Collider.PreviousPosition = Collider.Position;
    }

    protected virtual HashSet<Entity> GetSurroundingEntities()
    {
        Vector3d direction = Collider.Position - Collider.PreviousPosition;
        if (direction.Length < SearchRadius / 4)
        {
            Vector3d searchPoint = Collider.PreviousPosition + direction / 2;
            return entity.Api.World.GetEntitiesAround(new(searchPoint.X, searchPoint.Y, searchPoint.Z), SearchRadius, SearchRadius).ToHashSet();
        }

        HashSet<Entity> result = [];
        int subdivisions = (int)Math.Ceiling((Collider.Position - Collider.PreviousPosition).Length / SearchRadius * 2);
        direction = direction.Length / subdivisions * direction.Normalized();

        for (int subdivision = 0; subdivision <= subdivisions; subdivision++)
        {
            Vector3d searchPoint = Collider.PreviousPosition + direction * subdivision;

            Entity[] entitiesAroundSearchPoint = entity.Api.World.GetEntitiesAround(new(searchPoint.X, searchPoint.Y, searchPoint.Z), SearchRadius, SearchRadius);

            foreach (Entity entity in entitiesAroundSearchPoint)
            {
                result.Add(entity);
            }
        }

        return result;
    }
}
