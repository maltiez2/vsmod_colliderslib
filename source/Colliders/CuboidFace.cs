using OpenTK.Mathematics;

namespace CollidersLib;

public readonly struct CuboidFace
{
    public readonly Vector3d VertexA;
    public readonly Vector3d VertexB;
    public readonly Vector3d VertexC;
    public readonly Vector3d VertexD;

    public CuboidFace(Vector4d vertexA, Vector4d vertexB, Vector4d vertexC, Vector4d vertexD)
    {
        VertexA = new(vertexA.X, vertexA.Y, vertexA.Z);
        VertexB = new(vertexB.X, vertexB.Y, vertexB.Z);
        VertexC = new(vertexC.X, vertexC.Y, vertexC.Z);
        VertexD = new(vertexD.X, vertexD.Y, vertexD.Z);
    }

    private double IntersectPlaneWithLine(Vector3d start, Vector3d direction, Vector3d normal)
    {
        double startProjection = Vector3d.Dot(normal, start);
        double directionProjection = Vector3d.Dot(normal, start + direction);
        double planeProjection = Vector3d.Dot(normal, VertexA);

        return (planeProjection - startProjection) / (directionProjection - startProjection);
    }

    public bool Collide(Vector3d segmentStart, Vector3d segmentDirection, out double parameter, out Vector3d intersection)
    {
        Vector3d normal = Vector3d.Cross(VertexB - VertexA, VertexC - VertexA);

        #region Check if segment is parallel to the plane defined by the face
        double denominator = Vector3d.Dot(normal, segmentDirection);
        if (Math.Abs(denominator) < 0.0001f)
        {
            parameter = -1;
            intersection = Vector3d.Zero;
            return false;
        }
        #endregion

        #region Compute intersection point with the plane defined by the face and check if segment intersects the plane
        parameter = IntersectPlaneWithLine(segmentStart, segmentDirection, normal);
        if (parameter < 0 || parameter > 1)
        {
            intersection = Vector3d.Zero;
            return false;
        }
        #endregion

        intersection = segmentStart + parameter * segmentDirection;

        #region Check if the intersection point is within the face boundaries
        Vector3d edge0 = VertexB - VertexA;
        Vector3d vp0 = intersection - VertexA;
        if (Vector3d.Dot(normal, Vector3d.Cross(edge0, vp0)) < 0)
        {
            return false;
        }

        Vector3d edge1 = VertexC - VertexB;
        Vector3d vp1 = intersection - VertexB;
        if (Vector3d.Dot(normal, Vector3d.Cross(edge1, vp1)) < 0)
        {
            return false;
        }

        Vector3d edge2 = VertexD - VertexC;
        Vector3d vp2 = intersection - VertexC;
        if (Vector3d.Dot(normal, Vector3d.Cross(edge2, vp2)) < 0)
        {
            return false;
        }

        Vector3d edge3 = VertexA - VertexD;
        Vector3d vp3 = intersection - VertexD;
        if (Vector3d.Dot(normal, Vector3d.Cross(edge3, vp3)) < 0)
        {
            return false;
        }
        #endregion

        return true;
    }
}