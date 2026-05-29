using OpenTK.Mathematics;
using OverhaulLib.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CollidersLib;

public class CollidersConfig
{
    public Dictionary<string, string[]> Elements { get; set; } = [];
    public Dictionary<string, string> Colors { get; set; } = [];
}

public sealed class CollidersEntityBehavior : EntityBehavior
{
    public CollidersEntityBehavior(Entity entity) : base(entity)
    {
    }

    public CuboidAABBCollider BoundingBox { get; private set; }
    public bool HasOBBCollider { get; private set; } = false;


    public bool UnprocessedElementsLeft { get; set; } = false;
    public Dictionary<string, string> ShapeElementsToProcess { get; private set; } = [];

    public List<ShapeElementCollider> Colliders { get; private set; } = [];

    public ClientAnimator? Animator { get; set; }


    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        try
        {
            if (!attributes.KeyExists("elements"))
            {
                Log.Error(entity.Api, this, $"Error on parsing behavior properties for entity: {entity.Code}. 'elements' attribute was not found.");
                return;
            }

            CollidersConfig? defaultConfig = attributes.AsObject<CollidersConfig>();
            if (defaultConfig != null)
            {
                _activeConfigs["base"] = defaultConfig;
            }

            ReapplyActiveConfigs();

            _timeSinceLastUpdate = (float)entity.Api.World.Rand.NextDouble() * _updateTimeSec;
        }
        catch (Exception exception)
        {
            Log.Error(entity.Api, this, $"Error on parsing behavior properties for entity: {entity.Code}. Exception:\n{exception}");
            UnprocessedElementsLeft = false;
            HasOBBCollider = false;
        }
    }
    public override void OnGameTick(float deltaTime)
    {
        _timeSinceLastUpdate += deltaTime;
        if (_timeSinceLastUpdate < _updateTimeSec)
        {
            return;
        }
        _timeSinceLastUpdate = 0;

        if (entity?.Api is not ICoreClientAPI clientApi || !HasOBBCollider || !entity.Alive) return;

        Animator = entity.AnimManager?.Animator as ClientAnimator;

        if (Animator == null) return;

        if (UnprocessedElementsLeft)
        {
            ConstructCollidersForShapeElements();
        }

        if (entity.IsRendered && entity.Properties.Client.Renderer is EntityShapeRenderer renderer)
        {
            RecalculateColliders(Animator, clientApi, renderer);
        }
    }
    public override string PropertyName() => "CollidersEntityBehavior";

    public void AddOrReplaceConfig(string code, CollidersConfig config)
    {
        _activeConfigs[code] = config;
        ReapplyActiveConfigs();
    }
    public void RemoveConfig(string code)
    {
        _activeConfigs.Remove(code);
        ReapplyActiveConfigs();
    }



    private bool _reportedMissingColliders = false;
    private const int _updateFps = 60;
    private const float _updateTimeSec = 1f / _updateFps;
    private float _timeSinceLastUpdate = 0;
    private readonly Dictionary<string, CollidersConfig> _activeConfigs = [];
    private readonly Dictionary<string, Color4> _colors = [];


    private void ReapplyActiveConfigs()
    {
        Colliders.Clear();
        ShapeElementsToProcess.Clear();
        _colors.Clear();
        _reportedMissingColliders = false;

        if (_activeConfigs.Count == 0)
        {
            UnprocessedElementsLeft = false;
            HasOBBCollider = false;
            return;
        }

        UnprocessedElementsLeft = true;
        HasOBBCollider = true;
        foreach ((_, CollidersConfig config) in _activeConfigs)
        {
            ApplyConfig(config);
        }
    }
    private void SetColliderElement(ShapeElement element)
    {
        if (element?.Name == null || element.From == null || element.To == null) return;

        if (UnprocessedElementsLeft && ShapeElementsToProcess.TryGetValue(element.Name, out string? colliderType))
        {
            ShapeElementCollider collider = new(element, colliderType);
            if (_colors.TryGetValue(colliderType, out Color4 color))
            {
                collider.Color = color;
            }
            Colliders.Add(collider);
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
    private void RecalculateColliders(ClientAnimator animator, ICoreClientAPI clientApi, EntityShapeRenderer renderer)
    {
        foreach (ShapeElementCollider collider in Colliders)
        {
            collider.Transform(animator.TransformationMatrices, clientApi, renderer);
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
    private void ApplyConfig(CollidersConfig config)
    {
        foreach ((string colliderTypeName, string[] shapeElements) in config.Elements)
        {
            foreach (string shapeElement in shapeElements)
            {
                ShapeElementsToProcess.Add(shapeElement, colliderTypeName);
            }
        }

        foreach ((string collierTypeName, string color) in config.Colors)
        {
            (byte R, byte G, byte B) = HexToRgb(color);
            _colors[collierTypeName] = new Color4(R, G, B, 255);
        }
    }
    private static (byte R, byte G, byte B) HexToRgb(string hex)
    {
        hex = hex.TrimStart('#');

        if (hex.Length != 6) throw new ArgumentException("Hex color must be 6 characters long.", nameof(hex));

        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);

        return (r, g, b);
    }
    private void ConstructCollidersForShapeElements()
    {
        if (Animator == null) return;
        
        try
        {
            foreach (ElementPose pose in Animator.RootPoses)
            {
                AddPoseShapeElements(pose);
            }

            if (ShapeElementsToProcess.Any() && !_reportedMissingColliders)
            {
                string missingColliders = ShapeElementsToProcess.Keys.Aggregate((first, second) => $"{first}, {second}");
                Log.Warn(entity.Api, this, $"({entity.Code}) Listed colliders that were not found in shape: {missingColliders}");
                _reportedMissingColliders = true;
            }
        }
        catch (Exception exception)
        {
            if (_reportedMissingColliders)
            {
                Log.Error(entity.Api, this, $"({entity.Code}) Error during creating colliders: \n{exception}");
                _reportedMissingColliders = true;
            }
        }
    }
}