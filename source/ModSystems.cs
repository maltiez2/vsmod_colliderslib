using Vintagestory.API.Common;

namespace CollidersLib;

public sealed class CollidersLibSystem : ModSystem
{
    public Settings Settings { get; private set; } = new();

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CollidersLib:EntityColliders", typeof(CollidersEntityBehavior));
        api.RegisterEntityBehaviorClass("CollidersLib:CollidersTranform", typeof(CollidersTranformBehavior));

        HarmonyPatches.Patch("CollidersLib", api);

        _api = api;
    }

    public override void Dispose()
    {
        if (_api == null) return;
        
        HarmonyPatches.Unpatch("CollidersLib", _api);

        _api = null;
    }

    private ICoreAPI? _api;
}