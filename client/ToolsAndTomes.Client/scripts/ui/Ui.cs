using Godot;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Client.UI;

/// <summary>Small helpers for building the placeholder menu screens in code, with one consistent look.</summary>
public static class Ui
{
    public static readonly Color Background = new(0.06f, 0.08f, 0.12f);
    public static readonly Color Panel = new(0.11f, 0.14f, 0.2f);
    public static readonly Color Ink = new(0.88f, 0.9f, 0.94f);
    public static readonly Color Muted = new(0.6f, 0.65f, 0.73f);
    public static readonly Color Accent = new(1f, 0.78f, 0.3f);
    public static readonly Color Good = new(0.45f, 0.9f, 0.6f);
    public static readonly Color Bad = new(1f, 0.45f, 0.4f);
    public static readonly Color Ally = new(0.2f, 0.85f, 0.65f);

    /// <summary>A full-screen screen root: a dark backdrop with a padded column inside.</summary>
    public static (Control Root, VBoxContainer Column) Screen()
    {
        var root = new Control();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var backdrop = new ColorRect { Color = Background, MouseFilter = Control.MouseFilterEnum.Ignore };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(backdrop);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 28);
        root.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);
        return (root, column);
    }

    public static Label Title(string text)
    {
        var label = new Label { Text = text, Modulate = Accent };
        label.AddThemeFontSizeOverride("font_size", 30);
        return label;
    }

    public static Label Heading(string text, Color? color = null)
    {
        var label = new Label { Text = text, Modulate = color ?? Ink };
        label.AddThemeFontSizeOverride("font_size", 19);
        return label;
    }

    public static Label Text(string text, Color? color = null, int size = 15)
    {
        var label = new Label { Text = text, Modulate = color ?? Ink, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    public static Button Button(string text, Action onPressed, bool primary = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38) };
        button.AddThemeFontSizeOverride("font_size", primary ? 18 : 15);
        if (primary)
            button.Modulate = Accent;
        button.Pressed += onPressed;
        return button;
    }

    /// <summary>A rounded panel holding a padded column.</summary>
    public static (PanelContainer Panel, VBoxContainer Column) Card(float minWidth = 0)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(minWidth, 0) };
        var style = new StyleBoxFlat { BgColor = Panel, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 };
        style.SetContentMarginAll(14);
        panel.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);
        return (panel, column);
    }

    /// <summary>A column that scrolls vertically and fills the space it is given.</summary>
    public static VBoxContainer Scrolling(Container parent)
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        parent.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(column);
        return column;
    }

    public static void Clear(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>One line of a Crest's key stats.</summary>
    public static string StatsLine(CrestLoadout loadout, RulesConfig rules)
    {
        var s = LoadoutRules.ComputeStats(loadout, rules);
        return $"Wit {s.MaxWit}  ·  Motes {s.MaxMotes} (+1 / {s.MoteGenerationIntervalBeats}s)  ·  Speed {s.MoveSpeed}  ·  " +
               $"Heat {s.HeatCapacity}  ·  Cargo {s.CargoUsed}/{s.CargoCapacity}  ·  Weight {s.TotalWeight}";
    }

    /// <summary>One component, as listed in Damage Order (durability, ammo, disabled, and where it's held).</summary>
    public static string ComponentLine(EquipmentInstance part, int index, CrestLoadout? loadout = null)
    {
        var d = part.Definition;
        string hp = part.IsDisabled ? "DISABLED" : $"{part.CurrentDurability}/{d.MaxDurability}";
        string ammo = d.MaxAmmo > 0 ? $"  ·  ammo {part.CurrentAmmo}/{d.MaxAmmo}" : "";
        string hold = loadout is null ? "" : HoldTag(loadout, part);
        return $"{index}. {d.Name}  ({hp}){ammo}{(hold.Length > 0 ? $"  ·  {hold}" : "")}";
    }

    /// <summary>Where a handheld is: "in hand", "held by Articulated Arm", "stowed", "NO FREE HAND", or "dropped" (broken).</summary>
    public static string HoldTag(CrestLoadout loadout, EquipmentInstance part) => loadout.HoldStateOf(part) switch
    {
        HoldState.OwnHand => "✋ in hand",
        HoldState.Limb => $"✋ held by {loadout.HolderOf(part)?.Definition.Name}",
        HoldState.Stowed => $"stowed in {loadout.HolderOf(part)?.Definition.Name}",
        HoldState.Unheld when part.IsDisabled => "dropped (broken)",
        HoldState.Unheld => "⚠ NO FREE HAND (inactive)",
        _ => ""
    };

    /// <summary>Hands, tomes held, and exposure to Electrical and Disjunction damage.</summary>
    public static string MagicLine(CrestLoadout loadout, RulesConfig rules)
    {
        var s = LoadoutRules.ComputeStats(loadout, rules);
        int held = loadout.Installed.Count(e => loadout.HoldStateOf(e) is HoldState.OwnHand or HoldState.Limb);
        int electrical = Math.Min(rules.ElectricalMaxBonus, s.TomesHeld * rules.ElectricalBonusPerTome);
        int disjunction = rules.DisjunctionWitTaxPerBonus <= 0 ? 0 : s.EquipmentWitTax / rules.DisjunctionWitTaxPerBonus;
        return $"Hands {held}/{s.Hands}  ·  Tomes held {s.TomesHeld}" +
               (electrical > 0 ? $" (electrical +{electrical})" : "") +
               $"  ·  Wit tax {s.EquipmentWitTax}" + (disjunction > 0 ? $" (disjunction +{disjunction})" : "");
    }

    public static string Materials(GameCatalog catalog, IEnumerable<MaterialCount> counts) =>
        string.Join(", ", counts
            .GroupBy(c => c.MaterialId)
            .Select(g => $"{catalog.Materials[g.Key].Name} ×{g.Sum(c => c.Count)}"));
}
