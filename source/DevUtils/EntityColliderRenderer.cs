using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
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
        _api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "entitycollidersboxrenderer");
        _api.Event.ReloadShader += LoadShader;
        LoadShader();
        InitGpuObjects();
    }

    public double RenderOrder => 1.0;
    public int RenderRange => int.MaxValue;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_shader == null) return;
        EntityPlayer? localPlayer = _api.World.Player?.Entity;
        if (localPlayer == null) return;
        _cameraOrigin = new Vector3d(localPlayer.CameraPos.X, localPlayer.CameraPos.Y, localPlayer.CameraPos.Z);
        _vertexCount = 0;
        foreach (Entity entity in _api.World.LoadedEntities.Values)
        {
            if (!entity.IsRendered) continue;
            if (!entity.Alive) continue;
            CollidersEntityBehavior? behavior = entity.GetBehavior<CollidersEntityBehavior>();
            if (behavior == null) continue;
            if (!behavior.HasOBBCollider) continue;
            if (behavior.Colliders.Count == 0) continue;
            bool isLocalPlayer = entity.EntityId == localPlayer.EntityId;
            bool firstPerson = _api.World.Player.CameraMode == EnumCameraMode.FirstPerson;
            if (isLocalPlayer && firstPerson) continue;
            BuildCollidersForEntity(behavior);
        }
        if (_vertexCount == 0) return;
        UploadAndDraw();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _api.Event.ReloadShader -= LoadShader;
        _api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        GL.DeleteVertexArray(_vertexArrayObject);
        GL.DeleteBuffer(_vertexBufferObject);
        _shader?.Dispose();
    }

    private const int _edgesPerBox = 12;
    private const int _verticesPerBox = _edgesPerBox * 2;
    private const int _maxBoxes = 256;
    private const int _maxVertices = _maxBoxes * _verticesPerBox;
    private const int _stride = 20;

    private static readonly (int A, int B)[] _boxEdges =
    [
        (0,1),(0,3),(0,4),(1,2),(1,5),(2,3),(2,6),(3,7),(4,5),(4,7),(5,6),(6,7)
    ];

    private static readonly (byte R, byte G, byte B, byte A)[] _colliderTypeColors =
    [
        (255,255,255,255),(255,0,0,255),(0,255,0,255),(0,0,255,255),(255,255,0,255),(255,0,255,255)
    ];

    private readonly ICoreClientAPI _api;
    private IShaderProgram? _shader;
    private int _vertexArrayObject;
    private int _vertexBufferObject;
    private readonly WireframeVertex[] _vertices = new WireframeVertex[_maxVertices];
    private int _vertexCount = 0;
    private Vector3d _cameraOrigin;
    private bool _disposed = false;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WireframeVertex
    {
        public float X, Y, Z;
        public byte R, G, B, A;
        public int RenderFlags;
        public WireframeVertex(float x, float y, float z, byte r, byte g, byte b, byte a) { X = x; Y = y; Z = z; R = r; G = g; B = b; A = a; RenderFlags = 0; }
    }

    private void BuildCollidersForEntity(CollidersEntityBehavior behavior)
    {
        foreach (ShapeElementCollider collider in behavior.Colliders)
        {
            if (_vertexCount + _verticesPerBox > _maxVertices) return;
            (byte R, byte G, byte B, byte A) color = GetColor(collider.ColliderType);
            BuildBox(collider.InworldVertices, color);
        }
    }

    private void BuildBox(Vector4d[] vertices, (byte R, byte G, byte B, byte A) color)
    {
        foreach ((int a, int b) in _boxEdges)
        {
            Vector3d worldA = new(vertices[a].X, vertices[a].Y, vertices[a].Z);
            Vector3d worldB = new(vertices[b].X, vertices[b].Y, vertices[b].Z);
            AddLine(worldA, worldB, color);
        }
    }

    private void AddLine(Vector3d a, Vector3d b, (byte R, byte G, byte B, byte A) color)
    {
        Vector3d relativeA = a - _cameraOrigin;
        Vector3d relativeB = b - _cameraOrigin;
        _vertices[_vertexCount++] = new WireframeVertex((float)relativeA.X, (float)relativeA.Y, (float)relativeA.Z, color.R, color.G, color.B, color.A);
        _vertices[_vertexCount++] = new WireframeVertex((float)relativeB.X, (float)relativeB.Y, (float)relativeB.Z, color.R, color.G, color.B, color.A);
    }

    private void UploadAndDraw()
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _vertexCount * _stride, _vertices);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        _shader!.Use();
        _shader.UniformMatrix("projectionMatrix", _api.Render.CurrentProjectionMatrix);
        _shader.UniformMatrix("modelViewMatrix", _api.Render.CameraMatrixOriginf);
        _shader.Uniform("colorIn", new Vec4f(1f, 1f, 1f, 1f));
        GL.BindVertexArray(_vertexArrayObject);
        GL.DrawArrays(PrimitiveType.Lines, 0, _vertexCount);
        GL.BindVertexArray(0);
        _shader.Stop();
    }

    private void InitGpuObjects()
    {
        _vertexArrayObject = GL.GenVertexArray();
        _vertexBufferObject = GL.GenBuffer();
        GL.BindVertexArray(_vertexArrayObject);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
        GL.BufferData(BufferTarget.ArrayBuffer, _maxVertices * _stride, IntPtr.Zero, BufferUsageHint.DynamicDraw);
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

    private static (byte R, byte G, byte B, byte A) GetColor(int colliderType)
    {
        if (colliderType >= 0 && colliderType < _colliderTypeColors.Length) return _colliderTypeColors[colliderType];
        return _colliderTypeColors[0];
    }
}