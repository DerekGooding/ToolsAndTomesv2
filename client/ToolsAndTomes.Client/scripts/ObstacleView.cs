using Godot;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Client;

/// <summary>
/// Placeholder obstacles: Tall ones as stone columns, boulders, and walls; Low ones as short rubble mounds and
/// timber barricades; conjured Iron Walls in dark iron. They darken as durability falls and are rebuilt whenever
/// the terrain changes (a wall raised, damaged, wrecked, or left as rubble).
/// </summary>
public partial class ObstacleView : Node3D
{
    private const float TallHeight = 2.6f;
    private const float LowHeight = 0.6f;

    private int _builtVersion = -1;
    private int _builtCount = -1;

    public void Refresh(Terrain terrain)
    {
        if (terrain.Version == _builtVersion && terrain.Obstacles.Count == _builtCount)
            return;
        _builtVersion = terrain.Version;
        _builtCount = terrain.Obstacles.Count;

        foreach (var child in GetChildren())
            child.QueueFree();
        foreach (var obstacle in terrain.Obstacles)
            AddChild(Build(obstacle));
    }

    private static Node3D Build(Obstacle o)
    {
        var def = o.Definition;
        float height = def.Height == ObstacleHeight.Tall ? TallHeight : LowHeight;
        float health = def.Durability <= 0 ? 1f : Mathf.Clamp((float)o.Durability / def.Durability, 0f, 1f);
        var baseColor = def.Id switch
        {
            "iron_wall" => new Color(0.22f, 0.24f, 0.28f),
            _ when def.IsRubble => new Color(0.42f, 0.38f, 0.33f),
            _ when !def.Hard => new Color(0.5f, 0.36f, 0.22f),
            _ => new Color(0.55f, 0.56f, 0.6f)
        };
        // Damage darkens it toward soot.
        var color = baseColor.Lerp(new Color(0.12f, 0.1f, 0.1f), (1f - health) * 0.6f);
        var material = Coords.Flat(color);

        var node = new Node3D { Name = o.Id };
        float halfWidth = (float)o.HalfWidth;
        var a = Coords.ToWorld(o.A);
        var b = Coords.ToWorld(o.B);
        float length = a.DistanceTo(b);

        if (length < 0.01f)
        {
            Mesh mesh = def.IsRubble
                ? new SphereMesh { Radius = halfWidth, Height = height * 1.2f, RadialSegments = 10, Rings = 5 }
                : new CylinderMesh { TopRadius = halfWidth * 0.92f, BottomRadius = halfWidth, Height = height, RadialSegments = 16 };
            node.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = a + Vector3.Up * (def.IsRubble ? height * 0.25f : height / 2f) });
            return node;
        }

        // A wall: a box along a–b, with rounded (cylinder) ends matching its capsule footprint.
        var mid = (a + b) / 2f;
        var dir = b - a;
        var box = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(halfWidth * 2f, height, length) },
            MaterialOverride = material,
            Position = mid + Vector3.Up * (height / 2f),
            Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Z), 0) // the box's length runs along its local Z
        };
        node.AddChild(box);
        foreach (var end in new[] { a, b })
            node.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = halfWidth, BottomRadius = halfWidth, Height = height, RadialSegments = 12 },
                MaterialOverride = material,
                Position = end + Vector3.Up * (height / 2f)
            });
        return node;
    }
}
