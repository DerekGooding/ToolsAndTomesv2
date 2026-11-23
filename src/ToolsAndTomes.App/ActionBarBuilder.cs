using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.App;

public static class ActionBarBuilder
{
    public static void Populate(
        WrapPanel panel,
        CombatSimulation sim,
        Combatant player,
        Combatant? selectedTarget,
        ActionDefinition? armedPointAction,
        Action<ActionDefinition, Combatant?> onActionClick,
        Action onCancelClick)
    {
        panel.Children.Clear();

        foreach (var actionId in player.Loadout.GetOperationalActionIds().OrderBy(id => id))
        {
            var def = sim.Catalog.GetAction(actionId);
            int cd = player.GetCooldown(actionId);
            var targetForValidation = def.Targeting == TargetingType.EnemyUnit ? selectedTarget : null;
            Vec2? pointForValidation = def.Targeting == TargetingType.Point ? player.Position : null;
            var check = ActionValidationRules.ValidateUseAction(player, def, targetForValidation, pointForValidation);

            string cost = "";
            if (def.WitReserve > 0) cost += $" W:{def.WitReserve}";
            if (def.MoteCost > 0) cost += $" M:{def.MoteCost}";
            if (def.AmmoCost > 0) cost += $" A:{def.AmmoCost}";
            string cdText = cd > 0 ? $" ({cd}s)" : "";

            var btn = new Button
            {
                Content = $"{def.Name}{cost}{cdText}",
                ToolTip = $"{def.Description}\nRange: {def.Range} | Windup/Impact/Recovery: {def.WindupBeats}/{def.ImpactBeats}/{def.RecoveryBeats}s | Cooldown: {def.CooldownBeats}s | IntPower: {def.InterruptionPower}",
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 5, 10, 5),
                IsEnabled = check.IsValid,
                Background = armedPointAction?.Id == def.Id
                    ? new SolidColorBrush(Color.FromRgb(234, 179, 8))
                    : def.Category switch
                    {
                        ActionCategory.Defense => new SolidColorBrush(Color.FromRgb(15, 118, 110)),
                        ActionCategory.Spell => new SolidColorBrush(Color.FromRgb(109, 40, 217)),
                        _ => new SolidColorBrush(Color.FromRgb(30, 64, 175))
                    },
                Foreground = Brushes.White
            };

            btn.Click += (_, _) => onActionClick(def, selectedTarget);
            panel.Children.Add(btn);
        }

        var cancelBtn = new Button
        {
            Content = "Cancel Action",
            Margin = new Thickness(8, 0, 0, 6),
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromRgb(127, 29, 29)),
            Foreground = Brushes.White
        };
        cancelBtn.Click += (_, _) => onCancelClick();
        panel.Children.Add(cancelBtn);
    }
}
