using CollidersLib.Items;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CollidersLib;

public class HeldItemCapsuleRenderer : IRenderer
{
    public double RenderOrder => 1.0;
    public int RenderRange => int.MaxValue;

    private const int RingSegments = 16;
    private const int LongitudinalLines = 8;
    private const int HemisphereArcSegments = 8; // per arc, 2 arcs per cap

    // Vertices per capsule:
    // Ring: RingSegments lines * 2 verts
    // Longitudinal: LongitudinalLines * 2 verts
    // Each cap: 2 arcs * HemisphereArcSegments lines * 2 verts
    // Total per capsule: (RingSegments*2 + LongitudinalLines*2)*2 + 2*2*HemisphereArcSegments*2*2
    // We just preallocate a generous buffer.
    private const int MaxCapsules = 8;
    private const int VertsPerCapsule =
        RingSegments * 2 * 2                       // two rings
        + LongitudinalLines * 2                     // longitudinal lines
        + 2 * 2 * HemisphereArcSegments * 2;        // two caps, two arcs each

    private const int MaxVerts = MaxCapsules * VertsPerCapsule;

    private readonly ICoreClientAPI _api;
    private IShaderProgram? _shader;

    // GPU objects
    private int _vao;
    private int _vbo;

    // CPU-side staging buffer
    private readonly WireframeVertex[] _vertices = new WireframeVertex[MaxVerts];
    private int _vertexCount = 0;

    private bool _disposed = false;

    // ── Vertex layout matching the shader ──────────────────────────────────────
    // location 0: vec3  position   (12 bytes)
    // location 1: vec4  color      (4 bytes, packed as 4 ubytes normalized)
    // location 2: int   renderFlags(4 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WireframeVertex
    {
        public float X, Y, Z;       // vertexPositionIn
        public byte R, G, B, A;     // vertexColor  (normalized)
        public int RenderFlags;     // renderFlags

        public WireframeVertex(float x, float y, float z, byte r, byte g, byte b, byte a)
        {
            X = x; Y = y; Z = z;
            R = r; G = g; B = b; A = a;
            RenderFlags = 0;
        }
    }

    private const int Stride = 20; // 12 + 4 + 4

    // ── Colors ─────────────────────────────────────────────────────────────────
    private static readonly (byte R, byte G, byte B, byte A) ColorAxis = (255, 220, 0, 255);
    private static readonly (byte R, byte G, byte B, byte A) ColorCapsule = (0, 200, 255, 255);

    public HeldItemCapsuleRenderer(ICoreClientAPI api)
    {
        _api = api;
        _api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "helditemcapsulerenderer");
        _api.Event.ReloadShader += LoadShader;
        LoadShader();
        InitGpuObjects();
    }

    // ── Shader ─────────────────────────────────────────────────────────────────

    private bool LoadShader()
    {
        _shader?.Dispose();

        IShaderProgram prog = _api.Shader.NewShaderProgram();
        prog.VertexShader = _api.Shader.NewShader(EnumShaderType.VertexShader);
        prog.FragmentShader = _api.Shader.NewShader(EnumShaderType.FragmentShader);

        prog.VertexShader.Code = @"
#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPositionIn;
layout(location = 1) in vec4 vertexColor;
layout(location = 2) in int renderFlags;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;
uniform vec4 colorIn;
uniform vec3 origin;

out vec4 color;

void main(void)
{
    vec4 worldPos = vec4(vertexPositionIn + origin, 1.0);
    vec4 cameraPos = modelViewMatrix * worldPos;

    color = vertexColor * colorIn;

    gl_Position = projectionMatrix * cameraPos;

    // Match vanilla: push vertices slightly closer to camera
    // so wireframes render on top of geometry
    gl_Position.w += 0.0014 + (renderFlags >> 8) * 0.00025;
}
";

        prog.FragmentShader.Code = @"
#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

in vec4 color;

layout(location = 0) out vec4 outColor;
layout(location = 1) out vec4 outGlow;

void main()
{
    outColor = color;
    outGlow  = vec4(0.0, 0.0, 0.0, color.a);
}
";

        _api.Shader.RegisterMemoryShaderProgram("colliderslib_wireframe", prog);
        _shader = prog;

        bool ok = prog.Compile();
        if (!ok)
            _api.Logger.Error("[CollidersLib] Failed to compile wireframe shader.");

        return ok;
    }

    // ── GPU object setup ───────────────────────────────────────────────────────

    private void InitGpuObjects()
    {
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        // Allocate GPU buffer (dynamic, updated every frame)
        GL.BufferData(BufferTarget.ArrayBuffer, MaxVerts * Stride, IntPtr.Zero, BufferUsageHint.DynamicDraw);

        // location 0 – position (vec3 float)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Stride, 0);

        // location 1 – color (vec4 ubyte normalized)
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, Stride, 12);

        // location 2 – renderFlags (int)
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribIPointer(2, 1, VertexAttribIntegerType.Int, Stride, (IntPtr)16);

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    // ── IRenderer ──────────────────────────────────────────────────────────────

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_shader == null) return;

        EntityPlayer? player = _api.World.Player?.Entity;
        if (player == null) return;

        _vertexCount = 0;

        BuildCollidersForSlot(player, player.RightHandItemSlot, mainHand: true);
        BuildCollidersForSlot(player, player.LeftHandItemSlot, mainHand: false);

        if (_vertexCount == 0) return;

        UploadAndDraw(player);
    }

    // ── Mesh building ──────────────────────────────────────────────────────────

    private void BuildCollidersForSlot(EntityPlayer player, ItemSlot slot, bool mainHand)
    {
        if (slot?.Itemstack?.Collectible == null) return;

        ItemCollidersBehaviorClient? behavior =
            slot.Itemstack.Collectible.GetCollectibleBehavior<ItemCollidersBehaviorClient>(withInheritance: true);

        if (behavior == null) return;

        foreach (ItemCapsuleCollider collider in behavior.Colliders.Values)
        {
            if (!collider.TransformCollider(player, mainHand, resetPreviousCollider: false))
                continue;

            if (_vertexCount + VertsPerCapsule > MaxVerts) break;

            BuildCapsule(collider.InWorldCollider, collider.Radius);
        }
    }

    private void BuildCapsule(LineSegmentCollider segment, float radius)
    {
        Vector3d tail = segment.Position;
        Vector3d head = segment.Position + segment.Direction;

        Vector3d axis = segment.Direction;
        double axisLen = axis.Length;
        Vector3d axisNorm = axisLen > 1e-6 ? axis / axisLen : Vector3d.UnitY;

        GetPerpendicularBasis(axisNorm, out Vector3d basisU, out Vector3d basisV);

        // Central axis
        AddLine(tail, head, ColorAxis);

        // End rings
        AddRing(tail, basisU, basisV, radius, ColorCapsule);
        AddRing(head, basisU, basisV, radius, ColorCapsule);

        // Hemisphere arcs
        AddHemisphereArcs(tail, -axisNorm, basisU, basisV, radius, ColorCapsule);
        AddHemisphereArcs(head, axisNorm, basisU, basisV, radius, ColorCapsule);

        // Longitudinal connecting lines
        AddLongitudinalLines(tail, head, basisU, basisV, radius, ColorCapsule);
    }

    private void AddRing(Vector3d center, Vector3d basisU, Vector3d basisV, float radius,
        (byte R, byte G, byte B, byte A) color)
    {
        for (int i = 0; i < RingSegments; i++)
        {
            double angleA = 2.0 * Math.PI * i / RingSegments;
            double angleB = 2.0 * Math.PI * (i + 1) / RingSegments;

            Vector3d a = center + radius * (Math.Cos(angleA) * basisU + Math.Sin(angleA) * basisV);
            Vector3d b = center + radius * (Math.Cos(angleB) * basisU + Math.Sin(angleB) * basisV);

            AddLine(a, b, color);
        }
    }

    private void AddHemisphereArcs(Vector3d center, Vector3d pole, Vector3d basisU, Vector3d basisV,
        float radius, (byte R, byte G, byte B, byte A) color)
    {
        AddHemisphereArc(center, pole, basisU, radius, color);
        AddHemisphereArc(center, pole, basisV, radius, color);
    }

    private void AddHemisphereArc(Vector3d center, Vector3d pole, Vector3d radial, float radius,
        (byte R, byte G, byte B, byte A) color)
    {
        for (int i = 0; i < HemisphereArcSegments; i++)
        {
            double angleA = Math.PI * i / HemisphereArcSegments;
            double angleB = Math.PI * (i + 1) / HemisphereArcSegments;

            Vector3d a = center + radius * (Math.Sin(angleA) * radial + Math.Cos(angleA) * pole);
            Vector3d b = center + radius * (Math.Sin(angleB) * radial + Math.Cos(angleB) * pole);

            AddLine(a, b, color);
        }
    }

    private void AddLongitudinalLines(Vector3d tail, Vector3d head, Vector3d basisU, Vector3d basisV,
        float radius, (byte R, byte G, byte B, byte A) color)
    {
        for (int i = 0; i < LongitudinalLines; i++)
        {
            double angle = 2.0 * Math.PI * i / LongitudinalLines;
            Vector3d offset = radius * (Math.Cos(angle) * basisU + Math.Sin(angle) * basisV);

            AddLine(tail + offset, head + offset, color);
        }
    }

    // ── Primitive helpers ──────────────────────────────────────────────────────

    private void AddLine(Vector3d a, Vector3d b, (byte R, byte G, byte B, byte A) color)
    {
        _vertices[_vertexCount++] = new WireframeVertex(
            (float)a.X, (float)a.Y, (float)a.Z,
            color.R, color.G, color.B, color.A);

        _vertices[_vertexCount++] = new WireframeVertex(
            (float)b.X, (float)b.Y, (float)b.Z,
            color.R, color.G, color.B, color.A);
    }

    // ── Upload & draw ──────────────────────────────────────────────────────────

    private void UploadAndDraw(EntityPlayer player)
    {
        var originVec = player.Pos.XYZFloat;

        for (int i = 0; i < _vertexCount; i++)
        {
            _vertices[i].X -= originVec.X;
            _vertices[i].Y -= originVec.Y;
            _vertices[i].Z -= originVec.Z;
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
            _vertexCount * Stride, _vertices);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        _shader!.Use();

        _shader.UniformMatrix("projectionMatrix", _api.Render.CurrentProjectionMatrix);
        _shader.UniformMatrix("modelViewMatrix", _api.Render.CurrentModelviewMatrix);
        _shader.Uniform("origin", originVec);
        _shader.Uniform("colorIn", new Vec4f(1f, 1f, 1f, 1f));

        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Lines, 0, _vertexCount);
        GL.BindVertexArray(0);

        _shader.Stop();
    }

    // ── Math helpers ───────────────────────────────────────────────────────────

    private static void GetPerpendicularBasis(Vector3d axis, out Vector3d u, out Vector3d v)
    {
        Vector3d helper = Math.Abs(Vector3d.Dot(axis, Vector3d.UnitX)) < 0.9
            ? Vector3d.UnitX
            : Vector3d.UnitY;

        u = Vector3d.Normalize(Vector3d.Cross(axis, helper));
        v = Vector3d.Cross(axis, u);
    }

    // ── Disposal ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _api.Event.ReloadShader -= LoadShader;
        _api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);

        GL.DeleteVertexArray(_vao);
        GL.DeleteBuffer(_vbo);

        _shader?.Dispose();
    }
}