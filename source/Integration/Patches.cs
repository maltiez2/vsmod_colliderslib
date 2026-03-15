using HarmonyLib;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CollidersLib;

internal static class HarmonyPatches
{
    public static Settings ClientSettings { get; set; } = new();
    public static Settings ServerSettings { get; set; } = new();

    public static void Patch(string harmonyId, ICoreAPI api)
    {
        new Harmony(harmonyId).Patch(
                typeof(AnimationManager).GetMethod("OnClientFrame", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(CreateColliders)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(DoRender3DOpaque)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(DoRender3DOpaquePlayer)))
            );
    }

    public static void Unpatch(string harmonyId, ICoreAPI api)
    {
        new Harmony(harmonyId).Unpatch(typeof(AnimationManager).GetMethod("OnClientFrame", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
    }

    private static readonly FieldInfo? _entity = typeof(AnimationManager).GetField("entity", BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool CreateColliders(AnimationManager __instance, float dt)
    {
        EntityPlayer? entity = (Entity?)_entity?.GetValue(__instance) as EntityPlayer;

        if (entity?.Api?.Side != EnumAppSide.Client) return true;

        ClientAnimator? animator = __instance.Animator as ClientAnimator;

        if (animator == null) return true;

        return true;
    }

    private static void DoRender3DOpaque(EntityShapeRenderer __instance, float dt, bool isShadowPass)
    {
        try
        {
            CollidersEntityBehavior behavior = __instance.entity?.GetBehavior<CollidersEntityBehavior>();
            behavior?.Render(__instance.entity?.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
        }
        catch (Exception)
        {
            // just ignore
        }

    }

    private static void DoRender3DOpaquePlayer(EntityPlayerShapeRenderer __instance, float dt, bool isShadowPass)
    {
        try
        {
            CollidersEntityBehavior behavior = __instance.entity?.GetBehavior<CollidersEntityBehavior>();
            behavior?.Render(__instance.entity?.Api as ICoreClientAPI, __instance.entity as EntityAgent, __instance);
        }
        catch (Exception)
        {
            // just ignore
        }
    }
}