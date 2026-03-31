using System.Collections.Immutable;
using Vintagestory.API.Common;

namespace CollidersLib.Items;

public sealed class ItemCollisionTester
{
    public bool StopOnTerrainHit { get; set; } = false;
    public bool StopOnEntityHit { get; set; } = false;
    public bool CollideWithTerrain { get; set; } = true;
    public bool HitOnlyOneEntity { get; set; } = false;
    public bool IgnoreTerrainBehindAttacker { get; set; } = false;
    public int[] CollidersInOrderFromClosestToFurthest { get; set; }


    public ItemCollisionTester(ItemCollidersBehaviorClient collidersBehavior)
    {
        _collidersBehavior = collidersBehavior;
        CollidersInOrderFromClosestToFurthest = collidersBehavior.Colliders.Keys.ToArray();
    }

    public void Reset(EntityPlayer attacker, ItemSlot weaponSlot)
    {
        _collidedEntities.Clear();
        _collidersBehavior.ResetColliders(attacker, weaponSlot);
    }

    public List<SingleItemCollisionData> TryCollide(EntityPlayer player, ItemSlot itemSlot, out bool stop)
    {
        List<SingleItemCollisionData> collisionsSorted = _collidersBehavior.CheckForCollisionsInOrder(player, itemSlot, CollidersInOrderFromClosestToFurthest, IgnoreTerrainBehindAttacker);

        List<SingleItemCollisionData> collisionsValidated = ValidateCollisions(collisionsSorted, out stop);

        return collisionsValidated;
    }



    private readonly ItemCollidersBehaviorClient _collidersBehavior;
    private readonly HashSet<long> _collidedEntities = [];

    private List<SingleItemCollisionData> ValidateCollisions(List<SingleItemCollisionData> collisionsSorted, out bool stop)
    {
        List<SingleItemCollisionData> collisions = [];
        stop = false;

        foreach (SingleItemCollisionData collision in collisionsSorted)
        {
            if (collision.BehindTerrain && CollideWithTerrain)
            {
                continue;
            }

            if (collision.TerrainCollision != null && CollideWithTerrain)
            {
                collisions.Add(collision);
                if (StopOnTerrainHit)
                {
                    stop = true;
                    break;
                }
            }
            else if (collision.Target != null && collision.EntityCollision != null)
            {
                if (HitOnlyOneEntity && _collidedEntities.Count > 0)
                {
                    continue;
                }

                if (_collidedEntities.Contains(collision.Target.EntityId))
                {
                    continue;
                }

                collisions.Add(collision);
                _collidedEntities.Add(collision.Target.EntityId);
                if (StopOnEntityHit)
                {
                    stop = true;
                    break;
                }
            }
        }

        return collisions;
    }
}
