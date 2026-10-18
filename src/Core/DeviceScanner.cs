using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using PacketDotNet;
using SharpPcap;
using AlKatta.Models;

namespace AlKatta.Core;

/// <summary>
/// Active ARP scanner: broadcasts a request for every host in the subnet and
/// collects the replies. Also used to resolve the gateway MAC.
/// </summary>
public sealed class DeviceScanner
{
    private readonly CaptureHub _hub;
    private readonly NetworkContext _ctx;

    public DeviceScanner(CaptureHub hub, NetworkContext ctx)
    {
        _hub = hub;
        _ctx = ctx;
    }

    /// <summary>
    /// Sweep the subnet. Returns one NetDevice per responding host.
    /// Marks the gateway and ourselves, and fills in <c>ctx.GatewayMac</c>.
    /// </summary>
    public async Task<List<NetDevice>> ScanAsync(int waitMs = 2500, CancellationToken ct = default)
    {
        var found = new ConcurrentDictionary<string, NetDevice>();

        void Handler(RawCapture raw)
        {
            try
            {
                var pkt = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
                var arp = pkt.Extract<ArpPacket>();
                if (arp == null || arp.Operation != ArpOperation.Response) return;

                var ip = arp.SenderProtocolAddress;
                var mac = arp.SenderHardwareAddress;
                if (ip == null || mac == null) return;
                if (ip.Equals(IPAddress.Any) || ip.Equals(_ctx.MyIp)) return;

                var dev = new NetDevice
                {
                    Ip = ip,
                    Mac = mac,
                    IsGateway = ip.Equals(_ctx.GatewayIp)
                };
                found[dev.MacKey] = dev;

                if (dev.IsGateway)
                    _ctx.GatewayMac = mac;
            }
            catch { /* ignore malformed */ }
        }

        _hub.PacketArrived += Handler;
        try
        {
            // Fire requests for every host IP (plus the gateway explicitly).
            var targets = _ctx.HostAddresses().ToList();
            if (!_ctx.GatewayIp.Equals(IPAddress.None) && !targets.Contains(_ctx.GatewayIp))
                targets.Add(_ctx.GatewayIp);

            foreach (var ip in targets)
            {
                ct.ThrowIfCancellationRequested();
                _hub.Send(ArpTools.BuildRequest(_ctx.MyMac, _ctx.MyIp, ip));
                await Task.Delay(3, ct); // gentle pacing so we don't flood the NIC buffer
            }

            await Task.Delay(waitMs, ct);
        }
        finally
        {
            _hub.PacketArrived -= Handler;
        }

        // Add an explicit "self" entry so the user sees their own machine.
        var selfDev = new NetDevice { Ip = _ctx.MyIp, Mac = _ctx.MyMac, IsSelf = true };
        var list = found.Values.ToList();
        list.Insert(0, selfDev);

        // Sort: gateway, self, then by IP.
        return list
            .OrderByDescending(d => d.IsGateway)
            .ThenByDescending(d => d.IsSelf)
            .ThenBy(d => NetworkContext.IpToUint(d.Ip))
            .ToList();
    }
}
