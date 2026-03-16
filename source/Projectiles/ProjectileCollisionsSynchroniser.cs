using OpenTK.Mathematics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CollidersLib.Projectiles;

public class ProjectileCollisionsPacket
{
    public long ProjectileEntityId { get; set; }
    public Dictionary<long, ProjectileEntityCollisionPacketData[]> EntityCollisions { get; set; } = [];
    public List<ProjectileTerrainCollisionPacketData> TerrainCollisions { get; set; } = [];
}

public struct ProjectileEntityCollisionPacketData
{
    public string ColliderElementName { get; set; }
    public double IntersectionPointX { get; set; }
    public double IntersectionPointY { get; set; }
    public double IntersectionPointZ { get; set; }
    public double PositionInTime { get; set; }
}

public struct ProjectileTerrainCollisionPacketData
{
    public int BlockId { get; set; }
    public int FacingIndex { get; set; }
    public double NormalX { get; set; }
    public double NormalY { get; set; }
    public double NormalZ { get; set; }
    public double IntersectionPointX { get; set; }
    public double IntersectionPointY { get; set; }
    public double IntersectionPointZ { get; set; }
    public int BlockPositionX { get; set; }
    public int BlockPositionY { get; set; }
    public int BlockPositionZ { get; set; }
    public double PositionInTime { get; set; }
}


public class ProjectileCollisionsSynchroniserServer
{
}

public class ProjectileCollisionsSynchroniserClient
{
}
