using System.Net;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AlKatta.Models;

/// <summary>
/// A single device discovered on the LAN. Bound directly to the UI grid.
/// </summary>
public partial class NetDevice : ObservableObject
{
    public required IPAddress Ip { get; init; }
    public required PhysicalAddress Mac { get; init; }

    /// <summary>Normalized "AA:BB:CC:DD:EE:FF" — also the key used for persistence.</summary>
    public string MacKey => string.Join(":", Mac.GetAddressBytes().Select(b => b.ToString("X2")));

    public string IpText => Ip.ToString();

    [ObservableProperty] private string? hostName;
    [ObservableProperty] private string vendor = "";
    [ObservableProperty] private bool isGateway;
    [ObservableProperty] private bool isSelf;

    /// <summary>User-assigned label, saved by MAC. Takes priority over everything.</summary>
    [ObservableProperty] private string? customName;

    /// <summary>Best human label: custom name, then hostname, then vendor, then IP.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CustomName)) return CustomName!;
            if (!string.IsNullOrWhiteSpace(HostName)) return HostName!;
            if (!string.IsNullOrWhiteSpace(Vendor)) return Vendor;
            return IpText;
        }
    }

    // --- control state ---
    [ObservableProperty] private bool limitEnabled;
    [ObservableProperty] private int downKbps;   // 0 = unlimited
    [ObservableProperty] private int upKbps;     // 0 = unlimited

    // --- live stats (updated once per second) ---
    [ObservableProperty] private double liveDownKbps;
    [ObservableProperty] private double liveUpKbps;
    [ObservableProperty] private string status = "";

    partial void OnHostNameChanged(string? value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnVendorChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnCustomNameChanged(string? value) => OnPropertyChanged(nameof(DisplayName));
}
