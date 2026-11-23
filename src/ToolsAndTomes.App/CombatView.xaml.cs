using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.App;

public partial class CombatView : UserControl
{
    private sealed record ComponentRow(EquipmentInstance Instance, int OrderIndex)
    {
        public override string ToString()
        {
            var d = Instance.Definition;
            string hp = Instance.IsDisabled ? "[DISABLED]" : $"{Instance.CurrentDurability}/{d.MaxDurability}";
            string ammo = d.MaxAmmo > 0 ? $" | Ammo {Instance.CurrentAmmo}/{d.MaxAmmo}" : "";
            return $"{OrderIndex}. {d.Name} — {hp}{ammo}";
        }
    }

    /// <summary>Wall-clock length of one beat (one tabletop second) at normal speed.</summary>
    private static readonly TimeSpan BeatDuration = TimeSpan.FromSeconds(1);

    private readonly AppSessionState _session;
    private readonly DispatcherTimer _timer;
    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private TimeSpan _beatElapsed;
    private string? _selectedCombatantId;
    private ActionDefinition? _armedPointAction;
    private int _lastRenderedEventCount;

    public event Action? ReturnToHangarRequested;

    /// <summary>How much longer a beat lasts (wall-clock) while the player has a reaction window open.</summary>
    private const double SlowMotionFactor = 3.0;

    private TimeSpan _currentBeatDuration = BeatDuration;

    /// <summary>Progress through the current beat (0..1), used only to smooth presentation.</summary>
    private double BeatFraction => Math.Clamp(_beatElapsed / _currentBeatDuration, 0, 1);

    /// <summary>
    /// The reactions the player could declare right now (an enemy impact is landing on them).
    /// Presentation only: slow motion never changes simulation results.
    /// </summary>
    private List<ActionDefinition> AvailableReactions(CombatSimulation sim)
    {
        var player = sim.FindCombatant("player_crest");
        if (player is null || player.IsDefeated)
            return [];

        var impacts = sim.GetIncomingImpacts(player);
        if (impacts.Count == 0)
            return [];

        var attacker = impacts[0].Attacker;
        return player.Loadout.GetOperationalActionIds()
            .Select(id => sim.Catalog.GetAction(id))
            .Where(a => a.IsReactive)
            .Where(a => sim.ValidateUse(player, a, a.Targeting == TargetingType.EnemyUnit ? attacker : null, null).IsValid)
            .ToList();
    }

    public CombatView(AppSessionState session)
    {
        InitializeComponent();
        _session = session;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += Timer_Tick;
    }

    public void StartNewEncounter()
    {
        var sim = _session.StartEncounter();
        _selectedCombatantId = sim.Combatants.FirstOrDefault(c => c.Faction == Faction.Enemy)?.Id;
        _armedPointAction = null;
        _lastRenderedEventCount = 0;
        _beatElapsed = TimeSpan.Zero;
        _currentBeatDuration = BeatDuration;
        LstCombatLog.Items.Clear();

        _frameClock.Restart();
        _timer.Start();
        RefreshAll();
    }

    public void StopLoop() => _timer.Stop();

    public void TogglePause()
    {
        if (_session.ActiveSimulation is null)
            return;

        _session.ActiveSimulation.TogglePause();
        RefreshAll();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var frameDelta = _frameClock.Elapsed;
        _frameClock.Restart();

        var sim = _session.ActiveSimulation;
        if (sim is null || sim.IsPaused || sim.Outcome != EncounterOutcome.InProgress)
            return;

        _beatElapsed += frameDelta;
        while (_beatElapsed >= _currentBeatDuration && sim.Outcome == EncounterOutcome.InProgress)
        {
            _beatElapsed -= _currentBeatDuration;
            AdvanceOneBeat(sim);
        }

        RefreshAll();
    }

    private sealed record FieldOption(string Label, UseActionCommand Command)
    {
        public override string ToString() => Label;
    }

    /// <summary>Everything the player can shape from fast-iron or use from cargo right now.</summary>
    private static List<FieldOption> BuildFieldOptions(CombatSimulation sim, Combatant player)
    {
        var options = new List<FieldOption>();
        var catalog = sim.Catalog;
        var available = player.Loadout.GetOperationalActionIds().ToHashSet();
        var ammoWeapons = player.Loadout.Installed
            .Where(e => e.IsOperational && e.Definition.MaxAmmo > 0 && e.CurrentAmmo < e.Definition.MaxAmmo)
            .ToList();

        var shapeSpell = available.Select(catalog.GetAction).FirstOrDefault(a => a.IsShaping);
        if (shapeSpell is not null)
        {
            foreach (var w in ammoWeapons)
                options.Add(new FieldOption(
                    $"Shape ammo → {w.Definition.Name} ({w.Definition.AmmoRefillFastIronCost} fast-iron)",
                    new UseActionCommand(player.Id, shapeSpell.Id, OwnComponentInstanceId: w.InstanceId,
                        Shaping: new ShapingOrder(ShapingKind.AmmoRefill))));

            foreach (var c in catalog.Consumables.Values.OrderBy(c => c.Name))
                options.Add(new FieldOption(
                    $"Shape {c.Name} into cargo ({c.FastIronCost} fast-iron)",
                    new UseActionCommand(player.Id, shapeSpell.Id, Shaping: new ShapingOrder(ShapingKind.Consumable, c.Id))));

            var known = player.KnownBlueprints ?? catalog.Equipment.Keys.ToHashSet();
            foreach (var e in catalog.Equipment.Values.Where(e => known.Contains(e.Id)).OrderBy(e => e.Category).ThenBy(e => e.Name))
                options.Add(new FieldOption(
                    $"Shape {e.Name} ({ShapingRules.ComponentCost(e, player.Rules)} fast-iron) — joins end of Damage Order",
                    new UseActionCommand(player.Id, shapeSpell.Id, Shaping: new ShapingOrder(ShapingKind.Component, e.Id))));
        }

        foreach (var refill in available.Select(catalog.GetAction).Where(a => a.RefillsAmmo && a.ConsumableItemId is not null))
        {
            int count = player.Loadout.GetConsumableCount(refill.ConsumableItemId!);
            if (count == 0)
                continue;
            foreach (var w in ammoWeapons)
                options.Add(new FieldOption(
                    $"{refill.Name} → {w.Definition.Name} ({count} carried)",
                    new UseActionCommand(player.Id, refill.Id, OwnComponentInstanceId: w.InstanceId)));
        }

        return options;
    }

    private void CmbField_DropDownOpened(object? sender, EventArgs e)
    {
        var sim = _session.ActiveSimulation;
        var player = sim?.FindCombatant(EncounterFactory.PlayerCombatantId);
        CmbField.ItemsSource = sim is null || player is null ? null : BuildFieldOptions(sim, player);
    }

    private void BtnFieldUse_Click(object sender, RoutedEventArgs e)
    {
        var sim = _session.ActiveSimulation;
        if (sim is null || CmbField.SelectedItem is not FieldOption option)
        {
            TxtActionStatus.Text = "Pick something to shape or use first.";
            return;
        }

        var result = sim.IssueCommand(option.Command);
        TxtActionStatus.Text = result.IsValid ? $"Issued: {option.Label}" : $"Cannot: {result.FailureReason}";
        CmbField.SelectedItem = null;
        RefreshAll();
    }

    private string DescribeRewards(IReadOnlyList<MaterialCount> rewards) =>
        rewards.Count == 0
            ? "none"
            : string.Join(", ", rewards.Select(r => $"{_session.Catalog.Materials[r.MaterialId].Name} ×{r.Count}"));

    private void AdvanceOneBeat(CombatSimulation sim)
    {
        _session.ActiveAI.Update(sim);
        sim.StepBeat();
        UpdateSlowMotion(sim);
    }

    private void UpdateSlowMotion(CombatSimulation sim)
    {
        var reactions = AvailableReactions(sim);
        bool slow = ChkSlowMotion.IsChecked == true && reactions.Count > 0;
        _currentBeatDuration = slow ? BeatDuration * SlowMotionFactor : BeatDuration;

        if (reactions.Count > 0)
            TxtActionStatus.Text = $"⚠ INCOMING IMPACT — react now: {string.Join(", ", reactions.Select(r => r.Name))}";
    }

    private void RefreshAll()
    {
        var sim = _session.ActiveSimulation;
        if (sim is null)
            return;

        BtnPauseToggle.Content = sim.IsPaused ? "▶ RESUME (Space)" : "⏸ PAUSE (Space)";
        BtnStepTick.IsEnabled = sim.IsPaused && sim.Outcome == EncounterOutcome.InProgress;
        TxtClock.Text = $"Beat {sim.CurrentBeat} ({sim.CurrentBeat + BeatFraction:F1}s) | {(sim.IsPaused ? "PAUSED" : _currentBeatDuration > BeatDuration ? "SLOW-MO" : "RUNNING")}";

        TxtOutcome.Text = sim.Outcome switch
        {
            EncounterOutcome.PlayerVictory => $"★ VICTORY! Samples recovered: {DescribeRewards(_session.CompleteEncounter(sim))}",
            EncounterOutcome.PlayerDefeat => "✖ DEFEAT! Core failed. Return to Hangar to repair.",
            _ => ""
        };

        var player = sim.FindCombatant("player_crest");
        if (player is not null)
        {
            var stats = player.Stats;
            int availWit = stats.AvailableWit(player.Resources.ReservedWit);
            string aegis = player.HasActiveAegis ? $" | AEGIS: {player.AegisBlocksRemaining}" : "";
            string evade = player.IsEvading ? " | EVADING" : "";
            string heatWarn = player.Resources.IsOverheated ? " [OVERHEAT]" : "";
            TxtPlayerResources.Text =
                $"Wit: {availWit}/{stats.MaxWit} (Tax {stats.EquipmentWitTax}, Res {player.Resources.ReservedWit})  |  " +
                $"Motes: {player.Resources.CurrentMotes}/{stats.MaxMotes}  |  " +
                $"Heat: {player.Resources.CurrentHeat}/{stats.HeatCapacity}{heatWarn}{aegis}{evade}" +
                $"  |  Fast-iron: {player.TotalFastIron} ({player.Resources.FastIron} loose)  |  Cargo: {stats.CargoUsed}/{stats.CargoCapacity}";

            RebuildActionBar(sim, player);
        }

        BattlefieldRenderer.Render(ArenaCanvas, sim, _selectedCombatantId, _armedPointAction, BeatFraction);
        TimelineRenderer.Render(TimelinePanel, sim.GetTimelineEntries(), BeatFraction);
        RefreshInspector(sim);
        RefreshCombatLog(sim);
    }

    private void RebuildActionBar(CombatSimulation sim, Combatant player)
    {
        var selectedTarget = _selectedCombatantId is not null ? sim.FindCombatant(_selectedCombatantId) : null;
        if (selectedTarget?.Faction == Faction.Player)
            selectedTarget = sim.Combatants.FirstOrDefault(c => c.Faction == Faction.Enemy && !c.IsDefeated);

        ActionBarBuilder.Populate(
            ActionBarPanel,
            sim,
            player,
            selectedTarget,
            _armedPointAction,
            (def, target) => OnActionClicked(sim, player, def, target),
            () =>
            {
                _armedPointAction = null;
                sim.IssueCommand(new CancelActionCommand(player.Id));
                RefreshAll();
            });
    }

    private void OnActionClicked(CombatSimulation sim, Combatant player, ActionDefinition def, Combatant? target)
    {
        if (def.Targeting == TargetingType.Point)
        {
            _armedPointAction = def;
            TxtActionStatus.Text = $"Armed {def.Name}: Left-Click destination on the battlefield.";
            RefreshAll();
            return;
        }

        _armedPointAction = null;
        var res = sim.IssueCommand(new UseActionCommand(
            player.Id,
            def.Id,
            def.Targeting == TargetingType.EnemyUnit ? target?.Id : null));

        TxtActionStatus.Text = res.IsValid
            ? $"Issued {def.Name}" + (target is not null && def.Targeting == TargetingType.EnemyUnit ? $" -> {target.Name}" : ".")
            : $"Cannot use {def.Name}: {res.FailureReason}";
        RefreshAll();
    }

    private void RefreshInspector(CombatSimulation sim)
    {
        var c = _selectedCombatantId is not null ? sim.FindCombatant(_selectedCombatantId) : null;
        if (c is null)
            return;

        var s = c.Stats;
        TxtInspectorTitle.Text = $"{c.Name} ({c.Faction})";
        TxtInspectorStats.Text =
            $"State: {c.State} | Pos: ({c.Position.X:F1}, {c.Position.Y:F1})\n" +
            $"Wt: {s.TotalWeight:F0} | Spd: {s.MoveSpeed}/beat | Prot: 1+{s.BonusProtectionDice} d6 | DispRes: {s.DisplacementResistance}";

        var rows = c.Loadout.DamageOrder.Select((e, i) => new ComponentRow(e, i + 1)).ToList();
        LstInspectorComponents.ItemsSource = rows;
    }

    private void RefreshCombatLog(CombatSimulation sim)
    {
        if (sim.Events.Count == _lastRenderedEventCount)
            return;

        for (int i = _lastRenderedEventCount; i < sim.Events.Count; i++)
        {
            var ev = sim.Events[i];
            LstCombatLog.Items.Insert(0, $"[{ev.Beat}s] {ev.Message}");
        }

        _lastRenderedEventCount = sim.Events.Count;
    }

    private void ArenaCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var sim = _session.ActiveSimulation;
        if (sim is null)
            return;

        var world = BattlefieldRenderer.CanvasToWorld(e.GetPosition(ArenaCanvas), ArenaCanvas, sim);
        var player = sim.FindCombatant("player_crest");

        if (_armedPointAction is not null && player is not null)
        {
            var res = sim.IssueCommand(new UseActionCommand(player.Id, _armedPointAction.Id, null, world));
            TxtActionStatus.Text = res.IsValid
                ? $"Issued {_armedPointAction.Name} to ({world.X:F1}, {world.Y:F1})."
                : $"Cannot use {_armedPointAction.Name}: {res.FailureReason}";
            _armedPointAction = null;
            RefreshAll();
            return;
        }

        var nearest = sim.Combatants
            .OrderBy(c => c.Position.DistanceTo(world))
            .FirstOrDefault(c => c.Position.DistanceTo(world) <= 1.5);

        if (nearest is not null)
        {
            _selectedCombatantId = nearest.Id;
            RefreshAll();
        }
    }

    private void ArenaCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var sim = _session.ActiveSimulation;
        var player = sim?.FindCombatant("player_crest");
        if (sim is null || player is null)
            return;

        _armedPointAction = null;
        var world = BattlefieldRenderer.CanvasToWorld(e.GetPosition(ArenaCanvas), ArenaCanvas, sim);
        sim.IssueCommand(new MoveCommand(player.Id, world));
        TxtActionStatus.Text = $"Move command issued -> ({world.X:F1}, {world.Y:F1}).";
        RefreshAll();
    }

    private void ArenaCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshAll();
    private void BtnPauseToggle_Click(object sender, RoutedEventArgs e) => TogglePause();

    private void BtnStepTick_Click(object sender, RoutedEventArgs e)
    {
        var sim = _session.ActiveSimulation;
        if (sim is null || !sim.IsPaused)
            return;

        sim.Resume();
        AdvanceOneBeat(sim);
        sim.Pause();
        _beatElapsed = TimeSpan.Zero;
        RefreshAll();
    }

    private void BtnReturnHangar_Click(object sender, RoutedEventArgs e)
    {
        StopLoop();
        ReturnToHangarRequested?.Invoke();
    }
}
