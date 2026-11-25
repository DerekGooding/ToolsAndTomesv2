using Godot;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Client;

/// <summary>
/// Draws intent on the field: dashed move paths, attack lines, and windup tells. A windup draws a ring around
/// its target that fills as the strike approaches (enemy strikes on the player's lance turn from amber to red),
/// area attacks show their blast radius, and the impact beat flashes red. Presentation only.
/// </summary>
public partial class TelegraphView : MeshInstance3D
{
    private const float RingRadius = 1.35f;
    private readonly ImmediateMesh _mesh = new();
    private float _time;

    public override void _Ready()
    {
        Mesh = _mesh;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true
        };
        CastShadow = ShadowCastingSetting.Off;
    }

    public override void _Process(double delta) => _time += (float)delta;

    /// <param name="beatFraction">How far the current beat has played (0–1), so tells fill smoothly between beats.</param>
    public void Refresh(CombatSimulation sim, IReadOnlyDictionary<string, CrestView> views, double beatFraction)
    {
        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        bool any = false;
        float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 14f);

        foreach (var crest in sim.Combatants.Where(c => !c.IsDefeated))
        {
            var view = views[crest.Id];
            var from = view.Position + Vector3.Up * 1.2f;
            var baseColor = Coords.CrestColor(crest);

            if (crest.Movement is { } move)
            {
                Vec2? dest = move.FollowActorId is { } id && sim.FindCombatant(id) is { } followed
                    ? followed.Position
                    : move.Destination;
                if (dest is { } end)
                {
                    // The planned route around obstacles, as a dashed polyline.
                    var legStart = view.Position + Vector3.Up * 0.05f;
                    foreach (var point in sim.Terrain.FindPath(Coords.ToTable(view.Position), end))
                    {
                        var legEnd = Coords.ToWorld(point, 0.05f);
                        Dashed(legStart, legEnd, new Color(baseColor, 0.5f));
                        legStart = legEnd;
                    }
                    any = true;
                }
            }

            foreach (var act in crest.ActionsInMotion)
            {
                if (act.Phase is not (ActionState.Windup or ActionState.Executing))
                    continue;

                bool impact = act.Phase == ActionState.Executing;
                Combatant? targetCrest = act.TargetActorId is { } tid ? sim.FindCombatant(tid) : null;
                bool threat = crest.Faction == Faction.Enemy && (targetCrest?.Faction == Faction.Player || act.TargetActorId is null);

                // How far through its windup this action is (0–1), smoothed across the beat.
                float progress = act.PhaseTotalBeats <= 0
                    ? 1f
                    : Mathf.Clamp(1f - ((float)act.PhaseBeatsRemaining - (float)beatFraction) / act.PhaseTotalBeats, 0f, 1f);

                Color color = impact
                    ? new Color(1f, 0.2f, 0.15f, 0.65f + 0.35f * pulse)
                    : threat ? new Color(1f, 0.75f, 0.2f).Lerp(new Color(1f, 0.25f, 0.15f), progress) with { A = 0.95f }
                    : new Color(baseColor, 0.8f);

                Vector3? target = targetCrest is not null && views.TryGetValue(targetCrest.Id, out var tv)
                    ? tv.Position + Vector3.Up * 1.2f
                    : act.TargetPoint is { } p ? Coords.ToWorld(p, 0.1f) : null;

                if (target is { } t)
                {
                    Thick(from, t, color, threat || impact ? 0.06f : 0.03f);
                    any = true;

                    var ground = t with { Y = 0.06f };
                    if (act.Definition.AreaRadius > 0)
                    {
                        Circle(ground, (float)act.Definition.AreaRadius, color);
                        Circle(ground, (float)act.Definition.AreaRadius - 0.06f, color with { A = color.A * 0.6f });
                    }

                    if (threat || impact)
                    {
                        // The windup ring: a faint full circle, filled clockwise as the strike approaches.
                        Circle(ground, RingRadius, color with { A = 0.25f });
                        for (int k = 0; k < 3; k++)
                            Arc(ground, RingRadius - 0.05f * k, impact ? 1f : progress, color);
                    }
                }
            }
        }

        if (!any)
            Line(Vector3.Zero, Vector3.Zero, Colors.Transparent); // a surface needs at least one vertex pair
        _mesh.SurfaceEnd();
    }

    private void Line(Vector3 a, Vector3 b, Color color)
    {
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(a);
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(b);
    }

    /// <summary>A line drawn as a small bundle, so it reads at a distance (line primitives are one pixel wide).</summary>
    private void Thick(Vector3 a, Vector3 b, Color color, float width)
    {
        Line(a, b, color);
        Line(a + Vector3.Up * width, b + Vector3.Up * width, color);
        var side = (b - a).Cross(Vector3.Up).Normalized() * width;
        Line(a + side, b + side, color);
        Line(a - side, b - side, color);
    }

    private void Dashed(Vector3 a, Vector3 b, Color color)
    {
        float length = a.DistanceTo(b);
        int dashes = Math.Max(1, (int)(length / 0.6f));
        for (int i = 0; i < dashes; i += 2)
            Line(a.Lerp(b, (float)i / dashes), a.Lerp(b, (float)(i + 1) / dashes), color);
    }

    private void Circle(Vector3 center, float radius, Color color) => Arc(center, radius, 1f, color);

    /// <summary>Part of a circle on the ground, starting at twelve o'clock (−Z) and sweeping clockwise.</summary>
    private void Arc(Vector3 center, float radius, float fraction, Color color)
    {
        const int segments = 48;
        int count = Mathf.CeilToInt(segments * Mathf.Clamp(fraction, 0f, 1f));
        for (int i = 0; i < count; i++)
        {
            float a0 = Mathf.Tau * i / segments - Mathf.Pi / 2f, a1 = Mathf.Tau * Math.Min(i + 1, segments * fraction) / segments - Mathf.Pi / 2f;
            Line(center + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius,
                 center + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius, color);
        }
    }
}
