using OpenTK.Mathematics;
using OverhaulLib.Utils;
using System.Collections.Immutable;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CollidersLib;

public static class OffThreadCollidersConstructor
{
    public static float UpdateRange { get; set; } = 512;

    public static long ScheduleCollidersConstruction(CollierIdsManager idsManager, IEnumerable<CollidersConfig> configs, ClientAnimator animator, Action<long, ImmutableArray<ShapeElementProtoCollider>> consumer)
    {
        long taskId = _lastTaskId++;

        TyronThreadPool.QueueTask(() => ConstructProtoCollidersFromConfigs(taskId, idsManager, configs, animator, consumer), nameof(OffThreadCollidersConstructor));

        return taskId;
    }

    public static void UpdateEntitiesColliders(ICoreClientAPI clientApi)
    {
        EntityPlayer player = clientApi.World.Player.Entity;
        Entity[] entities = clientApi.World.GetEntitiesAround(player.Pos.XYZ, UpdateRange, UpdateRange, EntityMatcher);
        IEnumerable<IEntityColliderReceiver> colliderReceivers = entities.Select(entity => entity.GetInterface<IEntityColliderReceiver>()).OfType<IEntityColliderReceiver>();

        ProcessEntities(colliderReceivers, clientApi);
    }



    private static long _lastTaskId = 0;


    private static bool EntityMatcher(Entity entity)
    {
        return entity.IsCreature && entity.IsRendered && entity.Alive;
    }
    private static void ProcessEntities(IEnumerable<IEntityColliderReceiver> receivers, ICoreClientAPI clientApi)
    {
        Vector3d cameraPos = clientApi.World.Player.Entity.CameraPos.ToOpenTK();

        foreach (IEntityColliderReceiver receiver in receivers)
        {
            if (receiver.Animator == null || receiver.Renderer == null) continue;

            float[] modelMatrix = (float[])receiver.Renderer.ModelMat.Clone();
            float[] tranformMatrices = (float[])receiver.Animator.TransformationMatrices.Clone();

            ImmutableArray<ShapeElementInWorldCollider> colliders = Transform(receiver.ProtoColliders, tranformMatrices, modelMatrix, cameraPos);

            receiver.Colliders = colliders;
            if (colliders.Length > 0)
            {
                receiver.BoundingBox = CalcBoundingBox(colliders);
            }
            else if (receiver is Entity entity)
            {
                receiver.BoundingBox = new(entity);
            }
        }
    }
    private static ImmutableArray<ShapeElementInWorldCollider> Transform(ImmutableArray<ShapeElementProtoCollider> protoColliders, float[] tranformMatrices, float[] modelMatrix, Vector3d cameraPos)
    {
        var resultBuilder = ImmutableArray.CreateBuilder<ShapeElementInWorldCollider>(initialCapacity: protoColliders.Length);

        foreach (ref readonly ShapeElementProtoCollider protoCollider in protoColliders.AsSpan())
        {
            resultBuilder.Add(new ShapeElementInWorldCollider(in protoCollider, tranformMatrices, modelMatrix, cameraPos));
        }

        return resultBuilder.ToImmutable();
    }
    public static CuboidAABBCollider CalcBoundingBox(ImmutableArray<ShapeElementInWorldCollider> colliders)
    {
        Vector3d min = new(double.MaxValue, double.MaxValue, double.MaxValue);
        Vector3d max = new(double.MinValue, double.MinValue, double.MinValue);

        foreach (ref readonly ShapeElementInWorldCollider collider in colliders.AsSpan()) // to avoid copying
        {
            for (int i = 0; i < ShapeElementInWorldCollider.VertexCount; i++)
            {
                Vector3d vertex = collider[i];

                if (vertex.X < min.X) min.X = vertex.X;
                if (vertex.Y < min.Y) min.Y = vertex.Y;
                if (vertex.Z < min.Z) min.Z = vertex.Z;

                if (vertex.X > max.X) max.X = vertex.X;
                if (vertex.Y > max.Y) max.Y = vertex.Y;
                if (vertex.Z > max.Z) max.Z = vertex.Z;
            }
        }

        return new(min, max);
    }
    private static void ConstructProtoCollidersFromConfigs(long taskId, CollierIdsManager idsManager, IEnumerable<CollidersConfig> configs, ClientAnimator animator, Action<long, ImmutableArray<ShapeElementProtoCollider>> consumer)
    {
        idsManager.ReapplyConfigs(configs, out Dictionary<string, int> shapeElementsToColliderIds);
        Dictionary<ShapeElement, int> shapeElements = new(shapeElementsToColliderIds.Count);
        foreach (ElementPose pose in animator.RootPoses)
        {
            AddPoseShapeElements(pose, shapeElements, shapeElementsToColliderIds);
        }

        var resultBuilder = ImmutableArray.CreateBuilder<ShapeElementProtoCollider>(initialCapacity: shapeElements.Count);
        foreach ((ShapeElement shapeElement, int colliderId) in shapeElements)
        {
            resultBuilder.Add(new ShapeElementProtoCollider((short)shapeElement.JointId, (short)colliderId, shapeElement));
        }

        consumer.Invoke(taskId, resultBuilder.ToImmutable());
    }
    private static void SetColliderElement(ShapeElement element, Dictionary<ShapeElement, int> shapeElements, Dictionary<string, int> shapeElementsToColliderIds)
    {
        if (element?.Name == null || element.From == null || element.To == null) return;

        if (shapeElementsToColliderIds.TryGetValue(element.Name, out int colliderId))
        {
            shapeElements.Add(element, colliderId);
        }
    }
    private static void AddPoseShapeElements(ElementPose pose, Dictionary<ShapeElement, int> shapeElements, Dictionary<string, int> shapeElementsToColliderIds)
    {
        SetColliderElement(pose.ForElement, shapeElements, shapeElementsToColliderIds);

        foreach (ElementPose childPose in pose.ChildElementPoses)
        {
            AddPoseShapeElements(childPose, shapeElements, shapeElementsToColliderIds);
        }
    }
}