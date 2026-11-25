using Godot;
using ToolsAndTomes.Client.UI;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Campaign;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Client;

/// <summary>
/// The game's main scene. Owns the <see cref="CampaignSession"/> (the same profile the WPF tool uses) and moves
/// between screens: Crest Bay → Mission Select → fight → rest → fight → … → Crest Bay. A mission saved in
/// progress resumes on launch.
/// </summary>
/// <remarks>
/// Command-line options (after `--`):
/// <list type="bullet">
/// <item>`--encounter &lt;id&gt;` / `--solo`: open straight into a quick battle (any encounter; `--solo` without allies).</item>
/// <item>`--autoplay`: quick battle on autopilot at full speed; prints `AUTOPLAY RESULT` and quits (smoke test).</item>
/// <item>`--campaign-autoplay [missionId]`: plays through the real screens on autopilot with a throwaway profile:
/// the given mission, or, by default, the demo region in order (each newly unlocked mission next) until a mission
/// fails. Prints a `CAMPAIGN AUTOPLAY` line per mission and quits (exit code 1 if one failed).</item>
/// <item>`--profile &lt;path&gt;`: use another profile file.</item>
/// <item>`--beats-per-frame &lt;n&gt;`: autoplay speed (default 20); 1 makes a watchable recording with `--write-movie`.</item>
/// <item>`--hold &lt;seconds&gt;`: in campaign autoplay, linger on each menu screen and result panel (for recordings).</item>
/// </list>
/// </remarks>
public partial class GameRoot : Node
{
    /// <summary>The quick battle's default fight: a full demo encounter (Border Patrol's last).</summary>
    public const string DefaultQuickBattle = "enc_bm_border_fort";

    private CampaignSession _session = null!;
    private Node? _current;
    private int _quickSeed = 1337;

    private bool _autoplay;
    private bool _campaignAutoplay;
    private int _beatsPerFrame = 20;
    private double _hold;
    private bool _chainMissions;
    private int _missionsFailed;
    private string? _tempProfile;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string? Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
        }

        _autoplay = args.Contains("--autoplay");
        _campaignAutoplay = args.Contains("--campaign-autoplay");

        if (int.TryParse(Arg("--beats-per-frame"), out int bpf) && bpf > 0)
            _beatsPerFrame = bpf;
        if (double.TryParse(Arg("--hold"), System.Globalization.CultureInfo.InvariantCulture, out double hold) && hold > 0)
            _hold = hold;

        string? profile = Arg("--profile");
        if (profile is null && (_autoplay || _campaignAutoplay))
            profile = _tempProfile = Path.Combine(Path.GetTempPath(), $"tnt-autoplay-{Guid.NewGuid():N}.json");
        _session = new CampaignSession(profile);

        if (_campaignAutoplay)
        {
            RunCampaignAutoplay(Arg("--campaign-autoplay"));
            return;
        }

        if (_autoplay || args.Contains("--encounter") || args.Contains("--solo"))
        {
            QuickBattle(Arg("--encounter") ?? DefaultQuickBattle, solo: args.Contains("--solo"));
            return;
        }

        if (_session.ActiveMission is not null)
            Resume();
        else
            ShowCrestBay();
    }

    public override void _ExitTree()
    {
        if (_tempProfile is not null && File.Exists(_tempProfile))
            File.Delete(_tempProfile);
    }

    // ---- Screens ------------------------------------------------------------------------------

    private void Show(Node screen)
    {
        if (_current is not null)
        {
            RemoveChild(_current);
            _current.QueueFree();
        }
        _current = screen;
        AddChild(screen);
    }

    private CrestBayScreen ShowCrestBay()
    {
        var bay = new CrestBayScreen(_session);
        bay.MissionSelectRequested += () => ShowMissionSelect();
        bay.ResumeRequested += Resume;
        bay.QuickBattleRequested += () => QuickBattle(DefaultQuickBattle, solo: false);
        bay.QuitRequested += () => GetTree().Quit();
        Show(bay);
        return bay;
    }

    private MissionSelectScreen ShowMissionSelect()
    {
        var select = new MissionSelectScreen(_session);
        select.BackRequested += () => ShowCrestBay();
        select.MissionChosen += id =>
        {
            _session.StartMission(id);
            MissionFight();
        };
        Show(select);
        return select;
    }

    private RestScreen ShowRest()
    {
        var rest = new RestScreen(_session);
        rest.ContinueRequested += MissionFight;
        rest.WithdrawRequested += () => ShowCrestBay();
        Show(rest);
        return rest;
    }

    /// <summary>Picks up the mission in progress: at its rest, or at the start of its next fight.</summary>
    private void Resume()
    {
        if (_session.ActiveMission is { Phase: MissionPhase.Rest })
            ShowRest();
        else if (_session.ActiveMission is { Phase: MissionPhase.Encounter })
            MissionFight();
        else
            ShowCrestBay();
    }

    // ---- Fights -------------------------------------------------------------------------------

    /// <summary>The mission's current fight. Rewards bank the moment it is decided; Continue moves on.</summary>
    private void MissionFight()
    {
        var battle = new BattleController
        {
            SimFactory = _session.StartEncounter,
            Autoplay = _campaignAutoplay,
            AutoplayBeatsPerFrame = _beatsPerFrame,
            AllowRestart = false,
            ContinueLabel = "Continue"
        };
        battle.Finished += sim =>
        {
            if (sim.Outcome == EncounterOutcome.InProgress) // autoplay ran out of beats: count it as a withdrawal
            {
                _session.WithdrawFromMission();
                battle.SetResultText("The fight dragged on too long; the lance withdrew.");
                battle.ContinueLabel = "Return to the Crest Bay";
            }
            else
            {
                var rewards = _session.CompleteEncounter(sim);
                var lines = new List<string>();
                if (rewards.Count > 0)
                    lines.Add("Recovered: " + Ui.Materials(_session.Catalog, rewards));
                var down = sim.Combatants.Where(c => AllyAIController.IsAlly(c) && c.IsDefeated).Select(c => c.Name).ToList();
                if (down.Count > 0 && sim.Outcome == EncounterOutcome.PlayerVictory)
                    lines.Add($"Down: {string.Join(", ", down)}. Repair their Cores at the rest to bring them back.");
                if (_session.ActiveMission is null && _session.LastMissionReport is { } report)
                    lines.Add(report);
                battle.SetResultText(string.Join("\n", lines));
                battle.ContinueLabel = _session.ActiveMission is { Phase: MissionPhase.Rest } ? "Rest in the field ▶" : "Return to the Crest Bay";
            }

            if (_campaignAutoplay)
                Later(AfterMissionFight);
        };
        battle.ContinueRequested += AfterMissionFight;
        Show(battle);
    }

    private void AfterMissionFight()
    {
        if (_session.ActiveMission is { Phase: MissionPhase.Rest })
        {
            var rest = ShowRest();
            if (_campaignAutoplay)
                Later(() =>
                {
                    rest.AutoRest();
                    Later(rest.Continue);
                });
            return;
        }

        if (_campaignAutoplay)
        {
            FinishCampaignAutoplay();
            return;
        }
        ShowCrestBay();
    }

    /// <summary>A one-off fight outside the campaign, with a fresh starter Crest and the pilots' starting Crests.</summary>
    private void QuickBattle(string encounterId, bool solo)
    {
        var catalog = _session.Catalog;
        if (!catalog.Encounters.TryGetValue(encounterId, out var encounter))
        {
            GD.PrintErr($"Unknown encounter '{encounterId}'; using {DefaultQuickBattle}.");
            encounter = catalog.Encounters[DefaultQuickBattle];
        }

        var battle = new BattleController
        {
            SimFactory = () =>
            {
                var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, catalog, "player");
                var allies = solo ? [] : EncounterFactory.CreateAllies(catalog.Pilots.Keys.Take(MissionRun.MaxAllies), catalog, encounter.PlayerStartingMotes);
                return EncounterFactory.CreateEncounter(encounter, loadout, _quickSeed++, catalog, allies);
            },
            Autoplay = _autoplay,
            AutoplayBeatsPerFrame = _beatsPerFrame,
            AllowRestart = true,
            ContinueLabel = _autoplay ? null : "Back to the Crest Bay"
        };
        battle.Finished += sim =>
        {
            if (!_autoplay)
                return;
            GD.Print($"AUTOPLAY RESULT ({encounter.Id}): {sim.Outcome} after {sim.CurrentBeat} beats; " +
                     $"enemies defeated {sim.Combatants.Count(c => c.Faction == Faction.Enemy && c.IsDefeated)}, " +
                     $"allies {sim.Combatants.Count(AllyAIController.IsAlly)} ({sim.Combatants.Count(c => AllyAIController.IsAlly(c) && c.IsDefeated)} down), " +
                     $"events {sim.Events.Count}");
            GetTree().Quit();
        };
        battle.ContinueRequested += () => ShowCrestBay();
        Show(battle);
    }

    // ---- Headless campaign smoke test -----------------------------------------------------------

    private void RunCampaignAutoplay(string? missionId)
    {
        var catalog = _session.Catalog;
        _chainMissions = missionId is null;
        missionId ??= NextMission();
        if (missionId is null || !catalog.Missions.TryGetValue(missionId, out var mission) || !MissionRun.IsUnlocked(mission, _session.Workshop))
        {
            GD.PrintErr($"Mission '{missionId}' is unknown or locked on a fresh profile.");
            GetTree().Quit(2);
            return;
        }

        StartAutoplayMission(missionId);
    }

    /// <summary>Walks the real screens: Crest Bay, then Mission Select, then the mission itself.</summary>
    private void StartAutoplayMission(string missionId)
    {
        ShowCrestBay();
        Later(() =>
        {
            var select = ShowMissionSelect();
            Later(() => select.Choose(missionId));
        });
    }

    /// <summary>Runs an automated step after the current frame, or after <c>--hold</c> seconds when recording.</summary>
    private void Later(Action step)
    {
        if (_hold > 0)
            GetTree().CreateTimer(_hold).Timeout += step;
        else
            Callable.From(step).CallDeferred();
    }

    /// <summary>The first unlocked, not yet completed mission in content order.</summary>
    private string? NextMission() => _session.Catalog.Missions.Values
        .FirstOrDefault(m => MissionRun.IsUnlocked(m, _session.Workshop) && !_session.Workshop.HasCompleted(m.Id))?.Id;

    private void FinishCampaignAutoplay()
    {
        ShowCrestBay();
        bool completed = _session.LastMissionReport?.Contains(" completed") == true;
        if (!completed)
            _missionsFailed++;
        GD.Print($"CAMPAIGN AUTOPLAY: {_session.LastMissionReport} (encounters won so far {_session.EncountersWon})");

        // Carry on through the region while missions succeed.
        if (_chainMissions && completed && NextMission() is { } next &&
            _session.Catalog.Missions[next].Region is { Length: > 0 } region &&
            _session.Catalog.Missions.Values.Any(m => m.Region == region && _session.Workshop.HasCompleted(m.Id)))
        {
            StartAutoplayMission(next);
            return;
        }

        GD.Print($"CAMPAIGN AUTOPLAY RESULT: completed [{string.Join(", ", _session.Workshop.CompletedMissions)}]; " +
                 $"samples {string.Join(", ", _session.Workshop.Samples.Select(kv => $"{kv.Key} {kv.Value}"))}");
        GetTree().Quit(_missionsFailed > 0 ? 1 : 0);
    }
}
