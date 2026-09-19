using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using TouchControl.Game;
using TouchControl.Input;

namespace TouchControl.Bridge;

/// <summary>
/// Localhost TCP server the Android companion app talks to. The phone owns the touch layer (native multi-touch);
/// this side owns everything that needs the game: executing hotbar slots, holding movement keys, rotating the
/// camera, and streaming hotbar icons + cooldowns back so the buttons on the phone look like the real thing.
///
/// Sockets are serviced on thread-pool tasks; every message is queued and handled on the framework thread in
/// <see cref="Tick"/>, so game calls never happen off the main thread.
/// </summary>
public sealed class BridgeServer : IDisposable
{
    private sealed class Client
    {
        public required TcpClient Tcp;
        public required Channel<string> Outgoing;
        public readonly HashSet<int> HeldKeys = [];
        public List<int> Bars = [0, 1];
        public List<uint> GaIds = [];
        public bool SaidHello;
        public string Name = "?";
        public bool MovementOwner;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly GameActions game;
    private readonly KeySender keys;
    private readonly SheetCache sheets;
    private readonly MovementController movement;

    private readonly ConcurrentQueue<(Client Client, ClientMessage Msg)> inbox = new();
    private readonly List<Client> clients = [];
    private readonly object clientsGate = new();
    private readonly ConcurrentDictionary<uint, string?> iconCache = new();

    private TcpListener? listener;
    private CancellationTokenSource? cts;
    private DateTime lastMove = DateTime.MinValue;
    private DateTime lastState = DateTime.MinValue;
    private bool moving;
    private Guid? pngCodec;

    public bool Running => listener != null;
    public string? LastError { get; private set; }

    public int ClientCount
    {
        get
        {
            lock (clientsGate) return clients.Count;
        }
    }

    public BridgeServer(GameActions game, KeySender keys, SheetCache sheets)
    {
        this.game = game;
        this.keys = keys;
        this.sheets = sheets;
        movement = new MovementController(keys);
    }

    // ---- lifecycle ------------------------------------------------------------------------------------

    public void Start(int port, bool allowRemote)
    {
        Stop();
        try
        {
            listener = new TcpListener(allowRemote ? IPAddress.Any : IPAddress.Loopback, port);
            listener.Start();
            cts = new CancellationTokenSource();
            _ = AcceptLoop(listener, cts.Token);
            LastError = null;
            Plugin.Log.Information("Bridge listening on {Addr}:{Port}", allowRemote ? "0.0.0.0" : "127.0.0.1", port);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            listener = null;
            Plugin.Log.Error(ex, "Bridge failed to start on port {Port}", port);
        }
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;

        try
        {
            listener?.Stop();
        }
        catch
        {
            // ignored
        }

        listener = null;

        lock (clientsGate)
        {
            foreach (var c in clients) CloseClient(c);
            clients.Clear();
        }

        StopMovement();
    }

    public void Dispose() => Stop();

    // ---- sockets --------------------------------------------------------------------------------------

    private async Task AcceptLoop(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await l.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Bridge accept failed");
                continue;
            }

            tcp.NoDelay = true;
            var client = new Client
            {
                Tcp = tcp,
                Outgoing = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true }),
            };

            lock (clientsGate) clients.Add(client);
            Plugin.Log.Information("Bridge client connected from {Ep}", tcp.Client.RemoteEndPoint);

            _ = WriteLoop(client, ct);
            _ = ReadLoop(client, ct);
        }
    }

    private async Task ReadLoop(Client client, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(client.Tcp.GetStream(), Encoding.UTF8, false, 4096, leaveOpen: true);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (line.Length == 0) continue;

                ClientMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<ClientMessage>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (msg != null) inbox.Enqueue((client, msg));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or SocketException)
        {
            // normal disconnect
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Bridge read loop ended");
        }

        Disconnect(client);
    }

    private async Task WriteLoop(Client client, CancellationToken ct)
    {
        try
        {
            var stream = client.Tcp.GetStream();
            await foreach (var line in client.Outgoing.Reader.ReadAllAsync(ct))
            {
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                await stream.WriteAsync(bytes, ct);
            }
        }
        catch (Exception)
        {
            // disconnect handled by the read loop
        }
    }

    private void Disconnect(Client client)
    {
        bool removed;
        lock (clientsGate) removed = clients.Remove(client);
        if (!removed) return;

        CloseClient(client);
        Plugin.Log.Information("Bridge client {Name} disconnected", client.Name);

        // Anything this phone was holding must not stay pressed.
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            foreach (var vk in client.HeldKeys) keys.Up(vk);
            client.HeldKeys.Clear();
            if (client.MovementOwner) StopMovement();
        });
    }

    private static void CloseClient(Client client)
    {
        client.Outgoing.Writer.TryComplete();
        try
        {
            client.Tcp.Close();
        }
        catch
        {
            // ignored
        }
    }

    private static void Send(Client client, object message)
        => client.Outgoing.Writer.TryWrite(JsonSerializer.Serialize(message, message.GetType(), JsonOptions));

    private void Broadcast(string line)
    {
        lock (clientsGate)
        {
            foreach (var c in clients)
            {
                if (c.SaidHello) c.Outgoing.Writer.TryWrite(line);
            }
        }
    }

    // ---- framework thread -----------------------------------------------------------------------------

    /// <summary>Called every framework update.</summary>
    public void Tick()
    {
        while (inbox.TryDequeue(out var item))
        {
            try
            {
                Handle(item.Client, item.Msg);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Bridge message {Type} failed", item.Msg.T);
            }
        }

        // A phone that vanishes mid-drag must not leave the character running into a wall.
        if (moving && (DateTime.UtcNow - lastMove).TotalMilliseconds > 400)
            StopMovement();

        if (ClientCount > 0 && (DateTime.UtcNow - lastState).TotalMilliseconds >= 100)
        {
            lastState = DateTime.UtcNow;
            PublishState();
        }
    }

    private void Handle(Client client, ClientMessage m)
    {
        var cfg = Plugin.Config;

        switch (m.T)
        {
            case "hello":
                client.SaidHello = true;
                client.Name = m.Name ?? "app";
                Send(client, new HelloReply());
                Send(client, BuildCatalog());
                Plugin.Log.Information("Bridge client {Name} (protocol {Ver}) said hello", client.Name, m.Ver);
                break;

            case "sub":
                if (m.Bars != null) client.Bars = m.Bars.Where(b => b is >= 0 and <= 17).Distinct().ToList();
                if (m.Ga != null) client.GaIds = m.Ga.Distinct().ToList();
                break;

            case "move":
            {
                var stick = new Vector2(m.X, m.Y);
                if (stick.LengthSquared() > 1f) stick = Vector2.Normalize(stick);

                if (stick.LengthSquared() < 0.0001f)
                {
                    StopMovement();
                }
                else
                {
                    lastMove = DateTime.UtcNow;
                    moving = true;
                    client.MovementOwner = true;
                    movement.Update(stick, cfg.Joystick);
                }

                break;
            }

            case "cam":
            {
                var cam = cfg.CameraPad;
                var yaw = m.Dx * cam.Sensitivity;
                var pitch = -m.Dy * cam.Sensitivity * (cam.InvertY ? -1f : 1f);
                if (yaw != 0 || pitch != 0) game.RotateCamera(yaw, pitch);
                break;
            }

            case "slot":
                if (m.H is >= 0 and <= 17 && m.S is >= 0 and <= 15) game.ExecuteSlot(m.H, m.S);
                break;

            case "key":
                if (m.Vk is <= 0 or > 0xFE) break;
                if (m.Down)
                {
                    client.HeldKeys.Add(m.Vk);
                    keys.Down(m.Vk);
                }
                else
                {
                    client.HeldKeys.Remove(m.Vk);
                    keys.Up(m.Vk);
                }

                break;

            case "ga":
                if (m.Id > 0) game.UseGeneralAction(m.Id);
                break;

            case "mc":
                if (m.Id > 0) game.ExecuteMainCommand(m.Id);
                break;

            case "mount":
                game.ToggleMount();
                break;

            case "icon":
                if (m.Id > 0) ServeIcon(client, m.Id);
                break;
        }
    }

    private void StopMovement()
    {
        moving = false;
        movement.Stop();
        lock (clientsGate)
        {
            foreach (var c in clients) c.MovementOwner = false;
        }
    }

    private CatalogMessage BuildCatalog()
    {
        var cat = new CatalogMessage();
        foreach (var e in sheets.GeneralActions) cat.Ga.Add(new CatalogEntry { Id = e.RowId, N = e.Name, I = e.IconId });
        foreach (var e in sheets.MainCommands) cat.Mc.Add(new CatalogEntry { Id = e.RowId, N = e.Name, I = e.IconId });
        return cat;
    }

    private void PublishState()
    {
        List<Client> snapshot;
        lock (clientsGate) snapshot = clients.Where(c => c.SaidHello).ToList();
        if (snapshot.Count == 0) return;

        var loggedIn = Plugin.ClientState.IsLoggedIn;
        var mounted = loggedIn && Plugin.Condition[ConditionFlag.Mounted];
        var combat = loggedIn && Plugin.Condition[ConditionFlag.InCombat];

        // Bars and general actions are cheap to read; build once per distinct request set.
        var barCache = new Dictionary<int, BarState>();
        var gaCache = new Dictionary<uint, SlotState>();

        foreach (var client in snapshot)
        {
            var state = new StateMessage { In = loggedIn, Mounted = mounted, Combat = combat };

            if (loggedIn)
            {
                foreach (var h in client.Bars)
                {
                    if (!barCache.TryGetValue(h, out var bar))
                    {
                        bar = ReadBar(h);
                        barCache[h] = bar;
                    }

                    state.Bars.Add(bar);
                }

                foreach (var id in client.GaIds)
                {
                    if (!gaCache.TryGetValue(id, out var ga))
                    {
                        ga = ReadGeneralAction(id);
                        gaCache[id] = ga;
                    }

                    state.GaIds.Add(id);
                    state.Ga.Add(ga);
                }
            }

            Send(client, state);
        }
    }

    private BarState ReadBar(int h)
    {
        var bar = new BarState { H = h };
        for (var s = 0; s < 12; s++)
        {
            var slot = game.ReadSlot(h, s);
            var st = new SlotState { E = !slot.Valid || slot.Empty, I = slot.Empty ? 0 : slot.IconId, K = slot.Keybind };
            if (!st.E && game.TryGetCooldown(slot, out var remaining, out var total))
            {
                st.Cd = MathF.Round(remaining, 1);
                st.Tot = MathF.Round(total, 1);
            }

            bar.Slots.Add(st);
        }

        return bar;
    }

    private SlotState ReadGeneralAction(uint id)
    {
        var entry = sheets.GeneralAction(id);
        var st = new SlotState { E = entry == null, I = entry?.IconId ?? 0 };
        var pseudo = new GameActions.SlotInfo(true, false, 0, RaptureHotbarModule.HotbarSlotType.GeneralAction, id, string.Empty);
        if (game.TryGetCooldown(pseudo, out var remaining, out var total))
        {
            st.Cd = MathF.Round(remaining, 1);
            st.Tot = MathF.Round(total, 1);
        }

        return st;
    }

    // ---- icons ----------------------------------------------------------------------------------------

    private void ServeIcon(Client client, uint iconId)
    {
        if (iconCache.TryGetValue(iconId, out var cached))
        {
            Send(client, new IconMessage { Id = iconId, Png = cached });
            return;
        }

        _ = EncodeIconAsync(iconId).ContinueWith(t =>
        {
            var png = t.IsCompletedSuccessfully ? t.Result : null;
            if (!t.IsCompletedSuccessfully)
                Plugin.Log.Warning(t.Exception?.GetBaseException(), "Icon {Id} could not be encoded", iconId);

            iconCache[iconId] = png;
            Send(client, new IconMessage { Id = iconId, Png = png });
        });
    }

    private async Task<string?> EncodeIconAsync(uint iconId)
    {
        pngCodec ??= Plugin.TextureReadback.GetSupportedImageEncoderInfos()
            .FirstOrDefault(c => c.MimeTypes.Any(m => m.Equals("image/png", StringComparison.OrdinalIgnoreCase)))?.ContainerGuid;
        if (pngCodec == null) return null;

        var wrap = await Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).RentAsync();
        using var ms = new MemoryStream();
        await Plugin.TextureReadback.SaveToStreamAsync(wrap, pngCodec.Value, ms, leaveWrapOpen: false, leaveStreamOpen: true);
        return Convert.ToBase64String(ms.ToArray());
    }
}
