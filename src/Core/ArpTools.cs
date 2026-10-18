using System.Net;
using System.Net.NetworkInformation;
using PacketDotNet;
using SharpPcap.LibPcap;

namespace AlKatta.Core;

/// <summary>Helpers for building and sending ARP frames.</summary>
public static class ArpTools
{
    public static readonly PhysicalAddress Broadcast =
        new(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

    /// <summary>Build an ARP request asking "who has targetIp?" broadcast from us.</summary>
    public static byte[] BuildRequest(PhysicalAddress myMac, IPAddress myIp, IPAddress targetIp)
    {
        var arp = new ArpPacket(ArpOperation.Request,
            targetHardwareAddress: PhysicalAddress.Parse("00-00-00-00-00-00"),
            targetProtocolAddress: targetIp,
            senderHardwareAddress: myMac,
            senderProtocolAddress: myIp);

        var eth = new EthernetPacket(myMac, Broadcast, EthernetType.Arp) { PayloadPacket = arp };
        return eth.Bytes;
    }

    /// <summary>
    /// Build a (gratuitous/poison) ARP reply telling <paramref name="destMac"/> that
    /// <paramref name="claimedIp"/> is at <paramref name="claimedByMac"/>.
    /// </summary>
    public static byte[] BuildReply(
        PhysicalAddress claimedByMac, IPAddress claimedIp,
        PhysicalAddress destMac, IPAddress destIp)
    {
        var arp = new ArpPacket(ArpOperation.Response,
            targetHardwareAddress: destMac,
            targetProtocolAddress: destIp,
            senderHardwareAddress: claimedByMac,
            senderProtocolAddress: claimedIp);

        var eth = new EthernetPacket(claimedByMac, destMac, EthernetType.Arp) { PayloadPacket = arp };
        return eth.Bytes;
    }

    public static void Send(LibPcapLiveDevice device, byte[] frame)
    {
        lock (device) { device.SendPacket(frame); }
    }
}
