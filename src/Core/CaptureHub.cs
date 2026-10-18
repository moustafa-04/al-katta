using SharpPcap;
using SharpPcap.LibPcap;

namespace AlKatta.Core;

/// <summary>
/// Owns the single open handle on the interface. Both the scanner and the
/// control engine subscribe here, so we never fight over StartCapture().
/// </summary>
public sealed class CaptureHub : IDisposable
{
    private readonly LibPcapLiveDevice _device;
    private bool _open;
    private readonly object _gate = new();

    public LibPcapLiveDevice Device => _device;

    /// <summary>Raised on the capture thread for every captured frame (arp or ip).</summary>
    public event Action<RawCapture>? PacketArrived;

    public CaptureHub(LibPcapLiveDevice device) => _device = device;

    public void Open()
    {
        lock (_gate)
        {
            if (_open) return;

            var config = new DeviceConfiguration
            {
                Mode = DeviceModes.Promiscuous,
                ReadTimeout = 100
            };
            _device.Open(config);

            // Only ARP (discovery) and IPv4 (forwarding) interest us.
            try { _device.Filter = "arp or ip"; } catch { /* some drivers reject; ignore */ }

            _device.OnPacketArrival += OnArrival;
            _device.StartCapture();
            _open = true;
        }
    }

    private void OnArrival(object sender, PacketCapture e)
    {
        RawCapture raw;
        try { raw = e.GetPacket(); }
        catch { return; }
        PacketArrived?.Invoke(raw);
    }

    public void Send(byte[] frame) => ArpTools.Send(_device, frame);

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_open) return;
            try { _device.OnPacketArrival -= OnArrival; } catch { }
            try { _device.StopCapture(); } catch { }
            try { _device.Close(); } catch { }
            _open = false;
        }
    }
}
