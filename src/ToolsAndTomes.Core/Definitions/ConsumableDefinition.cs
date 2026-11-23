namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// A single-use item carried in a cargo component (e.g. an ammo pack or a thrown charge). Using it runs its
/// <see cref="UseActionId"/> on the cargo component and consumes one. Consumables are prepared in the Workshop
/// (free) or shaped from fast-iron in the field for <see cref="FastIronCost"/>.
/// </summary>
public sealed record ConsumableDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string UseActionId { get; init; }

    /// <summary>Cargo units occupied per item.</summary>
    public int CargoSize { get; init; } = 1;

    /// <summary>Weight per item while carried (counts toward the Crest's total weight).</summary>
    public int Weight { get; init; } = 1;

    /// <summary>Fast-iron spent to shape one in the field.</summary>
    public int FastIronCost { get; init; } = 1;
}

public enum CargoKind
{
    FastIron,
    Consumable
}

/// <summary>One line of a cargo manifest: <see cref="Count"/> units of fast-iron, or of a consumable.</summary>
public sealed record CargoEntry(CargoKind Kind, int Count, string? ItemId = null);

public enum ShapingKind
{
    /// <summary>Shape a whole component (installed at the end of the Damage Order).</summary>
    Component,

    /// <summary>Shape a consumable into cargo.</summary>
    Consumable,

    /// <summary>Shape ammunition to refill one of the Crest's weapons.</summary>
    AmmoRefill
}

/// <summary>
/// What a Shape Fast-iron spell produces. <see cref="ItemId"/> is a component or consumable id;
/// for <see cref="ShapingKind.AmmoRefill"/> the weapon is given by the command's own-component target.
/// </summary>
public sealed record ShapingOrder(ShapingKind Kind, string? ItemId = null);
