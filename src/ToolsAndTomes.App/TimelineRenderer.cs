using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.App;

public static class TimelineRenderer
{
    public static void Render(StackPanel panel, IReadOnlyList<TimelineEntry> entries)
    {
        panel.Children.Clear();

        if (entries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "All combatants idle. Issue commands or unpause.",
                Foreground = Brushes.SlateGray,
                Margin = new Thickness(4)
            });
            return;
        }

        foreach (var entry in entries.OrderBy(e => e.Faction).ThenBy(e => e.TicksUntilImpact < 0 ? 999 : e.TicksUntilImpact))
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

            var nameBlock = new TextBlock
            {
                Text = entry.ActorName,
                Foreground = entry.Faction == Faction.Player
                    ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
                    : new SolidColorBrush(Color.FromRgb(251, 146, 60)),
                FontWeight = FontWeights.SemiBold,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameBlock, 0);
            row.Children.Add(nameBlock);

            double progress = 1.0 - Math.Clamp(entry.TicksRemaining / (double)Math.Max(1, entry.PhaseTotalTicks), 0, 1);
            var barColor = entry.State switch
            {
                ActionState.Moving => Color.FromRgb(51, 65, 85),
                ActionState.Windup => entry.Faction == Faction.Player
                    ? Color.FromRgb(14, 165, 233)
                    : Color.FromRgb(234, 88, 12),
                ActionState.Executing => Color.FromRgb(239, 68, 68),
                ActionState.Recovery => Color.FromRgb(109, 40, 217),
                ActionState.Interrupted => Color.FromRgb(153, 27, 27),
                _ => Color.FromRgb(71, 85, 105)
            };

            var barContainer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Height = 18,
                Margin = new Thickness(4, 0, 6, 0)
            };

            var barGrid = new Grid();
            barGrid.Children.Add(new Border
            {
                Background = new SolidColorBrush(barColor),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, progress * 260),
                CornerRadius = new CornerRadius(2)
            });

            barGrid.Children.Add(new TextBlock
            {
                Text = $"{entry.State.ToString().ToUpperInvariant()} — {entry.ActionName}",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            barContainer.Child = barGrid;
            Grid.SetColumn(barContainer, 1);
            row.Children.Add(barContainer);

            double remSec = entry.TicksRemaining / (double)ActionCatalog.TicksPerSecond;
            string detail = entry.TicksUntilImpact >= 0
                ? $"Impact: {entry.TicksUntilImpact / (double)ActionCatalog.TicksPerSecond:F1}s" + (entry.CanBeInterrupted ? " [INT]" : "")
                : $"{remSec:F1}s";

            var detailBlock = new TextBlock
            {
                Text = detail,
                Foreground = entry.Faction == Faction.Enemy && entry.TicksUntilImpact >= 0
                    ? Brushes.Orange
                    : Brushes.LightGray,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(detailBlock, 2);
            row.Children.Add(detailBlock);

            panel.Children.Add(row);
        }
    }
}
