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

    private readonly AppSessionState _session;
    private bool _suppressSelection;

    public event Action? LaunchEncounterRequested;

    public HangarView(AppSessionState session)
    {
        InitializeComponent();
        _session = session;

        CmbCatalog.ItemsSource = _session.Catalog.Equipment.Values
            .OrderBy(e => e.Category)
            .ThenBy(e => e.Name)
            .Select(e => new CatalogOption(e))
            .ToList();
        CmbCatalog.SelectedIndex = 0;

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
        var stats = LoadoutRules.ComputeStats(loadout);

        TxtAssemblyName.Text = loadout.Name;
        TxtInstalledHeader.Text = $"{loadout.Name} — Installed Components ({loadout.Installed.Count})";

        LstInstalled.ItemsSource = loadout.Installed.Select(e => new EquipmentListItem(e)).ToList();
        LstSpares.ItemsSource = _session.SpareInventory.Select(e => new EquipmentListItem(e)).ToList();

        int moteSec = stats.MoteGenerationIntervalBeats;
        TxtStatsSummary.Text =
            $"Total Weight       : {stats.TotalWeight:F0} {(stats.TotalWeight >= 90 ? "(HEAVY)" : "(LIGHT)")}\n" +
            $"Move Speed         : {stats.MoveSpeed} per beat\n" +
            $"Displacement Resist: -{stats.DisplacementResistance} to push/pull\n" +
            $"Protection Dice    : 1 base + {stats.BonusProtectionDice} bonus (5+ save)\n" +
            $"Wit Capacity       : {stats.AvailableWit(0)} avail / {stats.MaxWit} max (Tax: {stats.EquipmentWitTax})\n" +
            $"Motes              : {stats.MaxMotes} max (+1 per {moteSec}s)\n" +
            $"Heat Management    : {stats.HeatCapacity} cap | -{stats.HeatDissipationPerBeat} per beat\n" +
            $"Spellbook          : {(stats.HasSpellbook ? "Operational" : "Missing/Disabled")}\n" +
            $"Arcane Amplifier   : {(stats.HasSpellAmplifier ? "Operational" : "Missing/Disabled")}\n" +
            $"Core Status        : {(stats.IsCoreOperational ? "OPERATIONAL" : "MISSING/DISABLED (Install/Repair Core)")}";

        var actions = loadout.GetOperationalActionIds()
            .Select(id => _session.Catalog.GetAction(id))
            .OrderBy(a => a.Category)
            .ThenBy(a => a.Name)
            .Select(a => $"• {a.Name} [{a.Category}] — Range {a.Range} | Beats {a.WindupBeats}/{a.ImpactBeats}/{a.RecoveryBeats} | Wit {a.WitReserve} | Mote {a.MoteCost}");

        TxtActionsSummary.Text = string.Join("\n", actions);
        BtnLaunch.IsEnabled = stats.IsCoreOperational;
    }

    private void CmbAssemblies_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;

        if (CmbAssemblies.SelectedItem is AssemblyOption opt)
        {
            _session.SelectedCrestId = opt.Assembly.Id;
            RefreshUI();
        }
    }

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TxtAssemblyName.Text))
        {
            _session.SelectedLoadout.Name = TxtAssemblyName.Text.Trim();
            RefreshUI();
        }
    }

    private void BtnSaveNew_Click(object sender, RoutedEventArgs e)
    {
        _session.SaveCurrentAsNewAssembly(TxtAssemblyName.Text + " (Copy)");
        RefreshUI();
    }

    private void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (LstInstalled.SelectedItem is EquipmentListItem item)
        {
            _session.UninstallFromSelected(item.Instance.InstanceId);
            RefreshUI();
        }
    }

    private void BtnTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (LstInstalled.SelectedItem is EquipmentListItem item &&
            CmbTransferTarget.SelectedItem is AssemblyOption target)
        {
            _session.TransferToOtherAssembly(item.Instance.InstanceId, target.Assembly.Id);
            RefreshUI();
        }
    }

    private void BtnInstallSpare_Click(object sender, RoutedEventArgs e)
    {
        if (LstSpares.SelectedItem is EquipmentListItem item)
        {
            _session.InstallFromSpare(item.Instance.InstanceId);
            RefreshUI();
        }
    }

    private void BtnFabricate_Click(object sender, RoutedEventArgs e)
    {
        if (CmbCatalog.SelectedItem is CatalogOption opt)
        {
            _session.AddSpare(opt.Def.Id);
            RefreshUI();
        }
    }

    private void BtnRepairSelected_Click(object sender, RoutedEventArgs e)
    {
        if (LstInstalled.SelectedItem is EquipmentListItem item)
        {
            item.Instance.Repair();
            item.Instance.RefillAmmo();
            RefreshUI();
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
        RefreshUI();
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtSeed.Text, out int seed))
            _session.EncounterSeed = seed;

        LaunchEncounterRequested?.Invoke();
    }
}
