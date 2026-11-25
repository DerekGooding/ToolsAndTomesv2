using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.AI;

/// <summary>
/// How AI Crests use the terrain (docs/DESIGN.md §3c): shove targets into hard obstacles, and stand in cover.
/// Deterministic, and simple enough to write down as table rules.
/// </summary>
public static class TerrainTactics
{
    /// <summary>Minimum shortfall that counts as a slam (a collision that deals damage).</summary>
    private const double MinSlam = 0.5;

    /// <summary>
    /// Whether using <paramref name="action"/> on <paramref name="target"/> from here would slam it into a hard
    /// obstacle (a push or pull cut short by stone or iron).
    /// </summary>
    public static bool WouldSlam(Combatant actor, ActionDefinition action, Combatant target)
    {
        if (action.PushDistance == 0 || target.IsPinned || !ActionValidationRules.IsInRange(actor, action, target, null))
            return false;

        int distance = DamageResolutionService.EffectiveDisplacement(action.PushDistance, target.Stats.DisplacementResistance);
        var offset = target.Position - actor.Position;
        if (distance < 0) // a pull never drags past its source
            distance = -Math.Min(-distance, Math.Max(0, (int)Math.Floor(offset.Length) - 1));
        if (distance == 0)
            return false;

        var dir = offset.Normalized();
        if (dir.Length < 0.01)
            dir = new Vec2(1, 0);
        var terrain = target.Terrain;
        var wanted = (target.Position + dir * distance).Clamp(0.5, terrain.ArenaWidth - 0.5, 0.5, terrain.ArenaHeight - 0.5);
        var slide = terrain.Sweep(target.Position, wanted);
        return slide.Hit is { Definition.Hard: true } && slide.Shortfall >= MinSlam;
    }

    /// <summary>Whether the Crest already has cover against <paramref name="threat"/>.</summary>
    public static bool InCover(Combatant crest, Combatant threat) =>
        crest.Terrain.CoverDice(threat.Position, crest.Position) > 0 || !crest.Terrain.HasLineOfFire(threat.Position, crest.Position);

    /// <summary>
    /// A nearby spot (within one beat's walk, reachable in a straight line) where the Crest has partial cover from
    /// <paramref name="threat"/> but can still shoot it within <paramref name="range"/>. Most cover first, then
    /// nearest. Null if there is none.
    /// </summary>
    public static Vec2? FindCoverSpot(Combatant crest, Combatant threat, double range)
    {
        var terrain = crest.Terrain;
        double walk = crest.EffectiveMoveSpeed;
        if (walk <= 0 || terrain.Obstacles.Count == 0)
            return null;

        Vec2? best = null;
        int bestCover = 0;
        double bestDistance = double.MaxValue;
        foreach (double r in new[] { walk / 2, walk })
            for (int k = 0; k < 16; k++)
            {
                double a = Math.Tau * k / 16;
                var spot = new Vec2(crest.Position.X + Math.Cos(a) * r, crest.Position.Y + Math.Sin(a) * r);
                if (!terrain.IsFree(spot) || !terrain.SegmentClear(crest.Position, spot) ||
                    spot.DistanceTo(threat.Position) > range || !terrain.HasLineOfFire(spot, threat.Position))
                    continue;
                int cover = terrain.CoverDice(threat.Position, spot);
                if (cover <= 0)
                    continue;
                double d = spot.DistanceTo(crest.Position);
                if (cover > bestCover || (cover == bestCover && d < bestDistance - 1e-9))
                {
                    best = spot;
                    bestCover = cover;
                    bestDistance = d;
                }
            }
        return best;
    }

    /// <summary>The longest range among the Crest's damaging, non-reactive actions (0 if none).</summary>
    public static int Reach(CombatSimulation sim, Combatant crest) =>
        crest.Loadout.GetOperationalActionIds()
            .Select(sim.Catalog.GetAction)
            .Where(a => a.BaseDamage > 0 && !a.IsReactive)
            .Select(a => a.Range)
            .DefaultIfEmpty(0)
            .Max();
}
