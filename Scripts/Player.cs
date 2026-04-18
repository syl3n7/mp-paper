using Godot;

public partial class Player : CharacterBody2D
{
    [Export] public float Speed         { get; set; } = 200f;

    // ── Bot AI tunables ──────────────────────────────────────────────────────
    [Export] public float WanderRadius   { get; set; } = 400f;  // max distance from spawn
    [Export] public float WaypointReach  { get; set; } = 24f;   // distance to consider waypoint reached
    [Export] public float AvoidRadius    { get; set; } = 64f;   // separation trigger distance
    [Export] public float AvoidStrength  { get; set; } = 1.8f;  // how hard to push away

    private bool    _isBotMode;
    private Vector2 _spawnOrigin;
    private Vector2 _wanderTarget;
    private float   _waypointTimer;     // force new waypoint after timeout

    public override void _Ready()
    {
        GD.Print($"[Player] {Name} ready — authority={GetMultiplayerAuthority()}, isAuthority={IsMultiplayerAuthority()}");

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--bot") { _isBotMode = true; break; }
        }

        _spawnOrigin   = GlobalPosition;
        _wanderTarget  = GlobalPosition;
        _waypointTimer = 0f;    // triggers immediate waypoint pick on first frame
    }

    public override void _PhysicsProcess(double delta)
    {
        // Only the owning peer drives this player.
        if (!IsMultiplayerAuthority())
            return;

        Vector2 input;

        if (_isBotMode)
        {
            input = GetBotInput((float)delta);
        }
        else
        {
            // Ignore input when this window is not in the foreground — prevents
            // all instances from moving at once when windows overlap.
            if (!GetViewport().GetWindow().HasFocus())
                return;

            input = Vector2.Zero;
            if (Input.IsActionPressed("RIGHT")) input.X += 1;
            if (Input.IsActionPressed("LEFT"))  input.X -= 1;
            if (Input.IsActionPressed("DOWN"))  input.Y += 1;
            if (Input.IsActionPressed("UP"))    input.Y -= 1;
        }

        Velocity = input.Normalized() * Speed;
        MoveAndSlide();

        // Broadcast position to all other peers so they update their puppet copy.
        if (Multiplayer.MultiplayerPeer != null)
            Rpc(nameof(SyncPosition), GlobalPosition);
    }

    // ── Bot AI ───────────────────────────────────────────────────────────────

    private Vector2 GetBotInput(float delta)
    {
        _waypointTimer -= delta;

        // Pick a new waypoint when close enough or the timer runs out.
        if (GlobalPosition.DistanceTo(_wanderTarget) < WaypointReach || _waypointTimer <= 0f)
        {
            _wanderTarget  = PickWanderTarget();
            _waypointTimer = (float)GD.RandRange(3.0, 8.0);
        }

        Vector2 dir = (_wanderTarget - GlobalPosition).Normalized();

        // Add separation so bots (and players) push each other apart.
        dir += GetSeparation();

        return dir;     // normalized in _PhysicsProcess
    }

    private Vector2 GetSeparation()
    {
        Vector2 sep    = Vector2.Zero;
        var     parent = GetParent();
        if (parent == null) return sep;

        foreach (Node sibling in parent.GetChildren())
        {
            if (sibling == this) continue;
            if (sibling is not CharacterBody2D other) continue;

            float dist = GlobalPosition.DistanceTo(other.GlobalPosition);
            if (dist < AvoidRadius && dist > 0.01f)
            {
                float weight = (AvoidRadius - dist) / AvoidRadius;
                sep += (GlobalPosition - other.GlobalPosition).Normalized() * weight * AvoidStrength;
            }
        }

        return sep;
    }

    private Vector2 PickWanderTarget()
    {
        float angle  = (float)GD.RandRange(0.0, Mathf.Tau);
        float radius = (float)GD.RandRange(WanderRadius * 0.2, WanderRadius);
        return _spawnOrigin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    // ── Network ──────────────────────────────────────────────────────────────

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void SyncPosition(Vector2 pos)
    {
        if (IsMultiplayerAuthority()) return;
        GlobalPosition = pos;
    }
}

