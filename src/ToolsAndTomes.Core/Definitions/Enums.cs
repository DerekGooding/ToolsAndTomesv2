namespace ToolsAndTomes.Core.Definitions;

public enum ActionCategory
{
    Weapon,
    Spell,
    Defense,
    Utility
}

public enum ActionState
{
    Idle,
    Moving,
    Windup,
    Executing,
    Recovery,
    Interrupted
}

public enum TargetingType
{
    Self,
    EnemyUnit,
    Point,

    /// <summary>A Crest on the caster's own side (the caster included), e.g. Ward Ally or Quick Mend.</summary>
    AllyUnit
}

/// <summary>
/// What kind of damage an action deals. Physical has no extra effect. Electrical deals extra damage per tome the
/// target holds; Disjunction deals extra damage per few points of the target's equipment Wit tax.
/// </summary>
public enum DamageType
{
    Physical,
    Electrical,
    Disjunction
}

/// <summary>
/// Organizational category for a component. Does not enforce fixed chassis slot limits.
/// </summary>
public enum ComponentCategory
{
    Core,
    Mobility,
    Armor,
    Weapon,
    Defense,
    Utility,
    Arcane
}

public enum InterruptionTier
{
    None = 0,
    Disruption = 1,
    Interruption = 2,
    Cancellation = 3
}

public enum Faction
{
    Player,
    Enemy
}

public enum TelegraphKind
{
    MeleeCommit,
    SpellWindup,
    AreaBlast,
    RangedQuick,
    DefensiveReaction
}

public enum EnemyArchetype
{
    Skirmisher,
    Bruiser,
    Artillery,

    /// <summary>A boss: every beat, commits every attack its Wit allows against its target (and closes in when out of reach).</summary>
    Warlord
}
