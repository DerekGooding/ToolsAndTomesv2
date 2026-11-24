using Godot;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Client;

/// <summary>
/// Godot vertical-slice entry point. Runs one Core encounter (the Proving Grounds) on the beat clock:
/// the simulation advances one beat per second (three seconds in slow motion while a reaction is available),
/// and everything in between is presentation-only interpolation. All rules come from ToolsAndTomes.Core.
/// </summary>
public partial class BattleController : Node3D
{
    private const double BeatSeconds = 1.0;
    private const double SlowMotionFactor = 3.0;

    private readonly Dictionary<string, CrestView> _views = [];
    private readonly EnemyAIController _enemyAi = new();
    private GameCatalog _catalog = null!;
    private CombatSimulation _sim = null!;
    private Combatant _player = null!;
    private TacticalCamera _camera = null!;
    private TelegraphView _telegraphs = null!;
    private BattleHud _hud = null!;
    private SoundBank _sounds = null!;
    private int _soundedEvents;
    private Node3D _arena = null!;
    private double _beatElapsed;
    private double _beatLength = BeatSeconds;
    private int _seed = 1337;
    private string? _targetId;
    private ActionDefinition? _armedPointAction;

    // Smoke-test mode: `godot --headless --path client/ToolsAndTomes.Client -- --autoplay`
    // runs the fight at full speed with the player on autopilot, prints the result, and quits.
    private bool _autoplay;
    private readonly AutoPilot _autoPilot = new();

    public override void _Ready()
    {
        _catalog = GameCatalog.Default;
        _autoplay = OS.GetCmdlineUserArgs().Contains("--autoplay");

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
        _sounds = new SoundBank();
        AddChild(_sounds);

        StartEncounter();
    }

    private void StartEncounter()
    {
        foreach (var view in _views.Values)
            view.QueueFree();
        _views.Clear();
        _arena?.QueueFree();

        var loadout = CrestLoadout.CreateFromConfiguration(ConfigurationCatalog.AgileSkirmish, _catalog, "player");
        _sim = EncounterFactory.CreateTestEncounter(loadout, _seed++, _catalog);
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

        _camera.Focus(new Vector3((float)_sim.ArenaWidth / 2f, 0, (float)_sim.ArenaHeight / 2f));
        _hud.ResetLog();

        if (_autoplay)
            _sim.Resume();
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
        if (_autoplay)
        {
            RunAutoplayFrame();
            return;
        }

        if (!_sim.IsPaused && _sim.Outcome == EncounterOutcome.InProgress)
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
        _telegraphs.Refresh(_sim, _views);
        _hud.Refresh(_sim, _player, Target(), fraction, _beatLength > BeatSeconds, _armedPointAction);
    }

    private void RunAutoplayFrame()
    {
        for (int i = 0; i < 20 && _sim.Outcome == EncounterOutcome.InProgress && _sim.CurrentBeat < 300; i++)
        {
            _autoPilot.Update(_sim, _player);
            AdvanceBeat();
        }

        foreach (var view in _views.Values)
            view.Refresh(_sim, 0, view.Crest.Id == _targetId);
        _telegraphs.Refresh(_sim, _views);
        _hud.Refresh(_sim, _player, Target(), 0, false, null);

        if (_sim.Outcome != EncounterOutcome.InProgress || _sim.CurrentBeat >= 300)
        {
            GD.Print($"AUTOPLAY RESULT: {_sim.Outcome} after {_sim.CurrentBeat} beats; " +
                     $"enemies defeated {_sim.Combatants.Count(c => c.Faction == Faction.Enemy && c.IsDefeated)}, events {_sim.Events.Count}");
            GetTree().Quit();
        }
    }

    private void AdvanceBeat()
    {
        _enemyAi.Update(_sim);
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
    }

    private Combatant? Target() =>
        _targetId is null ? null : _sim.FindCombatant(_targetId) is { IsDefeated: false } t ? t : null;

    private void OnActionPressed(ActionDefinition action)
    {
        if (action.Targeting == TargetingType.Point && action.BlinkDistance == 0)
        {
            _armedPointAction = action;
            return;
        }

        _armedPointAction = null;
        var target = action.Targeting == TargetingType.EnemyUnit ? Target() : null;
        var result = _sim.IssueCommand(new UseActionCommand(_player.Id, action.Id, target?.Id));
        if (!result.IsValid)
            GD.Print($"Cannot use {action.Name}: {result.FailureReason}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode);
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click when GroundPoint(click.Position) is { } point:
                if (_armedPointAction is { } armed)
                {
                    var result = _sim.IssueCommand(new UseActionCommand(_player.Id, armed.Id, TargetPoint: point));
                    if (!result.IsValid)
                        GD.Print($"Cannot use {armed.Name}: {result.FailureReason}");
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
            case Key.R:
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
