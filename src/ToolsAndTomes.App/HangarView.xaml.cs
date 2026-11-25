using ToolsAndTomes.Core.Campaign;
using System.Windows;
using System.Windows.Controls;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.App;

public partial class HangarView : UserControl
{
    private sealed record AssemblyOption(CrestLoadout Assembly)
    {
        public override string ToString() => $"{Assembly.Name} ({Assembly.Installed.Count} parts)";
    }

    private sealed record EquipmentListItem(EquipmentInstance Instance)
    {
        public override string ToString()
        {
            var d = Instance.Definition;
            string status = Instance.IsDisabled ? "[DISABLED]" : $"HP {Instance.CurrentDurability}/{d.MaxDurability}";
            string ammo = d.MaxAmmo > 0 ? $" | Ammo {Instance.CurrentAmmo}/{d.MaxAmmo}" : "";
            string tax = d.WitTax > 0 ? $" | WitTax {d.WitTax}" : "";
            string bonus = d.BonusWitCapacity > 0 ? $" | +Wit {d.BonusWitCapacity}" : "";
            return $"{d.Name} ({d.Category}) — {status}{ammo}{tax}{bonus} | Wt {d.Weight}";
        }
    }

    private sealed record CatalogOption(EquipmentDefinition Def)
    {
        public override string ToString() => $"{Def.Name} ({Def.Category}, WitTax {Def.WitTax}, Wt {Def.Weight})";
    }

    private sealed record ActionOption(ActionDefinition Def)
    {
        public override string ToString() => Def.Name;
    }

    private const string AnyTelegraph = "Any impact";

    private readonly CampaignSession _session;
    private bool _suppressSelection;
    private string? _reselectInstanceId;

    public event Action? LaunchEncounterRequested;
    public event Action? ResearchRequested;

    public HangarView(CampaignSession session)
    {
        InitializeComponent();
        _session = session;


        CmbRuleTelegraph.ItemsSource = new[] { AnyTelegraph }
            .Concat(Enum.GetNames<TelegraphKind>().Where(n => n != nameof(TelegraphKind.DefensiveReaction)))
            .ToList();
        CmbRuleTelegraph.SelectedIndex = 0;

        RefreshUI();
    }

    public void RefreshUI()
    {
        _suppressSelection = true;
        var assemblyOptions = _session.CrestAssemblies.Select(a => new AssemblyOption(a)).ToList();
        CmbAssemblies.ItemsSource = assemblyOptions;
        CmbAssemblies.SelectedItem = assemblyOptions.FirstOrDefault(o => o.Assembly.Id == _session.SelectedCrestId);

        var otherAssemblies = assemblyOptions.Where(o => o.Assembly.Id != _session.SelectedCrestId).ToList();
        CmbTransferTarget.ItemsSource = otherAssemblies;
        if (otherAssemblies.Count > 0)
            CmbTransferTarget.SelectedIndex = 0;
        _suppressSelection = false;

        var loadout = _session.SelectedLoadout;
        var stats = LoadoutRules.ComputeStats(loadout, _session.Catalog.Rules);

        TxtAssemblyName.Text = loadout.Name;
        TxtSeed.Text = _session.EncounterSeed.ToString();
        TxtInstalledHeader.Text = $"{loadout.Name} — Installed Components ({loadout.Installed.Count})";

        var installedItems = loadout.DamageOrder.Select(e => new EquipmentListItem(e)).ToList();
        LstInstalled.ItemsSource = installedItems;
        if (_reselectInstanceId is not null)
        {
            LstInstalled.SelectedItem = installedItems.FirstOrDefault(i => i.Instance.InstanceId == _reselectInstanceId);
            _reselectInstanceId = null;
        }

        RefreshFabricationList();
        RefreshCargo(loadout, stats);

        var warnings = LoadoutRules.GetWarnings(loadout, _session.Catalog.Rules);
        var allWarnings = (_session.LastMissionReport is { } report ? new[] { report } : [])
            .Concat(_session.ProfileWarnings)
            .Concat(warnings)
            .ToList();
        TxtWarnings.Text = string.Join("\n", allWarnings.Select(w => "⚠ " + w));
        TxtWarnings.Visibility = allWarnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LstSpares.ItemsSource = _session.SpareInventory.Select(e => new EquipmentListItem(e)).ToList();

        int moteSec = stats.MoteGenerationIntervalBeats;
        TxtStatsSummary.Text =
            $"Weight / Leg Load  : {stats.TotalWeight} / {stats.LoadCapacity} {(stats.TotalWeight >= 90 ? "(HEAVY)" : "(LIGHT)")}\n" +
            $"Move Speed         : {stats.MoveSpeed} per beat\n" +
            $"Displacement Resist: -{stats.DisplacementResistance} to push/pull\n" +
            $"Protection Dice    : 1 base + {stats.BonusProtectionDice} bonus (5+ save)\n" +
            $"Wit Capacity       : {stats.AvailableWit(0)} avail / {stats.MaxWit} max (Tax: {stats.EquipmentWitTax})\n" +
            $"Motes              : {stats.MaxMotes} max (+1 per {moteSec}s)\n" +
            $"Heat Management    : {stats.HeatCapacity} cap | -{stats.HeatDissipationPerBeat} per beat\n" +
            $"Tomes & hands      : {stats.TomesHeld} tome(s) held, {stats.Hands} hand(s)" +
            (stats.UnheldHandhelds > 0 ? $" ({stats.UnheldHandhelds} handheld(s) without a hand!)" : "") + "\n" +
            $"Arcane Amplifier   : {(stats.HasSpellAmplifier ? "Operational" : "Missing/Disabled")}\n" +
            $"Core Status        : {(stats.IsCoreOperational ? "OPERATIONAL" : "MISSING/DISABLED (Install/Repair Core)")}";

        var actions = loadout.GetOperationalActionIds()
            .Select(id => _session.Catalog.GetAction(id))
            .OrderBy(a => a.Category)
            .ThenBy(a => a.Name)
            .Select(a => $"• {a.Name} [{a.Category}] — Range {a.Range} | Beats {a.WindupBeats}/{a.ImpactBeats}/{a.RecoveryBeats} | Wit {a.WitReserve} | Mote {a.MoteCost}");

        TxtActionsSummary.Text = string.Join("\n", actions);

        LstReactionRules.ItemsSource = loadout.ReactionRules
            .Select((r, i) => $"{i + 1}. {r.Describe(_session.Catalog)}")
            .ToList();
        CmbRuleAction.ItemsSource = loadout.GetOperationalActionIds()
            .Select(id => _session.Catalog.GetAction(id))
            .Where(a => a.IsReactive)
            .Select(a => new ActionOption(a))
            .ToList();
        if (CmbRuleAction.Items.Count > 0 && CmbRuleAction.SelectedIndex < 0)
            CmbRuleAction.SelectedIndex = 0;
        BtnLaunch.IsEnabled = stats.IsCoreOperational;
    }

    private void CmbAssemblies_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;

        if (CmbAssemblies.SelectedItem is AssemblyOption opt)
        {
            _session.SelectedCrestId = opt.Assembly.Id;
            Commit();
        }
    }

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtAssemblyName.Text))
        {
            _session.SelectedLoadout.Name = TxtAssemblyName.Text.Trim();
            Commit();
        }
    }

    private void BtnSaveNew_Click(object sender, RoutedEventArgs e)
    {
        _session.SaveCurrentAsNewAssembly(TxtAssemblyName.Text + " (Copy)");
        Commit();
    }

    private sealed record CargoOption(CargoKind Kind, string? ItemId, string Label)
    {
        public override string ToString() => Label;
    }

    private void RefreshCargo(CrestLoadout loadout, CrestStats stats)
    {
        TxtCargoHeader.Text = $"Cargo: {stats.CargoUsed}/{stats.CargoCapacity} units (restocked from the Workshop before each fight)";
        LstCargo.ItemsSource = loadout.CargoManifest
            .Select(e => e.Kind == CargoKind.FastIron
                ? $"Fast-iron × {e.Count}"
                : $"{_session.Catalog.Consumables[e.ItemId!].Name} × {e.Count}")
            .ToList();

        if (CmbCargoItem.ItemsSource is null)
        {
            CmbCargoItem.ItemsSource = new[] { new CargoOption(CargoKind.FastIron, null, "Fast-iron") }
                .Concat(_session.Catalog.Consumables.Values.OrderBy(c => c.Name)
                    .Select(c => new CargoOption(CargoKind.Consumable, c.Id, c.Name)))
                .ToList();
            CmbCargoItem.SelectedIndex = 0;
        }
    }

    private void BtnCargoAdd_Click(object sender, RoutedEventArgs e)
    {
        if (CmbCargoItem.SelectedItem is not CargoOption opt)
            return;

        int count = int.TryParse(TxtCargoCount.Text, out var n) ? Math.Max(1, n) : 1;
        var manifest = _session.SelectedLoadout.CargoManifest.ToList();
        int existing = manifest.FindIndex(m => m.Kind == opt.Kind && m.ItemId == opt.ItemId);
        if (existing >= 0)
            manifest[existing] = manifest[existing] with { Count = manifest[existing].Count + count };
        else
            manifest.Add(new CargoEntry(opt.Kind, count, opt.ItemId));

        _session.SelectedLoadout.SetCargoManifest(manifest, _session.Catalog);
        Commit();
    }

    private void BtnCargoRemove_Click(object sender, RoutedEventArgs e)
    {
        int index = LstCargo.SelectedIndex;
        var manifest = _session.SelectedLoadout.CargoManifest.ToList();
        if (index < 0 || index >= manifest.Count)
            return;

        manifest.RemoveAt(index);
        _session.SelectedLoadout.SetCargoManifest(manifest, _session.Catalog);
        Commit();
    }

    private void BtnRuleAdd_Click(object sender, RoutedEventArgs e)
    {
        if (CmbRuleAction.SelectedItem is not ActionOption opt)
            return;

        int minDamage = int.TryParse(TxtRuleMinDamage.Text, out var d) ? Math.Max(1, d) : 1;
        TelegraphKind? telegraph = CmbRuleTelegraph.SelectedItem is string s && Enum.TryParse<TelegraphKind>(s, out var kind)
            ? kind
            : null;

        _session.SelectedLoadout.AddReactionRule(new ReactionRule
        {
            ActionId = opt.Def.Id,
            MinIncomingDamage = minDamage,
            Telegraph = telegraph
        });
        Commit();
    }

    private void BtnRuleRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_session.SelectedLoadout.RemoveReactionRuleAt(LstReactionRules.SelectedIndex))
            Commit();
    }

    private void BtnRuleUp_Click(object sender, RoutedEventArgs e) => MoveRule(-1);
    private void BtnRuleDown_Click(object sender, RoutedEventArgs e) => MoveRule(+1);

    private void MoveRule(int delta)
    {
        int index = LstReactionRules.SelectedIndex;
        if (_session.SelectedLoadout.MoveReactionRule(index, delta))
        {
            Commit();
            LstReactionRules.SelectedIndex = index + delta;
        }
    }

    private void BtnMoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void BtnMoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        if (LstInstalled.SelectedItem is EquipmentListItem item &&
            _session.SelectedLoadout.MoveInDamageOrder(item.Instance.InstanceId, delta))
        {
            _reselectInstanceId = item.Instance.InstanceId;
            Commit();
        }
    }

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (LstInstalled.SelectedItem is EquipmentListItem item)
        {
            _session.UninstallFromSelected(item.Instance.InstanceId);
            Commit();
        }
    }

    private void BtnTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (LstInstalled.SelectedItem is EquipmentListItem item &&
            CmbTransferTarget.SelectedItem is AssemblyOption target)
        {
            _session.TransferToOtherAssembly(item.Instance.InstanceId, target.Assembly.Id);
            Commit();
        }
    }

    private void BtnInstallSpare_Click(object sender, RoutedEventArgs e)
    {
        if (LstSpares.SelectedItem is EquipmentListItem item)
        {
            _session.InstallFromSpare(item.Instance.InstanceId);
            Commit();
        }
    }

    private void BtnFabricate_Click(object sender, RoutedEventArgs e)
    {
        if (CmbCatalog.SelectedItem is CatalogOption opt)
        {
            _session.Fabricate(opt.Def.Id);
            Commit();
        }
    }

    private void BtnRepairSelected_Click(object sender, RoutedEventArgs e)
    {
        if (LstInstalled.SelectedItem is EquipmentListItem item)
        {
            item.Instance.Repair();
            item.Instance.RefillAmmo();
            Commit();
        }
    }

    private void BtnRepairAll_Click(object sender, RoutedEventArgs e)
    {
        _session.SelectedLoadout.RepairAll();
        foreach (var spare in _session.SpareInventory)
        {
            spare.Repair();
            spare.RefillAmmo();
        }
        Commit();
    }

    /// <summary>Saves progress, then refreshes. Called after every change the player makes.</summary>
    private void Commit()
    {
        _session.Save();
        RefreshUI();
    }

    private void RefreshFabricationList()
    {
        string? previous = (CmbCatalog.SelectedItem as CatalogOption)?.Def.Id;
        var known = _session.Workshop.KnownComponentIds(_session.Catalog);
        var options = _session.Catalog.Equipment.Values
            .Where(e => known.Contains(e.Id))
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Name)
            .Select(e => new CatalogOption(e))
            .ToList();

        CmbCatalog.ItemsSource = options;
        CmbCatalog.SelectedItem = options.FirstOrDefault(o => o.Def.Id == previous) ?? options.FirstOrDefault();
    }

    private void BtnResearch_Click(object sender, RoutedEventArgs e) => ResearchRequested?.Invoke();

    private void BtnResetProgress_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Erase all progress (assemblies, spares, cargo, samples, and research) and start over with the starter builds?",
            "Reset Progress", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        _session.ResetProfile();
        RefreshUI();
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtSeed.Text, out int seed))
            _session.EncounterSeed = seed;

        LaunchEncounterRequested?.Invoke();
    }
}
