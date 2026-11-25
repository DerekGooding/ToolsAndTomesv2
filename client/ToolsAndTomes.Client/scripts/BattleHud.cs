using Godot;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Client;

/// <summary>
/// Placeholder HUD: status line, lance line, action bar with the Field menu (shaping and cargo), combat log,
/// an inspector (timeline, your Crest, your target), a leave/withdraw button, and the result panel.
/// </summary>
public partial class BattleHud : CanvasLayer
{
    private Label _status = null!;
    private Label _allies = null!;
    private Label _outcome = null!;
    private Label _prompt = null!;
    private HBoxContainer _actionBar = null!;
    private RichTextLabel _log = null!;
    private int _loggedEvents;
    private string _actionBarSignature = "";
    private readonly Dictionary<string, Texture2D?> _icons = [];

    /// <summary>Raised when an action button is pressed.</summary>
    public event Action<ActionDefinition>? ActionPressed;

    /// <summary>Raised when the result panel's button is pressed.</summary>
    public event Action? ContinuePressed;

    /// <summary>Raised when the player picks something to shape or use from the Field menu.</summary>
    public event Action<FieldOption>? FieldOptionChosen;

    /// <summary>Raised when the player leaves the fight (after confirming, when <see cref="LeaveConfirmText"/> is set).</summary>
    public event Action? LeavePressed;

    /// <summary>The leave button's text (null hides it).</summary>
    public string? LeaveLabel { get; set; }

    /// <summary>If set, leaving asks this question first.</summary>
    public string? LeaveConfirmText { get; set; }

    private RichTextLabel _inspector = null!;
    private MenuButton _field = null!;
    private List<FieldOption> _fieldOptions = [];
    private Func<List<FieldOption>>? _fieldSource;
    private Button _leave = null!;

    private PanelContainer _result = null!;
    private Label _resultHeadline = null!;
    private Label _resultBody = null!;
    private Button _resultButton = null!;

    public override void _Ready()
    {
        var top = new PanelContainer();
        top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        AddChild(top);
        var topBox = new VBoxContainer();
        top.AddChild(topBox);
        _status = new Label();
        _allies = new Label { Modulate = new Color(0.55f, 0.95f, 0.8f) };
        _outcome = new Label { Modulate = new Color(1f, 0.85f, 0.3f) };
        _prompt = new Label { Modulate = new Color(1f, 0.55f, 0.3f) };
        topBox.AddChild(_status);
        topBox.AddChild(_allies);
        topBox.AddChild(_outcome);
        topBox.AddChild(_prompt);

        var bottom = new PanelContainer();
        bottom.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        bottom.GrowVertical = Control.GrowDirection.Begin;
        AddChild(bottom);
        var bottomBox = new VBoxContainer();
        bottom.AddChild(bottomBox);
        var barRow = new HBoxContainer();
        barRow.AddThemeConstantOverride("separation", 10);
        bottomBox.AddChild(barRow);
        _field = new MenuButton { Text = "Field ▾", TooltipText = "Shape fast-iron (ammo, consumables, components) or use cargo", FocusMode = Control.FocusModeEnum.None };
        _field.AboutToPopup += BuildFieldMenu;
        _field.GetPopup().IdPressed += id =>
        {
            if (id >= 0 && id < _fieldOptions.Count)
                FieldOptionChosen?.Invoke(_fieldOptions[(int)id]);
        };
        barRow.AddChild(_field);
        _actionBar = new HBoxContainer();
        barRow.AddChild(_actionBar);
        bottomBox.AddChild(new Label
        {
            Text = "Left-click: select / place point action   Right-click: move   Space: pause   . : step beat   " +
                   "Tab: next target   WASD/QE/wheel/middle-drag: camera",
            Modulate = new Color(0.65f, 0.7f, 0.8f)
        });

        var right = new PanelContainer { CustomMinimumSize = new Vector2(430, 260) };
        right.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        right.GrowHorizontal = Control.GrowDirection.Begin;
        right.Position = new Vector2(-430, 90);
        AddChild(right);
        _log = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, CustomMinimumSize = new Vector2(420, 250) };
        right.AddChild(_log);

        // Inspector, top-left under the status lines: the timeline, your Crest, and your target.
        var left = new PanelContainer { CustomMinimumSize = new Vector2(340, 0), Position = new Vector2(0, 110) };
        AddChild(left);
        _inspector = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            CustomMinimumSize = new Vector2(330, 0), MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _inspector.AddThemeFontSizeOverride("normal_font_size", 13);
        left.AddChild(_inspector);

        _leave = new Button { Visible = false, FocusMode = Control.FocusModeEnum.None };
        _leave.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _leave.GrowHorizontal = Control.GrowDirection.Begin;
        _leave.Position = new Vector2(-12, 8);
        _leave.Pressed += Leave;
        AddChild(_leave);

        // The result panel, centered, shown when the fight is decided.
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);
        _result = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(460, 0) };
        var resultStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.1f, 0.15f, 0.97f),
            BorderColor = new Color(1f, 0.78f, 0.3f, 0.8f),
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10
        };
        resultStyle.SetContentMarginAll(22);
        _result.AddThemeStyleboxOverride("panel", resultStyle);
        center.AddChild(_result);
        var resultBox = new VBoxContainer();
        resultBox.AddThemeConstantOverride("separation", 12);
        _result.AddChild(resultBox);
        _resultHeadline = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _resultHeadline.AddThemeFontSizeOverride("font_size", 30);
        _resultBody = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _resultButton = new Button { CustomMinimumSize = new Vector2(220, 40), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        _resultButton.Pressed += () => ContinuePressed?.Invoke();
        resultBox.AddChild(_resultHeadline);
        resultBox.AddChild(_resultBody);
        resultBox.AddChild(_resultButton);
    }

    /// <summary>Where the Field menu gets its options (the player's Crest, right now).</summary>
    public void SetFieldSource(Func<List<FieldOption>> source) => _fieldSource = source;

    private void BuildFieldMenu()
    {
        var popup = _field.GetPopup();
        popup.Clear();
        _fieldOptions = _fieldSource?.Invoke() ?? [];
        if (_fieldOptions.Count == 0)
        {
            popup.AddItem("Nothing to shape or use (needs a Shaping spell, fast-iron, or cargo)", -1);
            popup.SetItemDisabled(0, true);
            return;
        }
        for (int i = 0; i < _fieldOptions.Count; i++)
            popup.AddItem(_fieldOptions[i].Label, i);
    }

    private void Leave()
    {
        if (LeaveConfirmText is null)
        {
            LeavePressed?.Invoke();
            return;
        }
        var dialog = new ConfirmationDialog { Title = LeaveLabel ?? "Leave", DialogText = LeaveConfirmText, OkButtonText = LeaveLabel ?? "Leave" };
        dialog.Confirmed += () => LeavePressed?.Invoke();
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>A one-line note under the status (e.g. what a Field command did).</summary>
    public void Flash(string text) => _flash = text;

    private string _flash = "";

    /// <summary>Shows the result panel (headline null: hidden).</summary>
    public void ShowResult(string? headline, string? body, string? buttonLabel, bool canRestart)
    {
        _result.Visible = headline is not null;
        if (headline is null)
            return;
        _resultHeadline.Text = headline;
        _resultHeadline.Modulate = headline.StartsWith("VICTORY") ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.45f, 0.4f);
        string hint = canRestart ? "Press R to fight again." : "";
        _resultBody.Text = string.Join("\n", new[] { body, hint }.Where(s => !string.IsNullOrEmpty(s)));
        _resultButton.Visible = buttonLabel is not null;
        _resultButton.Text = buttonLabel ?? "";
    }

    public override void _ExitTree() => _icons.Clear();

    public void ResetLog()
    {
        _log.Clear();
        _loggedEvents = 0;
        _actionBarSignature = "";
    }

    public void Refresh(CombatSimulation sim, Combatant player, Combatant? target, double beatFraction, bool slowMotion, ActionDefinition? armed)
    {
        var stats = player.Stats;
        string clock = sim.IsPaused ? "PAUSED" : slowMotion ? "SLOW-MO" : "RUNNING";
        _status.Text =
            $"Beat {sim.CurrentBeat} ({sim.CurrentBeat + beatFraction:F1}s) {clock}   |   " +
            $"Wit {stats.AvailableWit(player.Resources.ReservedWit)}/{stats.MaxWit}   Motes {player.Resources.CurrentMotes}/{stats.MaxMotes}   " +
            $"Heat {player.Resources.CurrentHeat}/{stats.HeatCapacity}   Fast-iron {player.TotalFastIron}   Cargo {stats.CargoUsed}/{stats.CargoCapacity}" +
            (player.HasActiveAegis ? $"   AEGIS {player.AegisBlocksRemaining}" : "") +
            $"   |   Target: {target?.Name ?? "none"}";

        _allies.Text = AlliesLine(sim, player);

        _outcome.Text = sim.Outcome == EncounterOutcome.InProgress && armed is not null
            ? armed.Targeting == TargetingType.AllyUnit
                ? $"Armed {armed.Name}: left-click yourself or an ally."
                : $"Armed {armed.Name}: left-click a point on the field."
            : _flash;
        _leave.Visible = LeaveLabel is not null && sim.Outcome == EncounterOutcome.InProgress;
        _leave.Text = LeaveLabel ?? "";
        RefreshInspector(sim, player, target);

        var impacts = sim.GetIncomingImpacts(player);
        _prompt.Text = impacts.Count > 0
            ? $"INCOMING IMPACT: {string.Join(", ", impacts.Select(i => $"{i.Attacker.Name}'s {i.Action.Definition.Name} ({i.ExpectedDamage})"))} - react now!"
            : "";

        RefreshActionBar(sim, player, target);
        RefreshLog(sim);
    }

    /// <summary>One entry per allied Crest: Core durability, parts still working, and what it is doing.</summary>
    private static string AlliesLine(CombatSimulation sim, Combatant player)
    {
        var allies = sim.Combatants.Where(c => c.Faction == Faction.Player && c.Id != player.Id).ToList();
        if (allies.Count == 0)
            return "";

        return "Lance: " + string.Join("   ", allies.Select(a =>
        {
            if (a.IsDefeated)
                return $"{a.Name} DOWN";
            var core = a.Loadout.Installed.Where(e => e.Definition.IsCore).ToList();
            int coreHp = core.Sum(e => e.CurrentDurability), coreMax = core.Sum(e => e.Definition.MaxDurability);
            int working = a.Loadout.Installed.Count(e => e.IsOperational);
            string doing = a.ActionsInMotion.FirstOrDefault() is { } act ? act.Definition.Name : a.Movement is not null ? "moving" : "ready";
            return $"{a.Name} Core {coreHp}/{coreMax} · parts {working}/{a.Loadout.Installed.Count} · {doing}";
        }));
    }

    private void RefreshActionBar(CombatSimulation sim, Combatant player, Combatant? target)
    {
        // Weapons and defenses, then tome spells, then the core spells every pilot knows; swaps live in the Field menu.
        var core = sim.Catalog.Rules.CoreSpellIds;
        var actions = player.Loadout.GetOperationalActionIds()
            .Select(sim.Catalog.GetAction)
            .Where(a => !a.IsShaping && !a.RefillsAmmo && !a.IsSwap)
            .OrderBy(a => core.Contains(a.Id) ? 2 : a.Category == ActionCategory.Spell ? 1 : 0)
            .ThenBy(a => a.Category).ThenBy(a => a.Name)
            .ToList();

        var states = actions.Select(a =>
        {
            Combatant? unit = a.Targeting == TargetingType.EnemyUnit ? target : a.Targeting == TargetingType.AllyUnit ? player : null;
            Vec2? point = a.Targeting == TargetingType.Point ? player.Position : null;
            bool valid = sim.ValidateUse(player, a, unit, point).IsValid;
            int cooldown = player.GetCooldown(a.Id);
            return (Action: a, Valid: valid, Cooldown: cooldown);
        }).ToList();

        string signature = string.Join("|", states.Select(s => $"{s.Action.Id}:{s.Valid}:{s.Cooldown}"));
        if (signature == _actionBarSignature)
            return;
        _actionBarSignature = signature;

        foreach (var child in _actionBar.GetChildren())
            child.QueueFree();

        foreach (var (action, valid, cooldown) in states)
        {
            string cost = (action.WitReserve > 0 ? $" W{action.WitReserve}" : "") + (action.MoteCost > 0 ? $" M{action.MoteCost}" : "");
            var button = new Button
            {
                Text = $"{action.Name}{cost}{(cooldown > 0 ? $" ({cooldown}s)" : "")}",
                Disabled = !valid,
                TooltipText = $"{action.Description}\nRange {action.Range} | {action.WindupBeats}/{action.ImpactBeats}/{action.RecoveryBeats} beats",
                FocusMode = Control.FocusModeEnum.None
            };
            if (Icon(action.Id) is { } icon)
            {
                button.Icon = icon;
                button.IconAlignment = HorizontalAlignment.Left;
                button.AddThemeConstantOverride("icon_max_width", 32);
            }
            if (action.IsReactive)
                button.Modulate = new Color(0.6f, 1f, 0.85f);
            else if (core.Contains(action.Id))
                button.Modulate = new Color(0.8f, 0.8f, 0.9f);
            else if (action.Category == ActionCategory.Spell)
                button.Modulate = new Color(0.85f, 0.7f, 1f);
            button.TooltipText = (core.Contains(action.Id) ? "Core spell (every pilot knows it)\n"
                                  : action.Category == ActionCategory.Spell ? $"Tome spell\n" : "") + button.TooltipText;
            button.Pressed += () => ActionPressed?.Invoke(action);
            _actionBar.AddChild(button);
        }
    }

    private void RefreshInspector(CombatSimulation sim, Combatant player, Combatant? target)
    {
        var text = new System.Text.StringBuilder();
        text.Append("[b]Timeline[/b]\n");
        var entries = sim.GetTimelineEntries()
            .OrderBy(e => e.Faction)
            .ThenBy(e => e.BeatsUntilImpact < 0 ? 999 : e.BeatsUntilImpact)
            .ToList();
        if (entries.Count == 0)
            text.Append("[color=#8a8f99]Everyone idle.[/color]\n");
        foreach (var e in entries.Take(8))
        {
            string who = e.Faction == Faction.Player ? "#7cc4ff" : "#ff9a5c";
            string state = e.State switch
            {
                ActionState.Windup => "#ffd36b",
                ActionState.Executing => "#ff6b6b",
                ActionState.Recovery => "#b89cff",
                ActionState.Interrupted => "#ff6b6b",
                _ => "#8a8f99"
            };
            string impact = e.BeatsUntilImpact >= 0 ? $" · hits in {e.BeatsUntilImpact}" : "";
            text.Append($"[color={who}]{Esc(e.ActorName)}[/color] [color={state}]{e.State}[/color] {Esc(e.ActionName)} ({e.BeatsRemaining}){impact}\n");
        }

        AppendCrest(text, "You", player);
        if (target is not null)
            AppendCrest(text, "Target", target);
        _inspector.Text = text.ToString();
    }

    private static void AppendCrest(System.Text.StringBuilder text, string heading, Combatant c)
    {
        text.Append($"\n[b]{heading}: {Esc(c.Name)}[/b] [color=#8a8f99]{c.State}[/color]\n");
        int n = 1;
        foreach (var part in c.Loadout.DamageOrder)
        {
            var d = part.Definition;
            string color = part.IsDisabled ? "#ff6b6b" : part.CurrentDurability < d.MaxDurability ? "#ffb36b" : "#d8dde6";
            string hp = part.IsDisabled ? "OFF" : $"{part.CurrentDurability}/{d.MaxDurability}";
            string ammo = d.MaxAmmo > 0 ? $" ammo {part.CurrentAmmo}" : "";
            text.Append($"[color={color}]{n++}. {Esc(d.Name)} {hp}{ammo}[/color]\n");
        }
    }

    private static string Esc(string s) => s.Replace("[", "[lb]");

    private void RefreshLog(CombatSimulation sim)
    {
        for (; _loggedEvents < sim.Events.Count; _loggedEvents++)
        {
            var e = sim.Events[_loggedEvents];
            if (e.Type == CombatEventType.MoteGenerated)
                continue;

            string color = e.Type switch
            {
                CombatEventType.ComponentDisabled or CombatEventType.CombatantDefeated => "#ff6b6b",
                CombatEventType.ComponentDamaged => "#ffb36b",
                CombatEventType.ActionInterrupted => "#ffd36b",
                CombatEventType.CommandRejected => "#8a8f99",
                CombatEventType.AegisBlocked or CombatEventType.DamageBlocked => "#7fe0c8",
                _ => "#d8dde6"
            };
            _log.AppendText($"[color={color}][{e.Beat}s] {EscapeBbcode(e.Message)}[/color]\n");
        }
    }

    /// <summary>The pipeline's icon for a content id (res://assets/icons/&lt;id&gt;.png), if one has been approved.</summary>
    private Texture2D? Icon(string contentId)
    {
        if (_icons.TryGetValue(contentId, out var cached))
            return cached;
        string path = $"res://assets/icons/{contentId}.png";
        return _icons[contentId] = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
    }

    private static string EscapeBbcode(string text) => text.Replace("[", "[lb]");
}
