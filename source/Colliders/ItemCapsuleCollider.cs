using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CollidersLib;

public class ItemCapsuleColliderJson
{
    public float[] Position { get; set; } = [];
    public float[] Direction { get; set; } = [];
    public float Radius { get; set; }
}

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

    public static ItemCapsuleCollider FromJson(JsonObject json)
    {
        ItemCapsuleColliderJson? jsonValues = json.AsObject<ItemCapsuleColliderJson>();
        if (jsonValues == null || jsonValues.Position.Length != 3 || jsonValues.Direction.Length != 3)
        {
            throw new InvalidDataException($"Failed to parse 'ItemCapsuleCollider' from json: {json.ToString()}");
        }

        Vector3 position = new(jsonValues.Position[0], jsonValues.Position[1], jsonValues.Position[2]);
        Vector3 direction = new(jsonValues.Direction[0], jsonValues.Direction[1], jsonValues.Direction[2]);
        return new(position, direction, jsonValues.Radius);
    }
}
