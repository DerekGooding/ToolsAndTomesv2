namespace ToolsAndTomes.Core.Instances;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 Zero => new(0, 0);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double DistanceTo(Vec2 other) => (other - this).Length;

    public Vec2 Normalized()
    {
        var len = Length;
        return len < 1e-9 ? Zero : new Vec2(X / len, Y / len);
    }

    public Vec2 Clamp(double minX, double maxX, double minY, double maxY) =>
        new(Math.Clamp(X, minX, maxX), Math.Clamp(Y, minY, maxY));

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 v, double s) => new(v.X * s, v.Y * s);
}
