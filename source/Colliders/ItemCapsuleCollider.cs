using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

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
}
