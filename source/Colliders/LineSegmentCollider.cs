using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CollidersLib;

public readonly struct LineSegmentCollider
{
    public readonly Vector3d Position;
    public readonly Vector3d Direction;

    public LineSegmentCollider(Vector3d position, Vector3d direction)
    {
        Position = position;
        Direction = direction;
    }
    public LineSegmentCollider(JsonObject json)
    {
        Position = new(json["X1"].AsFloat(0), json["Y1"].AsFloat(0), json["Z1"].AsFloat(0));
        Direction = new(json["X2"].AsFloat(0), json["Y2"].AsFloat(0), json["Z2"].AsFloat(0));
        Direction -= Position;
    }
    public LineSegmentCollider(params float[] positions)
    {
        Position = new(positions[0], positions[1], positions[2]);
        Direction = new(positions[3], positions[4], positions[5]);
        Direction -= Position;
    }

    public LineSegmentCollider Transform(Matrixf modelMatrix, Vec3d origin)
    {
        Vector3d tail = TransformVector(Position, modelMatrix, origin);
        Vector3d head = TransformVector(Direction + Position, modelMatrix, origin);

        return new LineSegmentCollider(tail, head - tail);
    }
    public static Vector3d TransformVector(Vector3d value, Matrixf modelMatrix, Vec3d playerPos)
    {
        _inputBufferD.X = value.X;
        _inputBufferD.Y = value.Y;
        _inputBufferD.Z = value.Z;

        Mat4f.MulWithVec4(modelMatrix.Values, _inputBufferD, _outputBufferD);

        _outputBufferD.X += playerPos.X;
        _outputBufferD.Y += playerPos.Y;
        _outputBufferD.Z += playerPos.Z;

        return new(_outputBufferD.X, _outputBufferD.Y, _outputBufferD.Z);
    }

    private static readonly Vec4d _inputBufferD = new(0, 0, 0, 1);
    private static readonly Vec4d _outputBufferD = new(0, 0, 0, 1);
}
