using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.Rules;

/// <summary>
/// One thing the player can shape from fast-iron or use from cargo, as a ready command.
/// Shared by every frontend's combat screen (issued as a command) and rest screen (performed directly).
/// </summary>
public sealed record FieldOption(string Label, UseActionCommand Command)
{
    public override string ToString() => Label;

    /// <summary>Everything the Crest can shape from fast-iron or use from cargo right now (and, mid-fight, harness swaps).</summary>
    /// <param name="includeSwaps">Include "swap the stowed item into this hand" options (fights only; rests can't swap).</param>
    public static List<FieldOption> Build(GameCatalog catalog, Combatant player, bool includeSwaps = false)
    {
        var options = new List<FieldOption>();
        if (includeSwaps)
        {
            foreach (var harness in player.Loadout.Installed.Where(e => e.Definition.StowSlots > 0 && player.Loadout.IsActive(e)))
            {
                if (player.Loadout.StowedIn(harness) is not { } stowed)
                    continue;
                var swap = harness.Definition.GrantedActionIds.Select(catalog.GetAction).FirstOrDefault(a => a.IsSwap);
                if (swap is null)
                    continue;
                foreach (var held in player.Loadout.Installed.Where(e => player.Loadout.HoldStateOf(e) is HoldState.OwnHand or HoldState.Limb))
                    options.Add(new FieldOption(
                        $"Swap {held.Definition.Name} ↔ {stowed.Definition.Name} ({swap.WindupBeats} beats)",
                        new UseActionCommand(player.Id, swap.Id, OwnComponentInstanceId: held.InstanceId)));
            }
        }
        var available = player.Loadout.GetOperationalActionIds().ToHashSet();
        var ammoWeapons = player.Loadout.Installed
            .Where(e => e.IsOperational && e.Definition.MaxAmmo > 0 && e.CurrentAmmo < e.Definition.MaxAmmo)
            .ToList();

        var shapeSpell = available.Select(catalog.GetAction).FirstOrDefault(a => a.IsShaping);
        if (shapeSpell is not null)
        {
            foreach (var w in ammoWeapons)
                options.Add(new FieldOption(
                    $"Shape ammo → {w.Definition.Name} ({w.Definition.AmmoRefillFastIronCost} fast-iron)",
                    new UseActionCommand(player.Id, shapeSpell.Id, OwnComponentInstanceId: w.InstanceId,
                        Shaping: new ShapingOrder(ShapingKind.AmmoRefill))));

            foreach (var c in catalog.Consumables.Values.OrderBy(c => c.Name))
                options.Add(new FieldOption(
                    $"Shape {c.Name} into cargo ({c.FastIronCost} fast-iron)",
                    new UseActionCommand(player.Id, shapeSpell.Id, Shaping: new ShapingOrder(ShapingKind.Consumable, c.Id))));

            var known = player.KnownBlueprints ?? catalog.Equipment.Keys.ToHashSet();
            foreach (var e in catalog.Equipment.Values.Where(e => known.Contains(e.Id)).OrderBy(e => e.Category).ThenBy(e => e.Name))
                options.Add(new FieldOption(
                    $"Shape {e.Name} ({ShapingRules.ComponentCost(e, player.Rules)} fast-iron) — joins end of Damage Order",
                    new UseActionCommand(player.Id, shapeSpell.Id, Shaping: new ShapingOrder(ShapingKind.Component, e.Id))));
        }

        foreach (var refill in available.Select(catalog.GetAction).Where(a => a.RefillsAmmo && a.ConsumableItemId is not null))
        {
            int count = player.Loadout.GetConsumableCount(refill.ConsumableItemId!);
            if (count == 0)
                continue;
            foreach (var w in ammoWeapons)
                options.Add(new FieldOption(
                    $"{refill.Name} → {w.Definition.Name} ({count} carried)",
                    new UseActionCommand(player.Id, refill.Id, OwnComponentInstanceId: w.InstanceId)));
        }

        return options;
    }
}
