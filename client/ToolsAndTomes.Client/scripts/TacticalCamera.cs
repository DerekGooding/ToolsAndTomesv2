using Godot;

namespace ToolsAndTomes.Client;

/// <summary>
/// The battle camera: a mid-distance third-person boom that follows the action. By default it frames the
/// player's Crest, leaning toward the current target, and eases there smoothly. Panning (WASD) takes manual
/// control; F (or Home) returns to following. Q/E rotate, the wheel zooms, middle-drag orbits and tilts.
/// <see cref="Shake"/> adds trauma-based screen shake (presentation only).
/// </summary>
public partial class TacticalCamera : Node3D
{
    private const float PanSpeed = 12f;
    private const float RotateSpeed = 1.6f;
    private const float MinDistance = 7f;
    private const float MaxDistance = 34f;
    private const float FollowSharpness = 3.5f;   // higher = snappier follow
    private const float ZoomSharpness = 8f;
    private const float MaxShakeOffset = 0.45f;   // world units at full trauma
    private const float MaxShakeRoll = 0.035f;    // radians at full trauma
    private const float TraumaDecay = 1.6f;       // per second

    private Camera3D _camera = null!;
    private float _distance = 12.5f;
    private float _targetDistance = 12.5f;
    private float _pitchDegrees = -34f;
    private bool _dragging;
    private Vector3 _focus;
    private float _trauma;
    private float _time;
    private readonly FastNoiseLite _noise = new() { Frequency = 2.2f, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin };

    public Camera3D Camera => _camera;

    /// <summary>When true, the camera eases toward <see cref="FollowPoint"/> every frame.</summary>
    public bool Following { get; set; } = true;

    /// <summary>Where the camera wants to look while following (set by the battle each frame).</summary>
    public Vector3 FollowPoint { get; set; }

    public override void _Ready()
    {
        _camera = new Camera3D { Fov = 55f, Current = true };
        AddChild(_camera);
        UpdateBoom(0);
    }

    /// <summary>Places the camera at once (no easing), looking along <paramref name="facing"/> from behind <paramref name="point"/>.</summary>
    public void Snap(Vector3 point, Vector3 facing)
    {
        _focus = point with { Y = 0 };
        FollowPoint = _focus;
        Position = _focus;
        var flat = facing with { Y = 0 };
        if (flat.LengthSquared() > 0.001f)
            Rotation = Rotation with { Y = Mathf.Atan2(-flat.X, -flat.Z) };
        Following = true;
    }

    /// <summary>Adds screen shake (0–1; it accumulates and decays).</summary>
    public void Shake(float amount) => _trauma = Mathf.Min(1f, _trauma + amount);

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;

        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) input.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) input.X += 1;

        if (input != Vector2.Zero)
        {
            Following = false;
            var forward = -GlobalBasis.Z with { Y = 0 };
            var right = GlobalBasis.X with { Y = 0 };
            _focus += (right.Normalized() * input.X + forward.Normalized() * -input.Y) * PanSpeed * dt;
        }
        else if (Following)
        {
            float t = 1f - Mathf.Exp(-FollowSharpness * dt);
            _focus = _focus.Lerp(FollowPoint with { Y = 0 }, t);
        }

        if (Input.IsKeyPressed(Key.Q)) RotateY(RotateSpeed * dt);
        if (Input.IsKeyPressed(Key.E)) RotateY(-RotateSpeed * dt);

        _distance = Mathf.Lerp(_distance, _targetDistance, 1f - Mathf.Exp(-ZoomSharpness * dt));
        _trauma = Mathf.Max(0f, _trauma - TraumaDecay * dt);

        // Shake grows with the square of trauma, so small hits barely register and big ones land hard.
        float shake = _trauma * _trauma;
        var jitter = new Vector3(
            _noise.GetNoise2D(_time * 40f, 0f),
            _noise.GetNoise2D(0f, _time * 40f),
            _noise.GetNoise2D(_time * 40f, 100f)) * MaxShakeOffset * shake;
        Position = _focus + jitter;
        UpdateBoom(_noise.GetNoise2D(_time * 30f, 200f) * MaxShakeRoll * shake);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.F or Key.Home }:
                Following = true;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _targetDistance = Mathf.Max(MinDistance, _targetDistance - 1.5f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _targetDistance = Mathf.Min(MaxDistance, _targetDistance + 1.5f);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _dragging = mb.Pressed;
                break;
            case InputEventMouseMotion motion when _dragging:
                RotateY(-motion.Relative.X * 0.005f);
                _pitchDegrees = Mathf.Clamp(_pitchDegrees - motion.Relative.Y * 0.2f, -80f, -15f);
                break;
        }
    }

    private void UpdateBoom(float roll)
    {
        if (_camera is null)
            return;

        float pitch = Mathf.DegToRad(_pitchDegrees);
        // Aim a little above the ground so Crests (about 2 units tall) sit in the middle of the frame.
        _camera.Position = new Vector3(0, -Mathf.Sin(pitch) * _distance + 1.2f, Mathf.Cos(pitch) * _distance);
        _camera.Rotation = new Vector3(pitch, 0, roll);
    }
}
