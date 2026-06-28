using OpenTK.Mathematics;
using System.Runtime.CompilerServices;
using Vintagestory.API.MathTools;

namespace CollidersLib;

public readonly struct ShapeElementInWorldCollider
{
    public const int VertexCount = 8;

    public readonly Vector3d Center;
    public readonly Vector3 HalfExtentX;
    public readonly Vector3 HalfExtentY;
    public readonly Vector3 HalfExtentZ;

    public readonly short JointId;
    public readonly short ColliderId;

    public Vector3d this[int index] => index switch
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

    public ShapeElementInWorldCollider(ref readonly ShapeElementProtoCollider proto, float[] transformMatrixAll, float[] modelMatrix, Vector3d cameraPos)
    {
        JointId = proto.JointId;
        ColliderId = proto.ColliderId;

        double[] transformMatrix = GetTransformMatrix(proto.JointId, transformMatrixAll);

        Center = TransformPosition(proto.Center, transformMatrix, modelMatrix, cameraPos);
        HalfExtentX = TransformDirection(proto.HalfExtentX, transformMatrix, modelMatrix);
        HalfExtentY = TransformDirection(proto.HalfExtentY, transformMatrix, modelMatrix);
        HalfExtentZ = TransformDirection(proto.HalfExtentZ, transformMatrix, modelMatrix);
    }

    public bool Collide(Vector3d head, Vector3d tail, double radius, out double distance, out Vector3d intersection, out Vector3d segmentClosestPoint)
    {
        double halfSizeX = HalfExtentX.Length;
        double halfSizeY = HalfExtentY.Length;
        double halfSizeZ = HalfExtentZ.Length;

        Vector3d normalX = halfSizeX > 1e-10 ? (Vector3d)HalfExtentX / halfSizeX : Vector3d.UnitX;
        Vector3d normalY = halfSizeY > 1e-10 ? (Vector3d)HalfExtentY / halfSizeY : Vector3d.UnitY;
        Vector3d normalZ = halfSizeZ > 1e-10 ? (Vector3d)HalfExtentZ / halfSizeZ : Vector3d.UnitZ;

        segmentClosestPoint = ClosestPointOnSegment(head, tail, Center);
        Vector3d direction = segmentClosestPoint - Center;

        intersection = Center;
        intersection += Math.Clamp(Vector3d.Dot(direction, normalX), -halfSizeX, halfSizeX) * normalX;
        intersection += Math.Clamp(Vector3d.Dot(direction, normalY), -halfSizeY, halfSizeY) * normalY;
        intersection += Math.Clamp(Vector3d.Dot(direction, normalZ), -halfSizeZ, halfSizeZ) * normalZ;

        distance = Vector3d.Distance(intersection, segmentClosestPoint);
        return distance <= radius;
    }

    public Vector3d GetSurfaceNormal(Vector3d worldPoint)
    {
        Vector3d localPoint = worldPoint - Center;

        double halfSizeX = HalfExtentX.Length;
        double halfSizeY = HalfExtentY.Length;
        double halfSizeZ = HalfExtentZ.Length;

        Vector3d axisX = halfSizeX > 1e-10 ? (Vector3d)HalfExtentX / halfSizeX : Vector3d.UnitX;
        Vector3d axisY = halfSizeY > 1e-10 ? (Vector3d)HalfExtentY / halfSizeY : Vector3d.UnitY;
        Vector3d axisZ = halfSizeZ > 1e-10 ? (Vector3d)HalfExtentZ / halfSizeZ : Vector3d.UnitZ;

        double projectionX = Vector3d.Dot(localPoint, axisX);
        double projectionXY = Vector3d.Dot(localPoint, axisY);
        double projectionXZ = Vector3d.Dot(localPoint, axisZ);

        // positive is outside that face
        double distPosX = halfSizeX - projectionX;
        double distNegX = halfSizeX + projectionX;
        double distPosY = halfSizeY - projectionXY;
        double distNegY = halfSizeY + projectionXY;
        double distPosZ = halfSizeZ - projectionXZ;
        double distNegZ = halfSizeZ + projectionXZ;

        double minDist = distPosX;
        Vector3d normal = axisX;

        if (distNegX < minDist) { minDist = distNegX; normal = -axisX; }
        if (distPosY < minDist) { minDist = distPosY; normal = axisY; }
        if (distNegY < minDist) { minDist = distNegY; normal = -axisY; }
        if (distPosZ < minDist) { minDist = distPosZ; normal = axisZ; }
        if (distNegZ < minDist) { normal = -axisZ; }

        return normal;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3d TransformPosition(Vector3 v, double[] jointMatrix, float[] modelMat, Vector3d cameraPos)
    {
        Vector4d v4 = new(v.X, v.Y, v.Z, 1.0);
        Vector4d step1 = MultiplyVectorByMatrix(jointMatrix, v4);
        step1.W = 1.0;
        Vector4d step2 = MultiplyVectorByMatrix(modelMat, step1);
        return new Vector3d(
            step2.X + cameraPos.X,
            step2.Y + cameraPos.Y,
            step2.Z + cameraPos.Z
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 TransformDirection(Vector3 v, double[] jointMatrix, float[] modelMat)
    {
        Vector4d v4 = new(v.X, v.Y, v.Z, 0.0);
        Vector4d step1 = MultiplyVectorByMatrix(jointMatrix, v4);
        step1.W = 0.0;
        Vector4d step2 = MultiplyVectorByMatrix(modelMat, step1);
        return new Vector3((float)step2.X, (float)step2.Y, (float)step2.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4d MultiplyVectorByMatrix(double[] matrix, Vector4d vector)
    {
        return new Vector4d(
            matrix[0] * vector.X + matrix[4] * vector.Y + matrix[8] * vector.Z + matrix[12] * vector.W,
            matrix[1] * vector.X + matrix[5] * vector.Y + matrix[9] * vector.Z + matrix[13] * vector.W,
            matrix[2] * vector.X + matrix[6] * vector.Y + matrix[10] * vector.Z + matrix[14] * vector.W,
            matrix[3] * vector.X + matrix[7] * vector.Y + matrix[11] * vector.Z + matrix[15] * vector.W
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector4d MultiplyVectorByMatrix(float[] matrix, Vector4d vector)
    {
        return new Vector4d(
            matrix[0] * vector.X + matrix[4] * vector.Y + matrix[8] * vector.Z + matrix[12] * vector.W,
            matrix[1] * vector.X + matrix[5] * vector.Y + matrix[9] * vector.Z + matrix[13] * vector.W,
            matrix[2] * vector.X + matrix[6] * vector.Y + matrix[10] * vector.Z + matrix[14] * vector.W,
            matrix[3] * vector.X + matrix[7] * vector.Y + matrix[11] * vector.Z + matrix[15] * vector.W
        );
    }

    private static double[] GetTransformMatrix(int jointId, float[] transformationMatrices)
    {
        double[] result = new double[16];
        Mat4d.Identity(result);

        int baseIndex = 16 * jointId;
        if (baseIndex + 16 > transformationMatrices.Length)
        {
            return result;
        }

        for (int i = 0; i < 16; i++)
        {
            result[i] = transformationMatrices[baseIndex + i];
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3d ClosestPointOnSegment(Vector3d a, Vector3d b, Vector3d p)
    {
        Vector3d ab = b - a;
        double t = Vector3d.Dot(p - a, ab) / Vector3d.Dot(ab, ab);
        t = Math.Clamp(t, 0.0, 1.0);
        return a + t * ab;
    }
}