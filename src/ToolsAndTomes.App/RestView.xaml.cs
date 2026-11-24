using System.Windows;
using System.Windows.Controls;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.App;

public partial class RestView : UserControl
{
    private sealed record ComponentRow(EquipmentInstance Instance, int Index)
    {
        public override string ToString()
        {
            var d = Instance.Definition;
            string hp = Instance.IsDisabled ? "DISABLED" : $"{Instance.CurrentDurability}/{d.MaxDurability}";
            string ammo = d.MaxAmmo > 0 ? $" | Ammo {Instance.CurrentAmmo}/{d.MaxAmmo}" : "";
            string flag = Instance.CurrentDurability < d.MaxDurability ? "  ⚒" : "";
            return $"{Index}. {d.Name} — {hp}{ammo}{flag}";
        }
    }

    private sealed record CrestOption(string? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppSessionState _session;

    /// <summary>The lance member the rest actions apply to (null: the player's Crest).</summary>
    private string? _crestId;
    private bool _refreshing;

    public event Action? ContinueRequested;
    public event Action? WithdrawRequested;

    public RestView(AppSessionState session)
    {
        InitializeComponent();
        _session = session;
    }

    private MissionRun? Run => _session.ActiveMission is { Phase: MissionPhase.Rest } run ? run : null;

    public void RefreshUI()
    {
        if (Run is not { } run)
            return;

        _refreshing = true;
        var crest = run.FindCrest(_crestId) ?? run.Crest;
        var options = run.Lance
            .Select(c => new CrestOption(c == run.Crest ? null : c.Id,
                (c == run.Crest ? $"{c.Name} (you)" : c.Name) + (c.IsDefeated ? " — DOWN" : "")))
            .ToList();
        CmbCrest.ItemsSource = options;
        CmbCrest.SelectedItem = options.FirstOrDefault(o => o.Id == (crest == run.Crest ? null : crest.Id));
        CmbCrest.Visibility = options.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var stats = crest.Stats;
        int total = run.Mission.EncounterIds.Count;
        var next = run.Catalog.Encounters[run.Mission.EncounterIds[run.StageIndex + 1]];
        var summon = crest.Loadout.GetOperationalActionIds().Select(run.Catalog.GetAction).FirstOrDefault(a => a.FastIronProduced > 0);

        TxtTitle.Text = $"REST IN THE FIELD — {run.Mission.Name} (after encounter {run.StageIndex + 1} of {total})";
        TxtStatus.Text =
            (crest == run.Crest ? "" : $"{crest.Name}{(crest.IsDefeated ? " (DOWN: repair its Core)" : "")}  |  ") +
            $"Time left: {run.RestBeatsRemaining} beats  |  Motes: {crest.Resources.CurrentMotes}/{stats.MaxMotes} (+1 per {run.MoteIntervalBeats} beats)  |  " +
            $"Fast-iron: {crest.TotalFastIron} ({crest.Resources.FastIron} loose, {crest.Loadout.CarriedFastIron} in cargo)  |  Cargo: {stats.CargoUsed}/{stats.CargoCapacity}";
        TxtNext.Text = $"Next: {next.Name} — {next.Description}  |  Samples banked this mission: {run.RewardsEarned.Sum(r => r.Count)}";

        BtnWait.Content = $"Wait {run.MoteIntervalBeats} beats (+1 Mote)";
        BtnWait.IsEnabled = run.RestBeatsRemaining >= run.MoteIntervalBeats;
        BtnWaitAll.IsEnabled = BtnWait.IsEnabled;
        BtnSummon.Content = summon is null ? "No Square Summon" : $"{summon.Name} ({summon.MoteCost} Motes → fast-iron)";
        BtnSummon.IsEnabled = summon is not null && !crest.IsDefeated && crest.Resources.CurrentMotes >= summon.MoteCost;
        BtnRepair.Content = $"Repair selected (1 fast-iron → +{Math.Max(1, crest.Rules.RepairPointsPerFastIron)} durability)";

        string? selected = (LstComponents.SelectedItem as ComponentRow)?.Instance.InstanceId;
        var rows = crest.Loadout.DamageOrder.Select((e, i) => new ComponentRow(e, i + 1)).ToList();
        LstComponents.ItemsSource = rows;
        LstComponents.SelectedItem = rows.FirstOrDefault(r => r.Instance.InstanceId == selected);

        LstLog.ItemsSource = run.RestLog.Select(e => e.Message).Reverse().ToList();
        _refreshing = false;
    }

    private void CmbCrest_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || CmbCrest.SelectedItem is not CrestOption option)
            return;
        _crestId = option.Id;
        CmbField.SelectedItem = null;
        RefreshUI();
    }

    private void Show(RestResult result)
    {
        TxtMessage.Text = result.Message;
        RefreshUI();
    }

    private void BtnWait_Click(object sender, RoutedEventArgs e)
    {
        if (Run is { } run)
            Show(run.Wait());
    }

    private void BtnWaitAll_Click(object sender, RoutedEventArgs e)
    {
        if (Run is not { } run)
            return;

        int motes = 0;
        RestResult last = RestResult.Fail("No rest time left.");
        while (run.RestBeatsRemaining >= run.MoteIntervalBeats)
        {
            int before = run.Crest.Resources.CurrentMotes;
            last = run.Wait();
            motes += run.Crest.Resources.CurrentMotes - before;
        }

        Show(last.Succeeded ? RestResult.Ok($"Waited out the rest: +{motes} Mote(s).") : last);
    }

    private void BtnSummon_Click(object sender, RoutedEventArgs e)
    {
        if (Run is { } run)
            Show(run.SquareSummon(_crestId));
    }

    private void BtnRepair_Click(object sender, RoutedEventArgs e)
    {
        if (Run is not { } run)
            return;

        if (LstComponents.SelectedItem is not ComponentRow row)
        {
            TxtMessage.Text = "Select a damaged component to repair.";
            return;
        }

        Show(run.Repair(row.Instance.InstanceId, _crestId));
    }

    private void CmbField_DropDownOpened(object? sender, EventArgs e)
    {
        CmbField.ItemsSource = Run is { } run ? FieldOption.Build(run.Catalog, run.FindCrest(_crestId) ?? run.Crest) : null;
    }

    private void BtnField_Click(object sender, RoutedEventArgs e)
    {
        if (Run is not { } run || CmbField.SelectedItem is not FieldOption option)
        {
            TxtMessage.Text = "Pick something to shape or use first.";
            return;
        }

        var cmd = option.Command;
        var result = cmd.Shaping is { } order
            ? run.Shape(order, cmd.OwnComponentInstanceId, _crestId)
            : run.UseCargoItem(cmd.ActionId, cmd.OwnComponentInstanceId ?? "", _crestId);
        CmbField.SelectedItem = null;
        Show(result);
    }

    private void BtnContinue_Click(object sender, RoutedEventArgs e)
    {
        if (Run is not { } run)
            return;

        run.ContinueToNextEncounter();
        _crestId = null;
        TxtMessage.Text = "";
        ContinueRequested?.Invoke();
    }

    private void BtnWithdraw_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show("Withdraw from the mission? Samples already recovered are kept; the completion bonus is lost.",
            "Withdraw", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        _session.WithdrawFromMission();
        _crestId = null;
        TxtMessage.Text = "";
        WithdrawRequested?.Invoke();
    }
}
