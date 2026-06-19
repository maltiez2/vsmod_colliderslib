using OpenTK.Mathematics;
using System.Collections.Immutable;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CollidersLib;

public class CollidersConfig
{
    /// <summary>
    /// Collier type to shape element names
    /// </summary>
    public Dictionary<string, string[]> Elements { get; set; } = [];
    /// <summary>
    /// Collider type to collider color in hexidecimal
    /// </summary>
    public Dictionary<string, string> Colors { get; set; } = [];
}

public interface IEntityCollidersProvider
{
    CuboidAABBCollider BoundingBox { get; }
    bool HasOBBCollider { get; }
    ImmutableArray<ShapeElementInWorldCollider> Colliders { get; }
    ImmutableDictionary<int, string> ColliderTypeById { get; }
    ImmutableDictionary<string, Color4> ColorByType { get; }
}

public interface IEntityColliderReceiver
{
    CuboidAABBCollider BoundingBox { set; }
    ImmutableArray<ShapeElementInWorldCollider> Colliders { set; }
    ImmutableArray<ShapeElementProtoCollider> ProtoColliders { get; }
    ClientAnimator? Animator { get; }
    EntityShapeRenderer? Renderer { get; }
}

public interface ICollidersConfigProcessor
{
    void Reconfigure();
}

public interface IColliderConfigsProvider
{
    IEnumerable<CollidersConfig> Configs { get; }
}