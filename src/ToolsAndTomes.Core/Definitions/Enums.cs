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
    Point
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
    Artillery
}
