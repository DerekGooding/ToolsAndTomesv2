using Godot;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Client.UI;

/// <summary>Every mission, in content order, with its fights, rewards, and what unlocks it.</summary>
public partial class MissionSelectScreen : Control
{
    private readonly CampaignSession _session;
    private VBoxContainer _list = null!;
    private Label _lance = null!;

    public event Action? BackRequested;
    public event Action<string>? MissionChosen;

    public MissionSelectScreen() : this(null!) { }

    public MissionSelectScreen(CampaignSession session) => _session = session;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var (root, column) = Ui.Screen();
        AddChild(root);

        var header = new HBoxContainer();
        column.AddChild(header);
        var title = Ui.Title("Mission Select");
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(title);
        header.AddChild(Ui.Button("◀ Crest Bay", () => BackRequested?.Invoke()));

        _lance = Ui.Text("", Ui.Muted);
        column.AddChild(_lance);
        _list = Ui.Scrolling(column);
        Refresh();
    }

    public void Refresh()
    {
        var catalog = _session.Catalog;
        string allies = _session.Lance.Count == 0
            ? "no allies"
            : string.Join(" and ", _session.Lance.Select(id => catalog.Pilots[id].DisplayName));
        _lance.Text = $"Flying {_session.SelectedLoadout.Name} with {allies}.";

        Ui.Clear(_list);
        foreach (var mission in catalog.Missions.Values)
            _list.AddChild(Card(mission));
    }

    /// <summary>Starts a mission as if its button were pressed (used by automated runs).</summary>
    public void Choose(string missionId) => MissionChosen?.Invoke(missionId);

    private Control Card(MissionDefinition mission)
    {
        var catalog = _session.Catalog;
        var workshop = _session.Workshop;
        bool unlocked = MissionRun.IsUnlocked(mission, workshop);
        bool completed = workshop.HasCompleted(mission.Id);

        var (card, column) = Ui.Card();
        card.Modulate = unlocked ? Colors.White : new Color(1, 1, 1, 0.55f);

        var header = new HBoxContainer();
        column.AddChild(header);
        var name = Ui.Heading(mission.Name + (completed ? "  ✓" : ""), unlocked ? Ui.Accent : Ui.Muted);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(name);
        var start = Ui.Button(completed ? "Play again" : "Start", () => MissionChosen?.Invoke(mission.Id), primary: true);
        start.Disabled = !unlocked;
        header.AddChild(start);

        column.AddChild(Ui.Text(mission.Description, Ui.Ink, 15));
        column.AddChild(Ui.Text("Fights: " + string.Join("  →  ", mission.EncounterIds.Select(id => catalog.Encounters[id].Name)) +
                                $"   ·   rests of {mission.RestBeats} beats between them", Ui.Muted, 14));
        if (mission.CompletionRewards.Count > 0)
            column.AddChild(Ui.Text("Completion bonus: " + Ui.Materials(catalog, mission.CompletionRewards), Ui.Muted, 14));

        var needs = mission.RequiredMissions
            .Select(id => $"complete {catalog.Missions[id].Name}{(workshop.HasCompleted(id) ? " ✓" : "")}")
            .Concat(mission.RequiredResearch.Select(id => $"research {catalog.Materials[id].Name}{(workshop.IsResearched(id) ? " ✓" : "")}"))
            .ToList();
        if (needs.Count > 0)
            column.AddChild(Ui.Text("Requires: " + string.Join(", ", needs), unlocked ? Ui.Good : Ui.Bad, 14));
        return card;
    }
}
