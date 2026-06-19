using OpenTK.Mathematics;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CollidersLib;


public sealed class CollidersEntityBehavior(Entity entity) : EntityBehavior(entity), IEntityCollidersProvider, IEntityColliderReceiver, ICollidersConfigProcessor
{
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
    }

    public override string PropertyName() => "CollidersEntityBehavior";

    public void Reconfigure()
    {
        CollectConfigs(out IEnumerable<CollidersConfig> configs);
        ScheduleReconstructColliders(configs);
    }



    private readonly CollierIdsManager _collierIdsManager = new();
    private long _lastReconstructionTask = 0;


    private void CollectConfigs(out IEnumerable<CollidersConfig> configs)
    {
        configs = entity.SidedProperties.Behaviors.OfType<IColliderConfigsProvider>().SelectMany(provider => provider.Configs);
    }

    private void ScheduleReconstructColliders(IEnumerable<CollidersConfig> configs)
    {
        if (Animator == null) return;

        _lastReconstructionTask = OffThreadCollidersConstructor.ScheduleCollidersConstruction(_collierIdsManager, configs, Animator, ReceiveProtoColliders);
    }

    private void ReceiveProtoColliders(long taskId, ImmutableArray<ShapeElementProtoCollider> colliders)
    {
        if (_lastReconstructionTask > taskId) return;

        ProtoColliders = colliders;
    }
}