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

    // Hit feedback (presentation only). Flashes are keyed by component instance id ("" = the whole Crest),
    // so they survive the rebuild that follows a part being disabled.
    private const float FlashSeconds = 0.22f;
    private readonly Dictionary<string, List<StandardMaterial3D>> _partMaterials = [];
    private readonly Dictionary<string, (float Time, Color Color)> _flashes = [];
    private float _recoil;
    private float _defeat;

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
            PixelSize = 0.0055f,
            FontSize = 40,
            OutlineSize = 12,
            Position = new Vector3(0, 2.5f, 0),
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

        _aegis.Visible = Crest.HasActiveAegis || _flashes.ContainsKey(AegisKey);
        _selection.Visible = selected && !Crest.IsDefeated;
        _body.Visible = true;
        Scale = Crest.IsEvading ? new Vector3(1f, 0.85f, 1f) : Vector3.One;

        // Compact: the name and working parts; a second line only while an action is under way.
        int working = Crest.Loadout.Installed.Count(e => e.IsOperational);
        string name = Crest.Id == EncounterFactory.PlayerCombatantId ? "You" : Crest.Name;
        string? activity = Crest.IsDefeated
            ? "DEFEATED"
            : Crest.CurrentAction is { Phase: not ActionState.Moving } act ? $"{act.Phase}: {act.Definition.Name}" : null;
        _label.Text = $"{name} {working}/{Crest.Loadout.Installed.Count}" + (activity is null ? "" : $"\n{activity}");
        _label.Modulate = Crest.IsDefeated ? new Color(0.5f, 0.5f, 0.5f) : Colors.White;
    }

    private const string AegisKey = "aegis";

    /// <summary>A part (or, with no id, the whole Crest) was struck: flash it and jolt the body back.</summary>
    public void Hit(string? componentInstanceId, Color color, float recoil)
    {
        _flashes[componentInstanceId ?? ""] = (FlashSeconds, color);
        _recoil = Mathf.Max(_recoil, recoil);
    }

    /// <summary>The Aegis absorbed a hit: flash the barrier.</summary>
    public void AegisFlash() => _flashes[AegisKey] = (FlashSeconds * 1.5f, new Color(0.5f, 1f, 0.85f));

    public override void _Process(double delta)
    {
        if (Crest is null)
            return;
        float dt = (float)delta;

        foreach (var key in _flashes.Keys.ToList())
        {
            var (time, color) = _flashes[key];
            time -= dt;
            if (time <= 0)
                _flashes.Remove(key);
            else
                _flashes[key] = (time, color);
        }

        foreach (var (key, materials) in _partMaterials)
        {
            float energy = 0;
            Color color = Colors.White;
            if (_flashes.TryGetValue(key, out var own)) { energy = own.Time / FlashSeconds; color = own.Color; }
            else if (_flashes.TryGetValue("", out var all)) { energy = all.Time / FlashSeconds * 0.6f; color = all.Color; }
            foreach (var material in materials)
            {
                material.EmissionEnabled = energy > 0;
                material.Emission = color;
                material.EmissionEnergyMultiplier = energy * 3f;
            }
        }

        if (_aegis.MaterialOverride is StandardMaterial3D shield)
            shield.AlbedoColor = shield.AlbedoColor with { A = _flashes.TryGetValue(AegisKey, out var a) ? 0.18f + a.Time * 1.6f : 0.18f };

        // Recoil: the body jolts backward and springs back.
        _recoil = Mathf.Max(0f, _recoil - dt * 4f);
        // Defeat: the Crest slumps forward and sinks over about half a second.
        if (Crest.IsDefeated)
            _defeat = Mathf.Min(1f, _defeat + dt * 2f);
        else
            _defeat = 0f;
        float ease = 1f - (1f - _defeat) * (1f - _defeat);
        _body.Position = new Vector3(0, -0.45f * ease, -0.35f * _recoil * _recoil);
        _body.Rotation = new Vector3(Mathf.DegToRad(28f) * ease, 0, Mathf.DegToRad(-12f) * ease);
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
        _partMaterials.Clear();

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
                    AddPart(new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.18f, Height = 0.9f }, new Vector3(-0.3f, 0.45f, 0), color, part.InstanceId);
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

            AddPart(mesh, position, color, part.InstanceId);
        }

        _builtSignature = string.Join(",", Crest.Loadout.Installed.Select(e => $"{e.InstanceId}:{e.IsOperational}"));
    }

    private void AddPart(Mesh mesh, Vector3 position, Color color, string instanceId)
    {
        var material = Coords.Flat(color);
        _body.AddChild(new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material });
        if (!_partMaterials.TryGetValue(instanceId, out var list))
            _partMaterials[instanceId] = list = [];
        list.Add(material);
    }

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
