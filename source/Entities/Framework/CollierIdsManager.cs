using OpenTK.Mathematics;
using System.Collections.Immutable;

namespace CollidersLib;

public sealed class CollierIdsManager
{
    public ImmutableDictionary<int, string> ColliderTypeById { get; private set; } = [];
    public ImmutableDictionary<string, int> ColliderIdByType { get; private set; } = [];
    public ImmutableDictionary<string, Color4> ColorByType { get; private set; } = [];

    public void ReapplyConfigs(IEnumerable<CollidersConfig> configs, out Dictionary<string, int> shapeElementsToColliderIds)
    {
        shapeElementsToColliderIds = [];
        Dictionary<string, Color4> colors = [];
        foreach (CollidersConfig config in configs)
        {
            foreach ((string colliderType, string[] elementNames) in config.Elements)
            {
                if (!_colliderIdByType.TryGetValue(colliderType, out int colliderId))
                {
                    colliderId = _lastColliderId++;
                }

                _colliderTypeById[colliderId] = colliderType;
                _colliderIdByType[colliderType] = colliderId;

                foreach (string elementName in elementNames)
                {
                    shapeElementsToColliderIds[elementName] = colliderId;
                }
            }

            foreach ((string collierTypeName, string color) in config.Colors)
            {
                (byte R, byte G, byte B) = HexToRgb(color);
                colors[collierTypeName] = new Color4(R, G, B, 255);
            }
        }

        // not entirely thread safe, but will do for now
        ColliderTypeById = _colliderTypeById.ToImmutableDictionary();
        ColliderIdByType = _colliderIdByType.ToImmutableDictionary();
        ColorByType = colors.ToImmutableDictionary();
    }

    private int _lastColliderId;
    private readonly Dictionary<int, string> _colliderTypeById = [];
    private readonly Dictionary<string, int> _colliderIdByType = [];


    private static (byte R, byte G, byte B) HexToRgb(string hex)
    {
        hex = hex.TrimStart('#');

        if (hex.Length != 6) throw new ArgumentException("Hex color must be 6 characters long.", nameof(hex));

        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);

        return (r, g, b);
    }
}