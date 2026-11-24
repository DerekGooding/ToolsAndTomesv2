using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.App;

public partial class MissionSelectView : UserControl
{
    private readonly AppSessionState _session;

    public event Action? BackRequested;
    public event Action? MissionStarted;

    public MissionSelectView(AppSessionState session)
    {
        InitializeComponent();
        _session = session;
    }

    public void RefreshUI()
    {
        var catalog = _session.Catalog;
        var crest = _session.SelectedLoadout;
        var warnings = LoadoutRules.GetWarnings(crest);
        bool canLaunch = LoadoutRules.ComputeStats(crest).IsCoreOperational;
        TxtCrest.Text = $"Crest: {crest.Name}" + (warnings.Count > 0 ? $"  (⚠ {warnings.Count} warning(s) in the Crest Bay)" : "");

        MissionsPanel.Children.Clear();
        foreach (var mission in catalog.Missions.Values)
        {
            bool unlocked = MissionRun.IsUnlocked(mission, _session.Workshop);
            string encounters = string.Join("  →  ", mission.EncounterIds.Select(id => catalog.Encounters[id].Name));
            string rewards = string.Join(", ", mission.CompletionRewards.Select(r => $"{catalog.Materials[r.MaterialId].Name} ×{r.Count}"));
            string requirement = mission.RequiredResearch.Count == 0
                ? "Available"
                : "Requires research: " + string.Join(", ", mission.RequiredResearch.Select(id =>
                    $"{catalog.Materials[id].Name}{(_session.Workshop.IsResearched(id) ? " ✓" : "")}"));

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(unlocked ? Color.FromRgb(251, 191, 36) : Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8),
                Opacity = unlocked ? 1.0 : 0.6
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = mission.Name, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            text.Children.Add(new TextBlock { Text = mission.Description, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 4) });
            text.Children.Add(new TextBlock { Text = $"Encounters: {encounters}", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)), FontSize = 11.5 });
            text.Children.Add(new TextBlock { Text = $"Rest between fights: {mission.RestBeats} beats  |  Completion bonus: {rewards}", Foreground = Brushes.LightGray, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) });
            text.Children.Add(new TextBlock
            {
                Text = requirement,
                Foreground = unlocked ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : Brushes.Orange,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            var launch = new Button
            {
                Content = unlocked ? "Launch ▶" : "Locked",
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = unlocked && canLaunch,
                Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };
            string missionId = mission.Id;
            launch.Click += (_, _) =>
            {
                _session.StartMission(missionId);
                MissionStarted?.Invoke();
            };
            Grid.SetColumn(launch, 1);
            grid.Children.Add(launch);

            card.Child = grid;
            MissionsPanel.Children.Add(card);
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
}
