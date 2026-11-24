using System.Windows;
using System.Windows.Input;
using ToolsAndTomes.Core.Campaign;

namespace ToolsAndTomes.App;

/// <summary>
/// Screen flow: Crest Bay (Workshop) → Mission Select → encounter → rest in the field → encounter → … → Crest Bay.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSessionState _session = new();
    private readonly HangarView _hangarView;
    private readonly CombatView _combatView;
    private readonly ResearchView _researchView;
    private readonly MissionSelectView _missionSelectView;
    private readonly RestView _restView;

    public MainWindow()
    {
        InitializeComponent();

        _hangarView = new HangarView(_session);
        _combatView = new CombatView(_session);
        _researchView = new ResearchView(_session);
        _missionSelectView = new MissionSelectView(_session);
        _restView = new RestView(_session);

        _hangarView.LaunchEncounterRequested += ShowMissionSelect;
        _hangarView.ResearchRequested += ShowResearch;
        _researchView.BackRequested += ShowHangar;
        _missionSelectView.BackRequested += ShowHangar;
        _missionSelectView.MissionStarted += ShowCombat;
        _combatView.ReturnToHangarRequested += AfterCombat;
        _restView.ContinueRequested += ShowCombat;
        _restView.WithdrawRequested += ShowHangar;

        // A mission saved in progress picks up where it left off: at its rest, or at the start of its next fight.
        if (_session.ActiveMission is { Phase: MissionPhase.Rest })
            ShowRest();
        else if (_session.ActiveMission is { Phase: MissionPhase.Encounter })
            ShowCombat();
        else
            MainHost.Content = _hangarView;
    }

    private void ShowCombat()
    {
        MainHost.Content = _combatView;
        _combatView.StartNewEncounter();
    }

    /// <summary>After a fight: rest if the mission continues, otherwise back to the Crest Bay.</summary>
    private void AfterCombat()
    {
        if (_session.ActiveMission is { Phase: MissionPhase.Rest })
            ShowRest();
        else
            ShowHangar();
    }

    private void ShowRest()
    {
        _restView.RefreshUI();
        MainHost.Content = _restView;
    }

    private void ShowHangar()
    {
        _hangarView.RefreshUI();
        MainHost.Content = _hangarView;
    }

    private void ShowMissionSelect()
    {
        _missionSelectView.RefreshUI();
        MainHost.Content = _missionSelectView;
    }

    private void ShowResearch()
    {
        _researchView.RefreshUI();
        MainHost.Content = _researchView;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && ReferenceEquals(MainHost.Content, _combatView))
        {
            _combatView.TogglePause();
            e.Handled = true;
        }
    }
}
