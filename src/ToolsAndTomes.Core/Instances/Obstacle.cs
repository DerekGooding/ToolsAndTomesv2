using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Instances;

/// <summary>
/// An obstacle on the field: a circle (<see cref="A"/> = center) or a wall (<see cref="A"/>–<see cref="B"/>),
/// with the shape's half-width (<see cref="HalfWidth"/>: radius, or half the thickness). Geometrically every
/// obstacle is a capsule: the set of points within <see cref="HalfWidth"/> of the segment A–B (A = B for a circle).
/// </summary>
public sealed class Obstacle
{
    public string Id { get; }
    public ObstacleDefinition Definition { get; }
    public Vec2 A { get; }
    public Vec2 B { get; }
    public double HalfWidth { get; }
    public int Durability { get; private set; }

    public bool IsDestroyed => Durability <= 0;
    public bool IsTall => Definition.Height == ObstacleHeight.Tall;

    public Obstacle(string id, ObstacleDefinition definition, Vec2 a, Vec2 b, double halfWidth)
    {
        Id = id;
        Definition = definition;
        A = a;
        B = b;
        HalfWidth = halfWidth;
        Durability = definition.Durability;
    }

    /// <summary>Builds an obstacle from an encounter placement (circle at a point, or wall between two points).</summary>
    public static Obstacle FromPlacement(string id, ObstacleDefinition def, ObstaclePlacement placement) =>
        def.Shape == ObstacleShape.Circle
            ? new Obstacle(id, def, placement.At!.Value, placement.At.Value, def.Radius)
            : new Obstacle(id, def, placement.From!.Value, placement.To!.Value, def.Thickness / 2);

    /// <summary>A copy of this footprint as another kind of obstacle (wreckage becoming rubble).</summary>
    public Obstacle Remade(string id, ObstacleDefinition def) => new(id, def, A, B, HalfWidth);

    /// <summary>Damages the obstacle; returns true if this destroyed it.</summary>
    public bool TakeDamage(int amount)
    {
        if (amount <= 0 || IsDestroyed)
            return false;
        Durability = Math.Max(0, Durability - amount);
        return IsDestroyed;
    }

    /// <summary>Distance from a point to the obstacle's surface (negative inside).</summary>
    public double DistanceTo(Vec2 p) => Geometry.PointSegmentDistance(p, A, B) - HalfWidth;

    /// <summary>Whether the segment p–q passes within <paramref name="clearance"/> of the obstacle.</summary>
    public bool Blocks(Vec2 p, Vec2 q, double clearance = 0) =>
        Geometry.SegmentSegmentDistance(p, q, A, B) < HalfWidth + clearance - Geometry.Epsilon;
}

/// <summary>Small 2D geometry helpers for obstacles (segments and capsules).</summary>
public static class Geometry
{
    public const double Epsilon = 1e-9;

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>The closest point to <paramref name="p"/> on segment a–b.</summary>
    public static Vec2 ClosestOnSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len2 = Dot(ab, ab);
        if (len2 < Epsilon)
            return a;
        double t = Math.Clamp(Dot(p - a, ab) / len2, 0, 1);
        return a + ab * t;
    }

    public static double PointSegmentDistance(Vec2 p, Vec2 a, Vec2 b) => p.DistanceTo(ClosestOnSegment(p, a, b));

    /// <summary>The shortest distance between segments p–q and a–b (0 if they cross).</summary>
    public static double SegmentSegmentDistance(Vec2 p, Vec2 q, Vec2 a, Vec2 b)
    {
        if (SegmentsIntersect(p, q, a, b))
            return 0;
        return Math.Min(
            Math.Min(PointSegmentDistance(p, a, b), PointSegmentDistance(q, a, b)),
            Math.Min(PointSegmentDistance(a, p, q), PointSegmentDistance(b, p, q)));
    }

    public static bool SegmentsIntersect(Vec2 p, Vec2 q, Vec2 a, Vec2 b)
    {
        double d1 = Cross(q - p, a - p), d2 = Cross(q - p, b - p);
        double d3 = Cross(b - a, p - a), d4 = Cross(b - a, q - a);
        return ((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon)) &&
               ((d3 > Epsilon && d4 < -Epsilon) || (d3 < -Epsilon && d4 > Epsilon));
    }
}
