using Godot;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Client.UI;

/// <summary>
/// Rest in the field between a mission's fights: time passes one Mote at a time; Square Summon, repairs, and
/// handing fast-iron to allies, shaping fast-iron, and using cargo happen between Motes. Every action saves the mission.
/// </summary>
public partial class RestScreen : Control
{
    private readonly CampaignSession _session;
    private string? _crestId; // null: the player's Crest
    private EquipmentInstance? _selectedPart;

    private Label _title = null!;
    private Label _next = null!;
    private HBoxContainer _crestTabs = null!;
    private Label _status = null!;
    private VBoxContainer _parts = null!;
    private VBoxContainer _actions = null!;
    private VBoxContainer _log = null!;
    private Label _message = null!;

    public event Action? ContinueRequested;
    public event Action? WithdrawRequested;

    public RestScreen() : this(null!) { }

    public RestScreen(CampaignSession session) => _session = session;

    private MissionRun? Run => _session.ActiveMission is { Phase: MissionPhase.Rest } run ? run : null;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var (root, column) = Ui.Screen();
        AddChild(root);

        var header = new HBoxContainer();
        column.AddChild(header);
        _title = Ui.Title("");
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(_title);
        header.AddChild(Ui.Button("Withdraw (keep samples)", () =>
        {
            _session.WithdrawFromMission();
            WithdrawRequested?.Invoke();
        }));
        header.AddChild(Ui.Button("Continue to the next fight ▶", Continue, primary: true));

        _next = Ui.Text("", Ui.Muted);
        column.AddChild(_next);

        _crestTabs = new HBoxContainer();
        _crestTabs.AddThemeConstantOverride("separation", 8);
        column.AddChild(_crestTabs);
        _status = Ui.Text("", Ui.Ink, 15);
        column.AddChild(_status);

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 16);
        column.AddChild(columns);

        var (partsCard, partsColumn) = Ui.Card();
        partsCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        partsCard.SizeFlagsStretchRatio = 1.2f;
        columns.AddChild(partsCard);
        partsColumn.AddChild(Ui.Heading("Components: pick one to repair", Ui.Accent));
        _parts = Ui.Scrolling(partsColumn);

        var (actionsCard, actionsColumn) = Ui.Card();
        actionsCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(actionsCard);
        actionsColumn.AddChild(Ui.Heading("Rest actions", Ui.Accent));
        actionsColumn.AddChild(Ui.Text("Time passes one Mote at a time. Square Summon, repairs, and handing over fast-iron happen between Motes and cost no time.", Ui.Muted, 13));
        _actions = new VBoxContainer();
        _actions.AddThemeConstantOverride("separation", 8);
        actionsColumn.AddChild(_actions);
        actionsColumn.AddChild(Ui.Heading("Rest log"));
        _log = Ui.Scrolling(actionsColumn);

        _message = Ui.Text("", Ui.Accent);
        column.AddChild(_message);
        Refresh();
    }

    public void Refresh()
    {
        if (Run is not { } run)
            return;

        var crest = run.FindCrest(_crestId) ?? run.Crest;
        var next = run.Catalog.Encounters[run.Mission.EncounterIds[run.StageIndex + 1]];
        _title.Text = $"Rest in the field · {run.Mission.Name}";
        _next.Text = $"After fight {run.StageIndex + 1} of {run.Mission.EncounterIds.Count}. Next: {next.Name}: {next.Description}\n" +
                     $"Time left: {run.RestBeatsRemaining} beats   ·   Samples banked this mission: {run.RewardsEarned.Sum(r => r.Count)}";

        Ui.Clear(_crestTabs);
        foreach (var member in run.Lance)
        {
            string label = (member == run.Crest ? $"{member.Name} (you)" : member.Name) + (member.IsDefeated ? " · DOWN" : "");
            var tab = new Button { Text = label, ToggleMode = true, ButtonPressed = member == crest, CustomMinimumSize = new Vector2(0, 34) };
            tab.Modulate = member.IsDefeated ? Ui.Bad : AllyAIController.IsAlly(member) ? Ui.Ally : Ui.Ink;
            var id = member == run.Crest ? null : member.Id;
            tab.Pressed += () =>
            {
                _crestId = id;
                _selectedPart = null;
                Refresh();
            };
            _crestTabs.AddChild(tab);
        }

        var stats = crest.Stats;
        _status.Text = $"{crest.Name}{(crest.IsDefeated ? ": DOWN (repair its Core to bring it back)" : "")}   ·   " +
                       $"Motes {crest.Resources.CurrentMotes}/{stats.MaxMotes}   ·   Fast-iron {crest.TotalFastIron} " +
                       $"({crest.Resources.FastIron} loose, {crest.Loadout.CarriedFastIron} in cargo)   ·   Cargo {stats.CargoUsed}/{stats.CargoCapacity}";

        Ui.Clear(_parts);
        int n = 1;
        foreach (var part in crest.Loadout.DamageOrder)
        {
            var item = part;
            bool damaged = part.CurrentDurability < part.Definition.MaxDurability;
            var button = new Button
            {
                Text = Ui.ComponentLine(part, n++) + (damaged ? "   ⚒" : ""),
                ToggleMode = true,
                ButtonPressed = part == _selectedPart,
                Alignment = HorizontalAlignment.Left,
                Disabled = !damaged,
                Modulate = part.IsDisabled ? Ui.Bad : damaged ? Ui.Accent : Ui.Ink
            };
            button.Pressed += () =>
            {
                _selectedPart = item;
                Refresh();
            };
            _parts.AddChild(button);
        }

        Ui.Clear(_actions);
        var wait = Ui.Button($"Wait {run.MoteIntervalBeats} beats (+1 Mote for every standing Crest)", () => Do(run.Wait()));
        wait.Disabled = run.RestBeatsRemaining < run.MoteIntervalBeats;
        _actions.AddChild(wait);
        var waitAll = Ui.Button("Wait out the rest", WaitOut);
        waitAll.Disabled = wait.Disabled;
        _actions.AddChild(waitAll);

        var summon = crest.Loadout.GetOperationalActionIds().Select(run.Catalog.GetAction).FirstOrDefault(a => a.FastIronProduced > 0);
        var summonButton = Ui.Button(summon is null ? "No Square Summon on this Crest" : $"{summon.Name}: {summon.MoteCost} Motes → {summon.FastIronProduced} fast-iron",
            () => Do(run.SquareSummon(_crestId)));
        summonButton.Disabled = summon is null || crest.IsDefeated || crest.Resources.CurrentMotes < summon.MoteCost;
        _actions.AddChild(summonButton);

        var repair = Ui.Button(_selectedPart is null
                ? "Repair: pick a damaged component"
                : $"Repair {_selectedPart.Definition.Name} (1 fast-iron → +{Math.Max(1, run.Catalog.Rules.RepairPointsPerFastIron)})",
            () =>
            {
                if (_selectedPart is not null)
                    Do(run.Repair(_selectedPart.InstanceId, _crestId));
                if (_selectedPart is { } p && p.CurrentDurability >= p.Definition.MaxDurability)
                    _selectedPart = null;
                Refresh();
            });
        repair.Disabled = _selectedPart is null || crest.TotalFastIron == 0;
        _actions.AddChild(repair);

        if (crest != run.Crest)
        {
            var give = Ui.Button($"Give 1 fast-iron from your Crest ({run.Crest.TotalFastIron} on hand)", () => Do(run.GiveFastIron(null, _crestId)));
            give.Disabled = run.Crest.TotalFastIron == 0;
            _actions.AddChild(give);
        }

        // Shaping fast-iron (ammo, consumables, whole components) and using cargo items.
        var options = crest.IsDefeated ? [] : FieldOption.Build(run.Catalog, crest);
        if (options.Count > 0)
        {
            _actions.AddChild(Ui.Text("Shape fast-iron or use cargo:", Ui.Accent, 14));
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            _actions.AddChild(row);
            var pick = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            foreach (var option in options)
                pick.AddItem(option.Label);
            row.AddChild(pick);
            row.AddChild(Ui.Button("Do it", () =>
            {
                var cmd = options[pick.Selected].Command;
                Do(cmd.Shaping is { } order
                    ? run.Shape(order, cmd.OwnComponentInstanceId, _crestId)
                    : run.UseCargoItem(cmd.ActionId, cmd.OwnComponentInstanceId ?? "", _crestId));
            }));
        }

        Ui.Clear(_log);
        foreach (var entry in run.RestLog.Reverse().Take(30))
            _log.AddChild(Ui.Text(entry.Message, Ui.Muted, 13));
    }

    /// <summary>Spends the rest with the balance runner's policy (summon, wait, repair the lance), for automated runs.</summary>
    public void AutoRest()
    {
        if (Run is { } run)
        {
            BalanceRunner.AutoRest(run);
            _session.Save();
            Refresh();
        }
    }

    public void Continue()
    {
        if (Run is not { } run)
            return;
        run.ContinueToNextEncounter();
        _session.Save();
        _crestId = null;
        _selectedPart = null;
        ContinueRequested?.Invoke();
    }

    private void WaitOut()
    {
        if (Run is not { } run)
            return;
        int waits = 0;
        while (run.Wait().Succeeded)
            waits++;
        _session.Save();
        _message.Text = $"Waited out the rest ({waits} Mote interval(s)).";
        Refresh();
    }

    private void Do(RestResult result)
    {
        _message.Text = result.Message;
        _message.Modulate = result.Succeeded ? Ui.Accent : Ui.Bad;
        if (result.Succeeded)
            _session.Save();
        Refresh();
    }
}
