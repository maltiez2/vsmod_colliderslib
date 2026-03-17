using CollidersLib.VectorsUtils;
using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace CollidersLib.Projectiles;

public class ProjectileColliderConfig
{
    public float Radius { get; set; } = 0;
}

public class ProjectileColliderServerBehavior : EntityBehavior
{
    public ProjectileColliderServerBehavior(Entity entity) : base(entity)
    {
    }

    public override string PropertyName() => "ProjectileColliderBehavior";
}

public class ProjectileColliderClientBehavior : EntityBehavior
{
    public ProjectileColliderClientBehavior(Entity entity) : base(entity)
    {
    }


    public EntitySphereCollider Collider { get; set; } = new();
    public long ShooterEntityId { get; set; }

    
    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        ProjectileColliderConfig config = attributes.AsObject<ProjectileColliderConfig>() ?? new();

        Collider.Radius = config.Radius;
        Collider.Position = entity.Pos.XYZ.ToOpenTK();
        Collider.PreviousPosition = Collider.Position;
    }

    public override void OnGameTick(float deltaTime)
    {

    }

    public override string PropertyName() => "ProjectileColliderBehavior";
}
