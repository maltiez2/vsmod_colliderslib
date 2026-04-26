using CollidersLib.DevTools;
using CollidersLib.Items;
using CollidersLib.Projectiles;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CollidersLib;

public sealed class CollidersLibSystem : ModSystem
{
    public Settings Settings { get; private set; } = new();

    public ProjectileCollisionsSynchroniserServer? ProjectileCollisionsSynchroniserServer { get; private set; }
    public ProjectileCollisionsSynchroniserClient? ProjectileCollisionsSynchroniserClient { get; private set; }
    public ItemCollisionsSynchroniserServer? ItemCollisionsSynchroniserServer { get; private set; }
    public ItemCollisionsSynchroniserClient? ItemCollisionsSynchroniserClient { get; private set; }


    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass("CollidersLib:EntityColliders", typeof(CollidersEntityBehavior));
        api.RegisterEntityBehaviorClass("CollidersLib:CollidersTranform", typeof(CollidersTranformBehavior));
        api.RegisterEntityBehaviorClass("CollidersLib:ProjectileColliderServer", typeof(ProjectileColliderServerBehavior));
        api.RegisterCollectibleBehaviorClass("CollidersLib:ItemCollidersServer", typeof(ItemCollidersBehaviorServer));
        api.RegisterCollectibleBehaviorClass("CollidersLib:ItemCollidersClient", typeof(ItemCollidersBehaviorClient));

        HarmonyPatches.Patch("CollidersLib", api);

        _api = api;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        ProjectileCollisionsSynchroniserClient = new(api);
        ItemCollisionsSynchroniserClient = new(api);
        _capsuleRenderer = new(api);
        _entityColliderRenderer = new(api);
        _itemCapsuleColliderEditor = new(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        ProjectileCollisionsSynchroniserServer = new(api);
        ItemCollisionsSynchroniserServer = new(api);
    }

    public override void Dispose()
    {
        if (_api == null) return;
        
        HarmonyPatches.Unpatch("CollidersLib", _api);

        _api = null;
        _capsuleRenderer?.Dispose();
        _entityColliderRenderer?.Dispose();
        _capsuleRenderer = null;
        _entityColliderRenderer = null;
    }

    private ICoreAPI? _api;
    private HeldItemCapsuleRenderer? _capsuleRenderer;
    private EntityCollidersBoxRenderer? _entityColliderRenderer;
    private ItemCapsuleColliderEditor? _itemCapsuleColliderEditor;
}