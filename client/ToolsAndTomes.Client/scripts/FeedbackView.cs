using Godot;

namespace ToolsAndTomes.Client;

/// <summary>
/// Short-lived combat feedback in the world: floating text (damage, saves, disabled parts, interruptions) that
/// rises and fades, and expanding bursts where parts break or Crests fall. Presentation only.
/// </summary>
public partial class FeedbackView : Node3D
{
    private sealed class Floater
    {
        public required Label3D Label;
        public float Age;
        public float Life;
        public Vector3 Velocity;
    }

    private sealed class Burst
    {
        public required MeshInstance3D Mesh;
        public required StandardMaterial3D Material;
        public float Age;
        public float Life;
        public float Size;
    }

    private readonly List<Floater> _floaters = [];
    private readonly List<Burst> _bursts = [];
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>Text that pops up above a point, drifts upward, and fades.</summary>
    public void Float(Vector3 at, string text, Color color, float size = 1f, float life = 1.1f)
    {
        var label = new Label3D
        {
            Text = text,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.009f * size,
            FontSize = 48,
            OutlineSize = 12,
            Modulate = color,
            OutlineModulate = new Color(0, 0, 0, 0.85f),
            NoDepthTest = true,
            Position = at + new Vector3(_rng.RandfRange(-0.35f, 0.35f), 0, _rng.RandfRange(-0.35f, 0.35f))
        };
        AddChild(label);
        _floaters.Add(new Floater { Label = label, Life = life, Velocity = new Vector3(0, 1.6f, 0) });
    }

    /// <summary>An expanding, fading sphere: a part breaking (small) or a Crest falling (large).</summary>
    public void Pop(Vector3 at, Color color, float size, float life = 0.45f)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = color,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var mesh = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 24, Rings = 12 },
            MaterialOverride = material,
            Position = at,
            Scale = Vector3.One * 0.2f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(mesh);
        _bursts.Add(new Burst { Mesh = mesh, Material = material, Life = life, Size = size });
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            var f = _floaters[i];
            f.Age += dt;
            f.Velocity *= Mathf.Exp(-2.5f * dt);
            f.Label.Position += f.Velocity * dt;
            float t = f.Age / f.Life;
            // Pop in, hold, then fade over the last 40%.
            float scale = t < 0.12f ? Mathf.Lerp(0.6f, 1.15f, t / 0.12f) : Mathf.Lerp(1.15f, 1f, Mathf.Min(1f, (t - 0.12f) / 0.2f));
            f.Label.Scale = Vector3.One * scale;
            float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            f.Label.Modulate = f.Label.Modulate with { A = alpha };
            f.Label.OutlineModulate = f.Label.OutlineModulate with { A = alpha * 0.85f };
            if (f.Age >= f.Life)
            {
                f.Label.QueueFree();
                _floaters.RemoveAt(i);
            }
        }

        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var b = _bursts[i];
            b.Age += dt;
            float t = Mathf.Min(1f, b.Age / b.Life);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            b.Mesh.Scale = Vector3.One * Mathf.Lerp(0.2f, b.Size, eased);
            b.Material.AlbedoColor = b.Material.AlbedoColor with { A = 0.7f * (1f - t) };
            if (b.Age >= b.Life)
            {
                b.Mesh.QueueFree();
                _bursts.RemoveAt(i);
            }
        }
    }
}
