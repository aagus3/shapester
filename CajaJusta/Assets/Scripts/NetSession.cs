using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Online layer built on Unity Gaming Services:
///  - Authentication (anonymous sign-in)
///  - Multiplayer Sessions (rooms you can list/create/join = the lobby)
///  - Relay (players connect through Unity's servers, no own server needed)
///  - Netcode for GameObjects custom messages (JSON strings) for the game protocol.
/// The room creator is the "host": it runs the match logic (timer, maps, scores).
/// </summary>
public class NetSession
{
    public const int MaxPlayers = 6;
    const string MsgName = "TB";

    public ISession Session { get; private set; }
    public bool InSession => Session != null;
    public bool IsHost => nm != null && nm.IsServer;
    public ulong LocalClientId => nm != null ? nm.LocalClientId : 0;

    /// <summary>Host only: a client sent a message to the host (sender, json).</summary>
    public event Action<ulong, string> MessageToHost;
    /// <summary>Every player (host included): the host sent a message (json).</summary>
    public event Action<string> MessageFromHost;
    /// <summary>Host only: a client left.</summary>
    public event Action<ulong> ClientLeft;
    /// <summary>We were disconnected / the room was closed (reason).</summary>
    public event Action<string> SessionLost;

    bool initialized, leaving;
    NetworkManager nm;

    public async Task InitAsync()
    {
        if (initialized) return;
        // a random profile = a different anonymous player per app launch
        // (lets you open two tabs on the same computer and play against yourself)
        var options = new InitializationOptions().SetProfile("p" + UnityEngine.Random.Range(0, 99999999));
        await UnityServices.InitializeAsync(options);
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        CreateNetworkManager();
        initialized = true;
    }

    void CreateNetworkManager()
    {
        var go = new GameObject("NetworkManager");
        go.SetActive(false);                       // configure before Awake runs
        Object.DontDestroyOnLoad(go);
        var transport = go.AddComponent<UnityTransport>();
        transport.UseWebSockets = true;            // Relay over WSS (required on web, works on phones too)
        nm = go.AddComponent<NetworkManager>();
        nm.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false,         // we don't sync scenes, only messages
        };
        go.SetActive(true);

        nm.OnClientDisconnectCallback += id =>
        {
            if (nm.IsServer) { if (id != nm.LocalClientId) ClientLeft?.Invoke(id); }
            else Lost("HOST LEFT");
        };
        nm.OnClientStopped += _ => Lost("DISCONNECTED");
    }

    // ------------------------------------------------------------------ rooms (lobby)

    public async Task<List<ISessionInfo>> ListRoomsAsync()
    {
        var res = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions { Count = 20 });
        var list = new List<ISessionInfo>();
        foreach (var s in res.Sessions)
            if (!s.IsLocked && s.AvailableSlots > 0) list.Add(s);
        return list;
    }

    static NetworkOptions Wss => new NetworkOptions { RelayProtocol = RelayProtocol.WSS }; // works on web AND phone

    public async Task CreateRoomAsync(string roomName)
    {
        var options = new SessionOptions { MaxPlayers = MaxPlayers, Name = roomName }
            .WithRelayNetwork()
            .WithNetworkOptions(Wss);
        Session = await MultiplayerService.Instance.CreateSessionAsync(options);
        Hook();
    }

    public async Task JoinRoomAsync(string sessionId)
    {
        var options = new JoinSessionOptions().WithNetworkOptions(Wss);
        Session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, options);
        Hook();
    }

    void Hook()
    {
        Session.RemovedFromSession += () => Lost("REMOVED FROM ROOM");
        Session.Deleted += () => Lost("ROOM CLOSED");
        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgName, OnNamedMessage);
    }

    /// <summary>Host: hide the room from the list while a match is running.</summary>
    public async void SetLocked(bool locked)
    {
        if (Session == null || !Session.IsHost) return;
        try
        {
            var host = Session.AsHost();
            host.IsLocked = locked;
            await host.SavePropertiesAsync();
        }
        catch (Exception e) { Debug.LogWarning("SetLocked: " + e.Message); }
    }

    public async Task LeaveAsync()
    {
        if (Session == null) return;
        leaving = true;
        var s = Session;
        Session = null;
        try
        {
            if (s.IsHost) await s.AsHost().DeleteAsync();
            else await s.LeaveAsync();
        }
        catch (Exception e) { Debug.LogWarning("Leave: " + e.Message); }
        StopNetwork();
        leaving = false;
    }

    void StopNetwork()
    {
        if (nm == null) return;
        if (nm.CustomMessagingManager != null) nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgName);
        if (nm.IsListening) nm.Shutdown();
    }

    void Lost(string reason)
    {
        if (leaving || Session == null) return;
        Session = null;
        leaving = true;           // avoid re-entering from the shutdown callbacks
        StopNetwork();
        leaving = false;
        SessionLost?.Invoke(reason);
    }

    // ------------------------------------------------------------------ messages

    void OnNamedMessage(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out string json);
        if (nm.IsServer) MessageToHost?.Invoke(sender, json);
        else MessageFromHost?.Invoke(json);
    }

    /// <summary>Client → host (on the host it is delivered locally).</summary>
    public void SendToHost(string json)
    {
        if (nm == null || !nm.IsListening) return;
        if (nm.IsServer) { MessageToHost?.Invoke(nm.LocalClientId, json); return; }
        Send(NetworkManager.ServerClientId, json);
    }

    /// <summary>Host → everyone (including the host itself, delivered locally).</summary>
    public void Broadcast(string json)
    {
        if (nm == null || !nm.IsServer) return;
        foreach (var id in nm.ConnectedClientsIds)
            if (id != nm.LocalClientId) Send(id, json);
        MessageFromHost?.Invoke(json);
    }

    void Send(ulong clientId, string json)
    {
        using var writer = new FastBufferWriter(256, Allocator.Temp, 65536);
        writer.WriteValueSafe(json);
        nm.CustomMessagingManager.SendNamedMessage(MsgName, clientId, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }
}
