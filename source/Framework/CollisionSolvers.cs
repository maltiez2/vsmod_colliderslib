using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CollidersLib;

public readonly struct EntityWithCapsuleIntersectionData
{
    public readonly int EntityColliderId;
    public readonly Vector3d IntersectionPoint;
    /// <summary>
    /// [strike, cut, slip]
    /// </summary>
    public readonly Vector3d ImpactVelocity;
    public readonly double DistanceFromTail;
    public readonly int Subdivision;
    public readonly int TotalSubdivisions;

    public EntityWithCapsuleIntersectionData(int entityColliderId, Vector3d intersectionPoint, Vector3d impactVelocity, double distanceFromTail, int subdivision, int totalSubdivisions)
    {
        EntityColliderId = entityColliderId;
        IntersectionPoint = intersectionPoint;
        ImpactVelocity = impactVelocity;
        DistanceFromTail = distanceFromTail;
        Subdivision = subdivision;
        TotalSubdivisions = totalSubdivisions;
    }
}

public readonly struct EntityWithSphereIntersectionData
{
    public readonly int EntityColliderId;
    public readonly Vector3d IntersectionPoint;
    public readonly Vector3d ImpactVelocity;
    public readonly double PositionInTime;

    public EntityWithSphereIntersectionData(int entityColliderId, Vector3d intersectionPoint, Vector3d impactVelocity, double positionInTime)
    {
        EntityColliderId = entityColliderId;
        IntersectionPoint = intersectionPoint;
        PositionInTime = positionInTime;
    }
}

public readonly struct TerrainWithCapsuleIntersectionData
{
    public readonly Block Block;
    public readonly BlockFacing Facing;
    public readonly Vector3d Normal;
    public readonly Vector3d IntersectionPoint;
    public readonly Vector3i BlockPosition;
    public readonly double DistanceFromTail;
    public readonly int Subdivision;
    public readonly int TotalSubdivisions;

    public TerrainWithCapsuleIntersectionData(Block block, BlockFacing facing, Vector3d normal, Vector3d intersectionPoint, Vector3i blockPosition, double distanceFromTail, int subdivision, int totalSubdivisions)
    {
        Block = block;
        Facing = facing;
        Normal = normal;
        IntersectionPoint = intersectionPoint;
        BlockPosition = blockPosition;
        DistanceFromTail = distanceFromTail;
        Subdivision = subdivision;
        TotalSubdivisions = totalSubdivisions;
    }
}

public readonly struct TerrainWithShpereIntersectionData
{
    public readonly Block Block;
    public readonly BlockFacing Facing;
    public readonly Vector3d Normal;
    public readonly Vector3d IntersectionPoint;
    public readonly Vector3i BlockPosition;
    public readonly double PositionInTime;

    public TerrainWithShpereIntersectionData(Block block, BlockFacing facing, Vector3d normal, Vector3d intersectionPoint, Vector3i blockPosition, double positionInTime)
    {
        Block = block;
        Facing = facing;
        Normal = normal;
        IntersectionPoint = intersectionPoint;
        BlockPosition = blockPosition;
        PositionInTime = positionInTime;
    }
}

public static class CollisionSolvers
{
    public static bool CollideWithEntity(this ItemCapsuleCollider collider, Entity target, IEntityCollidersProvider? entityColliders, out List<EntityWithCapsuleIntersectionData> intersections, TimeSpan deltaTime)
    {
        Vector3d previousTickDirection = collider.PreviousInWorldCollider.Direction;
        Vector3d previousTickStart = collider.PreviousInWorldCollider.Position;
        Vector3d thisTickStart = collider.InWorldCollider.Position;
        Vector3d thisTickDirection = collider.InWorldCollider.Direction;
        Vector3d startHead = previousTickStart;
        Vector3d startTail = previousTickStart + previousTickDirection;
        Vector3d directionHead = thisTickStart - startHead;
        Vector3d directionTail = thisTickStart + thisTickDirection - startTail;
        Vector3d headVelocity = (thisTickStart - previousTickStart) / deltaTime.TotalSeconds;
        Vector3d tailVelocity = ((thisTickStart + thisTickDirection) - (previousTickStart + previousTickDirection)) + previousTickDirection;
        float radius = collider.Radius;
        intersections = [];

        int subdivisions = (int)Math.Ceiling(Math.Max((thisTickStart - previousTickStart).Length, (thisTickStart + thisTickDirection - previousTickStart - previousTickDirection).Length) / radius);

        if (subdivisions == 0)
        {
            /*for (int i = 0; i < 11; i++)
            {
                var pos = (thisTickStart + i * 0.1f * thisTickDirection).ToVanillaRef();
                target.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 125, 125, 255), pos, pos, new(), new(), 1, 0, 1, EnumParticleModel.Cube);
            }*/
            CapsuleCollideWithEntity(target, entityColliders, thisTickStart + thisTickDirection, thisTickStart, radius, intersections, 0, 1, headVelocity, tailVelocity);
            return intersections.Count != 0;
        }

        for (int subdivision = 0; subdivision < subdivisions; subdivision++)
        {
            float subdivisionParameter = subdivision / (float)subdivisions;
            Vector3d head = startHead + directionHead * subdivisionParameter;
            Vector3d tail = startTail + directionTail * subdivisionParameter;

            /*for (int i = 0; i < 11; i++)
            {
                var pos = (head + i * 0.1f * (tail - head)).ToVanillaRef();
                target.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(255, 125, 125, 255), pos, pos, new(), new(), 1, 0, 1, EnumParticleModel.Cube);
            }*/

            CapsuleCollideWithEntity(target, entityColliders, head, tail, radius, intersections, subdivision, subdivisions, headVelocity, tailVelocity);
        }

        return intersections.Count != 0;
    }
    public static bool CollideWithEntity(this EntitySphereCollider collider, Entity target, IEntityCollidersProvider? entityColliders, out List<EntityWithSphereIntersectionData> intersections, TimeSpan deltaTime)
    {
        intersections = [];

        SphereCollideWithEntity(target, entityColliders, collider.Position, collider.PreviousPosition, collider.Radius, intersections, deltaTime);

        return intersections.Count != 0;
    }
    private static void CapsuleCollideWithEntity(Entity target, IEntityCollidersProvider? collider, Vector3d head, Vector3d tail, float radius, List<EntityWithCapsuleIntersectionData> intersections, int subdivision, int totalSubdivisions, Vector3d headVelocity, Vector3d tailVelocity)
    {
        if (collider == null || !collider.HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(target);
            if (AABBCollider.IntersectCapsule(head, tail, radius, out Vector3d intersection))
            {
                AABBCollider.GetFacing(-headVelocity, out Vector3d surfaceNormal);
                double positionOnCollider = (intersection - tail).Length / (head - tail).Length;
                Vector3d relativeVelocity = headVelocity * positionOnCollider + tailVelocity * (1 - positionOnCollider);
                Vector3d impactVelocity = GetStrikeCutSlip(relativeVelocity, surfaceNormal, head - tail);

                intersections.Add(new(-1, intersection, impactVelocity, (intersection - tail).Length, subdivision, totalSubdivisions));
            }

            return;
        }

        if (!collider.BoundingBox.IntersectCapsule(head, tail, radius, out _))
        {
            return;
        }

        foreach (ShapeElementInWorldCollider shapeElementCollider in collider.Colliders)
        {
            if (shapeElementCollider.Collide(head, tail, radius, out _, out Vector3d intersection, out _))
            {
                Vector3d surfaceNormal = shapeElementCollider.GetSurfaceNormal(intersection);
                double positionOnCollider = (intersection - tail).Length / (head - tail).Length;
                Vector3d relativeVelocity = headVelocity * positionOnCollider + tailVelocity * (1 - positionOnCollider);
                Vector3d impactVelocity = GetStrikeCutSlip(relativeVelocity, surfaceNormal, head - tail);

                intersections.Add(new(shapeElementCollider.ColliderId, intersection, impactVelocity, (intersection - tail).Length, subdivision, totalSubdivisions));
            }
        }
    }
    private static void SphereCollideWithEntity(Entity target, IEntityCollidersProvider? collider, Vector3d head, Vector3d tail, float radius, List<EntityWithSphereIntersectionData> intersections, TimeSpan deltaTime)
    {
        Vector3d realtiveVelocity = (head - tail) / deltaTime.TotalSeconds;

        if (collider == null || !collider.HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(target);
            if (AABBCollider.IntersectCapsule(head, tail, radius, out Vector3d intersection))
            {
                Vector3d segmentPoint = intersection - tail;
                double parameter = GameMath.Clamp(segmentPoint.Length / (head - tail).Length, 0, 1);

                AABBCollider.GetFacing(-realtiveVelocity, out Vector3d surfaceNormal);
                Vector3d impactVelocity = GetStrikeCutSlip(realtiveVelocity, surfaceNormal, Vector3d.Zero);

                intersections.Add(new(-1, intersection, impactVelocity, parameter));
            }

            return;
        }

        if (!collider.BoundingBox.IntersectCapsule(head, tail, radius, out _))
        {
            return;
        }

        double maxEntitySize = (collider.BoundingBox.Max - collider.BoundingBox.Min).Length;
        Vector3d extendedHead = head + (head - tail).Normalized() * maxEntitySize;
        double colliderLength = (head - tail).Length;

        foreach (ShapeElementInWorldCollider shapeElementCollider in collider.Colliders)
        {
            if (shapeElementCollider.Collide(extendedHead, tail, radius, out double currentDistance, out Vector3d currentIntersection, out Vector3d segmentClosestPoint))
            {
                double positionOnCollider = colliderLength < double.Epsilon * 2 ? 0 : (currentIntersection - tail).Length / colliderLength;

                Vector3d surfaceNormal = shapeElementCollider.GetSurfaceNormal(currentIntersection);
                Vector3d impactVelocity = GetStrikeCutSlip(realtiveVelocity, surfaceNormal, Vector3d.Zero);

                intersections.Add(new(shapeElementCollider.ColliderId, currentIntersection, impactVelocity, positionOnCollider));
            }
        }
    }

    public static bool CollideWithTerrain(this ItemCapsuleCollider collider, ICoreAPI api, out List<TerrainWithCapsuleIntersectionData> intersections)
    {
        Vector3d previousTickDirection = collider.PreviousInWorldCollider.Direction;
        Vector3d previousTickStart = collider.PreviousInWorldCollider.Position;
        Vector3d thisTickDirection = collider.InWorldCollider.Direction;
        Vector3d thisTickStart = collider.InWorldCollider.Position;
        Vector3d startHead = previousTickStart;
        Vector3d startTail = previousTickStart + previousTickDirection;
        Vector3d directionHead = thisTickStart - startHead;
        Vector3d directionTail = thisTickStart + thisTickDirection - startTail;
        float radius = collider.Radius;

        intersections = [];

        int subdivisions = (int)Math.Ceiling(Math.Max((thisTickStart - previousTickStart).Length, (thisTickStart + thisTickDirection - previousTickStart - previousTickDirection).Length) / radius);

        if (subdivisions == 0)
        {
            CollideWithTerrain(startHead + directionHead, startTail + directionTail, radius, api, intersections, 0, subdivisions);
            return intersections.Count != 0;
        }

        for (int subdivision = 0; subdivision < subdivisions; subdivision++)
        {
            float subdivisionParameter = subdivision / (float)subdivisions;
            Vector3d head = startHead + directionHead * subdivisionParameter;
            Vector3d tail = startTail + directionTail * subdivisionParameter;

            CollideWithTerrain(head, tail, radius, api, intersections, subdivision, subdivisions);
        }

        return intersections.Any();
    }
    public static bool CollideWithTerrain(this EntitySphereCollider collider, ICoreAPI api, out List<TerrainWithShpereIntersectionData> intersections)
    {
        intersections = [];

        CollideWithTerrain(collider.Position, collider.PreviousPosition, collider.Radius, api, intersections);

        return intersections.Count != 0;
    }
    private static void CollideWithTerrain(Vector3d head, Vector3d tail, float radius, ICoreAPI api, List<TerrainWithCapsuleIntersectionData> intersections, int subdivision, int totalSubdivisions)
    {
        int minX = (int)Math.Min(head.X, tail.X);
        int minY = (int)Math.Min(head.Y, tail.Y);
        int minZ = (int)Math.Min(head.Z, tail.Z);

        int maxX = (int)Math.Max(head.X, tail.X);
        int maxY = (int)Math.Max(head.Y, tail.Y);
        int maxZ = (int)Math.Max(head.Z, tail.Z);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    CollideWithBlock(head, tail, radius, api.World.BlockAccessor, x, y, z, intersections, subdivision, totalSubdivisions);
                }
            }
        }
    }
    private static void CollideWithTerrain(Vector3d head, Vector3d tail, float radius, ICoreAPI api, List<TerrainWithShpereIntersectionData> intersections)
    {
        int minX = (int)Math.Floor(Math.Min(head.X, tail.X) - radius);
        int minY = (int)Math.Floor(Math.Min(head.Y, tail.Y) - radius);
        int minZ = (int)Math.Floor(Math.Min(head.Z, tail.Z) - radius);

        int maxX = (int)Math.Ceiling(Math.Max(head.X, tail.X) + radius);
        int maxY = (int)Math.Ceiling(Math.Max(head.Y, tail.Y) + radius);
        int maxZ = (int)Math.Ceiling(Math.Max(head.Z, tail.Z) + radius);

        IBlockAccessor blockAccessor = api.World.BlockAccessor;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    CollideWithBlock(head, tail, radius, blockAccessor, x, y, z, intersections);
                }
            }
        }
    }
    private static void CollideWithBlock(Vector3d head, Vector3d tail, float radius, IBlockAccessor blockAccessor, int x, int y, int z, List<TerrainWithCapsuleIntersectionData> intersections, int subdivision, int totalSubdivisions)
    {
        BlockPos position = new(x, y, z, 0);
        Block block = blockAccessor.GetBlock(position, BlockLayersAccess.MostSolid);
        if (block.Id == 0)
        {
            return;
        }
        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, position);

        if (collisionBoxes == null || collisionBoxes.Length == 0) return;

        foreach (Cuboidf collisionBox in collisionBoxes)
        {
            CuboidAABBCollider blockCollider = new(collisionBox, position);

            if (blockCollider.IntersectCapsule(tail, head, radius, out Vector3d intersectionPoint))
            {
                BlockFacing facing = blockCollider.GetFacing(intersectionPoint - blockCollider.Center, out Vector3d normal);

                intersections.Add(new(
                    block,
                    facing,
                    normal,
                    intersectionPoint,
                    new(x, y, z),
                    (intersectionPoint - head).Length,
                    subdivision,
                    totalSubdivisions));
            }
        }
    }
    private static void CollideWithBlock(Vector3d head, Vector3d tail, float radius, IBlockAccessor blockAccessor, int x, int y, int z, List<TerrainWithShpereIntersectionData> intersections)
    {
        BlockPos position = new(x, y, z, 0);
        Block block = blockAccessor.GetBlock(position, BlockLayersAccess.MostSolid);
        if (block.Id == 0)
        {
            return;
        }
        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, position);

        if (collisionBoxes == null || collisionBoxes.Length == 0) return;

        double colliderLength = (head - tail).Length;

        foreach (Cuboidf collisionBox in collisionBoxes)
        {
            CuboidAABBCollider blockCollider = new(collisionBox, position);

            if (blockCollider.IntersectCapsule(head, tail, radius, out Vector3d intersectionPoint))
            {
                double positionOnCollider = colliderLength < double.Epsilon * 2 ? 0 : (intersectionPoint - tail).Length / colliderLength;
                BlockFacing facing = blockCollider.GetFacing(intersectionPoint - blockCollider.Center, out Vector3d normal);

                intersections.Add(new(
                    block,
                    facing,
                    normal,
                    intersectionPoint,
                    new(x, y, z),
                    positionOnCollider));
            }
        }
    }


    /// <summary>
    /// Decomposes relative velocity into strike/cut/slip components given a surface normal
    /// and the weapon's blade axis direction.
    ///
    /// Coordinate frame:
    ///   Strike axis = surface normal          (perpendicular to surface, into it)
    ///   Cut axis    = blade axis ⊥ normal     (along blade edge on surface plane)
    ///   Slip axis   = normal × cut axis       (across blade flat on surface plane)
    ///
    ///        normal (strike)
    ///           ↑
    ///           │        ← slip axis (across blade flat)
    ///           │      ↗
    ///    ───────┼──────────── cut axis (along blade edge)
    ///           │  surface
    ///
    /// </summary>
    /// <param name="relativeVelocity">
    ///     Velocity of weapon relative to target surface (weaponVelocity - entityVelocity).
    /// </param>
    /// <param name="surfaceNormal">
    ///     Outward unit normal of the struck surface face.
    ///     Does not need to be pre-normalized; will be normalized internally.
    /// </param>
    /// <param name="bladeAxis">
    ///     Direction along the weapon's cutting edge in world space (capsule axis direction).
    ///     Does not need to be pre-normalized; will be normalized internally.
    ///     Pass Vector3d.Zero for blunt/sphere weapons — cut and slip will be split
    ///     from tangential speed using the velocity direction instead.
    /// </param>
    /// <returns>
    ///     Decomposed speed components. All values are non-negative magnitudes.
    ///     Signs are preserved internally but exposed as absolute speeds since
    ///     direction is encoded in the axis definitions above.
    /// </returns>
    public static Vector3d GetStrikeCutSlip(Vector3d relativeVelocity, Vector3d surfaceNormal, Vector3d bladeAxis)
    {
        // -------------------------------------------------------------------------
        // 1. Normalize strike axis
        // -------------------------------------------------------------------------
        double normalLen = surfaceNormal.Length;

        // Degenerate normal — cannot decompose meaningfully
        if (normalLen < 1e-10)
        {
            return new(relativeVelocity.Length, 0, 0);
        }

        Vector3d strikeAxis = surfaceNormal / normalLen;

        // -------------------------------------------------------------------------
        // 2. Strike speed — signed projection onto surface normal
        //    Negative value means moving INTO the surface (actual impact).
        //    We store the magnitude; caller can check sign via dot product if needed.
        // -------------------------------------------------------------------------
        double strikeSpeed = Vector3d.Dot(relativeVelocity, strikeAxis);

        // -------------------------------------------------------------------------
        // 3. Tangential velocity — component lying on the surface plane
        //    This is what drives cut and slip.
        // -------------------------------------------------------------------------
        Vector3d tangentialVelocity = relativeVelocity - strikeSpeed * strikeAxis;

        // -------------------------------------------------------------------------
        // 4. Build cut axis — blade axis projected onto surface plane
        //    If blade axis is parallel to normal (e.g. stabbing straight in),
        //    or zero (blunt weapon), fall back to tangential velocity direction.
        // -------------------------------------------------------------------------
        double bladeLen = bladeAxis.Length;
        bool hasBlade = bladeLen > 1e-10;

        Vector3d cutAxis;

        if (hasBlade)
        {
            // Remove normal component from blade axis to get surface-plane projection
            Vector3d normalizedBlade = bladeAxis / bladeLen;
            Vector3d projectedBlade = normalizedBlade - Vector3d.Dot(normalizedBlade, strikeAxis) * strikeAxis;
            double projectedLen = projectedBlade.Length;

            if (projectedLen > 1e-10)
            {
                // Normal case: blade has a meaningful surface-plane component
                cutAxis = projectedBlade / projectedLen;
            }
            else
            {
                // Edge case: blade is perpendicular to surface (pure stab).
                // No preferred cut direction on the surface plane.
                // Use tangential velocity direction if available, else arbitrary.
                double tangentialLen = tangentialVelocity.Length;
                cutAxis = tangentialLen > 1e-10
                    ? tangentialVelocity / tangentialLen
                    : ComputeArbitraryPerpendicular(strikeAxis);
            }
        }
        else
        {
            // Blunt / sphere weapon — no blade axis.
            // Use tangential velocity direction as the primary tangential axis.
            double tangentialLen = tangentialVelocity.Length;
            cutAxis = tangentialLen > 1e-10
                ? tangentialVelocity / tangentialLen
                : ComputeArbitraryPerpendicular(strikeAxis);
        }

        // -------------------------------------------------------------------------
        // 5. Slip axis — completes the right-handed orthonormal frame
        //    slip = strike × cut
        //    Lies on surface plane, perpendicular to blade edge.
        // -------------------------------------------------------------------------
        Vector3d slipAxis = Vector3d.Cross(strikeAxis, cutAxis);

        // -------------------------------------------------------------------------
        // 6. Project tangential velocity onto cut and slip axes
        // -------------------------------------------------------------------------
        double cutSpeed = Vector3d.Dot(tangentialVelocity, cutAxis);
        double slipSpeed = Vector3d.Dot(tangentialVelocity, slipAxis);

        return new(Math.Abs(strikeSpeed), Math.Abs(cutSpeed), Math.Abs(slipSpeed));
    }

    /// <summary>
    /// Builds an arbitrary unit vector perpendicular to <paramref name="v"/>.
    /// Used as a fallback when no preferred tangential direction exists.
    /// Chooses the cross product axis that avoids near-parallel vectors.
    /// </summary>
    private static Vector3d ComputeArbitraryPerpendicular(Vector3d v)
    {
        // Pick the world axis least aligned with v to avoid degenerate cross product
        Vector3d candidate = Math.Abs(v.X) <= Math.Abs(v.Y) && Math.Abs(v.X) <= Math.Abs(v.Z)
            ? Vector3d.UnitX
            : Math.Abs(v.Y) <= Math.Abs(v.Z)
                ? Vector3d.UnitY
                : Vector3d.UnitZ;

        Vector3d perp = Vector3d.Cross(v, candidate);
        return perp / perp.Length;
    }


    /*private static void CollideWithTerrainFast(Vector3d head, Vector3d tail, float radius, ICoreAPI api, List<TerrainWithShpereIntersectionData> intersections)
    {
        var accessor = api.World.BlockAccessor;

        int r = (int)Math.Ceiling(radius);

        HashSet<long> visited = new HashSet<long>();

        Vector3d dir = head - tail;
        double length = dir.Length;

        if (length == 0) return;

        dir /= length;

        int x = (int)Math.Floor(tail.X);
        int y = (int)Math.Floor(tail.Y);
        int z = (int)Math.Floor(tail.Z);

        int endX = (int)Math.Floor(head.X);
        int endY = (int)Math.Floor(head.Y);
        int endZ = (int)Math.Floor(head.Z);

        int stepX = Math.Sign(dir.X);
        int stepY = Math.Sign(dir.Y);
        int stepZ = Math.Sign(dir.Z);

        double tMaxX = IntBound(tail.X, dir.X);
        double tMaxY = IntBound(tail.Y, dir.Y);
        double tMaxZ = IntBound(tail.Z, dir.Z);

        double tDeltaX = stepX / dir.X;
        double tDeltaY = stepY / dir.Y;
        double tDeltaZ = stepZ / dir.Z;

        while (true)
        {
            // Check neighborhood for capsule radius
            for (int ix = -r; ix <= r; ix++)
            {
                for (int iy = -r; iy <= r; iy++)
                {
                    for (int iz = -r; iz <= r; iz++)
                    {
                        int bx = x + ix;
                        int by = y + iy;
                        int bz = z + iz;

                        long key = ((long)bx << 42) ^ ((long)by << 21) ^ (long)bz;

                        if (!visited.Add(key)) continue;

                        CollideWithBlock(head, tail, radius, accessor, bx, by, bz, intersections);
                    }
                }
            }

            if (x == endX && y == endY && z == endZ)
                break;

            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ)
                {
                    x += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                }
            }
            else
            {
                if (tMaxY < tMaxZ)
                {
                    y += stepY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    z += stepZ;
                    tMaxZ += tDeltaZ;
                }
            }
        }
    }

    private static double IntBound(double s, double ds)
    {
        if (ds > 0)
            return (Math.Ceiling(s) - s) / ds;
        else if (ds < 0)
            return (s - Math.Floor(s)) / -ds;
        else
            return double.PositiveInfinity;
    }*/
}
