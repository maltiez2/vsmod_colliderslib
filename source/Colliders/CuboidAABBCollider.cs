using OpenTK.Mathematics;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CollidersLib;

public readonly struct CuboidAABBCollider
{
    public readonly Vector3d Min;
    public readonly Vector3d Max;

    public Vector3d Center => (Min + Max) / 2;

    public CuboidAABBCollider(Vector3d vertexA, Vector3d vertexB)
    {
        Min = new(Math.Min(vertexA.X, vertexB.X), Math.Min(vertexA.Y, vertexB.Y), Math.Min(vertexA.Z, vertexB.Z));
        Max = new(Math.Max(vertexA.X, vertexB.X), Math.Max(vertexA.Y, vertexB.Y), Math.Max(vertexA.Z, vertexB.Z));
    }
    public CuboidAABBCollider(Cuboidf cuboid)
    {
        Min = new(Math.Min(cuboid.X1, cuboid.X2), Math.Min(cuboid.Y1, cuboid.Y2), Math.Min(cuboid.Z1, cuboid.Z2));
        Max = new(Math.Max(cuboid.X1, cuboid.X2), Math.Max(cuboid.Y1, cuboid.Y2), Math.Max(cuboid.Z1, cuboid.Z2));
    }
    public CuboidAABBCollider(Entity entity)
    {
        Cuboidf collisionBox = entity.CollisionBox;
        EntityPos position = entity.Pos;

        Min = new(
            Math.Min(collisionBox.X1 + (float)position.X, collisionBox.X2 + (float)position.X),
            Math.Min(collisionBox.Y1 + (float)position.Y, collisionBox.Y2 + (float)position.Y),
            Math.Min(collisionBox.Z1 + (float)position.Z, collisionBox.Z2 + (float)position.Z));
        Max = new(
            Math.Max(collisionBox.X1 + (float)position.X, collisionBox.X2 + (float)position.X),
            Math.Max(collisionBox.Y1 + (float)position.Y, collisionBox.Y2 + (float)position.Y),
            Math.Max(collisionBox.Z1 + (float)position.Z, collisionBox.Z2 + (float)position.Z));
    }


    public bool IntersectShpere(Vector3d origin, double radius, out Vector3d intersection)
    {
        intersection = new(
            Math.Clamp(origin.X, Math.Min(Min.X, Max.X), Math.Max(Min.X, Max.X)),
            Math.Clamp(origin.Y, Math.Min(Min.Y, Max.Y), Math.Max(Min.Y, Max.Y)),
            Math.Clamp(origin.Z, Math.Min(Min.Z, Max.Z), Math.Max(Min.Z, Max.Z))
        );

        double distanceSquared = Vector3d.DistanceSquared(origin, intersection);

        return distanceSquared <= radius * radius;
    }
    public bool IntersectCapsule(Vector3d head, Vector3d tail, double radius, out Vector3d intersection)
    {
        if ((head - tail).LengthSquared < radius * radius / 4)
        {
            bool collided = IntersectShpere(head, radius, out _);

            intersection = head;

            return collided;
        }

        bool intersects = SegmentIntersectsAABB(tail, head, Min, Max, out Vector3d intersectionStart, out Vector3d intersectionEnd, out Vector3d closestPointOnSegment, out Vector3d closestPointOnBox);

        if (intersects)
        {
            intersection = intersectionStart - (intersectionEnd - intersectionStart).Normalized() * radius;
            return true;
        }
        else
        {
            intersection = closestPointOnSegment;
        }

        double distanceSquared = Vector3d.DistanceSquared(intersection, closestPointOnBox);

        return distanceSquared <= radius * radius;
    }

    public BlockFacing GetFacing(Vector3d direction, out Vector3d normal)
    {
        normal = GetIntersectingFaceNormal(Min, Max, direction);

        return BlockFacing.FromNormal(new Vec3f((float)normal.X, (float)normal.Y, (float)normal.Z));
    }



    private static Vector3d GetIntersectingFaceNormal(Vector3d min, Vector3d max, Vector3d dir)
    {
        // Normalize direction (optional — only needed for consistent t values)
        dir.Normalize();

        Vector3d center = (min + max) * 0.5;
        Vector3d halfExtents = (max - min) * 0.5;

        // Avoid division by zero — use double.MaxValue to represent no intersection in that axis
        double tx = dir.X != 0 ? (Math.Sign(dir.X) * halfExtents.X) / dir.X : double.MaxValue;
        double ty = dir.Y != 0 ? (Math.Sign(dir.Y) * halfExtents.Y) / dir.Y : double.MaxValue;
        double tz = dir.Z != 0 ? (Math.Sign(dir.Z) * halfExtents.Z) / dir.Z : double.MaxValue;

        // Pick smallest positive t
        double t = double.MaxValue;
        Vector3d normal = Vector3d.Zero;

        if (tx > 0 && tx < t) { t = tx; normal = new Vector3d(Math.Sign(dir.X), 0, 0); }
        if (ty > 0 && ty < t) { t = ty; normal = new Vector3d(0, Math.Sign(dir.Y), 0); }
        if (tz > 0 && tz < t) { t = tz; normal = new Vector3d(0, 0, Math.Sign(dir.Z)); }

        return normal;
    }

    private static bool SegmentIntersectsAABB(
        Vector3d p1,
        Vector3d p2,
        Vector3d boxMin,
        Vector3d boxMax,
        out Vector3d intersectionStart,
        out Vector3d intersectionEnd,
        out Vector3d closestPointOnSegment,
        out Vector3d closestPointOnBox)
    {
        intersectionStart = Vector3d.Zero;
        intersectionEnd = Vector3d.Zero;
        closestPointOnSegment = Vector3d.Zero;
        closestPointOnBox = Vector3d.Zero;

        Vector3d dir = p2 - p1;
        Vector3d invDir = new(
            1.0 / (dir.X != 0.0 ? dir.X : double.Epsilon),
            1.0 / (dir.Y != 0.0 ? dir.Y : double.Epsilon),
            1.0 / (dir.Z != 0.0 ? dir.Z : double.Epsilon)
        );

        double tmin = (boxMin.X - p1.X) * invDir.X;
        double tmax = (boxMax.X - p1.X) * invDir.X;
        if (tmin > tmax) Swap(ref tmin, ref tmax);

        double tymin = (boxMin.Y - p1.Y) * invDir.Y;
        double tymax = (boxMax.Y - p1.Y) * invDir.Y;
        if (tymin > tymax) Swap(ref tymin, ref tymax);

        if (tmin > tymax || tymin > tmax)
            return ComputeClosestPoints(out closestPointOnSegment, out closestPointOnBox);

        if (tymin > tmin) tmin = tymin;
        if (tymax < tmax) tmax = tymax;

        double tzmin = (boxMin.Z - p1.Z) * invDir.Z;
        double tzmax = (boxMax.Z - p1.Z) * invDir.Z;
        if (tzmin > tzmax) Swap(ref tzmin, ref tzmax);

        if (tmin > tzmax || tzmin > tmax)
            return ComputeClosestPoints(out closestPointOnSegment, out closestPointOnBox);

        if (tzmin > tmin) tmin = tzmin;
        if (tzmax < tmax) tmax = tzmax;

        if (tmax < 0.0 || tmin > 1.0)
            return ComputeClosestPoints(out closestPointOnSegment, out closestPointOnBox);

        double tEnter = Math.Max(0.0, tmin);
        double tExit = Math.Min(1.0, tmax);

        intersectionStart = p1 + dir * tEnter;
        intersectionEnd = p1 + dir * tExit;

        ComputeClosestPoints(out closestPointOnSegment, out closestPointOnBox);

        return true;

        // Closest point fallback
        bool ComputeClosestPoints(out Vector3d closestPointOnSegment, out Vector3d closestPointOnBox)
        {
            // Clamp each coordinate of the segment's closest point to the box
            Vector3d segClosest = ClosestPointOnSegmentToPoint(p1, p2, ClampPointToAABB((p1 + p2) * 0.5, boxMin, boxMax));
            Vector3d boxClosest = ClampPointToAABB(segClosest, boxMin, boxMax);

            closestPointOnSegment = segClosest;
            closestPointOnBox = boxClosest;
            return false;
        }
    }

    private static Vector3d ClosestPointOnSegmentToPoint(Vector3d a, Vector3d b, Vector3d point)
    {
        Vector3d ab = b - a;
        double t = Vector3d.Dot(point - a, ab) / ab.LengthSquared;

        t = Math.Clamp(t, 0.0, 1.0);
        return a + t * ab;
    }

    private static Vector3d ClampPointToAABB(Vector3d point, Vector3d min, Vector3d max)
    {
        return new Vector3d(
            Math.Clamp(point.X, min.X, max.X),
            Math.Clamp(point.Y, min.Y, max.Y),
            Math.Clamp(point.Z, min.Z, max.Z)
        );
    }

    private static void Swap(ref double a, ref double b) => (b, a) = (a, b);
}
