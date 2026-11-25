using Godot;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Client.UI;

/// <summary>
/// Refit any Crest (your assemblies or an allied pilot's) between missions: Damage Order, installing and
/// uninstalling, moving parts between Crests, fabricating known blueprints, cargo, reaction rules, and repairs.
/// Every change saves the profile.
/// </summary>
public partial class WorkshopScreen : Control
{
    private const string AnyTelegraph = "Any impact";

    private readonly CampaignSession _session;
    private int _targetIndex;
    private string? _selectedPart;
    private string? _selectedSpare;
    private int _selectedRule = -1;
    private int _selectedCargo = -1;

    private OptionButton _targetPicker = null!;
    private LineEdit _name = null!;
    private Button _saveCopy = null!;
    private Label _message = null!;
    private VBoxContainer _installed = null!;
    private HBoxContainer _partTools = null!;
    private VBoxContainer _middle = null!;
    private VBoxContainer _right = null!;

    public event Action? BackRequested;

    public WorkshopScreen() : this(null!) { }

    public WorkshopScreen(CampaignSession session) => _session = session;

    /// <summary>Opens the Workshop on a given Crest (e.g. a pilot's), by loadout id.</summary>
    public void Focus(string loadoutId)
    {
        int index = _session.RefitTargets.ToList().FindIndex(t => t.Loadout.Id == loadoutId);
        if (index >= 0)
            _targetIndex = index;
    }

    private RefitTarget Target => _session.RefitTargets[Math.Clamp(_targetIndex, 0, _session.RefitTargets.Count - 1)];
    private CrestLoadout Crest => Target.Loadout;
    private RulesConfig Rules => _session.Catalog.Rules;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var (root, column) = Ui.Screen();
        AddChild(root);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        column.AddChild(header);
        header.AddChild(Ui.Title("Workshop"));
        _targetPicker = new OptionButton { CustomMinimumSize = new Vector2(320, 0), TooltipText = "The Crest to refit" };
        _targetPicker.ItemSelected += index =>
        {
            _targetIndex = (int)index;
            ClearSelection();
            Refresh();
        };
        header.AddChild(_targetPicker);
        _name = new LineEdit { CustomMinimumSize = new Vector2(240, 0), PlaceholderText = "Crest name" };
        _name.TextSubmitted += _ => Rename();
        header.AddChild(_name);
        header.AddChild(Ui.Button("Rename", Rename));
        _saveCopy = Ui.Button("Save as a new assembly", () =>
        {
            _session.SelectedCrestId = Crest.Id;
            var copy = _session.SaveCurrentAsNewAssembly($"{Crest.Name} (Copy)");
            Commit($"Saved {copy.Name}.");
            Focus(copy.Id);
            Refresh();
        });
        header.AddChild(_saveCopy);
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        header.AddChild(Ui.Button("◀ Crest Bay", () => BackRequested?.Invoke(), primary: true));

        _message = Ui.Text("", Ui.Accent);
        column.AddChild(_message);

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 16);
        column.AddChild(columns);

        var (left, leftColumn) = Ui.Card();
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        left.SizeFlagsStretchRatio = 1.25f;
        columns.AddChild(left);
        leftColumn.AddChild(Ui.Heading("Installed (Damage Order: hits land top-down)", Ui.Accent));
        _partTools = new HBoxContainer();
        _partTools.AddThemeConstantOverride("separation", 6);
        leftColumn.AddChild(_partTools);
        _installed = Ui.Scrolling(leftColumn);

        var (middle, middleColumn) = Ui.Card();
        middle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(middle);
        _middle = Ui.Scrolling(middleColumn);

        var (right, rightColumn) = Ui.Card();
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(right);
        _right = Ui.Scrolling(rightColumn);

        var footer = new HBoxContainer();
        column.AddChild(footer);
        footer.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        footer.AddChild(Ui.Button("Reset all progress…", ConfirmReset));

        Refresh();
    }

    private void ClearSelection()
    {
        _selectedPart = null;
        _selectedSpare = null;
        _selectedRule = -1;
        _selectedCargo = -1;
    }

    private void Commit(string message)
    {
        _session.Save();
        _message.Text = message;
        Refresh();
    }

    private void Rename()
    {
        if (string.IsNullOrWhiteSpace(_name.Text) || !_session.CanRefit)
            return;
        Crest.Name = _name.Text.Trim();
        Commit($"Renamed to {Crest.Name}.");
    }

    public void Refresh()
    {
        var targets = _session.RefitTargets;
        _targetIndex = Math.Clamp(_targetIndex, 0, targets.Count - 1);
        _targetPicker.Clear();
        for (int i = 0; i < targets.Count; i++)
            _targetPicker.AddItem(targets[i].PilotId is null ? targets[i].Label : $"{targets[i].Label} (ally)", i);
        _targetPicker.Select(_targetIndex);
        _name.Text = Crest.Name;
        _saveCopy.Visible = Target.PilotId is null;

        if (!_session.CanRefit)
            _message.Text = "A mission is in progress: its Crests are in the field. Finish or withdraw to refit.";

        RefreshInstalled();
        RefreshMiddle();
        RefreshRight();
    }

    // ---- Installed parts ------------------------------------------------------------------------

    private void RefreshInstalled()
    {
        Ui.Clear(_installed);
        int n = 1;
        foreach (var part in Crest.DamageOrder)
            _installed.AddChild(PartButton(part, n++, part.InstanceId == _selectedPart, id => _selectedPart = id));

        Ui.Clear(_partTools);
        var selected = _selectedPart is null ? null : Crest.FindById(_selectedPart);
        bool can = selected is not null && _session.CanRefit;
        AddTool(_partTools, "▲", can, () => Move(-1), "Earlier in the Damage Order (takes hits sooner)");
        AddTool(_partTools, "▼", can, () => Move(+1), "Later in the Damage Order");
        AddTool(_partTools, "Uninstall", can, () =>
        {
            if (_session.Uninstall(Crest, _selectedPart!))
            {
                _selectedPart = null;
                Commit("Moved to spares.");
            }
        });

        var others = _session.RefitTargets.Where(t => t.Loadout != Crest).ToList();
        var transferTo = new OptionButton { Disabled = !can || others.Count == 0, TooltipText = "Move the selected part to another Crest" };
        transferTo.AddItem("Move to…", -1);
        for (int i = 0; i < others.Count; i++)
            transferTo.AddItem(others[i].Label, i);
        transferTo.ItemSelected += index =>
        {
            int pick = transferTo.GetItemId((int)index);
            if (pick >= 0 && _session.Transfer(Crest, _selectedPart!, others[pick].Loadout))
            {
                _selectedPart = null;
                Commit($"Moved to {others[pick].Label}.");
            }
        };
        _partTools.AddChild(transferTo);
        AddTool(_partTools, "Repair all", _session.CanRefit, () =>
        {
            _session.RepairInWorkshop(Crest);
            Commit("Repaired and restocked (the Workshop does this before every mission too).");
        });
    }

    private void Move(int delta)
    {
        if (_selectedPart is not null && Crest.MoveInDamageOrder(_selectedPart, delta))
            Commit("Damage Order changed.");
    }

    // ---- Spares, fabrication, cargo --------------------------------------------------------------

    private void RefreshMiddle()
    {
        Ui.Clear(_middle);
        var catalog = _session.Catalog;

        _middle.AddChild(Ui.Heading($"Spares ({_session.SpareInventory.Count})", Ui.Accent));
        int n = 1;
        foreach (var spare in _session.SpareInventory)
            _middle.AddChild(PartButton(spare, n++, spare.InstanceId == _selectedSpare, id => _selectedSpare = id));
        var install = Ui.Button("Install selected spare", () =>
        {
            if (_selectedSpare is not null && _session.InstallSpare(Crest, _selectedSpare))
            {
                _selectedSpare = null;
                Commit("Installed.");
            }
        });
        install.Disabled = _selectedSpare is null || !_session.CanRefit;
        _middle.AddChild(install);

        _middle.AddChild(Ui.Heading("Fabricate a known blueprint", Ui.Accent));
        var known = _session.Workshop.KnownComponentIds(catalog);
        var blueprints = catalog.Equipment.Values.Where(e => known.Contains(e.Id)).OrderBy(e => e.Category).ThenBy(e => e.Name).ToList();
        var fabRow = new HBoxContainer();
        _middle.AddChild(fabRow);
        var blueprint = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var e in blueprints)
            blueprint.AddItem($"{e.Name} ({e.Category}, Wit tax {e.WitTax}, wt {e.Weight})");
        fabRow.AddChild(blueprint);
        fabRow.AddChild(Ui.Button("Fabricate", () =>
        {
            var def = blueprints[blueprint.Selected];
            if (_session.Fabricate(def.Id))
                Commit($"Fabricated {def.Name} into spares.");
        }));

        var stats = LoadoutRules.ComputeStats(Crest, Rules);
        _middle.AddChild(Ui.Heading($"Cargo: {stats.CargoUsed}/{stats.CargoCapacity} units", Ui.Accent));
        _middle.AddChild(Ui.Text("Loaded in the Workshop; the Crest is restocked to this manifest before every mission.", Ui.Muted, 13));
        var manifest = Crest.CargoManifest.ToList();
        for (int i = 0; i < manifest.Count; i++)
        {
            int index = i;
            var entry = manifest[i];
            string label = entry.Kind == CargoKind.FastIron ? $"Fast-iron × {entry.Count}" : $"{catalog.Consumables[entry.ItemId!].Name} × {entry.Count}";
            _middle.AddChild(SelectButton(label, index == _selectedCargo, () => _selectedCargo = index));
        }
        var removeCargo = Ui.Button("Remove selected cargo", () =>
        {
            var list = Crest.CargoManifest.ToList();
            if (_selectedCargo < 0 || _selectedCargo >= list.Count)
                return;
            list.RemoveAt(_selectedCargo);
            Crest.SetCargoManifest(list, catalog);
            _selectedCargo = -1;
            Commit("Cargo removed.");
        });
        removeCargo.Disabled = _selectedCargo < 0 || !_session.CanRefit;
        _middle.AddChild(removeCargo);

        var items = new List<(CargoKind Kind, string? Id, string Label)> { (CargoKind.FastIron, null, "Fast-iron") };
        items.AddRange(catalog.Consumables.Values.OrderBy(c => c.Name).Select(c => (CargoKind.Consumable, (string?)c.Id, c.Name)));
        var addRow = new HBoxContainer();
        _middle.AddChild(addRow);
        var item = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var it in items)
            item.AddItem(it.Label);
        addRow.AddChild(item);
        var count = new SpinBox { MinValue = 1, MaxValue = 20, Value = 1 };
        addRow.AddChild(count);
        var add = Ui.Button("Add", () =>
        {
            var (kind, id, label) = items[item.Selected];
            var list = Crest.CargoManifest.ToList();
            int existing = list.FindIndex(m => m.Kind == kind && m.ItemId == id);
            if (existing >= 0)
                list[existing] = list[existing] with { Count = list[existing].Count + (int)count.Value };
            else
                list.Add(new CargoEntry(kind, (int)count.Value, id));
            Crest.SetCargoManifest(list, catalog);
            Commit($"Loaded {label} × {(int)count.Value}.");
        });
        add.Disabled = !_session.CanRefit;
        addRow.AddChild(add);
    }

    // ---- Stats, actions, reaction rules -----------------------------------------------------------

    private void RefreshRight()
    {
        Ui.Clear(_right);
        var catalog = _session.Catalog;
        var s = LoadoutRules.ComputeStats(Crest, Rules);

        _right.AddChild(Ui.Heading("Stats", Ui.Accent));
        _right.AddChild(Ui.Text(
            $"Weight {s.TotalWeight} / leg load {s.LoadCapacity}{(s.TotalWeight >= 90 ? " (heavy)" : "")}   ·   speed {s.MoveSpeed}/beat\n" +
            $"Wit {s.AvailableWit(0)} free of {s.MaxWit} (tax {s.EquipmentWitTax})   ·   Motes {s.MaxMotes} (+1 / {s.MoteGenerationIntervalBeats}s)\n" +
            $"Protection 1 + {s.BonusProtectionDice} dice (5+ saves)   ·   displacement resist {s.DisplacementResistance}\n" +
            $"Heat {s.HeatCapacity}, −{s.HeatDissipationPerBeat}/beat   ·   Spellbook {(s.HasSpellbook ? "yes" : "no")}   ·   " +
            $"Amplifier {(s.HasSpellAmplifier ? "yes" : "no")}", Ui.Ink, 14));
        if (!s.IsCoreOperational)
            _right.AddChild(Ui.Text("No working Core: install or repair one before launching.", Ui.Bad, 14));
        foreach (var warning in LoadoutRules.GetWarnings(Crest, Rules))
            _right.AddChild(Ui.Text("⚠ " + warning, Ui.Accent, 13));

        _right.AddChild(Ui.Heading("Actions", Ui.Accent));
        foreach (var a in Crest.GetOperationalActionIds().Select(catalog.GetAction).OrderBy(a => a.Category).ThenBy(a => a.Name))
            _right.AddChild(Ui.Text($"• {a.Name} [{a.Category}]: range {a.Range}, beats {a.WindupBeats}/{a.ImpactBeats}/{a.RecoveryBeats}, " +
                                    $"Wit {a.WitReserve}{(a.MoteCost > 0 ? $", Motes {a.MoteCost}" : "")}", Ui.Muted, 13));

        _right.AddChild(Ui.Heading("Reaction rules (checked in order)", Ui.Accent));
        for (int i = 0; i < Crest.ReactionRules.Count; i++)
        {
            int index = i;
            _right.AddChild(SelectButton($"{i + 1}. {Crest.ReactionRules[i].Describe(catalog)}", index == _selectedRule, () => _selectedRule = index));
        }
        var ruleTools = new HBoxContainer();
        ruleTools.AddThemeConstantOverride("separation", 6);
        _right.AddChild(ruleTools);
        bool ruleSelected = _selectedRule >= 0 && _selectedRule < Crest.ReactionRules.Count && _session.CanRefit;
        AddTool(ruleTools, "▲", ruleSelected, () => MoveRule(-1));
        AddTool(ruleTools, "▼", ruleSelected, () => MoveRule(+1));
        AddTool(ruleTools, "Remove", ruleSelected, () =>
        {
            if (Crest.RemoveReactionRuleAt(_selectedRule))
            {
                _selectedRule = -1;
                Commit("Rule removed.");
            }
        });

        var reactive = Crest.GetOperationalActionIds().Select(catalog.GetAction).Where(a => a.IsReactive).ToList();
        if (reactive.Count == 0)
        {
            _right.AddChild(Ui.Text("This Crest has no reactive actions (e.g. Blink Drive, Repulsor, Aegis).", Ui.Muted, 13));
            return;
        }

        var addRow = new HBoxContainer();
        addRow.AddThemeConstantOverride("separation", 6);
        _right.AddChild(addRow);
        var action = new OptionButton();
        foreach (var a in reactive)
            action.AddItem(a.Name);
        addRow.AddChild(action);
        addRow.AddChild(Ui.Text("when ≥", Ui.Muted, 14));
        var minDamage = new SpinBox { MinValue = 1, MaxValue = 9, Value = 1, TooltipText = "Minimum incoming damage" };
        addRow.AddChild(minDamage);
        var telegraphs = new[] { AnyTelegraph }
            .Concat(Enum.GetNames<TelegraphKind>().Where(n => n != nameof(TelegraphKind.DefensiveReaction)))
            .ToList();
        var telegraph = new OptionButton();
        foreach (var t in telegraphs)
            telegraph.AddItem(t);
        addRow.AddChild(telegraph);
        var add = Ui.Button("Add rule", () =>
        {
            Crest.AddReactionRule(new ReactionRule
            {
                ActionId = reactive[action.Selected].Id,
                MinIncomingDamage = (int)minDamage.Value,
                Telegraph = Enum.TryParse<TelegraphKind>(telegraphs[telegraph.Selected], out var kind) ? kind : null
            });
            Commit("Rule added.");
        });
        add.Disabled = !_session.CanRefit;
        addRow.AddChild(add);
    }

    private void MoveRule(int delta)
    {
        if (Crest.MoveReactionRule(_selectedRule, delta))
        {
            _selectedRule += delta;
            Commit("Rule order changed.");
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private Button PartButton(EquipmentInstance part, int index, bool selected, Action<string> select)
    {
        var d = part.Definition;
        string extra = (d.WitTax > 0 ? $"  ·  Wit tax {d.WitTax}" : "") + (d.BonusWitCapacity > 0 ? $"  ·  +{d.BonusWitCapacity} Wit" : "") + $"  ·  wt {d.Weight}";
        var button = SelectButton($"{Ui.ComponentLine(part, index)}   [{d.Category}]{extra}", selected, () => select(part.InstanceId));
        if (part.IsDisabled)
            button.Modulate = Ui.Bad;
        return button;
    }

    private Button SelectButton(string text, bool selected, Action onSelect)
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            ButtonPressed = selected,
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        button.Pressed += () =>
        {
            onSelect();
            Refresh();
        };
        return button;
    }

    private static void AddTool(HBoxContainer row, string text, bool enabled, Action action, string tooltip = "")
    {
        var button = Ui.Button(text, action);
        button.Disabled = !enabled;
        button.TooltipText = tooltip;
        row.AddChild(button);
    }

    private void ConfirmReset()
    {
        var dialog = new ConfirmationDialog
        {
            Title = "Reset all progress",
            DialogText = "Erase every assembly, spare, sample, research, pilot refit, and the mission in progress, and start over?",
            OkButtonText = "Erase and start over"
        };
        dialog.Confirmed += () =>
        {
            _session.ResetProfile();
            _targetIndex = 0;
            ClearSelection();
            _message.Text = "Progress reset.";
            Refresh();
        };
        AddChild(dialog);
        dialog.PopupCentered();
    }
}
