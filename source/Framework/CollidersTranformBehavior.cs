using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

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
    protected readonly Matrixf MatrixBuffer = new();

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
            return GetHeldItemModelMatrix(Player, Player.RightHandItemSlot, Api, mainHand: true);
        }
        else
        {
            return GetHeldItemModelMatrix(Player, Player.LeftHandItemSlot, Api, mainHand: false);
        }
    }

    protected Matrixf? GetHeldItemModelMatrix(EntityAgent entity, ItemSlot itemSlot, ICoreClientAPI api, bool mainHand = true)
    {
        if (entity.Properties.Client.Renderer is not EntityShapeRenderer entityShapeRenderer) return null;

        ItemStack? itemStack = itemSlot?.Itemstack;
        if (itemStack == null) return null;

        AttachmentPointAndPose? attachmentPointAndPose = entity.AnimManager?.Animator?.GetAttachmentPointPose(mainHand ? "RightHand" : "LeftHand");
        if (attachmentPointAndPose == null) return null;

        AttachmentPoint attachPoint = attachmentPointAndPose.AttachPoint;
        ItemRenderInfo itemStackRenderInfo = api.Render.GetItemStackRenderInfo(itemSlot, mainHand ? EnumItemRenderTarget.HandTp : EnumItemRenderTarget.HandTpOff, 0f);
        if (itemStackRenderInfo?.Transform == null) return null;

        return MatrixBuffer.Set(entityShapeRenderer.ModelMat).Mul(attachmentPointAndPose.AnimModelMatrix).Translate(itemStackRenderInfo.Transform.Origin.X, itemStackRenderInfo.Transform.Origin.Y, itemStackRenderInfo.Transform.Origin.Z)
            .Scale(itemStackRenderInfo.Transform.ScaleXYZ.X, itemStackRenderInfo.Transform.ScaleXYZ.Y, itemStackRenderInfo.Transform.ScaleXYZ.Z)
            .Translate(attachPoint.PosX / 16.0 + itemStackRenderInfo.Transform.Translation.X, attachPoint.PosY / 16.0 + itemStackRenderInfo.Transform.Translation.Y, attachPoint.PosZ / 16.0 + itemStackRenderInfo.Transform.Translation.Z)
            .RotateX((float)(attachPoint.RotationX + itemStackRenderInfo.Transform.Rotation.X) * (MathF.PI / 180f))
            .RotateY((float)(attachPoint.RotationY + itemStackRenderInfo.Transform.Rotation.Y) * (MathF.PI / 180f))
            .RotateZ((float)(attachPoint.RotationZ + itemStackRenderInfo.Transform.Rotation.Z) * (MathF.PI / 180f))
            .Translate(0f - itemStackRenderInfo.Transform.Origin.X, 0f - itemStackRenderInfo.Transform.Origin.Y, 0f - itemStackRenderInfo.Transform.Origin.Z);
    }
}
