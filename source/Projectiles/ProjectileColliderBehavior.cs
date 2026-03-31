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

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        Config = attributes.AsObject<ProjectileColliderConfig>() ?? new();
    }

    public void OnCollisionPacket(Dictionary<Entity, EntityWithSphereIntersectionData[]> entityCollisions, List<TerrainWithShpereIntersectionData> terrainCollisions)
    {
        OnCollision?.Invoke(entityCollisions, terrainCollisions);
    }

    public void ToggleCollisions(IServerPlayer owner, bool enable)
    {
        CollisionsSycnronizer.ToggleProjectileCollisions(owner, entity, enable, Config.Radius);
    }

    public override string PropertyName() => "ProjectileColliderBehavior";



    protected readonly ProjectileCollisionsSynchroniserServer CollisionsSycnronizer;
    protected ProjectileColliderConfig Config = new();
}

