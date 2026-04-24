using OpenTK.Mathematics;
using Vintagestory.API.Common;

namespace CollidersLib;


public interface ICollidersDamageSource
{
    Vector3d Position { get; set; }
    int Collider { get; set; }
    ItemStack? Weapon { get; set; }
}