using OpenTK.Mathematics;
using PlayerModelLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CollidersLib;

public class CollidersConfig
{
    public Dictionary<string, string[]> Elements { get; set; } = [];
}

public sealed class CollidersEntityBehavior : EntityBehavior
{
    public CollidersEntityBehavior(Entity entity) : base(entity)
    {
        _settings = entity.Api.ModLoader.GetModSystem<CollidersLibSystem>().Settings;
    }

    public CuboidAABBCollider BoundingBox { get; private set; }
    public bool HasOBBCollider { get; private set; } = false;


    public bool UnprocessedElementsLeft { get; set; } = false;
    public bool UnprocessedElementsLeftCustom { get; set; } = false;
    public Dictionary<string, int> ShapeElementsToProcess { get; private set; } = [];


    public List<string> ColliderTypeNames { get; private set; } = [];
    public List<ShapeElementCollider> Colliders { get; private set; } = [];

    public ClientAnimator? Animator { get; set; }
    static public bool RenderColliders { get; set; } = false;


    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        try
        {
            if (!attributes.KeyExists("elements"))
            {
                Utils.LoggerUtil.Error(entity.Api, this, $"Error on parsing behavior properties for entity: {entity.Code}. 'elements' attribute was not found.");
                return;
            }

            CollidersConfig? defaultConfig = attributes.AsObject<CollidersConfig>();
            if (defaultConfig != null)
            {
                ApplyConfig(defaultConfig);
            }

            _timeSinceLastUpdate = (float)entity.Api.World.Rand.NextDouble() * _updateTimeSec;
        }
        catch (Exception exception)
        {
            Utils.LoggerUtil.Error(entity.Api, this, $"Error on parsing behavior properties for entity: {entity.Code}. Exception:\n{exception}");
            UnprocessedElementsLeft = false;
            HasOBBCollider = false;
        }
    }
    public override void OnGameTick(float deltaTime)
    {
        if (!_subscribed)
        {
            if (entity?.Api != null && entity.Api.ModLoader.IsModEnabled(_playerModelLibId))
            {
                SubscribeOnModelChange();
            }

            _subscribed = true;
        }

        _timeSinceLastUpdate += deltaTime;
        if (_timeSinceLastUpdate < _updateTimeSec)
        {
            return;
        }
        _timeSinceLastUpdate = 0;

        if (entity?.Api is not ICoreClientAPI clientApi || !HasOBBCollider || !entity.Alive) return;

        Animator = entity.AnimManager?.Animator as ClientAnimator;

        if (Animator == null) return;

        if (UnprocessedElementsLeft && !UnprocessedElementsLeftCustom)
        {
            try
            {
                foreach (ElementPose pose in Animator.RootPoses)
                {
                    AddPoseShapeElements(pose);
                }

                if (ShapeElementsToProcess.Any() && !_reportedMissingColliders)
                {
                    string missingColliders = ShapeElementsToProcess.Keys.Aggregate((first, second) => $"{first}, {second}");
                    Utils.LoggerUtil.Warn(entity.Api, this, $"({entity.Code}) Listed colliders that were not found in shape: {missingColliders}");
                    _reportedMissingColliders = true;
                }
            }
            catch (Exception exception)
            {
                if (_reportedMissingColliders)
                {
                    Utils.LoggerUtil.Error(entity.Api, this, $"({entity.Code}) Error during creating colliders: \n{exception}");
                    _reportedMissingColliders = true;
                }
            }
        }

        ProcessCollidersForCustomModel();

        if (entity.IsRendered)
        {
            RecalculateColliders(Animator, clientApi);
        }
    }
    public override string PropertyName() => "CollidersLib:EntityColliders";

    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, EntityShapeRenderer renderer, int color = ColorUtil.WhiteArgb)
    {
        bool firstPerson = entity.Api is ICoreClientAPI { World.Player.CameraMode: EnumCameraMode.FirstPerson };
        if (api.World.Player.Entity.EntityId == entityPlayer.EntityId && firstPerson) return;
        if (!HasOBBCollider || !entity.Alive) return;

        IShaderProgram? currentShader = api.Render.CurrentActiveShader;
        currentShader?.Stop();

        foreach (ShapeElementCollider collider in Colliders)
        {
            if (!collider.HasRenderer)
            {
                collider.Renderer ??= renderer;
                collider.HasRenderer = true;
            }

            if (RenderColliders)
            {
                collider.Render(api, entityPlayer, _colliderColors[collider.ColliderType]);
            }
        }

        currentShader?.Use();
    }
    
    public bool Collide(Vector3d segmentStart, Vector3d segmentDirection, out ShapeElementCollider? shapeCollider, out double parameter, out Vector3d intersection)
    {

        parameter = float.MaxValue;
        bool foundIntersection = false;
        collider = "";
        intersection = Vector3d.Zero;
        colliderType = -1;

        if (!HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entity);
            bool collided = AABBCollider.Collide(segmentStart, segmentDirection, out parameter);
            intersection = segmentStart + parameter * segmentDirection;
            return collided;
        }

        if (!BoundingBox.Collide(segmentStart, segmentDirection, out _))
        {
            return false;
        }

        foreach (ShapeElementCollider shapeElementCollider in Colliders)
        {
            if (shapeElementCollider.Collide(segmentStart, segmentDirection, out double currentParameter, out Vector3d currentIntersection) && currentParameter < parameter)
            {
                parameter = currentParameter;
                collider = shapeElementCollider.ShapeElementName;
                colliderType = shapeElementCollider.ColliderType;
                intersection = currentIntersection;
                foundIntersection = true;
            }
        }

        return foundIntersection;
    }
    public bool Collide(Vector3d thisTickOrigin, Vector3d previousTickOrigin, float radius, float penetrationDistance, out List<(ShapeElementCollider collider, double parameter, Vector3d intersection)> intersections)
    {
        intersections = new();
        bool foundIntersection = false;

        if (!HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entity);
            return AABBCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out Vector3d intersection);
        }

        if (!BoundingBox.Collide(thisTickOrigin, previousTickOrigin, radius, out _))
        {
            return false;
        }

        Vector3d firstIntersection = previousTickOrigin;
        double lowestParameter = 1;

        foreach (ShapeElementCollider shapeElementCollider in Colliders)
        {
            if (shapeElementCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out _, out _, out Vector3d segmentClosestPoint))
            {
                Vector3d segmentPoint = segmentClosestPoint - previousTickOrigin;
                double parameter = segmentPoint.Length / (thisTickOrigin - previousTickOrigin).Length;

                if (lowestParameter >= parameter)
                {
                    firstIntersection = segmentClosestPoint;
                    lowestParameter = parameter;
                }

                foundIntersection = true;
            }
        }

        if (foundIntersection)
        {
            Vector3d thisTickOriginAdjustedForPenetration = firstIntersection + Vector3d.Normalize(thisTickOrigin - previousTickOrigin) * penetrationDistance;

            foundIntersection = false;
            foreach (ShapeElementCollider shapeElementCollider in Colliders)
            {
                if (shapeElementCollider.Collide(thisTickOriginAdjustedForPenetration, previousTickOrigin, radius, out double currentDistance, out Vector3d currentIntersection, out Vector3d segmentClosestPoint))
                {
                    Vector3d segmentPoint = segmentClosestPoint - previousTickOrigin;
                    double parameter = (segmentPoint.Length + currentDistance) / (thisTickOrigin - previousTickOrigin).Length;

                    intersections.Add((shapeElementCollider.ShapeElementName, shapeElementCollider.ColliderType, parameter, currentIntersection));
                    foundIntersection = true;
                }
            }
        }

        intersections.Sort((first, second) => (int)(first.Item2 - second.Item2));

        return foundIntersection;
    }
    public bool Collide(Vector3d thisTickOrigin, Vector3d previousTickOrigin, float radius, out List<(ShapeElementCollider collider, double parameter, Vector3d intersection)> intersections)
    {
        intersections = new();
        bool foundIntersection = false;

        if (!HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entity);
            return AABBCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out Vector3d intersection);
        }

        if (!BoundingBox.Collide(thisTickOrigin, previousTickOrigin, radius, out _))
        {
            return false;
        }

        double lowestParameter = 1;

        foreach (ShapeElementCollider shapeElementCollider in Colliders)
        {
            if (shapeElementCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out _, out _, out Vector3d segmentClosestPoint))
            {
                Vector3d segmentPoint = segmentClosestPoint - previousTickOrigin;
                double parameter = segmentPoint.Length / (thisTickOrigin - previousTickOrigin).Length;

                if (lowestParameter >= parameter)
                {
                    lowestParameter = parameter;
                }

                foundIntersection = true;
            }
        }

        if (foundIntersection)
        {
            foundIntersection = false;
            foreach (ShapeElementCollider shapeElementCollider in Colliders)
            {
                if (shapeElementCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out double currentDistance, out Vector3d currentIntersection, out Vector3d segmentClosestPoint))
                {
#if DEBUG
                    Vec3d pos5 = new(currentIntersection.X, currentIntersection.Y, currentIntersection.Z);
                    Vec3d pos6 = new(segmentClosestPoint.X, segmentClosestPoint.Y, segmentClosestPoint.Z);
                    //entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(0, 0, 255, 125), pos5, pos5, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
                    //entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(0, 255, 0, 125), pos6, pos6, new Vec3f(), new Vec3f(), 1, 0, 1.0f, EnumParticleModel.Cube);
#endif

                    Vector3d segmentPoint = segmentClosestPoint - previousTickOrigin;
                    double parameter = GameMath.Clamp(1 - (segmentPoint.Length + currentDistance) / (thisTickOrigin - previousTickOrigin).Length, 0, 1);

                    intersections.Add((shapeElementCollider.ShapeElementName, parameter, currentIntersection));
                    foundIntersection = true;
                }
            }
        }

        intersections.Sort((first, second) => (int)(first.Item2 - second.Item2));

        return foundIntersection;
    }
    public bool Collide(Vector3d thisTickOrigin, Vector3d previousTickOrigin, float radius, out ShapeElementCollider? shapeCollider, out double parameter, out Vector3d intersection)
    {
        collider = "";
        parameter = 0;
        intersection = Vector3d.Zero;
        colliderType = -1;

        if (!HasOBBCollider)
        {
            CuboidAABBCollider AABBCollider = new(entity);
            return AABBCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out intersection);
        }

        if (!BoundingBox.Collide(thisTickOrigin, previousTickOrigin, radius, out _))
        {
            return false;
        }

        foreach (ShapeElementCollider shapeElementCollider in Colliders)
        {
            if (shapeElementCollider.Collide(thisTickOrigin, previousTickOrigin, radius, out double currentDistance, out Vector3d currentIntersection, out Vector3d segmentClosestPoint))
            {
                intersection = segmentClosestPoint - previousTickOrigin;
                parameter = (intersection.Length + currentDistance) / (thisTickOrigin - previousTickOrigin).Length;
                collider = shapeElementCollider.ShapeElementName;
                colliderType = shapeElementCollider.ColliderType;

                return true;
            }
        }

        return false;
    }
    public bool Collide(Vector3d thisTickStart, Vector3d previousTickStart, Vector3d thisTickDirection, Vector3d previousTickDirection, int subdivisions, out ShapeElementCollider? shapeCollider, out double parameter, out Vector3d intersection)
    {
        Vector3d startHead = previousTickStart;
        Vector3d startTail = previousTickStart + previousTickDirection;
        Vector3d directionHead = thisTickStart - startHead;
        Vector3d directionTail = thisTickStart + thisTickDirection - startTail;
        for (int subdivision = 0; subdivision < subdivisions; subdivision++)
        {
            float subdivisionParameter = subdivision / (float)subdivisions;
            Vector3d head = startHead + directionHead * subdivisionParameter;
            Vector3d tail = startTail + directionTail * subdivisionParameter;

            if (Collide(head, tail - head, out collider, out colliderType, out parameter, out intersection))
            {
                return true;
            }
        }

        collider = "";
        parameter = 0;
        intersection = Vector3d.Zero;
        colliderType = -1;

        return false;
    }
    public bool Collide(Vector3d thisTickStart, Vector3d previousTickStart, Vector3d thisTickDirection, Vector3d previousTickDirection, float radius, out ShapeElementCollider? shapeCollider, out double parameter, out Vector3d intersection)
    {
        collider = "";
        parameter = 0;
        intersection = Vector3d.Zero;

        Vector3d startHead = previousTickStart;
        Vector3d startTail = previousTickStart + previousTickDirection;
        Vector3d directionHead = thisTickStart - startHead;
        Vector3d directionTail = thisTickStart + thisTickDirection - startTail;

        int subdivisions = (int)Math.Ceiling(Math.Max((thisTickStart - previousTickStart).Length, (thisTickStart + thisTickDirection - previousTickStart - previousTickDirection).Length) / radius);

        List<(string, double, Vector3d)> intersections = [];

        for (int subdivision = 0; subdivision < subdivisions; subdivision++)
        {
            float subdivisionParameter = subdivision / (float)subdivisions;
            Vector3d head = startHead + directionHead * subdivisionParameter;
            Vector3d tail = startTail + directionTail * subdivisionParameter;

            const float particleSizeFactor = 16;
            const float lifeTime = 0.5f;

            if (Collide(head, tail, radius, out List<(string, double, Vector3d)> currentIntersections))
            {
                intersections = intersections.Concat(currentIntersections).ToList();

                if (_settings.DebugWeaponTrailParticles)
                {
                    Vec3d pos7 = new(head.X, head.Y, head.Z);
                    entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(125, 125, 255, 128), pos7, pos7, new Vec3f(), new Vec3f(), lifeTime, 0, 1.0f * radius * particleSizeFactor * _settings.DebugWeaponTrailParticlesSize, EnumParticleModel.Cube);
                    Vec3d pos8 = new(tail.X, tail.Y, tail.Z);
                    entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(125, 125, 255, 128), pos8, pos8, new Vec3f(), new Vec3f(), lifeTime, 0, 1.0f * radius * particleSizeFactor * _settings.DebugWeaponTrailParticlesSize, EnumParticleModel.Cube);

                    float c = 8;
                    for (int i = 0; i < c; i++)
                    {
                        Vec3d pos5 = pos7 + (i / c) * (pos8 - pos7);
                        entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(64, 64, 128, 128), pos5, pos5, new Vec3f(), new Vec3f(), lifeTime, 0, radius * particleSizeFactor * _settings.DebugWeaponTrailParticlesSize, EnumParticleModel.Cube);
                    }
                }

                break;
            }
            else
            {
                if (_settings.DebugWeaponTrailParticles)
                {
                    Vec3d pos7 = new(head.X, head.Y, head.Z);
                    entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(125, 125, 125, 128), pos7, pos7, new Vec3f(), new Vec3f(), lifeTime, 0, 1.0f * radius * particleSizeFactor * _settings.DebugWeaponTrailParticlesSize, EnumParticleModel.Cube);
                    Vec3d pos8 = new(tail.X, tail.Y, tail.Z);
                    entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(125, 125, 125, 128), pos8, pos8, new Vec3f(), new Vec3f(), lifeTime, 0, 1.0f * radius * particleSizeFactor * _settings.DebugWeaponTrailParticlesSize, EnumParticleModel.Cube);

                    float c = 8;
                    for (int i = 0; i < c; i++)
                    {
                        Vec3d pos5 = pos7 + (i / c) * (pos8 - pos7);
                        entity.Api.World.SpawnParticles(1, ColorUtil.ColorFromRgba(64, 64, 64, 128), pos5, pos5, new Vec3f(), new Vec3f(), lifeTime, 0, radius * particleSizeFactor * _settings.DebugWeaponTrailParticlesSize, EnumParticleModel.Cube);
                    }
                }
            }
        }

        if (intersections.Any())
        {
            intersections.Sort((first, second) => (int)(first.Item2 - second.Item2));

            double smallestParameter = 1;
            foreach ((string firstCollider, double firstParameter, Vector3d firstIntersection) in intersections)
            {
                if (smallestParameter < firstParameter) continue;

                smallestParameter = firstParameter;
                collider = firstCollider;
                parameter = firstParameter;
                intersection = firstIntersection;
            }

            return true;
        }

        return false;
    }
    public bool Collide(ItemCapsuleCollider itemCollider, out ShapeElementCollider? shapeCollider, out double parameter, out Vector3d intersection)
    {
        return Collide(itemCollider.InWorldCollider.Position, itemCollider.PreviousInWorldCollider.Position, itemCollider.InWorldCollider.Direction, itemCollider.PreviousInWorldCollider.Direction, itemCollider.Radius, out shapeCollider, out parameter, out intersection);
    }



    private readonly Dictionary<int, int> _colliderColors = new()
    {
        { 0, ColorUtil.WhiteArgb },
        { 1, ColorUtil.ColorFromRgba(255, 0, 0, 255 ) }, // Red
        { 2, ColorUtil.ColorFromRgba(0, 255, 0, 255 ) }, // Green
        { 3, ColorUtil.ColorFromRgba(0, 0, 255, 255 ) }, // Blue
        { 4, ColorUtil.ColorFromRgba(255, 255, 0, 255 ) }, // Yellow
        { 5, ColorUtil.ColorFromRgba(255, 0, 255, 255 ) } // Magenta
    };
    private bool _reportedMissingColliders = false;
    private const int _updateFps = 30;
    private const float _updateTimeSec = 1f / _updateFps;
    private float _timeSinceLastUpdate = 0;
    private readonly Settings _settings;
    private bool _subscribed = false;
    private const string _playerModelLibId = "playermodellib";


    private void SetColliderElement(ShapeElement element)
    {
        if (element?.Name == null || element.From == null || element.To == null) return;

        if (UnprocessedElementsLeft && ShapeElementsToProcess.TryGetValue(element.Name, out int colliderType))
        {
            Colliders.Add(new ShapeElementCollider(element, colliderType));
            ShapeElementsToProcess.Remove(element.Name);
            UnprocessedElementsLeft = ShapeElementsToProcess.Count > 0;
        }
    }
    private void AddPoseShapeElements(ElementPose pose)
    {
        SetColliderElement(pose.ForElement);

        foreach (ElementPose childPose in pose.ChildElementPoses)
        {
            AddPoseShapeElements(childPose);
        }
    }
    private void RecalculateColliders(ClientAnimator animator, ICoreClientAPI clientApi)
    {
        foreach (ShapeElementCollider collider in Colliders)
        {
            collider.Transform(animator.TransformationMatrices, clientApi);
        }
        CalculateBoundingBox();
    }
    private void CalculateBoundingBox()
    {
        Vector3d min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3d max = new(float.MinValue, float.MinValue, float.MinValue);

        foreach (ShapeElementCollider collider in Colliders)
        {
            for (int vertex = 0; vertex < ShapeElementCollider.VertexCount; vertex++)
            {
                Vector4d inworldVertex = collider.InworldVertices[vertex];
                min.X = Math.Min(min.X, inworldVertex.X);
                min.Y = Math.Min(min.Y, inworldVertex.Y);
                min.Z = Math.Min(min.Z, inworldVertex.Z);
                max.X = Math.Max(max.X, inworldVertex.X);
                max.Y = Math.Max(max.Y, inworldVertex.Y);
                max.Z = Math.Max(max.Z, inworldVertex.Z);
            }
        }

        BoundingBox = new CuboidAABBCollider(min, max);
    }
    private void ReloadCollidersForCustomModel(string modelCode)
    {
        /*PlayerModelLibCompatibilitySystem? system = Api?.ModLoader.GetModSystem<PlayerModelLibCompatibilitySystem>();

        if (system == null) return;

        if (!system.CustomModelConfigs.TryGetValue(modelCode, out PlayerModelConfig? customModelConfig) || customModelConfig.Colliders == null)
        {
            ApplyConfig(_defaultConfig);
            return;
        }

        CollidersTypes.Clear();
        ShapeElementsToProcess.Clear();

        ColliderTypesJson types = customModelConfig.Colliders.Elements;
        foreach (string collider in types.Torso)
        {
            CollidersTypes.Add(collider, ColliderTypes.Torso);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Head)
        {
            CollidersTypes.Add(collider, ColliderTypes.Head);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Arm)
        {
            CollidersTypes.Add(collider, ColliderTypes.Arm);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Leg)
        {
            CollidersTypes.Add(collider, ColliderTypes.Leg);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Critical)
        {
            CollidersTypes.Add(collider, ColliderTypes.Critical);
            ShapeElementsToProcess.Add(collider);
        }
        foreach (string collider in types.Resistant)
        {
            CollidersTypes.Add(collider, ColliderTypes.Resistant);
            ShapeElementsToProcess.Add(collider);
        }

        DefaultPenetrationResistance = customModelConfig.Colliders.DefaultPenetrationResistance;
        PenetrationResistances = customModelConfig.Colliders.PenetrationResistances;
        ResistantCollidersStopProjectiles = customModelConfig.Colliders.ResistantCollidersStopProjectiles;

        UnprocessedElementsLeftCustom = true;
        HasOBBCollider = true;*/
    }
    private void ApplyConfig(CollidersConfig config)
    {
        ColliderTypeNames.Clear();
        Colliders.Clear();
        ShapeElementsToProcess.Clear();
        UnprocessedElementsLeft = true;
        HasOBBCollider = true;

        foreach ((string colliderTypeName, string[] shapeElements) in config.Elements)
        {
            int colliderType = ColliderTypeNames.Count;
            ColliderTypeNames.Add(colliderTypeName);

            foreach (string shapeElement in shapeElements)
            {
                ShapeElementsToProcess.Add(shapeElement, colliderType);
            }
        }
    }
    private void SubscribeOnModelChange()
    {
        PlayerSkinBehavior? skinBehavior = entity.GetBehavior<PlayerSkinBehavior>();

        if (skinBehavior != null)
        {
            skinBehavior.OnModelChanged += ReloadCollidersForCustomModel;
            ReloadCollidersForCustomModel(skinBehavior.CurrentModelCode);
        }
    }
    private void ProcessCollidersForCustomModel()
    {
        if (!UnprocessedElementsLeftCustom) return;

        Animator = entity.AnimManager?.Animator as ClientAnimator;

        if (Animator == null) return;

        UnprocessedElementsLeft = UnprocessedElementsLeftCustom;
        Colliders.Clear();

        try
        {
            foreach (ElementPose pose in Animator.RootPoses)
            {
                AddPoseShapeElements(pose);
            }

            if (ShapeElementsToProcess.Count != 0 && !_reportedMissingColliders)
            {
                string missingColliders = ShapeElementsToProcess.Select(entry => entry.Key).Aggregate((first, second) => $"{first}, {second}");
                Utils.LoggerUtil.Warn(entity.Api, this, $"({entity.Code}) Listed colliders that were not found in shape: {missingColliders}");
                _reportedMissingColliders = true;
            }
        }
        catch (Exception exception)
        {
            if (_reportedMissingColliders)
            {
                Utils.LoggerUtil.Error(entity.Api, this, $"({entity.Code}) Error during creating colliders: \n{exception}");
                _reportedMissingColliders = true;
            }
        }

        UnprocessedElementsLeftCustom = false;
    }
}