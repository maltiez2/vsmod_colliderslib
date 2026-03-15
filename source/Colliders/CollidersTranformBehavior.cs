using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CollidersLib;

public class CollidersTranformBehavior : EntityBehavior
{
    public CollidersTranformBehavior(Entity entity) : base(entity)
    {
        Api = entity.Api as ICoreClientAPI ?? throw new InvalidOperationException("CollidersTranformBehavior is client side behavior");
        Player = entity as EntityPlayer ?? throw new InvalidOperationException("CollidersTranformBehavior can be attached only to EntityPlayer");
    }

    public override string PropertyName() => "CollidersTranformBehavior";

    public long TickCounter { get; protected set; } = 0;

    public Matrixf? GetHeldItemModelMatrix(bool mainHand = true)
    {
        if (TryGetCachedModelMatrix(mainHand, out var modelMatrix))
        {
            return modelMatrix;
        }

        modelMatrix = GenerateModelMatrix(mainHand);

        if (mainHand)
        {
            CachedMainHandModelMatrix = modelMatrix;
            LastMainHandItem = Player.RightHandItemSlot.Itemstack?.Collectible?.Id ?? 0;
        }
        else
        {
            CachedOffHandModelMatrix = modelMatrix;
            LastOffhandHandItem = Player.LeftHandItemSlot.Itemstack?.Collectible?.Id ?? 0;
        }

        return modelMatrix;
    }

    public override void OnGameTick(float deltaTime)
    {
        TickCounter++;
    }



    protected ICoreClientAPI Api;
    protected EntityPlayer Player;
    protected long LastTick = 0;
    protected long LastMainHandItem = 0;
    protected long LastOffhandHandItem = 0;
    protected Matrixf? CachedMainHandModelMatrix;
    protected Matrixf? CachedOffHandModelMatrix;

    protected virtual bool TryGetCachedModelMatrix(bool mainHand, out Matrixf? modelMatrix)
    {
        modelMatrix = null;

        if (LastTick != TickCounter)
        {
            return false;
        }

        ItemSlot activeSlot = mainHand ? Player.RightHandItemSlot : Player.LeftHandItemSlot;
        long lastItem = mainHand ? LastMainHandItem : LastOffhandHandItem;

        if (activeSlot.Itemstack?.Collectible?.Id != lastItem)
        {
            return false;
        }

        modelMatrix = mainHand ? CachedMainHandModelMatrix : CachedOffHandModelMatrix;

        return true;
    }

    protected virtual Matrixf? GenerateModelMatrix(bool mainHand)
    {
        if (mainHand)
        {
            return ColliderTools.GetHeldItemModelMatrix(Player, Player.RightHandItemSlot, Api, mainHand: true);
        }
        else
        {
            return ColliderTools.GetHeldItemModelMatrix(Player, Player.LeftHandItemSlot, Api, mainHand: false);
        }
    }
}
