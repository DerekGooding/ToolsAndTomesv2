using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Simulation;

/// <summary>Where a sliding Crest stops, what it hit, and how much of the slide it didn't get to make.</summary>
public readonly record struct SweepResult(Vec2 Stop, Obstacle? Hit, double Travelled, double Shortfall);

/// <summary>
/// The field's obstacles and the spatial rules around them (docs/DESIGN.md §3c): line of fire (Tall obstacles
/// block it), partial cover (Low obstacles near the defender), free space for a Crest's footprint
/// (<see cref="RulesConfig.CrestRadius"/>), slides that stop at obstacles, shortest paths around obstacles, and
/// obstacle damage (wrecks become rubble when the destroying hit leaves rubble). Deterministic.
/// </summary>
public sealed class Terrain
{
    private const int CirclePoints = 12;
    private const double WaypointMargin = 0.05;
    private readonly List<Obstacle> _obstacles = [];
    private readonly ObstacleDefinition? _rubble;
    private int _nextId;

    public double ArenaWidth { get; }
    public double ArenaHeight { get; }
    public RulesConfig Rules { get; }
    public IReadOnlyList<Obstacle> Obstacles => _obstacles;

    /// <summary>Bumped whenever an obstacle appears, changes, or goes away (for presentation).</summary>
    public int Version { get; private set; }

    public double CrestRadius => Rules.CrestRadius;

    /// <summary>An open field with no obstacles (used outside a simulation).</summary>
    public static Terrain Empty { get; } = new([], 1e6, 1e6, RulesConfig.Default, null);

    public Terrain(IEnumerable<Obstacle> obstacles, double arenaWidth, double arenaHeight, RulesConfig rules, ObstacleDefinition? rubble)
    {
        _obstacles.AddRange(obstacles);
        ArenaWidth = arenaWidth;
        ArenaHeight = arenaHeight;
        Rules = rules;
        _rubble = rubble;
        _nextId = _obstacles.Count + 1;
    }

    // ---- Line of fire and cover -------------------------------------------------------------------

    /// <summary>True if no Tall obstacle crosses the line between the two centers.</summary>
    public bool HasLineOfFire(Vec2 from, Vec2 to) => !_obstacles.Any(o => o.IsTall && o.Blocks(from, to));

    /// <summary>Cover dice a defender gets against a direct attack: the best Low obstacle on the line within cover range of the defender.</summary>
    public int CoverDice(Vec2 attacker, Vec2 defender) =>
        _obstacles
            .Where(o => !o.IsTall && o.Definition.CoverDice > 0 && o.Blocks(attacker, defender) && o.DistanceTo(defender) <= Rules.CoverRange)
            .Select(o => o.Definition.CoverDice)
            .DefaultIfEmpty(0)
            .Max();

    // ---- Free space and slides --------------------------------------------------------------------

    /// <summary>Whether a Crest's footprint fits here: inside the arena and clear of every obstacle.</summary>
    public bool IsFree(Vec2 p) =>
        p.X >= CrestRadius - Geometry.Epsilon && p.Y >= CrestRadius - Geometry.Epsilon &&
        p.X <= ArenaWidth - CrestRadius + Geometry.Epsilon && p.Y <= ArenaHeight - CrestRadius + Geometry.Epsilon &&
        _obstacles.All(o => o.DistanceTo(p) >= CrestRadius - 1e-6);

    /// <summary>Whether a Crest can slide straight from a to b without touching an obstacle.</summary>
    public bool SegmentClear(Vec2 a, Vec2 b) => !_obstacles.Any(o => o.Blocks(a, b, CrestRadius));

    /// <summary>
    /// Slides a Crest from <paramref name="from"/> toward <paramref name="to"/> (already clamped to the arena),
    /// stopping where its footprint would first touch an obstacle.
    /// </summary>
    public SweepResult Sweep(Vec2 from, Vec2 to)
    {
        double total = from.DistanceTo(to);
        if (total < Geometry.Epsilon || SegmentClear(from, to))
            return new SweepResult(to, null, total, 0);

        double lo = 0, hi = 1;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (SegmentClear(from, from + (to - from) * mid)) lo = mid;
            else hi = mid;
        }

        var stop = from + (to - from) * lo;
        var ahead = from + (to - from) * Math.Min(1, hi + 1e-6);
        var hit = _obstacles
            .Where(o => o.Blocks(stop, ahead, CrestRadius + 1e-6))
            .OrderBy(o => o.DistanceTo(stop))
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        double travelled = total * lo;
        return new SweepResult(stop, hit, travelled, total - travelled);
    }

    /// <summary>The nearest free point to <paramref name="p"/> (rings outward, in a fixed order), or p if it is free.</summary>
    public Vec2 NearestFree(Vec2 p)
    {
        if (IsFree(p))
            return p;
        for (double r = 0.25; r <= 8; r += 0.25)
            for (int k = 0; k < 16; k++)
            {
                double a = Math.Tau * k / 16;
                var q = new Vec2(p.X + Math.Cos(a) * r, p.Y + Math.Sin(a) * r);
                if (IsFree(q))
                    return q;
            }
        return p;
    }

    /// <summary>The free point closest to <paramref name="to"/> along the straight line back toward <paramref name="from"/> (for blinks).</summary>
    public Vec2 LastFreeAlong(Vec2 from, Vec2 to)
    {
        if (IsFree(to))
            return to;
        double length = from.DistanceTo(to);
        for (double back = 0.1; back < length; back += 0.1)
        {
            var q = to + (from - to).Normalized() * back;
            if (IsFree(q))
                return q;
        }
        return from;
    }

    // ---- Paths --------------------------------------------------------------------------------------

    /// <summary>
    /// The shortest route of straight legs from <paramref name="from"/> to <paramref name="to"/> that keeps the
    /// Crest's footprint clear of obstacles (waypoints only, excluding the start). If the destination is blocked,
    /// the route ends at the nearest free point; if no route exists, it is the straight line (and the Crest
    /// stops at the first obstacle).
    /// </summary>
    public IReadOnlyList<Vec2> FindPath(Vec2 from, Vec2 to)
    {
        var goal = NearestFree(to);
        if (_obstacles.Count == 0 || SegmentClear(from, goal))
            return [goal];

        var nodes = new List<Vec2> { from };
        foreach (var o in _obstacles)
            nodes.AddRange(Waypoints(o));
        nodes.Add(goal);
        int goalIndex = nodes.Count - 1;

        var dist = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
        var prev = Enumerable.Repeat(-1, nodes.Count).ToArray();
        var done = new bool[nodes.Count];
        dist[0] = 0;
        for (int iter = 0; iter < nodes.Count; iter++)
        {
            int u = -1;
            for (int i = 0; i < nodes.Count; i++)
                if (!done[i] && !double.IsPositiveInfinity(dist[i]) && (u < 0 || dist[i] < dist[u]))
                    u = i;
            if (u < 0 || u == goalIndex)
                break;
            done[u] = true;
            for (int v = 0; v < nodes.Count; v++)
            {
                if (done[v] || v == u)
                    continue;
                double d = dist[u] + nodes[u].DistanceTo(nodes[v]);
                if (d < dist[v] - Geometry.Epsilon && SegmentClear(nodes[u], nodes[v]))
                {
                    dist[v] = d;
                    prev[v] = u;
                }
            }
        }

        if (prev[goalIndex] < 0)
            return [goal];

        var path = new List<Vec2>();
        for (int at = goalIndex; at > 0; at = prev[at])
            path.Add(nodes[at]);
        path.Reverse();
        return path;
    }

    /// <summary>Length of a route from <paramref name="from"/> through <paramref name="path"/>.</summary>
    public static double PathLength(Vec2 from, IReadOnlyList<Vec2> path)
    {
        double total = 0;
        var at = from;
        foreach (var p in path)
        {
            total += at.DistanceTo(p);
            at = p;
        }
        return total;
    }

    /// <summary>The point <paramref name="distance"/> along a route.</summary>
    public static Vec2 Walk(Vec2 from, IReadOnlyList<Vec2> path, double distance)
    {
        var at = from;
        foreach (var p in path)
        {
            double leg = at.DistanceTo(p);
            if (distance <= leg)
                return leg < Geometry.Epsilon ? p : at + (p - at) * (distance / leg);
            distance -= leg;
            at = p;
        }
        return at;
    }

    /// <summary>Free points around an obstacle, just outside its footprint inflated by a Crest's radius.</summary>
    private IEnumerable<Vec2> Waypoints(Obstacle o)
    {
        double r = (o.HalfWidth + CrestRadius + WaypointMargin) / Math.Cos(Math.PI / CirclePoints);
        var ends = o.A.DistanceTo(o.B) < Geometry.Epsilon ? new[] { o.A } : new[] { o.A, o.B };
        foreach (var end in ends)
            for (int k = 0; k < CirclePoints; k++)
            {
                double a = Math.Tau * k / CirclePoints;
                var p = new Vec2(end.X + Math.Cos(a) * r, end.Y + Math.Sin(a) * r);
                if (IsFree(p))
                    yield return p;
            }
    }

    // ---- Changes ------------------------------------------------------------------------------------

    /// <summary>
    /// Damages an obstacle. If that destroys it, it is removed, and replaced by rubble in the same footprint when
    /// <paramref name="leavesRubble"/> (rubble itself just vanishes). Returns true if it was destroyed.
    /// </summary>
    public bool Damage(Obstacle obstacle, int amount, bool leavesRubble, string sourceName, int beat, string sourceActorId, List<CombatEvent> events)
    {
        if (!_obstacles.Contains(obstacle) || amount <= 0)
            return false;

        bool destroyed = obstacle.TakeDamage(amount);
        Version++;
        if (!destroyed)
        {
            events.Add(new CombatEvent(beat, CombatEventType.ObstacleDamaged, sourceActorId,
                $"{sourceName} hit the {obstacle.Definition.Name} ({obstacle.Durability}/{obstacle.Definition.Durability}).",
                Value: amount, Position: obstacle.A, ObstacleId: obstacle.Id));
            return false;
        }

        int index = _obstacles.IndexOf(obstacle);
        _obstacles.RemoveAt(index);
        bool rubble = leavesRubble && !obstacle.Definition.IsRubble && _rubble is not null;
        if (rubble)
            _obstacles.Insert(index, obstacle.Remade($"{obstacle.Id}_rubble", _rubble!));
        events.Add(new CombatEvent(beat, CombatEventType.ObstacleDestroyed, sourceActorId,
            $"{sourceName} destroyed the {obstacle.Definition.Name}" + (rubble ? ", leaving rubble." : "."),
            Position: obstacle.A, ObstacleId: obstacle.Id));
        return true;
    }

    /// <summary>Adds an obstacle (e.g. a conjured Iron Wall). Its id is made unique.</summary>
    public Obstacle Add(ObstacleDefinition def, Vec2 a, Vec2 b, double halfWidth)
    {
        var obstacle = new Obstacle($"obstacle_{_nextId++}_{def.Id}", def, a, b, halfWidth);
        _obstacles.Add(obstacle);
        Version++;
        return obstacle;
    }

    /// <summary>Whether a wall a–b of this half-width fits: inside the arena, clear of obstacles and of the given Crest positions.</summary>
    public bool WallFits(Vec2 a, Vec2 b, double halfWidth, IEnumerable<Vec2> crests)
    {
        bool inside = new[] { a, b }.All(p => p.X >= halfWidth && p.Y >= halfWidth && p.X <= ArenaWidth - halfWidth && p.Y <= ArenaHeight - halfWidth);
        return inside &&
               _obstacles.All(o => Geometry.SegmentSegmentDistance(a, b, o.A, o.B) >= halfWidth + o.HalfWidth) &&
               crests.All(c => Geometry.PointSegmentDistance(c, a, b) >= halfWidth + CrestRadius);
    }
}
