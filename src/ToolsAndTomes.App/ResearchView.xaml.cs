using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ToolsAndTomes.App;

public partial class ResearchView : UserControl
{
    private readonly AppSessionState _session;

    public event Action? BackRequested;

    public ResearchView(AppSessionState session)
    {
        InitializeComponent();
        _session = session;
    }

    public void RefreshUI()
    {
        var catalog = _session.Catalog;
        var workshop = _session.Workshop;

        TxtSummary.Text = $"Encounters won: {_session.EncountersWon}  |  Materials researched: {workshop.ResearchedMaterials.Count}/{catalog.Materials.Count}";

        MaterialsPanel.Children.Clear();
        foreach (var material in catalog.Materials.Values.OrderBy(m => m.Name))
        {
            bool researched = workshop.IsResearched(material.Id);
            int samples = workshop.GetSamples(material.Id);
            string unlocks = string.Join(", ", material.UnlocksComponentIds.Select(id => catalog.GetEquipment(id).Name));

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(researched ? Color.FromRgb(52, 211, 153) : Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = material.Name, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            text.Children.Add(new TextBlock { Text = material.Description, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 4) });
            text.Children.Add(new TextBlock { Text = $"Unlocks: {unlocks}", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)), FontSize = 11.5 });
            text.Children.Add(new TextBlock
            {
                Text = researched
                    ? $"RESEARCHED ✓  (spare samples: {samples})"
                    : $"Samples: {samples} / {material.SamplesToResearch}",
                Foreground = researched ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : Brushes.Orange,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            var button = new Button
            {
                Content = researched ? "Researched" : "Research",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = workshop.CanResearch(material),
                Background = new SolidColorBrush(Color.FromRgb(190, 24, 93)),
                Foreground = Brushes.White
            };
            string materialId = material.Id;
            button.Click += (_, _) =>
            {
                var result = _session.Research(materialId);
                TxtStatus.Text = result.Succeeded
                    ? $"{result.Message} New blueprints: {string.Join(", ", result.UnlockedComponentIds.Select(id => catalog.GetEquipment(id).Name))}."
                    : result.Message;
                RefreshUI();
            };
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);

            card.Child = grid;
            MaterialsPanel.Children.Add(card);
        }

        var known = workshop.KnownComponentIds(catalog);
        LstKnown.ItemsSource = catalog.Equipment.Values
            .Where(e => known.Contains(e.Id))
            .OrderBy(e => e.Category).ThenBy(e => e.Name)
            .Select(e => $"{e.Name} ({e.Category}){(catalog.BaselineComponentIds.Contains(e.Id) ? "" : "  ★ researched")}")
            .ToList();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
}
