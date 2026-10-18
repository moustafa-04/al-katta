using System.Net;
using System.Net.Sockets;
using System.Text;
using AlKatta.Models;

namespace AlKatta.Core;

/// <summary>
/// Resolves a friendly device name NetCut-style, combining:
///   1. NetBIOS node-status query (UDP 137)  — great for Windows machines
///   2. Reverse DNS (PTR)                     — whatever the router/DNS knows
///   3. OUI vendor name                       — always-available fallback
/// Vendor is filled immediately; name resolution is async/best-effort.
/// </summary>
public static class NameResolver
{
    public static void FillVendor(NetDevice dev)
    {
        if (string.IsNullOrEmpty(dev.Vendor))
            dev.Vendor = OuiLookup.Lookup(dev.Mac);
    }

    public static async Task ResolveNameAsync(NetDevice dev, CancellationToken ct = default)
    {
        FillVendor(dev);

        // 1) NetBIOS — fast on LAN and gives the real Windows computer name.
        var nb = await TryNetBiosAsync(dev.Ip, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(nb)) { dev.HostName = nb; return; }

        // 2) Reverse DNS
        var dns = await TryReverseDnsAsync(dev.Ip, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(dns)) { dev.HostName = dns; return; }

        // 3) Vendor already set as fallback (DisplayName will use it).
    }

    private static async Task<string?> TryReverseDnsAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            var task = Dns.GetHostEntryAsync(ip);
            if (await Task.WhenAny(task, Task.Delay(800, ct)) != task) return null;
            var host = (await task).HostName;
            if (string.IsNullOrWhiteSpace(host)) return null;
            // Strip domain suffix for a cleaner label.
            var dot = host.IndexOf('.');
            return dot > 0 ? host[..dot] : host;
        }
        catch { return null; }
    }

    private static async Task<string?> TryNetBiosAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 700;

            // NBSTAT node status request (name "*" padded) — RFC 1002.
            byte[] req =
            {
                0xA2, 0x48,             // transaction id
                0x00, 0x00,             // flags
                0x00, 0x01,             // questions
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x20,                   // name length (32)
                // encoded "*"
                0x43, 0x4B, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41,
                0x41, 0x00,
                0x00, 0x21,             // type NBSTAT
                0x00, 0x01              // class IN
            };

            var ep = new IPEndPoint(ip, 137);
            await udp.SendAsync(req, req.Length, ep).ConfigureAwait(false);

            var recvTask = udp.ReceiveAsync();
            if (await Task.WhenAny(recvTask, Task.Delay(700, ct)) != recvTask) return null;

            var data = (await recvTask).Buffer;
            return ParseNetBiosName(data);
        }
        catch { return null; }
    }

    private static string? ParseNetBiosName(byte[] data)
    {
        // Header is 12 bytes; then echoed question; then answer with a name count
        // followed by 18-byte records (15-char name + suffix + flags).
        try
        {
            if (data.Length < 57) return null;
            int pos = 56;              // skip to the "number of names" byte region
            int count = data[pos];
            pos += 1;

            for (int i = 0; i < count && pos + 18 <= data.Length; i++)
            {
                var raw = Encoding.ASCII.GetString(data, pos, 15).TrimEnd();
                byte suffix = data[pos + 15];
                byte flags = data[pos + 16];
                bool isGroup = (flags & 0x80) != 0;

                // suffix 0x00 = workstation/unique name — that's the machine name.
                if (suffix == 0x00 && !isGroup && raw.Length > 0 && raw != "__MSBROWSE__")
                    return raw;

                pos += 18;
            }
        }
        catch { }
        return null;
    }
}
