using Godot;

namespace ToolsAndTomes.Client;

/// <summary>
/// Tactical camera: a pivot on the ground that pans (WASD / arrows / middle-drag), rotates (Q/E or right-drag with
/// Shift), and zooms (mouse wheel), with the camera on an angled boom.
/// </summary>
public partial class TacticalCamera : Node3D
{
    private const float PanSpeed = 14f;
    private const float RotateSpeed = 1.6f;
    private const float MinDistance = 8f;
    private const float MaxDistance = 40f;

    private Camera3D _camera = null!;
    private float _distance = 22f;
    private float _pitchDegrees = -55f;
    private bool _dragging;

    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _camera = new Camera3D { Fov = 50f, Current = true };
        AddChild(_camera);
        UpdateBoom();
    }

    public override void _Process(double delta)
    {
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) input.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) input.X += 1;

        if (input != Vector2.Zero)
        {
            var forward = -GlobalBasis.Z with { Y = 0 };
            var right = GlobalBasis.X with { Y = 0 };
            Position += (right.Normalized() * input.X + forward.Normalized() * -input.Y) * PanSpeed * (float)delta;
        }

        if (Input.IsKeyPressed(Key.Q)) RotateY(RotateSpeed * (float)delta);
        if (Input.IsKeyPressed(Key.E)) RotateY(-RotateSpeed * (float)delta);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _distance = Mathf.Max(MinDistance, _distance - 1.5f);
                UpdateBoom();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _distance = Mathf.Min(MaxDistance, _distance + 1.5f);
                UpdateBoom();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _dragging = mb.Pressed;
                break;
            case InputEventMouseMotion motion when _dragging:
                RotateY(-motion.Relative.X * 0.005f);
                _pitchDegrees = Mathf.Clamp(_pitchDegrees - motion.Relative.Y * 0.2f, -80f, -25f);
                UpdateBoom();
                break;
        }
    }

    /// <summary>Centers the pivot over a point on the ground.</summary>
    public void Focus(Vector3 point) => Position = point with { Y = 0 };

    private void UpdateBoom()
    {
        if (_camera is null)
            return;

        float pitch = Mathf.DegToRad(_pitchDegrees);
        _camera.Position = new Vector3(0, -Mathf.Sin(pitch) * _distance, Mathf.Cos(pitch) * _distance);
        _camera.Rotation = new Vector3(pitch, 0, 0);
    }
}
