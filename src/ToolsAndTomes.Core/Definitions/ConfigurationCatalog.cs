namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// Convenience accessors for the shipped starter builds (defined in Content/configurations.json).
/// These are saved component lists in Damage Order, not predefined Crest classes or chassis types.
/// </summary>
public static class ConfigurationCatalog
{
    public const string AgileSkirmishId = "cfg_agile_skirmish";
    public const string HeavyAnchorId = "cfg_heavy_anchor";

    public static CrestConfiguration AgileSkirmish => GameCatalog.Default.StarterConfigurations[AgileSkirmishId];
    public static CrestConfiguration HeavyAnchor => GameCatalog.Default.StarterConfigurations[HeavyAnchorId];
}
