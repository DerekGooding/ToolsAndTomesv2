using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.App;

public static class BattlefieldRenderer
{
    public static Vec2 CanvasToWorld(Point p, Canvas canvas, CombatSimulation sim)
    {
        double w = Math.Max(1, canvas.ActualWidth);
        double h = Math.Max(1, canvas.ActualHeight);
        return new Vec2(p.X / w * sim.ArenaWidth, p.Y / h * sim.ArenaHeight);
    }

    public static Point WorldToCanvas(Vec2 pos, Canvas canvas, CombatSimulation sim)
    {
        double w = Math.Max(1, canvas.ActualWidth);
        double h = Math.Max(1, canvas.ActualHeight);
        return new Point(pos.X / sim.ArenaWidth * w, pos.Y / sim.ArenaHeight * h);
    }

    /// <summary>
    /// Smoothly interpolates a combatant toward where its movement will place it at the end of the current beat.
    /// Presentation only: the simulation itself moves in whole beats.
    /// </summary>
    public static Vec2 DisplayPosition(CombatSimulation sim, Combatant c, double beatFraction)
    {
        var end = sim.ProjectEndOfBeatPosition(c);
        double t = Math.Clamp(beatFraction, 0, 1);
        return c.Position + (end - c.Position) * t;
    }

    public static void Render(
        Canvas canvas,
        CombatSimulation sim,
        string? selectedCombatantId,
        ActionDefinition? armedAction,
        double beatFraction = 0)
    {
        canvas.Children.Clear();
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w < 20 || h < 20)
            return;

        DrawGrid(canvas, w, h, sim);
        DrawTelegraphs(canvas, sim, beatFraction);
        DrawCombatants(canvas, sim, selectedCombatantId, armedAction, beatFraction);
    }

    private static void DrawGrid(Canvas canvas, double w, double h, CombatSimulation sim)
    {
        var gridBrush = new SolidColorBrush(Color.FromRgb(32, 42, 56));
        for (int x = 2; x < sim.ArenaWidth; x += 2)
        {
            double cx = x / sim.ArenaWidth * w;
            canvas.Children.Add(new Line { X1 = cx, Y1 = 0, X2 = cx, Y2 = h, Stroke = gridBrush, StrokeThickness = 1 });
        }

        for (int y = 2; y < sim.ArenaHeight; y += 2)
        {
            double cy = y / sim.ArenaHeight * h;
            canvas.Children.Add(new Line { X1 = 0, Y1 = cy, X2 = w, Y2 = cy, Stroke = gridBrush, StrokeThickness = 1 });
        }
    }

    private static void DrawTelegraphs(Canvas canvas, CombatSimulation sim, double beatFraction)
    {
        double scaleX = canvas.ActualWidth / sim.ArenaWidth;
        double scaleY = canvas.ActualHeight / sim.ArenaHeight;

        foreach (var actor in sim.Combatants)
        {
            if (actor.IsDefeated)
                continue;

            var start = WorldToCanvas(DisplayPosition(sim, actor, beatFraction), canvas, sim);

            if (actor.State == ActionState.Moving && actor.MoveDestination is { } dest)
            {
                var end = WorldToCanvas(dest, canvas, sim);
                canvas.Children.Add(new Line
                {
                    X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
                    Stroke = actor.Faction == Faction.Player ? Brushes.DeepSkyBlue : Brushes.IndianRed,
                    StrokeThickness = 1.5,
                    StrokeDashArray = [4, 4],
                    Opacity = 0.65
                });
            }

            if (actor.CurrentAction is not { } act || actor.State is not (ActionState.Windup or ActionState.Executing))
                continue;

            var color = actor.Faction == Faction.Player
                ? Color.FromRgb(56, 189, 248)
                : Color.FromRgb(249, 115, 22);

            Vec2? targetPos = act.TargetPoint;
            if (targetPos is null && act.TargetActorId is not null)
                targetPos = sim.FindCombatant(act.TargetActorId) is { } tgt ? DisplayPosition(sim, tgt, beatFraction) : null;

            if (targetPos is not null)
            {
                var end = WorldToCanvas(targetPos.Value, canvas, sim);
                canvas.Children.Add(new Line
                {
                    X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y,
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = actor.State == ActionState.Executing ? 3.5 : 2.0,
                    Opacity = 0.85
                });

                if (act.Definition.AreaRadius > 0)
                {
                    double rx = act.Definition.AreaRadius * scaleX;
                    double ry = act.Definition.AreaRadius * scaleY;
                    var aoe = new Ellipse
                    {
                        Width = rx * 2,
                        Height = ry * 2,
                        Stroke = new SolidColorBrush(color),
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B))
                    };
                    Canvas.SetLeft(aoe, end.X - rx);
                    Canvas.SetTop(aoe, end.Y - ry);
                    canvas.Children.Add(aoe);

                    double sec = Math.Max(0, act.BeatsUntilImpact() - beatFraction);
                    var label = new TextBlock
                    {
                        Text = $"{act.Definition.Name} ({sec:F1}s)",
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold
                    };
                    Canvas.SetLeft(label, end.X - 42);
                    Canvas.SetTop(label, end.Y - 8);
                    canvas.Children.Add(label);
                }
            }
        }
    }

    private static void DrawCombatants(
        Canvas canvas,
        CombatSimulation sim,
        string? selectedId,
        ActionDefinition? armedAction,
        double beatFraction = 0)
    {
        double scaleX = canvas.ActualWidth / sim.ArenaWidth;
        double scaleY = canvas.ActualHeight / sim.ArenaHeight;

        foreach (var c in sim.Combatants)
        {
            var pt = WorldToCanvas(DisplayPosition(sim, c, beatFraction), canvas, sim);

            if (c.Faction == Faction.Player && !c.IsDefeated && armedAction is { Range: > 0 })
            {
                double rx = armedAction.Range * scaleX;
                double ry = armedAction.Range * scaleY;
                var rangeRing = new Ellipse
                {
                    Width = rx * 2,
                    Height = ry * 2,
                    Stroke = Brushes.Cyan,
                    StrokeThickness = 1.2,
                    StrokeDashArray = [5, 5],
                    Opacity = 0.45
                };
                Canvas.SetLeft(rangeRing, pt.X - rx);
                Canvas.SetTop(rangeRing, pt.Y - ry);
                canvas.Children.Add(rangeRing);
            }

            if (c.Id == selectedId)
            {
                var sel = new Ellipse
                {
                    Width = 44, Height = 44,
                    Stroke = Brushes.Gold, StrokeThickness = 2.5
                };
                Canvas.SetLeft(sel, pt.X - 22);
                Canvas.SetTop(sel, pt.Y - 22);
                canvas.Children.Add(sel);
            }

            if (c.HasActiveAegis)
            {
                var aegis = new Ellipse
                {
                    Width = 38, Height = 38,
                    Stroke = Brushes.Aquamarine, StrokeThickness = 2.5,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 127, 255, 212))
                };
                Canvas.SetLeft(aegis, pt.X - 19);
                Canvas.SetTop(aegis, pt.Y - 19);
                canvas.Children.Add(aegis);
            }

            Brush fill = c.IsDefeated
                ? Brushes.DimGray
                : c.Faction == Faction.Player
                    ? new SolidColorBrush(Color.FromRgb(14, 165, 233))
                    : c.Archetype switch
                    {
                        EnemyArchetype.Bruiser => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                        EnemyArchetype.Artillery => new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                        _ => new SolidColorBrush(Color.FromRgb(244, 63, 94))
                    };

            var body = new Ellipse
            {
                Width = 28, Height = 28,
                Fill = fill,
                Stroke = c.IsEvading ? Brushes.White : Brushes.Black,
                StrokeThickness = c.IsEvading ? 3 : 1.5,
                Opacity = c.IsDefeated ? 0.45 : 1.0
            };
            Canvas.SetLeft(body, pt.X - 14);
            Canvas.SetTop(body, pt.Y - 14);
            canvas.Children.Add(body);

            int opCount = c.Loadout.Installed.Count(e => e.IsOperational);
            int totalCount = c.Loadout.Installed.Count;
            string status = c.IsDefeated
                ? "DEFEATED"
                : c.CurrentAction is not null
                    ? $"{c.State}: {c.CurrentAction.Definition.Name}"
                    : c.State.ToString();

            var title = new TextBlock
            {
                Text = $"{c.Name} [{opCount}/{totalCount}]",
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(title, pt.X - 48);
            Canvas.SetTop(title, pt.Y - 34);
            canvas.Children.Add(title);

            var sub = new TextBlock
            {
                Text = status,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                FontSize = 10
            };
            Canvas.SetLeft(sub, pt.X - 48);
            Canvas.SetTop(sub, pt.Y + 18);
            canvas.Children.Add(sub);
        }
    }
}
