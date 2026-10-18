using System.IO;
using System.Text.Json;

namespace AlKatta.Config;

/// <summary>A saved per-device limit, keyed by MAC so it survives IP changes.</summary>
public sealed class DeviceProfile
{
    public string Mac { get; set; } = "";
    public string? Name { get; set; }          // last known display name, for the UI
    public string? CustomName { get; set; }     // user-assigned label (survives IP changes)
    public int DownKbps { get; set; }           // 0 = unlimited
    public int UpKbps { get; set; }             // 0 = unlimited
    public bool Enabled { get; set; }           // was the limit active?
}

/// <summary>
/// Persists limits to %AppData%\AlKatta\profiles.json.
/// This is the piece that stops a limit from "disappearing": when a device
/// comes back on the network, its saved profile is re-applied automatically.
/// </summary>
public sealed class ProfileStore
{
    private readonly string _path;
    private Dictionary<string, DeviceProfile> _byMac = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public ProfileStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AlKatta");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "profiles.json");
        Load();
    }

    public DeviceProfile? Get(string mac)
    {
        lock (_gate)
            return _byMac.TryGetValue(mac, out var p) ? p : null;
    }

    public IReadOnlyCollection<DeviceProfile> All()
    {
        lock (_gate) return _byMac.Values.ToList();
    }

    public void Upsert(DeviceProfile profile)
    {
        lock (_gate)
        {
            _byMac[profile.Mac] = profile;
            SaveNoLock();
        }
    }

    public void Remove(string mac)
    {
        lock (_gate)
        {
            if (_byMac.Remove(mac)) SaveNoLock();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var list = JsonSerializer.Deserialize<List<DeviceProfile>>(json);
            if (list != null)
                _byMac = list.Where(p => !string.IsNullOrEmpty(p.Mac))
                             .ToDictionary(p => p.Mac, p => p, StringComparer.OrdinalIgnoreCase);
        }
        catch { /* corrupt file — start fresh */ }
    }

    private void SaveNoLock()
    {
        try
        {
            var json = JsonSerializer.Serialize(_byMac.Values.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch { /* best effort */ }
    }
}
