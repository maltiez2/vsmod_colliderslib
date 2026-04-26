using CollidersLib.Items;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CollidersLib.DevTools;

public sealed class HeldItemCapsuleRenderer : IRenderer
{
    public HeldItemCapsuleRenderer(ICoreClientAPI clientApi) { _clientApi = clientApi; _clientApi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "helditemcapsulerenderer"); _clientApi.Event.ReloadShader += LoadShader; LoadShader(); InitializeGraphicsProcessingUnitObjects(); }


    public double RenderOrder => 1.0;
    public int RenderRange => int.MaxValue;

    public static bool RenderColliders { get; set; } = false;


    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!RenderColliders) return;
        if (_shaderProgram == null) return;
        EntityPlayer? player = _clientApi.World.Player?.Entity;
        if (player == null) return;
        _cameraOrigin = new Vector3d(player.CameraPos.X, player.CameraPos.Y, player.CameraPos.Z);
        _vertexCount = 0;
        BuildCollidersForSlot(player, player.RightHandItemSlot, true);
        BuildCollidersForSlot(player, player.LeftHandItemSlot, false);
        if (_vertexCount == 0) return;
        UploadAndDraw();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _clientApi.Event.ReloadShader -= LoadShader;
        _clientApi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        GL.DeleteVertexArray(_vertexArrayObject);
        GL.DeleteBuffer(_vertexBufferObject);
        _shaderProgram?.Dispose();
    }



    private const int _ringSegments = 16;
    private const int _longitudinalLines = 8;
    private const int _hemisphereArcSegments = 8;
    private const int _maxCapsules = 8;
    private const int _verticesPerCapsule = _ringSegments * 2 * 2 + _longitudinalLines * 2 + 2 * 2 * _hemisphereArcSegments * 2;
    private const int _maxVertices = _maxCapsules * _verticesPerCapsule;
    private const int _vertexStride = 20;

    private readonly ICoreClientAPI _clientApi;
    private IShaderProgram? _shaderProgram;
    private int _vertexArrayObject;
    private int _vertexBufferObject;
    private readonly WireframeVertex[] _vertexArray = new WireframeVertex[_maxVertices];
    private int _vertexCount = 0;
    private Vector3d _cameraOrigin;
    private bool _isDisposed = false;

    private static readonly (byte R, byte G, byte B, byte A) _colorAxis = (255, 220, 0, 255);
    private static readonly (byte R, byte G, byte B, byte A) _colorCapsule = (0, 200, 255, 255);


    private bool LoadShader()
    {
        _shaderProgram?.Dispose();
        IShaderProgram shaderProgram = _clientApi.Shader.NewShaderProgram();
        shaderProgram.VertexShader = _clientApi.Shader.NewShader(EnumShaderType.VertexShader);
        shaderProgram.FragmentShader = _clientApi.Shader.NewShader(EnumShaderType.FragmentShader);

        shaderProgram.VertexShader.Code = @"#version 330 core
#extension GL_ARB_explicit_attrib_location: enable
layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec4 vertexColor;
layout(location = 2) in int renderFlags;
uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 colorIn;
out vec4 color;
void main(void)
{
    vec4 cameraPosition = modelViewMatrix * vec4(vertexPositionIn, 1.0);
    color = vertexColor * colorIn;
    gl_Position = projectionMatrix * cameraPosition;
}";

        shaderProgram.FragmentShader.Code = @"#version 330 core
#extension GL_ARB_explicit_attrib_location: enable
in vec4 color;
layout(location = 0) out vec4 outputColor;
layout(location = 1) out vec4 outputGlow;
void main()
{
    outputColor = color;
    outputGlow = vec4(0.0, 0.0, 0.0, color.a);
}";

        _clientApi.Shader.RegisterMemoryShaderProgram("colliderslib_wireframe", shaderProgram);
        _shaderProgram = shaderProgram;

        bool success = shaderProgram.Compile();
        if (!success) _clientApi.Logger.Error("[CollidersLib] Failed to compile wireframe shader.");
        return success;
    }

    private void InitializeGraphicsProcessingUnitObjects()
    {
        _vertexArrayObject = GL.GenVertexArray();
        _vertexBufferObject = GL.GenBuffer();
        GL.BindVertexArray(_vertexArrayObject);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
        GL.BufferData(BufferTarget.ArrayBuffer, _maxVertices * _vertexStride, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, _vertexStride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, _vertexStride, 12);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribIPointer(2, 1, VertexAttribIntegerType.Int, _vertexStride, 16);
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    private void BuildCollidersForSlot(EntityPlayer player, ItemSlot slot, bool isMainHand)
    {
        if (slot?.Itemstack?.Collectible == null) return;
        ItemCollidersBehaviorClient? behavior = slot.Itemstack.Collectible.GetCollectibleBehavior<ItemCollidersBehaviorClient>(true);
        if (behavior == null) return;
        foreach (ItemCapsuleCollider collider in behavior.Colliders.Values)
        {
            if (!collider.TransformCollider(player, isMainHand, false, updatePrevious: false)) continue;
            if (_vertexCount + _verticesPerCapsule > _maxVertices) break;
            BuildCapsule(collider.InWorldCollider, collider.Radius);
        }
    }

    private void BuildCapsule(LineSegmentCollider segment, float radius)
    {
        Vector3d tail = segment.Position;
        Vector3d head = segment.Position + segment.Direction;
        Vector3d axis = segment.Direction;
        double axisLength = axis.Length;
        Vector3d axisNormalized = axisLength > 1e-6 ? axis / axisLength : Vector3d.UnitY;
        GetPerpendicularBasis(axisNormalized, out Vector3d basisU, out Vector3d basisV);
        AddLine(tail, head, _colorAxis);
        AddRing(tail, basisU, basisV, radius, _colorCapsule);
        AddRing(head, basisU, basisV, radius, _colorCapsule);
        AddHemisphereArcs(tail, -axisNormalized, basisU, basisV, radius, _colorCapsule);
        AddHemisphereArcs(head, axisNormalized, basisU, basisV, radius, _colorCapsule);
        AddLongitudinalLines(tail, head, basisU, basisV, radius, _colorCapsule);
    }

    private void AddRing(Vector3d center, Vector3d basisU, Vector3d basisV, float radius, (byte R, byte G, byte B, byte A) color)
    {
        for (int i = 0; i < _ringSegments; i++)
        {
            double angleA = 2.0 * Math.PI * i / _ringSegments;
            double angleB = 2.0 * Math.PI * (i + 1) / _ringSegments;
            Vector3d a = center + radius * (Math.Cos(angleA) * basisU + Math.Sin(angleA) * basisV);
            Vector3d b = center + radius * (Math.Cos(angleB) * basisU + Math.Sin(angleB) * basisV);
            AddLine(a, b, color);
        }
    }

    private void AddHemisphereArcs(Vector3d center, Vector3d pole, Vector3d basisU, Vector3d basisV, float radius, (byte R, byte G, byte B, byte A) color)
    {
        int arcCount = _longitudinalLines / 2;
        for (int i = 0; i < arcCount; i++)
        {
            double angle = Math.PI * i / arcCount;
            Vector3d radial = Math.Cos(angle) * basisU + Math.Sin(angle) * basisV;
            AddHemisphereArc(center, pole, radial, radius, color);
        }
    }

    private void AddHemisphereArc(Vector3d center, Vector3d pole, Vector3d radial, float radius, (byte R, byte G, byte B, byte A) color)
    {
        for (int i = 0; i < _hemisphereArcSegments; i++)
        {
            double angleA = Math.PI * i / _hemisphereArcSegments;
            double angleB = Math.PI * (i + 1) / _hemisphereArcSegments;
            Vector3d a = center + radius * (-Math.Cos(angleA) * radial + Math.Sin(angleA) * pole);
            Vector3d b = center + radius * (-Math.Cos(angleB) * radial + Math.Sin(angleB) * pole);
            AddLine(a, b, color);
        }
    }

    private void AddLongitudinalLines(Vector3d tail, Vector3d head, Vector3d basisU, Vector3d basisV, float radius, (byte R, byte G, byte B, byte A) color)
    {
        for (int i = 0; i < _longitudinalLines; i++)
        {
            double angle = 2.0 * Math.PI * i / _longitudinalLines;
            Vector3d offset = radius * (Math.Cos(angle) * basisU + Math.Sin(angle) * basisV);
            AddLine(tail + offset, head + offset, color);
        }
    }

    private void AddLine(Vector3d a, Vector3d b, (byte R, byte G, byte B, byte A) color)
    {
        Vector3d relativeA = a - _cameraOrigin;
        Vector3d relativeB = b - _cameraOrigin;
        _vertexArray[_vertexCount++] = new WireframeVertex((float)relativeA.X, (float)relativeA.Y, (float)relativeA.Z, color.R, color.G, color.B, color.A);
        _vertexArray[_vertexCount++] = new WireframeVertex((float)relativeB.X, (float)relativeB.Y, (float)relativeB.Z, color.R, color.G, color.B, color.A);
    }

    private void UploadAndDraw()
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBufferObject);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, _vertexCount * _vertexStride, _vertexArray);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        _shaderProgram!.Use();
        _shaderProgram.UniformMatrix("projectionMatrix", _clientApi.Render.CurrentProjectionMatrix);
        _shaderProgram.UniformMatrix("modelViewMatrix", _clientApi.Render.CameraMatrixOriginf);
        _shaderProgram.Uniform("colorIn", new Vec4f(1f, 1f, 1f, 1f));
        GL.BindVertexArray(_vertexArrayObject);
        GL.DrawArrays(PrimitiveType.Lines, 0, _vertexCount);
        GL.BindVertexArray(0);
        _shaderProgram.Stop();
    }

    private static void GetPerpendicularBasis(Vector3d axis, out Vector3d u, out Vector3d v)
    {
        Vector3d helper = Math.Abs(Vector3d.Dot(axis, Vector3d.UnitX)) < 0.9 ? Vector3d.UnitX : Vector3d.UnitY;
        u = Vector3d.Normalize(Vector3d.Cross(axis, helper));
        v = Vector3d.Cross(axis, u);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WireframeVertex
    {
        public float X, Y, Z;
        public byte R, G, B, A;
        public int RenderFlags;
        public WireframeVertex(float x, float y, float z, byte r, byte g, byte b, byte a) { X = x; Y = y; Z = z; R = r; G = g; B = b; A = a; RenderFlags = 0; }
    }
}