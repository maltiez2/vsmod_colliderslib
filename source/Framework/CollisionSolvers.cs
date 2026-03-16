using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CollidersLib;

public readonly struct EntityWithCapsuleIntersectionData
{
    /// <summary>
    /// Entity shape elements collider if it has any
    /// </summary>
    public readonly ShapeElementCollider? EntityCollider;
    /// <summary>
    /// Intersection point in world coordinates between entity collider and capsule collider
    /// </summary>
    public readonly Vector3d IntersectionPoint;
    /// <summary>
    /// How far on collider intersection is.<br/>
    /// From 0 to 1, where 0 is the start of the collider.
    /// </summary>
    public readonly double PositionOnCollider;
    /// <summary>
    /// Intersection is done between previous and current positions. This number represents lerp paramerter between the two, where 0 is previous position and 1 is current one.
    /// </summary>
    public readonly double PositionInTime;

    /// <summary>
    /// Combined PositionOnCollider and PositionInTime normilized back to [0, 1].<br/>
    /// Is meant to be used for fidning earliest and closest intersection.
    /// </summary>
    public double NormalizedPosition => Math.Clamp((PositionInTime * 10.0 + PositionOnCollider) / 11.0, 0, 1);

    public EntityWithCapsuleIntersectionData(ShapeElementCollider? collider, Vector3d point, double positionOnCollider, double positionInTime)
    {
        EntityCollider = collider;
        IntersectionPoint = point;
        PositionOnCollider = positionOnCollider;
        PositionInTime = positionInTime;
    }
}

public readonly struct EntityWithSphereIntersectionData
{
    /// <summary>
    /// Entity shape elements collider if it has any
    /// </summary>
    public readonly ShapeElementCollider? EntityCollider;
    /// <summary>
    /// Intersection point in world coordinates between entity collider and capsule collider
    /// </summary>
    public readonly Vector3d IntersectionPoint;
    /// <summary>
    /// Intersection is done between previous and current positions. This number represents lerp paramerter between the two, where 0 is previous position and 1 is current one.
    /// </summary>
    public readonly double PositionInTime;

    public EntityWithSphereIntersectionData(ShapeElementCollider? entityCollider, Vector3d intersectionPoint, double positionInTime)
    {
        EntityCollider = entityCollider;
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
    public readonly double PositionOnCollider;
    public readonly double PositionInTime;

    public double NormalizedPosition => Math.Clamp((PositionInTime * 10.0 + PositionOnCollider) / 11.0, 0, 1);

    public TerrainWithCapsuleIntersectionData(Block block, BlockFacing facing, Vector3d normal, Vector3d intersectionPoint, Vector3i blockPosition, double positionOnCollider, double positionInTime)
    {
        Block = block;
        Facing = facing;
        Normal = normal;
        IntersectionPoint = intersectionPoint;
        BlockPosition = blockPosition;
        PositionOnCollider = positionOnCollider;
        PositionInTime = positionInTime;
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
    public static bool CollideWithEntity(this ItemCapsuleCollider itemCollider, CollidersEntityBehavior entityColliders, out List<EntityWithCapsuleIntersectionData> intersections)
    {
        Vector3d previousTickDirection = itemCollider.PreviousInWorldCollider.Direction;
        Vector3d previousTickStart = itemCollider.PreviousInWorldCollider.Position;
        Vector3d thisTickStart = itemCollider.InWorldCollider.Position;
        Vector3d thisTickDirection = itemCollider.InWorldCollider.Direction;
        Vector3d startHead = previousTickStart;
        Vector3d startTail = previousTickStart + previousTickDirection;
        Vector3d directionHead = thisTickStart - startHead;
        Vector3d directionTail = thisTickStart + thisTickDirection - startTail;
        float radius = itemCollider.Radius;

        int subdivisions = (int)Math.Ceiling(Math.Max((thisTickStart - previousTickStart).Length, (thisTickStart + thisTickDirection - previousTickStart - previousTickDirection).Length) / radius);

        intersections = [];

        for (int subdivision = 0; subdivision < subdivisions; subdivision++)
        {
            float subdivisionParameter = subdivision / (float)subdivisions;
            Vector3d head = startHead + directionHead * subdivisionParameter;
            Vector3d tail = startTail + directionTail * subdivisionParameter;

            CollideWithEntity(entityColliders, head, tail, radius, intersections, subdivisionParameter);
        }

        return intersections.Any();
    }
    public static bool CollideWithEntity(this EntitySphereCollider itemCollider, CollidersEntityBehavior entityColliders, out List<EntityWithSphereIntersectionData> intersections)
    {
        intersections = [];

        CollideWithEntity(entityColliders, itemCollider.Position, itemCollider.PreviousPosition, itemCollider.Radius, intersections);

        return intersections.Count != 0;
    }
    private static void CollideWithEntity(CollidersEntityBehavior entityColliders, Vector3d head, Vector3d tail, float radius, List<EntityWithCapsuleIntersectionData> intersections, float subdivision = 1f)
    {
        if (!entityColliders.HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entityColliders.entity);
            AABBCollider.IntersectCapsule(head, tail, radius, out Vector3d intersection);

            Vector3d segmentPoint = intersection - tail;
            double parameter = GameMath.Clamp(1 - segmentPoint.Length / (head - tail).Length, 0, 1);

            intersections.Add(new(null, intersection, parameter, subdivision));
        }

        if (!entityColliders.BoundingBox.IntersectCapsule(head, tail, radius, out _))
        {
            return;
        }

        foreach (ShapeElementCollider shapeElementCollider in entityColliders.Colliders)
        {
            if (shapeElementCollider.Collide(head, tail, radius, out double currentDistance, out Vector3d currentIntersection, out Vector3d segmentClosestPoint))
            {
                Vector3d segmentPoint = segmentClosestPoint - tail;
                double parameter = GameMath.Clamp(1 - (segmentPoint.Length + currentDistance) / (head - tail).Length, 0, 1);

                intersections.Add(new(shapeElementCollider, currentIntersection, parameter, subdivision));
            }
        }
    }
    private static void CollideWithEntity(CollidersEntityBehavior entityColliders, Vector3d head, Vector3d tail, float radius, List<EntityWithSphereIntersectionData> intersections)
    {
        if (!entityColliders.HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entityColliders.entity);
            AABBCollider.IntersectCapsule(head, tail, radius, out Vector3d intersection);

            Vector3d segmentPoint = intersection - tail;
            double parameter = GameMath.Clamp(1 - segmentPoint.Length / (head - tail).Length, 0, 1);

            intersections.Add(new(null, intersection, parameter));
        }

        if (!entityColliders.BoundingBox.IntersectCapsule(head, tail, radius, out _))
        {
            return;
        }

        foreach (ShapeElementCollider shapeElementCollider in entityColliders.Colliders)
        {
            if (shapeElementCollider.Collide(head, tail, radius, out double currentDistance, out Vector3d currentIntersection, out Vector3d segmentClosestPoint))
            {
                Vector3d segmentPoint = segmentClosestPoint - tail;
                double parameter = GameMath.Clamp(1 - (segmentPoint.Length + currentDistance) / (head - tail).Length, 0, 1);

                intersections.Add(new(shapeElementCollider, currentIntersection, parameter));
            }
        }
    }

    public static bool CollideWithTerrain(this ItemCapsuleCollider itemCollider, ICoreAPI api, out List<TerrainWithCapsuleIntersectionData> intersections)
    {
        Vector3d previousTickDirection = itemCollider.PreviousInWorldCollider.Direction;
        Vector3d previousTickStart = itemCollider.PreviousInWorldCollider.Position;
        Vector3d thisTickStart = itemCollider.InWorldCollider.Position;
        Vector3d thisTickDirection = itemCollider.InWorldCollider.Direction;
        Vector3d startHead = previousTickStart;
        Vector3d startTail = previousTickStart + previousTickDirection;
        Vector3d directionHead = thisTickStart - startHead;
        Vector3d directionTail = thisTickStart + thisTickDirection - startTail;
        float radius = itemCollider.Radius;

        int subdivisions = (int)Math.Ceiling(Math.Max((thisTickStart - previousTickStart).Length, (thisTickStart + thisTickDirection - previousTickStart - previousTickDirection).Length) / radius);

        intersections = [];

        for (int subdivision = 0; subdivision < subdivisions; subdivision++)
        {
            float subdivisionParameter = subdivision / (float)subdivisions;
            Vector3d head = startHead + directionHead * subdivisionParameter;
            Vector3d tail = startTail + directionTail * subdivisionParameter;

            CollideWithTerrain(head, tail, radius, api, intersections, subdivisionParameter);
        }

        return intersections.Any();
    }
    public static bool CollideWithTerrain(this EntitySphereCollider itemCollider, ICoreAPI api, out List<TerrainWithShpereIntersectionData> intersections)
    {
        intersections = [];

        CollideWithTerrain(itemCollider.Position, itemCollider.PreviousPosition, itemCollider.Radius, api, intersections);

        return intersections.Count != 0;
    }
    private static void CollideWithTerrain(Vector3d head, Vector3d tail, float radius, ICoreAPI api, List<TerrainWithCapsuleIntersectionData> intersections, float subdivision = 1f)
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
                    CollideWithBlock(head, tail, radius, api.World.BlockAccessor, x, y, z, intersections, subdivision);
                }
            }
        }
    }
    private static void CollideWithTerrain(Vector3d head, Vector3d tail, float radius, ICoreAPI api, List<TerrainWithShpereIntersectionData> intersections)
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
                    CollideWithBlock(head, tail, radius, api.World.BlockAccessor, x, y, z, intersections);
                }
            }
        }
    }
    private static void CollideWithBlock(Vector3d head, Vector3d tail, float radius, IBlockAccessor blockAccessor, int x, int y, int z, List<TerrainWithCapsuleIntersectionData> intersections, float subdivision = 1f)
    {
        BlockPos position = new(x, y, z, 0);
        Block block = blockAccessor.GetBlock(position, BlockLayersAccess.MostSolid);
        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, position);

        if (collisionBoxes == null || collisionBoxes.Length == 0) return;

        foreach (Cuboidf collisionBox in collisionBoxes)
        {
            CuboidAABBCollider blockCollider = new(collisionBox);

            if (blockCollider.IntersectCapsule(head, tail, radius, out Vector3d intersectionPoint))
            {
                double positionOnCollider = (intersectionPoint - tail).Length;
                BlockFacing facing = blockCollider.GetFacing(intersectionPoint - blockCollider.Center, out Vector3d normal);

                intersections.Add(new(
                    block,
                    facing,
                    normal,
                    intersectionPoint,
                    new(x, y, z),
                    positionOnCollider,
                    subdivision));
            }
        }
    }
    private static void CollideWithBlock(Vector3d head, Vector3d tail, float radius, IBlockAccessor blockAccessor, int x, int y, int z, List<TerrainWithShpereIntersectionData> intersections)
    {
        BlockPos position = new(x, y, z, 0);
        Block block = blockAccessor.GetBlock(position, BlockLayersAccess.MostSolid);
        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, position);

        if (collisionBoxes == null || collisionBoxes.Length == 0) return;

        foreach (Cuboidf collisionBox in collisionBoxes)
        {
            CuboidAABBCollider blockCollider = new(collisionBox);

            if (blockCollider.IntersectCapsule(head, tail, radius, out Vector3d intersectionPoint))
            {
                double positionOnCollider = (intersectionPoint - tail).Length;
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
}
