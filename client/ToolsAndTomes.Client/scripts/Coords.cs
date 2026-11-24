using Godot;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Client;

/// <summary>Conversions between Core's tabletop plane (x, y) and Godot's 3D world (x, 0, z).</summary>
public static class Coords
{
    public static Vector3 ToWorld(Vec2 p, float height = 0f) => new((float)p.X, height, (float)p.Y);

    public static Vec2 ToTable(Vector3 p) => new(p.X, p.Z);

    public static Color FactionColor(Faction faction) =>
        faction == Faction.Player ? new Color(0.25f, 0.65f, 1.0f) : new Color(1.0f, 0.45f, 0.2f);

    public static StandardMaterial3D Flat(Color color, bool unshaded = false, float alpha = 1f)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(color, alpha),
            Roughness = 0.85f
        };
        if (unshaded)
            material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        if (alpha < 1f)
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return material;
    }
}
