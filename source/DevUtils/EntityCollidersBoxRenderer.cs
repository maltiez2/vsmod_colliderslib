using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OverhaulLib.Utils;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace CollidersLib.DevTools;

public sealed class EntityCollidersBoxRenderer : IRenderer
{
    public EntityCollidersBoxRenderer(ICoreClientAPI api)
    {
        _api = api;
        _api.Event.RegisterRenderer(this, RenderStage, "CollidersLib:EntityCollidersBoxRenderer");
        _api.Event.ReloadShader += LoadShader;
        _ = LoadShader();
        InitGpuObjects();
    }


    public double RenderOrder => 1.0;
    public int RenderRange => int.MaxValue;

    public float MaxEntityDistance { get; set; } = 128;
    public static bool RenderColliders { get; set; } = true;

    public const EnumRenderStage RenderStage = EnumRenderStage.Done;

    public const float FaceAlphaFactor = 0.5f;


    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!RenderColliders) return;
        if (_shader == null) return;

        UpdateCollidersFadeOutTime(deltaTime);

        EntityPlayer? localPlayer = _api.World.Player?.Entity;
        if (localPlayer == null) return;

        _cameraOrigin = new Vector3d(localPlayer.CameraPos.X, localPlayer.CameraPos.Y, localPlayer.CameraPos.Z);
        _edgeVertexCount = 0;
        _faceVertexCount = 0;

        foreach ((long entityId, Dictionary<string, ColliderHighlightData>? highlitedColliders) in _highlightedColliders)
        {
            Entity? entity = _api.World.GetEntityById(entityId);
            if (entity == null || !entity.Alive)
            {
                _highlightedColliders.Remove(entityId);
                continue;
            }

            if (!entity.IsRendered) continue;

            CollidersEntityBehavior? behavior = entity.GetBehavior<CollidersEntityBehavior>();
            if (behavior == null) continue;
            if (!behavior.HasOBBCollider) continue;
            if (behavior.Colliders.Count == 0) continue;

            bool isLocalPlayer = entity.EntityId == localPlayer.EntityId;
            bool firstPerson = _api.World.Player?.CameraMode == EnumCameraMode.FirstPerson;
            if (isLocalPlayer && firstPerson) continue;

            BuildCollidersForEntity(behavior, highlitedColliders);
        }

        if (_edgeVertexCount == 0 && _faceVertexCount == 0) return;

        UploadAndDraw();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _api.Event.ReloadShader -= LoadShader;
        _api.Event.UnregisterRenderer(this, RenderStage);
        GL.DeleteVertexArray(_edgeVao);
        GL.DeleteBuffer(_edgeVbo);
        GL.DeleteVertexArray(_faceVao);
        GL.DeleteBuffer(_faceVbo);
        _shader?.Dispose();
    }


    public void HighlightColliders(long entityId, TimeSpan fadeOutTime, Color4 color, params string[] colliders)
    {
        if (!_highlightedColliders.TryGetValue(entityId, out Dictionary<string, ColliderHighlightData>? highlitedColliders))
        {
            highlitedColliders = [];
            _highlightedColliders[entityId] = highlitedColliders;
        }

        foreach (string collider in colliders)
        {
            highlitedColliders[collider] = new ColliderHighlightData()
            {
                Color = color,
                TotalTime = (float)fadeOutTime.TotalSeconds,
                TimeLeft = (float)fadeOutTime.TotalSeconds
            };
        }
    }
    public void ShowColliders(long entityId, params string[] colliders)
    {
        if (!_highlightedColliders.TryGetValue(entityId, out Dictionary<string, ColliderHighlightData>? highlitedColliders))
        {
            highlitedColliders = [];
            _highlightedColliders[entityId] = highlitedColliders;
        }

        foreach (string collider in colliders)
        {
            highlitedColliders[collider] = new ColliderHighlightData()
            {
                Color = Color4.Transparent,
                TotalTime = 0,
                TimeLeft = 0
            };
        }
    }
    public void HideColliders(long entityId, params string[] colliders)
    {
        if (!_highlightedColliders.TryGetValue(entityId, out Dictionary<string, ColliderHighlightData>? highlitedColliders))
        {
            return;
        }

        foreach (string collider in colliders)
        {
            if (highlitedColliders.TryGetValue(collider, out ColliderHighlightData data) && data.TotalTime <= 0)
            {
                highlitedColliders.Remove(collider);
            }
        }
    }



    private const int _edgesPerBox = 12;
    private const int _edgeVerticesPerBox = _edgesPerBox * 2;  // 24 — two endpoints per edge

    private const int _facesPerBox = 6;
    private const int _trianglesPerFace = 2;
    private const int _verticesPerTriangle = 3;
    private const int _faceVerticesPerBox = _facesPerBox * _trianglesPerFace * _verticesPerTriangle; // 36

    private const int _maxBoxes = 256;
    private const int _maxEdgeVertices = _maxBoxes * _edgeVerticesPerBox;
    private const int _maxFaceVertices = _maxBoxes * _faceVerticesPerBox;
    private const int _stride = 20;

    private static readonly (int A, int B)[] _boxEdges =
    [
        (0,1),(0,3),(0,4),(1,2),(1,5),(2,3),(2,6),(3,7),(4,5),(4,7),(5,6),(6,7)
    ];

    private static readonly (int A, int B, int C, int D)[] _boxFaces =
    [
        (0, 1, 2, 3), // bottom
        (4, 7, 6, 5), // top
        (0, 4, 5, 1), // front
        (3, 2, 6, 7), // back
        (0, 3, 7, 4), // left
        (1, 5, 6, 2), // righ
    ];

    private readonly ICoreClientAPI _api;
    private readonly Dictionary<long, Dictionary<string, ColliderHighlightData>> _highlightedColliders = [];
    private IShaderProgram? _shader;

    private int _edgeVao;
    private int _edgeVbo;
    private int _faceVao;
    private int _faceVbo;

    private readonly WireframeVertex[] _edgeVertices = new WireframeVertex[_maxEdgeVertices];
    private readonly WireframeVertex[] _faceVertices = new WireframeVertex[_maxFaceVertices];
    private int _edgeVertexCount = 0;
    private int _faceVertexCount = 0;

    private Vector3d _cameraOrigin;
    private bool _disposed = false;

    private sealed class ColliderHighlightData
    {
        public Color4 Color;
        public float TotalTime;
        public float TimeLeft;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WireframeVertex(float x, float y, float z, byte r, byte g, byte b, byte a)
    {
        public float X = x;
        public float Y = y;
        public float Z = z;

        public byte R = r;
        public byte G = g;
        public byte B = b;
        public byte A = a;

        public int RenderFlags = 0;
    }

    private void UpdateCollidersFadeOutTime(float deltaSeconds)
    {
        foreach ((long entityId, Dictionary<string, ColliderHighlightData> colliders) in _highlightedColliders)
        {
            foreach ((string collider, ColliderHighlightData data) in colliders)
            {
                if (data.TotalTime > 0)
                {
                    data.TimeLeft -= deltaSeconds;
                    if (data.TimeLeft < 0)
                    {
                        colliders.Remove(collider);
                    }
                }
            }

            if (colliders.Count == 0)
            {
                _highlightedColliders.Remove(entityId);
            }
        }
    }

    private void BuildCollidersForEntity(CollidersEntityBehavior behavior, Dictionary<string, ColliderHighlightData> colliders)
    {
        foreach (ShapeElementCollider collider in behavior.Colliders)
        {
            if (!colliders.TryGetValue(collider.ColliderType, out ColliderHighlightData? highlightData)) continue;
            if (_edgeVertexCount + _edgeVerticesPerBox > _maxEdgeVertices) return;
            if (_faceVertexCount + _faceVerticesPerBox > _maxFaceVertices) return;

            (byte R, byte G, byte B, byte A) edgeColor;
            if (highlightData.Color != Color4.Transparent)
            {
                edgeColor = FromColor4(highlightData.Color);
            }
            else
            {
                edgeColor = GetColor(collider);
            }

            if (highlightData.TotalTime > 0)
            {
                edgeColor.A = (byte)(edgeColor.A * highlightData.TimeLeft / highlightData.TotalTime);
            }

            (byte R, byte G, byte B, byte A) faceColor = (
                edgeColor.R,
                edgeColor.G,
                edgeColor.B,
                (byte)(edgeColor.A * FaceAlphaFactor)
            );

            BuildBox(collider.InworldVertices, edgeColor, faceColor);
        }
    }

    private void BuildBox(Vector4d[] vertices, (byte R, byte G, byte B, byte A) edgeColor, (byte R, byte G, byte B, byte A) faceColor)
    {
        foreach ((int a, int b) in _boxEdges)
        {
            Vector3d worldA = new(vertices[a].X, vertices[a].Y, vertices[a].Z);
            Vector3d worldB = new(vertices[b].X, vertices[b].Y, vertices[b].Z);
            AddEdge(worldA, worldB, edgeColor);
        }

        foreach ((int a, int b, int c, int d) in _boxFaces)
        {
            Vector3d va = new(vertices[a].X, vertices[a].Y, vertices[a].Z);
            Vector3d vb = new(vertices[b].X, vertices[b].Y, vertices[b].Z);
            Vector3d vc = new(vertices[c].X, vertices[c].Y, vertices[c].Z);
            Vector3d vd = new(vertices[d].X, vertices[d].Y, vertices[d].Z);
            AddFaceQuad(va, vb, vc, vd, faceColor);
        }
    }

    private void AddEdge(Vector3d a, Vector3d b, (byte R, byte G, byte B, byte A) color)
    {
        Vector3d relativeA = a - _cameraOrigin;
        Vector3d relativeB = b - _cameraOrigin;
        _edgeVertices[_edgeVertexCount++] = new WireframeVertex((float)relativeA.X, (float)relativeA.Y, (float)relativeA.Z, color.R, color.G, color.B, color.A);
        _edgeVertices[_edgeVertexCount++] = new WireframeVertex((float)relativeB.X, (float)relativeB.Y, (float)relativeB.Z, color.R, color.G, color.B, color.A);
    }

    private void AddFaceQuad(Vector3d a, Vector3d b, Vector3d c, Vector3d d, (byte R, byte G, byte B, byte A) color)
    {
        Vector3d ra = a - _cameraOrigin;
        Vector3d rb = b - _cameraOrigin;
        Vector3d rc = c - _cameraOrigin;
        Vector3d rd = d - _cameraOrigin;

        _faceVertices[_faceVertexCount++] = MakeVertex(ra, color);
        _faceVertices[_faceVertexCount++] = MakeVertex(rb, color);
        _faceVertices[_faceVertexCount++] = MakeVertex(rc, color);

        _faceVertices[_faceVertexCount++] = MakeVertex(ra, color);
        _faceVertices[_faceVertexCount++] = MakeVertex(rc, color);
        _faceVertices[_faceVertexCount++] = MakeVertex(rd, color);
    }

    private static WireframeVertex MakeVertex(Vector3d pos, (byte R, byte G, byte B, byte A) color)
        => new((float)pos.X, (float)pos.Y, (float)pos.Z, color.R, color.G, color.B, color.A);

    private void UploadAndDraw()
    {
        if (_edgeVertexCount > 0)
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, _edgeVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _edgeVertexCount * _stride, _edgeVertices);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        if (_faceVertexCount > 0)
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, _faceVbo);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _faceVertexCount * _stride, _faceVertices);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        _shader!.Use();
        _shader.UniformMatrix("projectionMatrix", _api.Render.CurrentProjectionMatrix);
        _shader.UniformMatrix("modelViewMatrix", _api.Render.CameraMatrixOriginf);
        _shader.Uniform("colorIn", new Vec4f(1f, 1f, 1f, 1f));

        // enables blending for transparent faces
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        // disables backface culling
        GL.Disable(EnableCap.CullFace);

        if (_faceVertexCount > 0)
        {
            // to avoid z-fight
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(1f, 1f);

            GL.BindVertexArray(_faceVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _faceVertexCount);
            GL.BindVertexArray(0);

            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(0f, 0f);
        }

        if (_edgeVertexCount > 0)
        {
            GL.BindVertexArray(_edgeVao);
            GL.DrawArrays(PrimitiveType.Lines, 0, _edgeVertexCount);
            GL.BindVertexArray(0);
        }

        GL.Enable(EnableCap.CullFace);
        GL.Disable(EnableCap.Blend);

        _shader.Stop();
    }

    private void InitGpuObjects()
    {
        _edgeVao = GL.GenVertexArray();
        _edgeVbo = GL.GenBuffer();
        SetupVaoVbo(_edgeVao, _edgeVbo, _maxEdgeVertices);

        _faceVao = GL.GenVertexArray();
        _faceVbo = GL.GenBuffer();
        SetupVaoVbo(_faceVao, _faceVbo, _maxFaceVertices);
    }

    private static void SetupVaoVbo(int vao, int vbo, int maxVertices)
    {
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, maxVertices * _stride, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, _stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, _stride, 12);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribIPointer(2, 1, VertexAttribIntegerType.Int, _stride, 16);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    private bool LoadShader()
    {
        _shader?.Dispose();
        IShaderProgram program = _api.Shader.NewShaderProgram();
        program.VertexShader = _api.Shader.NewShader(EnumShaderType.VertexShader);
        program.FragmentShader = _api.Shader.NewShader(EnumShaderType.FragmentShader);

        program.VertexShader.Code = @"#version 330 core
#extension GL_ARB_explicit_attrib_location: enable
layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec4 vertexColor;
layout(location = 2) in int renderFlags;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 colorIn;
out vec4 color;
void main(void){vec4 cameraPos=modelViewMatrix*vec4(vertexPositionIn,1.0);color=vertexColor*colorIn;gl_Position=projectionMatrix*cameraPos;gl_Position.w+=0.0014+(renderFlags>>8)*0.00025;}";

        program.FragmentShader.Code = @"#version 330 core
#extension GL_ARB_explicit_attrib_location: enable
in vec4 color;
layout(location = 0) out vec4 outColor;
layout(location = 1) out vec4 outGlow;
void main(){outColor=color;outGlow=vec4(0.0,0.0,0.0,color.a);}";

        _api.Shader.RegisterMemoryShaderProgram("colliderslib_entity_wireframe", program);
        _shader = program;
        bool success = program.Compile();
        if (!success) _api.Logger.Error("[CollidersLib] EntityCollidersBoxRenderer: Failed to compile wireframe shader.");
        return success;
    }

    private static (byte R, byte G, byte B, byte A) GetColor(ShapeElementCollider collider)
    {
        return FromColor4(collider.Color);
    }

    private static (byte R, byte G, byte B, byte A) FromColor4(Color4 color) => ((byte)(255 * color.R), (byte)(255 * color.G), (byte)(255 * color.B), (byte)(255 * color.A));
}