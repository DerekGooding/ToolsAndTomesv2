using System.Windows;
using System.Windows.Input;

namespace ToolsAndTomes.App;

public partial class MainWindow : Window
{
    private readonly AppSessionState _session = new();
    private readonly HangarView _hangarView;
    private readonly CombatView _combatView;

    public MainWindow()
    {
        InitializeComponent();

        _hangarView = new HangarView(_session);
        _combatView = new CombatView(_session);

        _hangarView.LaunchEncounterRequested += ShowCombat;
        _combatView.ReturnToHangarRequested += ShowHangar;

        MainHost.Content = _hangarView;
    }

    private void ShowCombat()
    {
        MainHost.Content = _combatView;
        _combatView.StartNewEncounter();
    }

    private void ShowHangar()
    {
        _hangarView.RefreshUI();
        MainHost.Content = _hangarView;
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
