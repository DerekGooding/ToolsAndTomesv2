using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Campaign;

/// <summary>
/// A mission in progress, saved between fights (at the start of each encounter and during rests; never
/// mid-fight). Quitting mid-fight resumes at the start of that fight, with the lance as it left the last rest.
/// Every Crest is saved in full, including field-shaped parts, damage, ammo, cargo, Motes, and fast-iron.
/// </summary>
public sealed record MissionSave
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public required string MissionId { get; init; }
    public int BaseSeed { get; init; }
    public int StageIndex { get; init; }

    /// <summary><see cref="MissionPhase.Encounter"/> (the fight not yet begun) or <see cref="MissionPhase.Rest"/>.</summary>
    public MissionPhase Phase { get; init; }

    public int RestBeatsRemaining { get; init; }
    public int RestOps { get; init; }
    public IReadOnlyList<MaterialCount> RewardsEarned { get; init; } = [];
    public IReadOnlyList<RestLogEntry> RestLog { get; init; } = [];

    /// <summary>The player's Crest first, then the allies in lance order.</summary>
    public IReadOnlyList<CrestSave> Lance { get; init; } = [];
}

public sealed record RestLogEntry(string ActorId, string Message);

/// <summary>One Crest's full state between fights.</summary>
public sealed record CrestSave
{
    public required string CombatantId { get; init; }
    public required string Name { get; init; }

    /// <summary>The allied pilot flying this Crest (null for the player's Crest).</summary>
    public string? PilotId { get; init; }

    /// <summary>Components in Damage Order (including shaped ones), reaction rules, and the Workshop cargo manifest.</summary>
    public required CrestConfiguration Assembly { get; init; }

    /// <summary>Per-component state, in the same order as <see cref="CrestConfiguration.ComponentIds"/>.</summary>
    public IReadOnlyList<ComponentSave> Components { get; init; } = [];

    public int Motes { get; init; }

    /// <summary>Beats already run toward the next Mote (the timer carries across fights).</summary>
    public int MoteProgressBeats { get; init; }
    public int FastIron { get; init; }
    public int CarriedFastIron { get; init; }
    public IReadOnlyDictionary<string, int> Consumables { get; init; } = new Dictionary<string, int>();
}

public sealed record ComponentSave(string InstanceId, int Durability, int Ammo);
