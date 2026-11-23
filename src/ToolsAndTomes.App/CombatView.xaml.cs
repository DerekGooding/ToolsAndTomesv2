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
    private sealed record ComponentRow(EquipmentInstance Instance)
    {
        public override string ToString()
        {
            var d = Instance.Definition;
            string hp = Instance.IsDisabled ? "[DISABLED]" : $"{Instance.CurrentDurability}/{d.MaxDurability}";
            string ammo = d.MaxAmmo > 0 ? $" | Ammo {Instance.CurrentAmmo}/{d.MaxAmmo}" : "";
            return $"{d.Name} — {hp}{ammo}";
        }
    }

    /// <summary>Wall-clock length of one beat (one tabletop second) at normal speed.</summary>
    private static readonly TimeSpan BeatDuration = TimeSpan.FromSeconds(1);

    private readonly AppSessionState _session;
    private readonly DispatcherTimer _timer;
    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private TimeSpan _beatElapsed;
    private string? _selectedCombatantId;
    private string? _targetedComponentId;
    private ActionDefinition? _armedPointAction;
    private int _lastRenderedEventCount;

    public event Action? ReturnToHangarRequested;

    /// <summary>Progress through the current beat (0..1), used only to smooth presentation.</summary>
    private double BeatFraction => Math.Clamp(_beatElapsed / BeatDuration, 0, 1);

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
        _targetedComponentId = null;
        _armedPointAction = null;
        _lastRenderedEventCount = 0;
        _beatElapsed = TimeSpan.Zero;
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
        while (_beatElapsed >= BeatDuration && sim.Outcome == EncounterOutcome.InProgress)
        {
            _beatElapsed -= BeatDuration;
            AdvanceOneBeat(sim);
        }

        RefreshAll();
    }

    private void AdvanceOneBeat(CombatSimulation sim)
    {
        _session.ActiveAI.Update(sim);
        sim.StepBeat();
    }

    private void RefreshAll()
    {
        var sim = _session.ActiveSimulation;
        if (sim is null)
            return;

        BtnPauseToggle.Content = sim.IsPaused ? "▶ RESUME (Space)" : "⏸ PAUSE (Space)";
        BtnStepTick.IsEnabled = sim.IsPaused && sim.Outcome == EncounterOutcome.InProgress;
        TxtClock.Text = $"Beat {sim.CurrentBeat} ({sim.CurrentBeat + BeatFraction:F1}s) | {(sim.IsPaused ? "PAUSED" : "RUNNING")}";

        TxtOutcome.Text = sim.Outcome switch
        {
            EncounterOutcome.PlayerVictory => "★ VICTORY! Return to Hangar to repair/reconfigure.",
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
                (player.Resources.FastIron > 0 ? $"  |  Fast-iron: {player.Resources.FastIron}" : "");

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
        string? compTarget = target is not null && target.Id == _selectedCombatantId ? _targetedComponentId : null;
        var res = sim.IssueCommand(new UseActionCommand(
            player.Id,
            def.Id,
            def.Targeting == TargetingType.EnemyUnit ? target?.Id : null,
            null,
            compTarget));

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

        var rows = c.Loadout.Installed.Select(e => new ComponentRow(e)).ToList();
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
            _targetedComponentId = null;
            TxtTargetedCompHint.Text = nearest.Faction == Faction.Enemy
                ? $"Selected {nearest.Name}. Click a component to aim specifically at it."
                : "Inspecting Player Crest.";
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

    private void LstInspectorComponents_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstInspectorComponents.SelectedItem is ComponentRow row)
        {
            var sim = _session.ActiveSimulation;
            var selected = _selectedCombatantId is not null ? sim?.FindCombatant(_selectedCombatantId) : null;
            if (selected?.Faction == Faction.Enemy && row.Instance.IsOperational)
            {
                _targetedComponentId = row.Instance.InstanceId;
                TxtTargetedCompHint.Text = $"Aiming at: {row.Instance.Definition.Name}";
            }
        }
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
