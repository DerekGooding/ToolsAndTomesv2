using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.App;

public partial class HangarView : UserControl
{
    private sealed record EquipmentListItem(EquipmentInstance Instance)
    {
        public override string ToString()
        {
            var d = Instance.Definition;
            string status = Instance.IsDisabled ? "[DISABLED]" : $"HP {Instance.CurrentDurability}/{d.MaxDurability}";
            string ammo = d.MaxAmmo > 0 ? $" | Ammo {Instance.CurrentAmmo}/{d.MaxAmmo}" : "";
            string tax = d.WitTax > 0 ? $" | WitTax {d.WitTax}" : "";
            return $"{d.Name} ({d.SlotType}) — {status}{ammo}{tax} | Wt {d.Weight}";
        }
    }

    private sealed record CatalogOption(EquipmentDefinition Def)
    {
        public override string ToString() => $"{Def.Name} ({Def.SlotType}, WitTax {Def.WitTax}, Wt {Def.Weight})";
    }

    private readonly AppSessionState _session;

    public event Action? LaunchEncounterRequested;

    public HangarView(AppSessionState session)
    {
        InitializeComponent();
        _session = session;

        CmbCatalog.ItemsSource = _session.Catalog.Equipment.Values
            .Where(e => !e.IsCore)
            .OrderBy(e => e.SlotType)
            .ThenBy(e => e.Name)
            .Select(e => new CatalogOption(e))
            .ToList();
        CmbCatalog.SelectedIndex = 0;

        RefreshUI();
    }

    public void RefreshUI()
    {
        var loadout = _session.SelectedLoadout;
        var stats = LoadoutRules.ComputeStats(loadout);

        BtnSelectStrider.Background = _session.SelectedCrestId == TemplateCatalog.Strider.Id
            ? new SolidColorBrush(Color.FromRgb(2, 132, 199))
            : new SolidColorBrush(Color.FromRgb(51, 65, 85));
        BtnSelectStrider.Foreground = Brushes.White;

        BtnSelectBulwark.Background = _session.SelectedCrestId == TemplateCatalog.Bulwark.Id
            ? new SolidColorBrush(Color.FromRgb(2, 132, 199))
            : new SolidColorBrush(Color.FromRgb(51, 65, 85));
        BtnSelectBulwark.Foreground = Brushes.White;

        TxtInstalledHeader.Text = $"{loadout.Template.Name} — Installed Components ({loadout.Installed.Count})";
        TxtCrestDesc.Text = loadout.Template.Description;

        LstInstalled.ItemsSource = loadout.Installed.Select(e => new EquipmentListItem(e)).ToList();
        LstSpares.ItemsSource = _session.SpareInventory.Select(e => new EquipmentListItem(e)).ToList();

        TxtStatsSummary.Text =
            $"Total Weight       : {stats.TotalWeight:F0} {(stats.TotalWeight >= 90 ? "(HEAVY)" : "(LIGHT)")}\n" +
            $"Move Speed         : {stats.MoveSpeed:F2} m/s\n" +
            $"Displacement Resist: {stats.DisplacementResistance:P0}\n" +
            $"Protection Dice    : 1 base + {stats.BonusProtectionDice} bonus (5+ save)\n" +
            $"Wit Capacity       : {stats.AvailableWit(0)} avail / {stats.MaxWit} max (Tax: {stats.EquipmentWitTax})\n" +
            $"Mote Storage       : {stats.MaxMotes} max (+1 per 5.0s)\n" +
            $"Heat Management    : {stats.HeatCapacity:F1} cap | -{stats.HeatDissipationPerSecond:F1}/s\n" +
            $"Spellbook          : {(stats.HasSpellbook ? "Operational" : "Missing/Disabled")}\n" +
            $"Arcane Amplifier   : {(stats.HasSpellAmplifier ? "Operational" : "Missing/Disabled")}\n" +
            $"Core Status        : {(stats.IsCoreOperational ? "OPERATIONAL" : "DISABLED (Repair to launch)")}";

        var actions = loadout.GetOperationalActionIds()
            .Select(id => _session.Catalog.GetAction(id))
            .OrderBy(a => a.Category)
            .ThenBy(a => a.Name)
            .Select(a => $"• {a.Name} [{a.Category}] — Range {a.Range:F1}m | Windup {a.WindupTicks * 0.05:F2}s | Wit {a.WitReserve} | Mote {a.MoteCost}");

        TxtActionsSummary.Text = string.Join("\n", actions);
        BtnLaunch.IsEnabled = stats.IsCoreOperational;
    }

    private void BtnSelectStrider_Click(object sender, RoutedEventArgs e)
    {
        _session.SelectedCrestId = TemplateCatalog.Strider.Id;
        RefreshUI();
    }

    private void BtnSelectBulwark_Click(object sender, RoutedEventArgs e)
    {
        _session.SelectedCrestId = TemplateCatalog.Bulwark.Id;
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
