using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CollidersLib;

public class ItemCapsuleCollider
{
    public LineSegmentCollider RelativeCollider { get; set; }
    public LineSegmentCollider InWorldCollider { get; set; }
    public LineSegmentCollider PreviousInWorldCollider { get; set; }
    public float Radius { get; set; }


    public ItemCapsuleCollider(Vector3 position, Vector3 direction, float radius)
    {
        RelativeCollider = new LineSegmentCollider(position, direction);
        InWorldCollider = RelativeCollider;
        PreviousInWorldCollider = RelativeCollider;
        Radius = radius;
    }

    public bool TransformCollider(EntityPlayer player, bool mainHand = true, bool resetPreviousCollider = false)
    {
        Matrixf? modelMatrix = player.GetBehavior<CollidersTranformBehavior>()?.GetHeldItemModelMatrix(mainHand);

        if (modelMatrix == null)
        {
            return false;
        }

        PreviousInWorldCollider = InWorldCollider;
        InWorldCollider = RelativeCollider.Transform(modelMatrix, player.Pos);

        if (resetPreviousCollider)
        {
            PreviousInWorldCollider = InWorldCollider;
        }

        return true;
    }

    public bool CollideWithEntity(Entity target, out ShapeElementCollider? collider, out Vector3d collisionPoint, out double colliderPosition)
    {
        colliderPosition = 1f;
        collider = null;
        collisionPoint = Vector3.Zero;
        
        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        if (colliders != null)
        {
            bool intersects = colliders.Collide(this, out collider, out colliderPosition, out collisionPoint);
            return intersects;
        }

        CuboidAABBCollider collisionBox = new(target);
        if (!InWorldCollider.RoughIntersect(collisionBox))
        {
            return false;
        }

        Vector3d? point = InWorldCollider.IntersectCuboid(collisionBox, out colliderPosition);
        if (point == null)
        {
            return false;
        }
        collisionPoint = point.Value;

        return true;
    }

    public bool CollideWithTerrain(ICoreClientAPI api, out Block? block, out Vector3d collisionPoint, out double colliderPosition)
    {
        (Block block, Vector3d position, double parameter)? result = InWorldCollider.IntersectTerrain(api);

        if (result != null)
        {
            block = result.Value.block;
            collisionPoint = result.Value.position;
            colliderPosition = result.Value.parameter;
            return true;
        }
        else
        {
            block = null;
            collisionPoint = Vector3d.Zero;
            colliderPosition = 1f;
            return false;
        }
    }
}
