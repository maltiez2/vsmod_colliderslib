using OpenTK.Mathematics;

namespace CollidersLib;

public class EntitySphereCollider
{
    public Vector3d Position { get; set; }
    public Vector3d PreviousPosition { get; set; }
    public float Radius { get; set; }
}
