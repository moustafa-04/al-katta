using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AlKatta.Config;
using AlKatta.Core;
using AlKatta.Models;

namespace AlKatta.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ProfileStore _store = new();
    private CaptureHub? _hub;
    private ControlEngine? _engine;

    private readonly DispatcherTimer _statsTimer;
    private readonly Dictionary<string, (long rx, long tx, long ticks)> _last = new();

    public ObservableCollection<NetworkContext> Interfaces { get; } = new();
    public ObservableCollection<NetDevice> Devices { get; } = new();

    [ObservableProperty] private NetworkContext? selectedInterface;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private string statusLine = "اختر واجهة واضغط فحص.";

    public MainViewModel()
    {
        foreach (var ni in NetworkContext.ListInterfaces())
            Interfaces.Add(ni);
        SelectedInterface = Interfaces.FirstOrDefault();

        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) => UpdateLiveStats();
        _statsTimer.Start();
    }

    partial void OnSelectedInterfaceChanged(NetworkContext? value)
    {
        // Switching interface tears down the old engine/handle.
        TearDown();
        if (value != null)
        {
            value.ResolveGatewayIp();
            StatusLine = $"الغيتواي: {value.GatewayIp}. جاهز للفحص.";
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (SelectedInterface == null || IsScanning) return;
        IsScanning = true;
        try
        {
            EnsureEngine();
            StatusLine = "جاري الفحص…";

            var scanner = new DeviceScanner(_hub!, SelectedInterface);
            var list = await scanner.ScanAsync();

            foreach (var old in Devices) old.PropertyChanged -= OnDevicePropertyChanged;
            Devices.Clear();
            foreach (var d in list)
            {
                NameResolver.FillVendor(d);
                ApplySavedProfile(d);                       // loads custom name BEFORE we subscribe
                d.PropertyChanged += OnDevicePropertyChanged;
                Devices.Add(d);
                _ = ResolveNameSoon(d);
            }

            StatusLine = SelectedInterface.GatewayMac.Equals(System.Net.NetworkInformation.PhysicalAddress.None)
                ? $"تم العثور على {Devices.Count - 1} جهاز — تنبيه: لم يتم العثور على MAC الغيتواي، لن تُطبَّق الحدود."
                : $"تم العثور على {Devices.Count - 1} جهاز. الغيتواي عند {SelectedInterface.GatewayIp}.";
        }
        catch (Exception ex)
        {
            StatusLine = "فشل الفحص: " + ex.Message;
        }
        finally { IsScanning = false; }
    }

    private async Task ResolveNameSoon(NetDevice d)
    {
        if (d.IsSelf) { d.HostName = "هذا الجهاز"; return; }
        try { await NameResolver.ResolveNameAsync(d); } catch { }
    }

    /// <summary>Persist a device's current profile (limits + custom name) by MAC.</summary>
    private void PersistProfile(NetDevice d) => _store.Upsert(new DeviceProfile
    {
        Mac = d.MacKey,
        Name = d.DisplayName,
        CustomName = d.CustomName,
        DownKbps = d.DownKbps,
        UpKbps = d.UpKbps,
        Enabled = d.LimitEnabled
    });

    /// <summary>Auto-save the custom name the instant the user types it.</summary>
    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is NetDevice d && e.PropertyName == nameof(NetDevice.CustomName))
            PersistProfile(d);
    }

    /// <summary>Apply a limit/cut for one device and persist it.</summary>
    [RelayCommand]
    private void Apply(NetDevice? dev)
    {
        if (dev == null || _engine == null || SelectedInterface == null) return;
        if (dev.IsSelf || dev.IsGateway) { dev.Status = "محمي"; return; }

        try
        {
            _engine.SetLimit(dev.Mac, dev.Ip, dev.DownKbps, dev.UpKbps, dev.LimitEnabled);
            PersistProfile(dev);
            dev.Status = dev.LimitEnabled
                ? $"محدود ↓{Fmt(dev.DownKbps)} ↑{Fmt(dev.UpKbps)}"
                : "محرَّر";
        }
        catch (Exception ex) { dev.Status = "خطأ: " + ex.Message; }
    }

    /// <summary>Full block ≈ NetCut "cut": tiny rate both ways.</summary>
    [RelayCommand]
    private void Cut(NetDevice? dev)
    {
        if (dev == null) return;
        dev.DownKbps = 1;
        dev.UpKbps = 1;
        dev.LimitEnabled = true;
        Apply(dev);
        if (!dev.IsSelf && !dev.IsGateway) dev.Status = "مقطوع";
    }

    [RelayCommand]
    private void Release(NetDevice? dev)
    {
        if (dev == null || _engine == null) return;
        dev.LimitEnabled = false;
        _engine.Release(dev.Mac);
        PersistProfile(dev);
        dev.Status = "محرَّر";
    }

    [RelayCommand]
    private void ReleaseAll()
    {
        _engine?.StopAll();
        foreach (var d in Devices) { d.LimitEnabled = false; d.Status = ""; }
        StatusLine = "تم تحرير كل الأجهزة.";
    }

    // ---- internals --------------------------------------------------------------

    private void EnsureEngine()
    {
        if (SelectedInterface == null) return;
        if (_hub != null && _engine != null) return;

        _hub = new CaptureHub(SelectedInterface.Device);
        _hub.Open();
        _engine = new ControlEngine(_hub, SelectedInterface);
    }

    private void ApplySavedProfile(NetDevice d)
    {
        var p = _store.Get(d.MacKey);
        if (p == null) return;

        d.CustomName = p.CustomName;
        d.DownKbps = p.DownKbps;
        d.UpKbps = p.UpKbps;
        d.LimitEnabled = p.Enabled;

        // Auto-reapply so a saved limit survives reconnects — the key fix.
        if (p.Enabled && _engine != null && !d.IsSelf && !d.IsGateway)
        {
            try { _engine.SetLimit(d.Mac, d.Ip, d.DownKbps, d.UpKbps, true); d.Status = "محدود (مستعاد)"; }
            catch { }
        }
    }

    private void UpdateLiveStats()
    {
        if (_engine == null) return;
        long now = Stopwatch.GetTimestamp();
        double freq = Stopwatch.Frequency;

        foreach (var d in Devices)
        {
            if (!_engine.TryGetCounters(d.MacKey, out var rx, out var tx)) { d.LiveDownKbps = 0; d.LiveUpKbps = 0; continue; }
            if (_last.TryGetValue(d.MacKey, out var prev))
            {
                double secs = (now - prev.ticks) / freq;
                if (secs > 0)
                {
                    d.LiveDownKbps = Math.Max(0, (rx - prev.rx) * 8.0 / 1000.0 / secs);
                    d.LiveUpKbps = Math.Max(0, (tx - prev.tx) * 8.0 / 1000.0 / secs);
                }
            }
            _last[d.MacKey] = (rx, tx, now);
        }
    }

    private void TearDown()
    {
        try { _engine?.StopAll(); } catch { }
        try { _engine?.Dispose(); } catch { }
        try { _hub?.Dispose(); } catch { }
        _engine = null; _hub = null;
        _last.Clear();
    }

    private static string Fmt(int kbps) => kbps <= 0 ? "∞" : kbps >= 1000 ? $"{kbps / 1000.0:0.#}M" : $"{kbps}K";

    public void OnClosing() => TearDown();
}
