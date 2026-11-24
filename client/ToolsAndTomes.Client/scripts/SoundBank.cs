using Godot;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Client;

/// <summary>
/// Plays the pipeline's sounds from the engine's event stream. Files follow the asset pipeline's layout
/// (assets/specs.json → res://assets/sfx/&lt;key&gt;/&lt;slot&gt;_&lt;n&gt;.ogg):
/// an action's sounds live under its id (windup when it starts, impact when it resolves), and engine events under
/// event_&lt;CombatEventType&gt; (slot "play"). A random variant is picked each time; missing sounds are simply silent.
/// </summary>
public partial class SoundBank : Node3D
{
    private const int MaxVariants = 8;
    private const int Voices = 16;

    // Events that mean "this action landed" — they carry the action's id (see ActionEffectResolver).
    private static readonly HashSet<CombatEventType> ImpactEvents =
    [
        CombatEventType.ActionExecuted, CombatEventType.AegisActivated, CombatEventType.CombatantBlinked,
        CombatEventType.CombatantDisplaced, CombatEventType.FastIronProduced, CombatEventType.FieldShaped,
        CombatEventType.AmmoRefilled
    ];

    private readonly Dictionary<string, AudioStream[]> _cache = [];
    private readonly List<AudioStreamPlayer3D> _voices = [];
    private readonly RandomNumberGenerator _rng = new();
    private int _nextVoice;
    private readonly bool _headless = DisplayServer.GetName() == "headless";

    public override void _Ready()
    {
        for (int i = 0; i < Voices; i++)
        {
            var voice = new AudioStreamPlayer3D { UnitSize = 25f, VolumeDb = -4f, MaxPolyphony = 1 };
            AddChild(voice);
            _voices.Add(voice);
        }
    }

    public override void _ExitTree()
    {
        // Stop and drop every stream so no playback outlives the scene (quitting mid-sound otherwise leaks them).
        foreach (var voice in _voices)
        {
            voice.Stop();
            voice.Stream = null;
        }
        _cache.Clear();
    }

    /// <summary>
    /// Plays the sounds for a batch of new events. Each sound plays at most once per batch, so a multi-hit
    /// salvo or a beat full of damage doesn't stack copies of the same sound.
    /// </summary>
    public void PlayEvents(IEnumerable<CombatEvent> events, Func<string, Vector3?> positionOf, string playerId)
    {
        var played = new HashSet<string>();
        foreach (var e in events)
        {
            (string Key, string Slot)? sound = e switch
            {
                { Type: CombatEventType.ActionStarted, ActionId: { } id } => (id, "windup"),
                { ActionId: { } id } when ImpactEvents.Contains(e.Type) => (id, "impact"),
                { Type: CombatEventType.MoteGenerated } when e.ActorId != playerId => null, // only the player's own Motes
                { Type: CombatEventType.CommandRejected or CombatEventType.DamageRolled or CombatEventType.ActionStateChanged } => null,
                _ => ($"event_{e.Type}", "play")
            };

            if (sound is not { } s || !played.Add($"{s.Key}/{s.Slot}/{e.ActorId}"))
                continue;

            // A reaction has no windup: if it starts and resolves together, its impact sound covers it.
            if (s.Slot == "windup" && !Has(s.Key, s.Slot))
                continue;

            Play(s.Key, s.Slot, positionOf(e.ActorId));
        }
    }

    public bool Has(string key, string slot) => Streams(key, slot).Length > 0;

    public void Play(string key, string slot, Vector3? position)
    {
        var streams = Streams(key, slot);
        if (streams.Length == 0 || _headless)
            return; // headless (smoke tests): load to prove the import, but the dummy driver never frees playbacks

        var voice = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Count;
        voice.Stream = streams[_rng.RandiRange(0, streams.Length - 1)];
        voice.Position = position ?? Vector3.Zero;
        voice.PitchScale = _rng.RandfRange(0.95f, 1.05f); // small per-play variation on top of the variants
        voice.Play();
    }

    private AudioStream[] Streams(string key, string slot)
    {
        string cacheKey = $"{key}/{slot}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var streams = new List<AudioStream>();
        for (int n = 1; n <= MaxVariants; n++)
        {
            string path = $"res://assets/sfx/{key}/{slot}_{n}.ogg";
            if (!ResourceLoader.Exists(path))
                break;
            if (ResourceLoader.Load<AudioStream>(path) is { } stream)
                streams.Add(stream);
        }

        return _cache[cacheKey] = [.. streams];
    }
}
