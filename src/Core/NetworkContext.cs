using System.Net;
using System.Net.NetworkInformation;
using SharpPcap;
using SharpPcap.LibPcap;

namespace AlKatta.Core;

/// <summary>
/// Everything we need to know about the chosen network interface:
/// our own IP/MAC, the gateway, and the subnet range to scan.
/// </summary>
public sealed class NetworkContext
{
    public required LibPcapLiveDevice Device { get; init; }
    public required string FriendlyName { get; init; }
    public required IPAddress MyIp { get; init; }
    public required IPAddress SubnetMask { get; init; }
    public required PhysicalAddress MyMac { get; init; }

    public IPAddress GatewayIp { get; set; } = IPAddress.None;
    public PhysicalAddress GatewayMac { get; set; } = PhysicalAddress.None;

    public uint NetworkBase => IpToUint(MyIp) & IpToUint(SubnetMask);
    public uint Broadcast => NetworkBase | ~IpToUint(SubnetMask);

    /// <summary>Usable host IPs in the subnet, excluding network/broadcast and ourselves.</summary>
    public IEnumerable<IPAddress> HostAddresses()
    {
        for (uint a = NetworkBase + 1; a < Broadcast; a++)
        {
            var ip = UintToIp(a);
            if (ip.Equals(MyIp)) continue;
            yield return ip;
        }
    }

    public static uint IpToUint(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    public static IPAddress UintToIp(uint v) =>
        new(new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

    /// <summary>
    /// Enumerate candidate interfaces (those with an IPv4 address + MAC).
    /// </summary>
    public static List<NetworkContext> ListInterfaces()
    {
        var result = new List<NetworkContext>();
        foreach (var dev in CaptureDeviceList.Instance.OfType<LibPcapLiveDevice>())
        {
            try
            {
                var ipv4 = dev.Interface.Addresses
                    .FirstOrDefault(a => a.Addr?.ipAddress != null &&
                                         a.Addr.ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ipv4?.Addr?.ipAddress == null) continue;

                var mask = ipv4.Netmask?.ipAddress ?? IPAddress.Parse("255.255.255.0");
                var mac = dev.MacAddress;
                if (mac == null) continue;

                var friendly = dev.Interface.FriendlyName ?? dev.Description ?? dev.Name;

                result.Add(new NetworkContext
                {
                    Device = dev,
                    FriendlyName = $"{friendly}  ({ipv4.Addr.ipAddress})",
                    MyIp = ipv4.Addr.ipAddress,
                    SubnetMask = mask,
                    MyMac = mac
                });
            }
            catch
            {
                // skip interfaces we can't read
            }
        }
        return result;
    }

    /// <summary>Read the default gateway for this interface from the OS routing info.</summary>
    public void ResolveGatewayIp()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            var props = ni.GetIPProperties();
            var hasMyIp = props.UnicastAddresses.Any(u => u.Address.Equals(MyIp));
            if (!hasMyIp) continue;

            var gw = props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                     && !g.Address.Equals(IPAddress.Any));
            if (gw != null) { GatewayIp = gw.Address; return; }
        }
    }
}
