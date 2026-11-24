using Godot;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Client;

/// <summary>
/// Draws every action's telegraph: a line from attacker to target during windup, brighter in the impact window,
/// a ring for area attacks, and dashed paths for movement. Rebuilt every frame from the simulation.
/// </summary>
public partial class TelegraphView : MeshInstance3D
{
    private readonly ImmediateMesh _mesh = new();

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

    public void Refresh(CombatSimulation sim, IReadOnlyDictionary<string, CrestView> views)
    {
        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        bool any = false;

        foreach (var crest in sim.Combatants.Where(c => !c.IsDefeated))
        {
            var from = views[crest.Id].Position + Vector3.Up * 1.2f;
            var baseColor = Coords.FactionColor(crest.Faction);

            if (crest.Movement is { } move)
            {
                Vector3? dest = move.FollowActorId is { } id && views.TryGetValue(id, out var followed)
                    ? followed.Position
                    : move.Destination is { } d ? Coords.ToWorld(d) : null;
                if (dest is { } end)
                {
                    Dashed(views[crest.Id].Position + Vector3.Up * 0.05f, end + Vector3.Up * 0.05f, new Color(baseColor, 0.5f));
                    any = true;
                }
            }

            foreach (var act in crest.ActionsInMotion)
            {
                if (act.Phase is not (ActionState.Windup or ActionState.Executing))
                    continue;

                bool impact = act.Phase == ActionState.Executing;
                var color = impact ? new Color(1f, 0.2f, 0.15f) : new Color(baseColor, 0.85f);

                Vector3? target = act.TargetActorId is { } tid && views.TryGetValue(tid, out var tv)
                    ? tv.Position + Vector3.Up * 1.2f
                    : act.TargetPoint is { } p ? Coords.ToWorld(p, 0.1f) : null;

                if (target is { } t)
                {
                    Line(from, t, color);
                    if (impact)
                        Line(from + Vector3.Up * 0.08f, t + Vector3.Up * 0.08f, color);
                    any = true;
                }

                if (act.Definition.AreaRadius > 0 && target is { } center)
                {
                    Circle(center with { Y = 0.08f }, act.Definition.AreaRadius, color);
                    any = true;
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

    private void Dashed(Vector3 a, Vector3 b, Color color)
    {
        float length = a.DistanceTo(b);
        int dashes = Math.Max(1, (int)(length / 0.6f));
        for (int i = 0; i < dashes; i += 2)
            Line(a.Lerp(b, (float)i / dashes), a.Lerp(b, (float)(i + 1) / dashes), color);
    }

    private void Circle(Vector3 center, float radius, Color color)
    {
        const int segments = 40;
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Tau * i / segments, a1 = Mathf.Tau * (i + 1) / segments;
            Line(center + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius,
                 center + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius, color);
        }
    }
}
