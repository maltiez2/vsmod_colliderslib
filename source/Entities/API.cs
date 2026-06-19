using OpenTK.Mathematics;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CollidersLib;

public class CollidersConfig
{
    public Dictionary<string, string[]> Elements { get; set; } = [];
    public Dictionary<string, string> Colors { get; set; } = [];
}

public interface IEntityCollidersProvider
{
    CuboidAABBCollider BoundingBox { get; }
    bool HasOBBCollider { get; }
    ImmutableArray<ShapeElementInWorldCollider> Colliders { get; }
    ImmutableDictionary<int, string> ColliderTypeById { get; }
    ImmutableDictionary<string, Color4> ColorByType { get; }
    void Reconfigure();
}

public interface IEntityColliderReceiver
{
    CuboidAABBCollider BoundingBox { set; }
    ImmutableArray<ShapeElementInWorldCollider> Colliders { set; }
    ImmutableArray<ShapeElementProtoCollider> ProtoColliders { get; }
    ClientAnimator? Animator { get; }
    EntityShapeRenderer? Renderer { get; }
}

public interface IColliderConfigsProvider
{
    IEnumerable<CollidersConfig> Configs { get; }
}