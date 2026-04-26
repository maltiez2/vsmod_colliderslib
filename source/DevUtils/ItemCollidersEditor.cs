using CollidersLib.Items;
using ImGuiNET;
using OverhaulLib.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VSImGui;
using VSImGui.API;

namespace CollidersLib.DevTools;

public sealed class ItemCapsuleColliderEditor
{
    public ItemCapsuleColliderEditor(ICoreClientAPI api)
    {
        _api = api;
        api.ModLoader.GetModSystem<ImGuiModSystem>().Draw += DrawEditor;
        api.Input.RegisterHotKey("CollidersLib:ItemCapsuleColliderEditor", "(Colliders lib) Open colliders editor", GlKeys.PageUp, shiftPressed: true);
        api.Input.SetHotKeyHandler("CollidersLib:ItemCapsuleColliderEditor", _ => _editorOpened = !_editorOpened);
    }


    public void Open() => _editorOpened = true;
    public void Close() => _editorOpened = false;



    private bool _editorOpened = false;
    private readonly ICoreClientAPI _api;


    private CallbackGUIStatus DrawEditor(float deltaSeconds)
    {
        if (!_editorOpened) return CallbackGUIStatus.Closed;

        if (!ImGui.Begin("Held item colliders editor", ref _editorOpened)) return CallbackGUIStatus.Closed;

        IEnumerable<ItemCapsuleCollider> colliders = GetCurrentColliders(_api.World.Player.Entity, _api.World.Player.Entity.ActiveHandItemSlot, true);

        bool shiftPressed = _api.World.Player.Entity.Controls.ShiftKey;

        if (ImGui.Button("Toggle colliders rendering"))
        {
            HeldItemCapsuleRenderer.RenderColliders = !HeldItemCapsuleRenderer.RenderColliders;
            EntityCollidersBoxRenderer.RenderColliders = HeldItemCapsuleRenderer.RenderColliders;
        }

        int index = 0;
        bool first = true;
        foreach (ItemCapsuleCollider collider in colliders)
        {
            if (!first) ImGui.Separator();
            first = false;
            ImGui.Text($"Index: {index}");
            ImGui.SameLine();
            if (ImGui.Button($"Copy JSON##{index}"))
            {
                ImGui.SetClipboardText(ItemCapsuleCollider.ToJson(collider));
            }
            DrawColliderEditor(collider, $"collider{index}", shiftPressed);
            index++;
        }

        return CallbackGUIStatus.GrabMouse;
    }

    private IEnumerable<ItemCapsuleCollider> GetCurrentColliders(EntityPlayer player, ItemSlot slot, bool isMainHand)
    {
        if (slot?.Itemstack?.Collectible == null) return [];

        ItemCollidersBehaviorClient? behavior = slot.Itemstack.Collectible.GetCollectibleBehavior<ItemCollidersBehaviorClient>(true);
        if (behavior == null) return [];

        return behavior.Colliders.Values;
    }

    private void DrawColliderEditor(ItemCapsuleCollider collider, string id, bool shift)
    {
        LineSegmentCollider segment = collider.RelativeCollider;
        System.Numerics.Vector3 position = segment.Position.ToSystem();
        System.Numerics.Vector3 direction = segment.Direction.ToSystem();
        float radius = collider.Radius;

        ImGui.DragFloat($"Radius##{id}", ref radius, !shift ? 0.01f : 0.001f);
        ImGui.DragFloat3($"Position##{id}", ref position, !shift ? 0.01f : 0.001f);
        ImGui.DragFloat3($"Direction##{id}", ref direction, !shift ? 0.01f : 0.001f);

        collider.RelativeCollider = new(position.ToOpenTK(), direction.ToOpenTK());
        collider.Radius = radius;
    }
}
