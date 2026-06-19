using OpenTK.Mathematics;
using OverhaulLib.Utils;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CollidersLib;

public readonly struct ShapeElementProtoCollider
{
    public const int VertexCount = 8;

    public readonly Vector3 Center;
    public readonly Vector3 HalfExtentX;
    public readonly Vector3 HalfExtentY;
    public readonly Vector3 HalfExtentZ;

    public readonly short JointId;
    public readonly short ColliderId;

    public Vector3 this[int index] => index switch
    {
        0 => Center - HalfExtentX - HalfExtentY - HalfExtentZ,
        1 => Center + HalfExtentX - HalfExtentY - HalfExtentZ,
        2 => Center + HalfExtentX + HalfExtentY - HalfExtentZ,
        3 => Center - HalfExtentX + HalfExtentY - HalfExtentZ,
        4 => Center - HalfExtentX - HalfExtentY + HalfExtentZ,
        5 => Center + HalfExtentX - HalfExtentY + HalfExtentZ,
        6 => Center + HalfExtentX + HalfExtentY + HalfExtentZ,
        7 => Center - HalfExtentX + HalfExtentY + HalfExtentZ,
        _ => throw new IndexOutOfRangeException($"Vertex index {index} out of range")
    };

    public ShapeElementProtoCollider(short jointId, short colliderId, ShapeElement element)
    {
        JointId = jointId;
        ColliderId = colliderId;

        if (element.From == null || element.To == null)
            throw new ArgumentException($"Invalid shape element '{element.Name}'", nameof(element));

        if (element.RotationOrigin == null)
            element.RotationOrigin = [0, 0, 0];

        Vector4d from = new(element.From[0], element.From[1], element.From[2], 1);
        Vector4d to = new(element.To[0], element.To[1], element.To[2], 1);
        Vector4d diagonal = to - from;

        double[] elementMatrixValues = new double[16];
        Mat4d.Identity(elementMatrixValues);
        Matrixd elementMatrix = new(elementMatrixValues);

        if (element.ParentElement != null)
            GetElementTransformMatrix(elementMatrix, element.ParentElement);

        elementMatrix
            .Translate(element.RotationOrigin[0],
                       element.RotationOrigin[1],
                       element.RotationOrigin[2])
            .RotateX((float)element.RotationX * GameMath.DEG2RAD)
            .RotateY((float)element.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
            .Translate(-element.RotationOrigin[0],
                       -element.RotationOrigin[1],
                       -element.RotationOrigin[2]);

        double[] matrix = elementMatrix.Values;

        Vector4d localCenter = new(
            from.X + diagonal.X * 0.5,
            from.Y + diagonal.Y * 0.5,
            from.Z + diagonal.Z * 0.5,
            1.0);

        Center = (Vector3)ToVector3d(TransformPosition(matrix, localCenter));
        HalfExtentX = (Vector3)ToVector3d(TransformDirection(matrix, new(diagonal.X * 0.5, 0, 0, 0)));
        HalfExtentY = (Vector3)ToVector3d(TransformDirection(matrix, new(0, diagonal.Y * 0.5, 0, 0)));
        HalfExtentZ = (Vector3)ToVector3d(TransformDirection(matrix, new(0, 0, diagonal.Z * 0.5, 0)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3d ToVector3d(Vector4d v) => new(v.X, v.Y, v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4d TransformPosition(double[] transform, Vector4d position)
    {
        position /= 16.0;
        return new Vector4d(
            transform[0] * position.X + transform[4] * position.Y + transform[8] * position.Z + transform[12] * position.W,
            transform[1] * position.X + transform[5] * position.Y + transform[9] * position.Z + transform[13] * position.W,
            transform[2] * position.X + transform[6] * position.Y + transform[10] * position.Z + transform[14] * position.W,
            1.0
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4d TransformDirection(double[] transform, Vector4d direction)
    {
        direction /= 16.0;
        return new Vector4d(
            transform[0] * direction.X + transform[4] * direction.Y + transform[8] * direction.Z,
            transform[1] * direction.X + transform[5] * direction.Y + transform[9] * direction.Z,
            transform[2] * direction.X + transform[6] * direction.Y + transform[10] * direction.Z,
            0.0
        );
    }

    private static void GetElementTransformMatrix(Matrixd transform, ShapeElement element)
    {
        Debug.Assert(element.RotationOrigin != null);
        Debug.Assert(element.From != null);

        if (element.ParentElement != null)
            GetElementTransformMatrix(transform, element.ParentElement);

        transform
            .Translate(element.RotationOrigin[0],
                       element.RotationOrigin[1],
                       element.RotationOrigin[2])
            .RotateX((float)element.RotationX * GameMath.DEG2RAD)
            .RotateY((float)element.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
            .Translate(-element.RotationOrigin[0],
                       -element.RotationOrigin[1],
                       -element.RotationOrigin[2])
            .Translate(element.From[0], element.From[1], element.From[2]);
    }
}
