using OpenTK.Mathematics;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CollidersLib;

public sealed class StaticEntityCollidersBehavior(Entity entity) : EntityBehavior(entity), IEntityCollidersProvider, IEntityColliderReceiver
{
    public CollidersConfig? Config { get; set; }

    public CuboidAABBCollider BoundingBox { get; set; }
    public bool HasOBBCollider => Colliders.Length > 0;
    public ImmutableArray<ShapeElementInWorldCollider> Colliders { get; set; } = [];
    public ImmutableDictionary<int, string> ColliderTypeById => _collierIdsManager.ColliderTypeById;
    public ImmutableDictionary<string, Color4> ColorByType => _collierIdsManager.ColorByType;
    public ImmutableArray<ShapeElementProtoCollider> ProtoColliders { get; private set; } = [];
    public ClientAnimator? Animator { get; set; }
    public EntityShapeRenderer? Renderer => entity.Properties.Client.Renderer as EntityShapeRenderer;



    public override void OnGameTick(float deltaTime)
    {
        Animator = entity.AnimManager?.Animator as ClientAnimator;

        if (!_configured)
        {
            Reconfigure();
        }
    }

    public override string PropertyName() => "StaticEntityCollidersBehavior";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        Config = attributes.AsObject<CollidersConfig>();
    }

    public void Reconfigure()
    {
        if (Config == null) return;
        
        EntityCollidersOffthreadUpdater? cacheManager = entity.Api.ModLoader.GetModSystem<EntityCollidersOffthreadUpdater>();
        if (cacheManager == null || cacheManager.ProtoCollidersByShape == null)
        {
            ScheduleReconstructColliders(Config);
            return;
        }

        string shapePath = entity.Properties.Client.Shape.Base;
        if (!cacheManager.ProtoCollidersByShape.Get(shapePath, out ImmutableArray<ShapeElementProtoCollider> cachedProtoColliders))
        {
            ScheduleReconstructColliders(Config);
        }
        ProtoColliders = cachedProtoColliders;
        _configured = true;
    }



    private readonly CollierIdsManager _collierIdsManager = new();
    private long _lastReconstructionTask = 0;
    private bool _configured = false;


    private void ScheduleReconstructColliders(CollidersConfig config)
    {
        if (Animator == null) return;

        _lastReconstructionTask = OffThreadCollidersConstructor.ScheduleCollidersConstruction(_collierIdsManager, [config], Animator, ReceiveProtoColliders);

        _configured = true;
    }

    private void ReceiveProtoColliders(long taskId, ImmutableArray<ShapeElementProtoCollider> colliders)
    {
        if (_lastReconstructionTask > taskId) return;

        ProtoColliders = colliders;

        EntityCollidersOffthreadUpdater? cacheManager = entity.Api.ModLoader.GetModSystem<EntityCollidersOffthreadUpdater>();
        if (cacheManager != null && cacheManager.ProtoCollidersByShape != null)
        {
            string shapePath = entity.Properties.Client.Shape.Base;
            cacheManager.ProtoCollidersByShape.Add(shapePath, colliders);
        }
    }
}