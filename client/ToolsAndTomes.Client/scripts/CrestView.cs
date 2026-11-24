using Godot;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Client;

/// <summary>
/// Placeholder 3D presentation of one Crest. The body is assembled from one primitive per installed component,
/// shaped by its category — the Crest "emerges from its components" visually too. Real art replaces the
/// primitives per component later (see docs/ASSET_PIPELINES.md); the assembly logic stays.
/// </summary>
public partial class CrestView : Node3D
{
    private static readonly Color DisabledColor = new(0.18f, 0.18f, 0.2f);

    private Node3D _body = null!;
    private Label3D _label = null!;
    private MeshInstance3D _aegis = null!;
    private MeshInstance3D _selection = null!;
    private string _builtSignature = "";

    public Combatant Crest { get; private set; } = null!;

    public void Bind(Combatant crest)
    {
        Crest = crest;
        Name = crest.Id;

        _body = new Node3D { Name = "Body" };
        AddChild(_body);

        _label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.008f,
            FontSize = 40,
            OutlineSize = 10,
            Position = new Vector3(0, 2.6f, 0),
            NoDepthTest = true
        };
        AddChild(_label);

        _aegis = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1.4f, Height = 2.8f },
            MaterialOverride = Coords.Flat(new Color(0.5f, 1f, 0.85f), unshaded: true, alpha: 0.18f),
            Position = new Vector3(0, 1f, 0),
            Visible = false
        };
        AddChild(_aegis);

        _selection = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 1.05f, OuterRadius = 1.2f, Rings = 32 },
            MaterialOverride = Coords.Flat(new Color(1f, 0.85f, 0.2f), unshaded: true),
            Position = new Vector3(0, 0.03f, 0),
            Visible = false
        };
        AddChild(_selection);

        Rebuild();
    }

    /// <summary>Refreshes the view from the simulation (called every frame).</summary>
    public void Refresh(CombatSimulation sim, double beatFraction, bool selected)
    {
        var from = Crest.Position;
        var to = sim.ProjectEndOfBeatPosition(Crest);
        var display = new Vec2(from.X + (to.X - from.X) * beatFraction, from.Y + (to.Y - from.Y) * beatFraction);
        Position = Coords.ToWorld(display);

        FaceToward(sim, to);

        string signature = string.Join(",", Crest.Loadout.Installed.Select(e => $"{e.InstanceId}:{e.IsOperational}"));
        if (signature != _builtSignature)
            Rebuild();

        _aegis.Visible = Crest.HasActiveAegis;
        _selection.Visible = selected && !Crest.IsDefeated;
        _body.Visible = true;
        Scale = Crest.IsEvading ? new Vector3(1f, 0.85f, 1f) : Vector3.One;

        int working = Crest.Loadout.Installed.Count(e => e.IsOperational);
        string activity = Crest.IsDefeated
            ? "DEFEATED"
            : Crest.CurrentAction is { } act ? $"{act.Phase}: {act.Definition.Name}" : Crest.State.ToString();
        _label.Text = $"{Crest.Name} [{working}/{Crest.Loadout.Installed.Count}]\n{activity}";
        _label.Modulate = Crest.IsDefeated ? new Color(0.5f, 0.5f, 0.5f) : Colors.White;
    }

    private void FaceToward(CombatSimulation sim, Vec2 moveTarget)
    {
        Vec2? look = null;
        if (Crest.CurrentAction is { TargetActorId: { } id } && sim.FindCombatant(id) is { } target)
            look = target.Position;
        else if (Crest.CurrentAction?.TargetPoint is { } point)
            look = point;
        else if (moveTarget.DistanceTo(Crest.Position) > 0.05)
            look = moveTarget;

        if (look is { } l && l.DistanceTo(Crest.Position) > 0.05)
        {
            var dir = Coords.ToWorld(l) - Position;
            Rotation = Rotation with { Y = Mathf.Atan2(dir.X, dir.Z) };
        }
    }

    /// <summary>Builds the placeholder body: one primitive per component, laid out by category.</summary>
    private void Rebuild()
    {
        foreach (var child in _body.GetChildren())
            child.QueueFree();

        var faction = Coords.CrestColor(Crest);
        int weaponSide = 0, armorIndex = 0, utilityIndex = 0;

        foreach (var part in Crest.Loadout.DamageOrder)
        {
            var def = part.Definition;
            Color color = part.IsOperational ? CategoryColor(def.Category, faction) : DisabledColor;
            Mesh mesh;
            Vector3 position;

            switch (def.Category)
            {
                case ComponentCategory.Core:
                    mesh = new SphereMesh { Radius = 0.45f, Height = 0.9f };
                    position = new Vector3(0, 1.25f, 0);
                    break;

                case ComponentCategory.Mobility when def.IsLocomotion:
                    // Two legs per locomotion component.
                    AddPart(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.18f, Height = 0.9f }, new Vector3(-0.3f, 0.45f, 0), color);
                    mesh = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.18f, Height = 0.9f };
                    position = new Vector3(0.3f, 0.45f, 0);
                    break;

                case ComponentCategory.Mobility:
                    mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.12f, Height = 0.45f };
                    position = new Vector3(0, 1.3f, -0.45f);
                    break;

                case ComponentCategory.Armor:
                    mesh = new BoxMesh { Size = new Vector3(1.1f, 0.7f, 0.12f) };
                    position = new Vector3(0, 1.2f, 0.42f + 0.08f * armorIndex++);
                    break;

                case ComponentCategory.Weapon:
                {
                    float side = weaponSide++ % 2 == 0 ? 0.75f : -0.75f;
                    float length = 0.6f + Math.Min(1.2f, def.Weight / 20f);
                    mesh = new BoxMesh { Size = new Vector3(0.22f, 0.22f, length) };
                    position = new Vector3(side, 1.25f, length / 2f - 0.1f);
                    break;
                }

                case ComponentCategory.Defense:
                    mesh = new TorusMesh { InnerRadius = 0.5f, OuterRadius = 0.6f, Rings = 24 };
                    position = new Vector3(0, 1.75f, 0);
                    break;

                default: // Arcane, Utility
                {
                    float x = (utilityIndex % 3 - 1) * 0.35f;
                    float y = 1.7f + utilityIndex++ / 3 * 0.3f;
                    mesh = new BoxMesh { Size = new Vector3(0.26f, 0.26f, 0.26f) };
                    position = new Vector3(x, y, -0.3f);
                    break;
                }
            }

            AddPart(mesh, position, color);
        }

        _builtSignature = string.Join(",", Crest.Loadout.Installed.Select(e => $"{e.InstanceId}:{e.IsOperational}"));
    }

    private void AddPart(Mesh mesh, Vector3 position, Color color) =>
        _body.AddChild(new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = Coords.Flat(color) });

    private static Color CategoryColor(ComponentCategory category, Color faction) => category switch
    {
        ComponentCategory.Core => new Color(1f, 0.85f, 0.35f),
        ComponentCategory.Armor => faction.Darkened(0.35f),
        ComponentCategory.Weapon => new Color(0.75f, 0.75f, 0.8f),
        ComponentCategory.Defense => new Color(0.4f, 0.95f, 0.8f),
        ComponentCategory.Arcane => new Color(0.7f, 0.45f, 1f),
        _ => faction
    };
}
