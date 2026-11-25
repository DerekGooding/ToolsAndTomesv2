using Godot;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Client;

/// <summary>
/// Presents one Core encounter on the beat clock: the simulation advances one beat per second (three seconds in
/// slow motion while a reaction is available), and everything in between is presentation-only interpolation.
/// All rules come from ToolsAndTomes.Core. <see cref="GameRoot"/> creates it with a <see cref="SimFactory"/>
/// (a mission fight from the campaign, or a quick battle) and listens for <see cref="Finished"/>.
/// </summary>
public partial class BattleController : Node3D
{
    private const double BeatSeconds = 1.0;
    private const double SlowMotionFactor = 3.0;

    /// <summary>The longest an autoplayed fight runs before it counts as a timeout.</summary>
    public const int AutoplayBeatLimit = 300;

    /// <summary>Builds the fight to show (called again on restart, when <see cref="AllowRestart"/>).</summary>
    public Func<CombatSimulation> SimFactory { get; set; } = null!;

    /// <summary>Runs at full speed with the player on autopilot (headless smoke tests).</summary>
    public bool Autoplay { get; set; }

    /// <summary>Beats simulated per frame in autoplay; 0 plays in real time on the beat clock (for watching and recordings).</summary>
    public int AutoplayBeatsPerFrame { get; set; } = 20;

    /// <summary>Whether R restarts the fight (quick battles only: a mission fight can't be replayed).</summary>
    public bool AllowRestart { get; set; } = true;

    /// <summary>Text for the result panel's button; null hides the button (quick battles restart with R).</summary>
    public string? ContinueLabel { get; set; }

    /// <summary>Raised once when the fight ends (or, in autoplay, hits <see cref="AutoplayBeatLimit"/>).</summary>
    public event Action<CombatSimulation>? Finished;

    /// <summary>Raised when the player presses the result panel's button.</summary>
    public event Action? ContinueRequested;

    /// <summary>Raised when the player leaves mid-fight (the HUD's leave button).</summary>
    public event Action? LeaveRequested;

    /// <summary>The HUD's leave button text (null hides it), and an optional confirmation question.</summary>
    public string? LeaveLabel { get; set; }
    public string? LeaveConfirmText { get; set; }

    private readonly Dictionary<string, CrestView> _views = [];
    private EnemyAIController _enemyAi = new();
    private readonly AllyAIController _allyAi = new();
    private bool _finished;
    private string? _resultText;
    private GameCatalog _catalog = null!;
    private CombatSimulation _sim = null!;
    private Combatant _player = null!;
    private TacticalCamera _camera = null!;
    private TelegraphView _telegraphs = null!;
    private BattleHud _hud = null!;
    private SoundBank _sounds = null!;
    private FeedbackView _feedback = null!;
    private int _soundedEvents;

    /// <summary>Remaining hit-stop: a brief freeze of the beat clock on big hits (presentation only; rules unaffected).</summary>
    private double _hitStop;
    private Node3D _arena = null!;
    private double _beatElapsed;
    private double _beatLength = BeatSeconds;
    private string? _targetId;
    private ActionDefinition? _armedPointAction;

    private readonly AutoPilot _autoPilot = new();

    /// <summary>The fight on screen.</summary>
    public CombatSimulation Simulation => _sim;

    /// <summary>Shown on the result panel when the fight ends (e.g. samples recovered).</summary>
    public void SetResultText(string text) => _resultText = text;

    public override void _Ready()
    {
        _catalog = GameCatalog.Default;

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.06f, 0.08f, 0.12f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.6f, 0.7f),
                AmbientLightEnergy = 0.6f
            }
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -35, 0), ShadowEnabled = true, LightEnergy = 1.1f });

        _camera = new TacticalCamera();
        AddChild(_camera);
        _telegraphs = new TelegraphView();
        AddChild(_telegraphs);
        _hud = new BattleHud();
        AddChild(_hud);
        _hud.ActionPressed += OnActionPressed;
        _hud.ContinuePressed += () => ContinueRequested?.Invoke();
        _hud.LeaveLabel = Autoplay ? null : LeaveLabel;
        _hud.LeaveConfirmText = LeaveConfirmText;
        _hud.LeavePressed += () => LeaveRequested?.Invoke();
        _hud.SetFieldSource(() => FieldOption.Build(_catalog, _player, includeSwaps: true));
        _hud.FieldOptionChosen += option =>
        {
            var result = _sim.IssueCommand(option.Command);
            _hud.Flash(result.IsValid ? $"Ordered: {option.Label}" : $"Can't: {result.FailureReason}");
        };
        _sounds = new SoundBank();
        AddChild(_sounds);
        _feedback = new FeedbackView();
        AddChild(_feedback);

        StartEncounter();
    }

    private void StartEncounter()
    {
        foreach (var view in _views.Values)
            view.QueueFree();
        _views.Clear();
        _arena?.QueueFree();

        _sim = SimFactory();
        _enemyAi = new EnemyAIController();
        _finished = false;
        _resultText = null;
        _player = _sim.FindCombatant(EncounterFactory.PlayerCombatantId)!;
        _targetId = _sim.Combatants.FirstOrDefault(c => c.Faction == Faction.Enemy)?.Id;
        _armedPointAction = null;
        _beatElapsed = 0;
        _beatLength = BeatSeconds;
        _soundedEvents = 0;
        _sim.Pause();

        BuildArena();
        foreach (var crest in _sim.Combatants)
        {
            var view = new CrestView();
            AddChild(view);
            view.Bind(crest);
            _views[crest.Id] = view;
        }

        // Start behind the player's Crest, looking toward the enemy.
        var enemies = _sim.Combatants.Where(c => c.Faction == Faction.Enemy).ToList();
        var enemyCenter = enemies.Count == 0
            ? Coords.ToWorld(_player.Position) + Vector3.Right
            : enemies.Aggregate(Vector3.Zero, (sum, c) => sum + Coords.ToWorld(c.Position)) / enemies.Count;
        var start = Coords.ToWorld(_player.Position);
        _camera.Snap(start.Lerp(enemyCenter, 0.3f), enemyCenter - start);
        _hitStop = 0;
        _hud.ResetLog();

        if (Autoplay)
            _sim.Resume();

        // Draw the fight once now, so its first frame isn't the un-laid-out scene.
        foreach (var view in _views.Values)
            view.Refresh(_sim, 0, view.Crest.Id == _targetId);
        _telegraphs.Refresh(_sim, _views, 0);
        _hud.Refresh(_sim, _player, Target(), 0, false, null);
    }

    private void BuildArena()
    {
        _arena = new Node3D { Name = "Arena" };
        AddChild(_arena);

        float w = (float)_sim.ArenaWidth, h = (float)_sim.ArenaHeight;
        _arena.AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(w, h) },
            Position = new Vector3(w / 2f, 0, h / 2f),
            MaterialOverride = Coords.Flat(new Color(0.16f, 0.2f, 0.24f))
        });

        var gridMaterial = Coords.Flat(new Color(0.25f, 0.31f, 0.38f), unshaded: true);
        for (int x = 0; x <= (int)w; x += 2)
            _arena.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.01f, h) }, Position = new Vector3(x, 0.005f, h / 2f), MaterialOverride = gridMaterial });
        for (int z = 0; z <= (int)h; z += 2)
            _arena.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(w, 0.01f, 0.03f) }, Position = new Vector3(w / 2f, 0.005f, z), MaterialOverride = gridMaterial });
    }

    public override void _Process(double delta)
    {
        if (Autoplay && AutoplayBeatsPerFrame > 0)
        {
            RunAutoplayFrame();
            return;
        }

        if (_hitStop > 0)
            _hitStop -= delta;
        else if (!_sim.IsPaused && _sim.Outcome == EncounterOutcome.InProgress)
        {
            _beatElapsed += delta;
            while (_beatElapsed >= _beatLength && _sim.Outcome == EncounterOutcome.InProgress)
            {
                _beatElapsed -= _beatLength;
                AdvanceBeat();
            }
        }

        double fraction = Math.Clamp(_beatElapsed / _beatLength, 0, 1);
        foreach (var view in _views.Values)
            view.Refresh(_sim, fraction, view.Crest.Id == _targetId);
        _telegraphs.Refresh(_sim, _views, fraction);
        UpdateCameraFollow();
        _hud.Refresh(_sim, _player, Target(), fraction, _beatLength > BeatSeconds, _armedPointAction);
        _hud.ShowResult(_finished ? ResultHeadline() : null, _resultText, ContinueLabel, AllowRestart);
        CheckFinished();
    }

    private string ResultHeadline() => _sim.Outcome switch
    {
        EncounterOutcome.PlayerVictory => "VICTORY",
        EncounterOutcome.PlayerDefeat => "DEFEAT: your Crest's Core failed",
        _ => "TIME UP"
    };

    /// <summary>Raises <see cref="Finished"/> once, when the fight is decided (or autoplay runs out of beats).</summary>
    private void CheckFinished()
    {
        bool over = _sim.Outcome != EncounterOutcome.InProgress || (Autoplay && _sim.CurrentBeat >= AutoplayBeatLimit);
        if (_finished || !over)
            return;
        _finished = true;
        _sim.Pause();
        Finished?.Invoke(_sim);
    }

    private void RunAutoplayFrame()
    {
        for (int i = 0; i < AutoplayBeatsPerFrame && _sim.Outcome == EncounterOutcome.InProgress && _sim.CurrentBeat < AutoplayBeatLimit; i++)
            AdvanceBeat();

        foreach (var view in _views.Values)
            view.Refresh(_sim, 0, view.Crest.Id == _targetId);
        _telegraphs.Refresh(_sim, _views, 0);
        UpdateCameraFollow();
        _hud.Refresh(_sim, _player, Target(), 0, false, null);
        CheckFinished();
        _hud.ShowResult(_finished ? ResultHeadline() : null, _resultText, ContinueLabel, AllowRestart);
    }

    private void AdvanceBeat()
    {
        if (Autoplay)
            _autoPilot.Update(_sim, _player);
        _enemyAi.Update(_sim);
        _allyAi.Update(_sim);
        _sim.StepBeat();
        PlayNewSounds();

        // Slow motion while the player has a reaction window open (presentation only).
        bool reactionWindow = _sim.GetIncomingImpacts(_player).Count > 0;
        _beatLength = reactionWindow ? BeatSeconds * SlowMotionFactor : BeatSeconds;

        if (Target() is null)
            _targetId = _sim.Combatants.FirstOrDefault(c => c.Faction == Faction.Enemy && !c.IsDefeated)?.Id;
    }

    private void PlayNewSounds()
    {
        if (_soundedEvents >= _sim.Events.Count)
            return;
        var fresh = _sim.Events.Skip(_soundedEvents).ToList();
        _soundedEvents = _sim.Events.Count;
        _sounds.PlayEvents(fresh, id => _views.TryGetValue(id, out var view) ? view.Position : null, _player.Id);
        ShowFeedback(fresh);
    }

    /// <summary>
    /// The camera frames the player's Crest, leaning a third of the way toward the target while it is close
    /// enough to share the frame (once the player's Crest falls, it frames the nearest standing ally).
    /// </summary>
    private void UpdateCameraFollow()
    {
        var anchor = _player.IsDefeated
            ? _sim.Combatants.Where(c => c.Faction == Faction.Player && !c.IsDefeated).FirstOrDefault() ?? _player
            : _player;
        var from = _views[anchor.Id].Position;
        var target = Target();
        _camera.FollowPoint = target is not null && _views.TryGetValue(target.Id, out var tv) && from.DistanceTo(tv.Position) < 14f
            ? from.Lerp(tv.Position, 0.33f)
            : from;
    }

    private static readonly Color HurtColor = new(1f, 0.3f, 0.25f);
    private static readonly Color StrikeColor = new(1f, 0.88f, 0.6f);
    private static readonly Color BreakColor = new(1f, 0.6f, 0.2f);
    private static readonly Color GuardColor = new(0.45f, 1f, 0.85f);
    private static readonly Color WarnColor = new(1f, 0.85f, 0.35f);

    /// <summary>
    /// Turns the beat's events into feel: struck parts flash and the body recoils; damage, saves, and breaks
    /// float up; parts breaking and Crests falling burst; the camera shakes and the clock briefly stops on big
    /// hits, hardest when they land on the player's own Crest. Presentation only.
    /// </summary>
    private void ShowFeedback(IReadOnlyList<CombatEvent> events)
    {
        float shake = 0;
        double stop = 0;
        var damage = new Dictionary<string, int>();
        var floated = new HashSet<(string, CombatEventType)>(); // one text of each kind per Crest per beat

        foreach (var e in events)
        {
            if (!_views.TryGetValue(e.ActorId, out var view))
                continue;
            bool firstOfKind = floated.Add((e.ActorId, e.Type));
            var head = view.Position + Vector3.Up * 2.3f;
            bool mine = e.ActorId == _player.Id;
            bool lance = view.Crest.Faction == Faction.Player;

            switch (e.Type)
            {
                case CombatEventType.ComponentDamaged:
                    damage[e.ActorId] = damage.GetValueOrDefault(e.ActorId) + (e.Value ?? 1);
                    view.Hit(e.ComponentInstanceId, lance ? HurtColor : StrikeColor, 0.7f);
                    shake = Math.Max(shake, mine ? 0.38f : lance ? 0.22f : 0.14f);
                    if (mine)
                        stop = Math.Max(stop, 0.05);
                    break;

                case CombatEventType.ComponentDisabled:
                {
                    string part = e.ComponentInstanceId is { } id && view.Crest.Loadout.FindById(id) is { } p ? p.Definition.Name : "Part";
                    _feedback.Float(head + Vector3.Up * 0.55f, $"{part} DISABLED", BreakColor, 0.75f, 1.7f);
                    _feedback.Pop(view.Position + Vector3.Up * 1.2f, BreakColor with { A = 0.7f }, 2.4f);
                    view.Hit(e.ComponentInstanceId, BreakColor, 1f);
                    shake = Math.Max(shake, mine ? 0.6f : 0.32f);
                    stop = Math.Max(stop, mine ? 0.12 : 0.07);
                    break;
                }

                case CombatEventType.DamageBlocked when firstOfKind:
                    _feedback.Float(head, e.Message.Contains("evaded", StringComparison.OrdinalIgnoreCase) ? "EVADED" : "SAVED", GuardColor, 0.75f, 0.9f);
                    break;

                case CombatEventType.AegisBlocked:
                    view.AegisFlash();
                    _feedback.Float(head, "AEGIS", GuardColor, 0.8f, 0.9f);
                    break;

                case CombatEventType.CombatantDefeated:
                    _feedback.Pop(view.Position + Vector3.Up * 1.1f, (lance ? HurtColor : BreakColor) with { A = 0.8f }, 5f, 0.7f);
                    _feedback.Float(head + Vector3.Up * 0.4f, lance && !mine ? $"{view.Crest.Name} DOWN" : "DEFEATED", lance ? HurtColor : BreakColor, 1.3f, 2.2f);
                    shake = Math.Max(shake, mine ? 1f : 0.7f);
                    stop = Math.Max(stop, 0.18);
                    break;

                case CombatEventType.ActionInterrupted when firstOfKind:
                    _feedback.Float(head, e.Interruption.ToString().ToUpperInvariant(), WarnColor, 0.8f, 1.2f);
                    break;

                case CombatEventType.CombatantBlinked when firstOfKind:
                    _feedback.Float(head, "BLINK", GuardColor, 0.7f, 0.8f);
                    break;

                case CombatEventType.Overheated when firstOfKind:
                    _feedback.Float(head, "OVERHEAT", BreakColor, 0.8f, 1.2f);
                    break;
            }
        }

        foreach (var (id, points) in damage)
        {
            var view = _views[id];
            bool lance = view.Crest.Faction == Faction.Player;
            _feedback.Float(view.Position + Vector3.Up * 2.3f, $"-{points}", lance ? HurtColor : Colors.White, 1f + 0.2f * (points - 1));
        }

        if (shake > 0)
            _camera.Shake(shake);
        if (!Autoplay || AutoplayBeatsPerFrame == 0)
            _hitStop = Math.Max(_hitStop, stop);
    }

    private Combatant? Target() =>
        _targetId is null ? null : _sim.FindCombatant(_targetId) is { IsDefeated: false } t ? t : null;

    private void OnActionPressed(ActionDefinition action)
    {
        // Point actions and ally-targeted spells (Ward Ally, Quick Mend) arm, then take a left-click.
        if ((action.Targeting == TargetingType.Point && action.BlinkDistance == 0) || action.Targeting == TargetingType.AllyUnit)
        {
            _armedPointAction = action;
            return;
        }

        _armedPointAction = null;
        var target = action.Targeting == TargetingType.EnemyUnit ? Target() : null;
        var result = _sim.IssueCommand(new UseActionCommand(_player.Id, action.Id, target?.Id));
        if (!result.IsValid)
            _hud.Flash($"Can't use {action.Name}: {result.FailureReason}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode);
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click when GroundPoint(click.Position) is { } point:
                if (_armedPointAction is { Targeting: TargetingType.AllyUnit } wardOrMend)
                {
                    var ally = _sim.Combatants
                        .Where(c => c.Faction == Faction.Player && !c.IsDefeated)
                        .OrderBy(c => c.Position.DistanceTo(point))
                        .FirstOrDefault(c => c.Position.DistanceTo(point) <= 1.6);
                    var result = ally is null
                        ? Core.Rules.ActionValidationResult.Fail("click on yourself or an ally")
                        : _sim.IssueCommand(new UseActionCommand(_player.Id, wardOrMend.Id, ally.Id));
                    _hud.Flash(result.IsValid ? $"{wardOrMend.Name} → {ally!.Name}" : $"Can't use {wardOrMend.Name}: {result.FailureReason}");
                    _armedPointAction = null;
                }
                else if (_armedPointAction is { } armed)
                {
                    var result = _sim.IssueCommand(new UseActionCommand(_player.Id, armed.Id, TargetPoint: point));
                    if (!result.IsValid)
                        _hud.Flash($"Can't use {armed.Name}: {result.FailureReason}");
                    _armedPointAction = null;
                }
                else
                {
                    var picked = _sim.Combatants
                        .Where(c => c.Faction == Faction.Enemy && !c.IsDefeated)
                        .OrderBy(c => c.Position.DistanceTo(point))
                        .FirstOrDefault(c => c.Position.DistanceTo(point) <= 1.6);
                    if (picked is not null)
                        _targetId = picked.Id;
                }
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } click when GroundPoint(click.Position) is { } point:
                _armedPointAction = null;
                _sim.IssueCommand(new MoveCommand(_player.Id, point));
                break;
        }
    }

    private void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space:
                _sim.TogglePause();
                break;
            case Key.Period when _sim.IsPaused:
                _sim.Resume();
                AdvanceBeat();
                _sim.Pause();
                _beatElapsed = 0;
                break;
            case Key.Tab:
                var enemies = _sim.Combatants.Where(c => c.Faction == Faction.Enemy && !c.IsDefeated).ToList();
                if (enemies.Count > 0)
                {
                    int index = enemies.FindIndex(c => c.Id == _targetId);
                    _targetId = enemies[(index + 1) % enemies.Count].Id;
                }
                break;
            case Key.R when AllowRestart:
                StartEncounter();
                break;
            case Key.Escape:
                _armedPointAction = null;
                break;
        }
    }

    /// <summary>Where a screen position hits the ground plane (y = 0), in Core's table coordinates.</summary>
    private Vec2? GroundPoint(Vector2 screen)
    {
        var camera = _camera.Camera;
        var origin = camera.ProjectRayOrigin(screen);
        var direction = camera.ProjectRayNormal(screen);
        if (Mathf.Abs(direction.Y) < 1e-4f)
            return null;

        float t = -origin.Y / direction.Y;
        if (t < 0)
            return null;

        var hit = origin + direction * t;
        return Coords.ToTable(hit);
    }
}
