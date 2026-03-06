using Godot;

public partial class Player : CharacterBody2D
{
    [Export] public float Speed { get; set; } = 200f;

    public override void _Ready()
    {
        GD.Print($"[Player] {Name} ready — authority={GetMultiplayerAuthority()}, isAuthority={IsMultiplayerAuthority()}");
    }

    public override void _PhysicsProcess(double delta)
    {
        // Only the owning peer moves this player.
        if (!IsMultiplayerAuthority())
            return;

        // Ignore input when this window is not in the foreground — prevents
        // all instances from moving at once when windows overlap.
        if (!GetViewport().GetWindow().HasFocus())
            return;

        Vector2 input = Vector2.Zero;
        if (Input.IsActionPressed("RIGHT")) input.X += 1;
        if (Input.IsActionPressed("LEFT"))  input.X -= 1;
        if (Input.IsActionPressed("DOWN"))  input.Y += 1;
        if (Input.IsActionPressed("UP"))    input.Y -= 1;
        input = input.Normalized();

        Velocity = input * Speed;
        MoveAndSlide();

        // Broadcast position to all other peers so they update their puppet copy.
        if (Multiplayer.MultiplayerPeer != null)
            Rpc(nameof(SyncPosition), GlobalPosition);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void SyncPosition(Vector2 pos)
    {
        if (IsMultiplayerAuthority()) return;
        GlobalPosition = pos;
    }
}

