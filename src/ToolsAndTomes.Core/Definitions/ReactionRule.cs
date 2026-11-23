namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// A pre-set reaction configured in the Workshop: "when an incoming impact matches, react with this action".
/// At the table: at the start of the reaction step, each Crest that has not declared a reaction checks its
/// rules in order and declares the first one that matches and is legal.
/// </summary>
public sealed record ReactionRule
{
    public required string ActionId { get; init; }

    /// <summary>Only trigger when a single incoming impact would deal at least this much damage (before saves).</summary>
    public int MinIncomingDamage { get; init; } = 1;

    /// <summary>Only trigger on impacts with this telegraph kind; null matches any.</summary>
    public TelegraphKind? Telegraph { get; init; }

    public string Describe(GameCatalog catalog)
    {
        string action = catalog.Actions.TryGetValue(ActionId, out var def) ? def.Name : ActionId;
        string kind = Telegraph is null ? "any impact" : $"{Telegraph} impact";
        return $"{action} when {kind} ≥ {MinIncomingDamage} damage";
    }
}
