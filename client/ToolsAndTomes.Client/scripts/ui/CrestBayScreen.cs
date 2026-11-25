using Godot;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Client.UI;

/// <summary>
/// The hub between missions: pick your Crest, choose who rides in your lance, research recovered samples, and
/// head out (or resume the mission in progress). Refitting opens the Workshop.
/// </summary>
public partial class CrestBayScreen : Control
{
    private readonly CampaignSession _session;
    private VBoxContainer _crestColumn = null!;
    private VBoxContainer _lanceColumn = null!;
    private VBoxContainer _researchColumn = null!;
    private HBoxContainer _footer = null!;
    private Label _messages = null!;

    public event Action? MissionSelectRequested;
    public event Action? ResumeRequested;
    public event Action? QuickBattleRequested;
    public event Action? QuitRequested;

    /// <summary>Open the Workshop on a Crest (by loadout id).</summary>
    public event Action<string>? WorkshopRequested;

    public CrestBayScreen() : this(null!) { }

    public CrestBayScreen(CampaignSession session) => _session = session;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var (root, column) = Ui.Screen();
        AddChild(root);

        column.AddChild(Ui.Title("Tools and Tomes · Crest Bay"));
        _messages = Ui.Text("", Ui.Muted);
        column.AddChild(_messages);

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 16);
        column.AddChild(columns);
        _crestColumn = AddColumn(columns, "Your Crest", 1.3f);
        _lanceColumn = AddColumn(columns, $"Your lance (up to {MissionRun.MaxAllies} allies)", 1.1f);
        _researchColumn = AddColumn(columns, "Research desk", 0.9f);

        _footer = new HBoxContainer();
        _footer.AddThemeConstantOverride("separation", 12);
        column.AddChild(_footer);

        Refresh();
    }

    private static VBoxContainer AddColumn(HBoxContainer parent, string heading, float stretch)
    {
        var (card, column) = Ui.Card();
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.SizeFlagsStretchRatio = stretch;
        parent.AddChild(card);
        column.AddChild(Ui.Heading(heading, Ui.Accent));
        return Ui.Scrolling(column);
    }

    public void Refresh()
    {
        var notes = new List<string>();
        if (_session.LastMissionReport is { } report)
            notes.Add(report);
        notes.AddRange(_session.ProfileWarnings);
        _messages.Text = notes.Count > 0 ? string.Join("\n", notes) : "Pick a Crest and a lance, then choose a mission.";

        RefreshCrest();
        RefreshLance();
        RefreshResearch();
        RefreshFooter();
    }

    private void RefreshCrest()
    {
        Ui.Clear(_crestColumn);
        var rules = _session.Catalog.Rules;
        bool locked = _session.ActiveMission is not null;

        var picker = new OptionButton { Disabled = locked, TooltipText = locked ? "Finish or withdraw from the mission in progress first." : "" };
        for (int i = 0; i < _session.CrestAssemblies.Count; i++)
        {
            picker.AddItem(_session.CrestAssemblies[i].Name, i);
            if (_session.CrestAssemblies[i].Id == _session.SelectedCrestId)
                picker.Select(i);
        }
        picker.ItemSelected += index =>
        {
            _session.SelectCrest(_session.CrestAssemblies[(int)index].Id);
            Refresh();
        };
        _crestColumn.AddChild(picker);

        var crest = _session.SelectedLoadout;
        _crestColumn.AddChild(Ui.Text(Ui.StatsLine(crest, rules), Ui.Muted, 14));
        _crestColumn.AddChild(Ui.Text(Ui.MagicLine(crest, rules), Ui.Muted, 14));
        foreach (var warning in LoadoutRules.GetWarnings(crest, rules))
            _crestColumn.AddChild(Ui.Text("⚠ " + warning, warning.StartsWith("LIMB BEFORE") ? Ui.Bad : Ui.Accent, 14));

        _crestColumn.AddChild(Ui.Heading("Components (Damage Order: hits land top-down)"));
        int n = 1;
        foreach (var part in crest.DamageOrder)
            _crestColumn.AddChild(Ui.Text(Ui.ComponentLine(part, n++, crest), part.IsDisabled || crest.HoldStateOf(part) == HoldState.Unheld ? Ui.Bad : Ui.Ink, 14));

        if (crest.ReactionRules.Count > 0)
        {
            _crestColumn.AddChild(Ui.Heading("Reaction rules"));
            foreach (var rule in crest.ReactionRules)
                _crestColumn.AddChild(Ui.Text(
                    $"{_session.Catalog.GetAction(rule.ActionId).Name} when ≥{rule.MinIncomingDamage} incoming" +
                    (rule.Telegraph is { } t ? $" ({t})" : ""), Ui.Muted, 14));
        }

        var refit = Ui.Button("Refit in the Workshop ▶", () => WorkshopRequested?.Invoke(crest.Id));
        refit.Disabled = locked;
        _crestColumn.AddChild(refit);
    }

    private void RefreshLance()
    {
        Ui.Clear(_lanceColumn);
        bool locked = _session.ActiveMission is not null;
        var rules = _session.Catalog.Rules;

        foreach (var pilot in _session.Catalog.Pilots.Values)
        {
            bool inLance = _session.Lance.Contains(pilot.Id);
            var (card, column) = Ui.Card();
            _lanceColumn.AddChild(card);

            var toggle = new CheckBox
            {
                Text = $"{pilot.DisplayName}: {pilot.Name}",
                ButtonPressed = inLance,
                Disabled = locked || (!inLance && _session.Lance.Count >= MissionRun.MaxAllies),
                Modulate = inLance ? Ui.Ally : Ui.Ink
            };
            toggle.Toggled += on =>
            {
                var ids = _session.Lance.ToList();
                if (on) ids.Add(pilot.Id);
                else ids.Remove(pilot.Id);
                _session.SetLance(ids);
                Refresh();
            };
            column.AddChild(toggle);
            column.AddChild(Ui.Text(pilot.Description, Ui.Muted, 14));

            if (_session.PilotCrests.TryGetValue(pilot.Id, out var crest))
            {
                column.AddChild(Ui.Text($"{crest.Name}: {string.Join(", ", crest.DamageOrder.Select(e => e.Definition.Name))}", Ui.Ink, 14));
                column.AddChild(Ui.Text(Ui.StatsLine(crest, rules), Ui.Muted, 13));
                var refit = Ui.Button($"Refit {pilot.DisplayName}'s Crest", () => WorkshopRequested?.Invoke(crest.Id));
                refit.Disabled = locked;
                refit.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
                column.AddChild(refit);
            }
        }

        if (locked)
            _lanceColumn.AddChild(Ui.Text("The lance is set while a mission is in progress.", Ui.Muted, 13));
    }

    private void RefreshResearch()
    {
        Ui.Clear(_researchColumn);
        var catalog = _session.Catalog;
        var workshop = _session.Workshop;

        foreach (var material in catalog.Materials.Values)
        {
            int have = workshop.GetSamples(material.Id);
            bool done = workshop.IsResearched(material.Id);
            var row = new HBoxContainer();
            _researchColumn.AddChild(row);
            row.AddChild(Ui.Text(done ? $"{material.Name} ✓" : $"{material.Name}  {have}/{material.SamplesToResearch}",
                done ? Ui.Good : Ui.Ink, 15));
            row.GetChild<Label>(0).SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (!done)
            {
                var button = Ui.Button("Research", () =>
                {
                    var result = _session.Research(material.Id);
                    _messages.Text = result.Message;
                    RefreshResearch();
                    RefreshCrest();
                });
                button.Disabled = !workshop.CanResearch(material);
                row.AddChild(button);
            }

            string unlocks = string.Join(", ", material.UnlocksComponentIds.Select(id => catalog.GetEquipment(id).Name));
            _researchColumn.AddChild(Ui.Text($"Unlocks: {unlocks}", Ui.Muted, 13));
        }
    }

    private void RefreshFooter()
    {
        Ui.Clear(_footer);
        if (_session.ActiveMission is { } run)
        {
            string where = run.Phase == MissionPhase.Rest ? "rest" : $"fight {run.StageIndex + 1}";
            _footer.AddChild(Ui.Button($"Resume {run.Mission.Name} ({where})", () => ResumeRequested?.Invoke(), primary: true));
            _footer.AddChild(Ui.Button("Withdraw (keep samples)", () =>
            {
                _session.WithdrawFromMission();
                Refresh();
            }));
        }
        else
        {
            var go = Ui.Button("Choose a mission ▶", () => MissionSelectRequested?.Invoke(), primary: true);
            go.Disabled = !LoadoutRules.ComputeStats(_session.SelectedLoadout, _session.Catalog.Rules).IsCoreOperational;
            _footer.AddChild(go);
        }

        _footer.AddChild(Ui.Button("Quick battle", () => QuickBattleRequested?.Invoke()));
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _footer.AddChild(spacer);
        var won = Ui.Text($"Encounters won: {_session.EncountersWon}", Ui.Muted, 14);
        won.AutowrapMode = TextServer.AutowrapMode.Off;
        won.VerticalAlignment = VerticalAlignment.Center;
        _footer.AddChild(won);
        _footer.AddChild(Ui.Button("Quit", () => QuitRequested?.Invoke()));
    }
}
