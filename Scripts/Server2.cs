#if USE_ALTNET
using Godot;
using System.Collections.Generic;

/// <summary>
/// Alternative multiplayer backend.
/// Replace the body of this class with your own transport logic.
/// This file compiles only when built with: dotnet build -c AltNet
/// </summary>
public partial class Server : Node
{
	// Mirror the exports from the ENet version so scene properties still work.
	[Export] public int Port       { get; set; } = 7777;
	[Export] public int MaxPlayers { get; set; } = 32;

	private readonly List<long> _connectedPlayers = new();

	public override void _Ready()
	{
		GD.Print("[Server/AltNet] _Ready() called");
		// TODO: initialise your transport here instead of ENet.
		StartServer();
	}

	private void StartServer()
	{
		GD.Print("[Server/AltNet] StartServer() — replace with your backend startup");
		// TODO: create your server peer and assign it to Multiplayer.MultiplayerPeer.

		// Spawn the host's own player (same convention as the ENet version).
		SpawnPlayerLocally(1);
		_connectedPlayers.Add(1);
	}

	// Called by your transport layer when a remote peer connects.
	protected void OnPeerConnected(long id)
	{
		GD.Print($"[Server/AltNet] Peer {id} connected");

		foreach (long existingId in _connectedPlayers)
			RpcId(id, nameof(SpawnPlayerRpc), existingId);

		SpawnPlayerLocally(id);
		_connectedPlayers.Add(id);
		Rpc(nameof(SpawnPlayerRpc), id);
	}

	// Called by your transport layer when a remote peer disconnects.
	protected void OnPeerDisconnected(long id)
	{
		GD.Print($"[Server/AltNet] Peer {id} disconnected");
		_connectedPlayers.Remove(id);
		GetNodeOrNull($"Player_{id}")?.QueueFree();
		Rpc(nameof(DespawnPlayerRpc), id);
	}

	private void SpawnPlayerLocally(long clientId)
	{
		var playerScene = ResourceLoader.Load<PackedScene>("res://Prefabs/p_player.tscn");
		if (playerScene == null) { GD.PrintErr("[Server/AltNet] Could not load p_player.tscn!"); return; }

		var instance = playerScene.Instantiate();
		instance.Name = $"Player_{clientId}";
		AddChild(instance);
		instance.SetMultiplayerAuthority((int)clientId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void SpawnPlayerRpc(long clientId) => SpawnPlayerLocally(clientId);

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void DespawnPlayerRpc(long clientId) => GetNodeOrNull($"Player_{clientId}")?.QueueFree();
}
#endif // USE_ALTNET
