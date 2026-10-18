using System.IO;
using System.Net.NetworkInformation;

namespace AlKatta.Core;

/// <summary>
/// Maps a MAC's first 3 bytes (OUI) to a manufacturer name.
/// Ships with a small built-in table; if an IEEE "oui.txt" sits next to the
/// exe, the full ~30k-entry database is loaded from it for complete coverage.
/// </summary>
public static class OuiLookup
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    // A handful of very common vendors so names show up even with no oui.txt.
    private static readonly Dictionary<string, string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F0:9F:C2"] = "Ubiquiti", ["FC:EC:DA"] = "Ubiquiti",
        ["3C:5A:B4"] = "Google",  ["F4:F5:E8"] = "Google",
        ["A4:5E:60"] = "Apple",   ["AC:BC:32"] = "Apple",   ["DC:A9:04"] = "Apple",
        ["F0:18:98"] = "Apple",   ["88:66:5A"] = "Apple",
        ["00:12:FB"] = "Samsung", ["5C:0A:5B"] = "Samsung", ["8C:77:12"] = "Samsung",
        ["E8:50:8B"] = "Samsung",
        ["48:3C:0C"] = "Huawei",  ["00:E0:FC"] = "Huawei",  ["04:BD:70"] = "Huawei",
        ["64:09:80"] = "Xiaomi",  ["28:6C:07"] = "Xiaomi",  ["F8:A4:5F"] = "Xiaomi",
        ["50:EB:F6"] = "Intel",   ["3C:A9:F4"] = "Intel",   ["A4:C3:F0"] = "Intel",
        ["AC:84:C6"] = "TP-Link", ["50:C7:BF"] = "TP-Link", ["EC:08:6B"] = "TP-Link",
        ["B0:BE:76"] = "TP-Link",
        ["00:1A:11"] = "Google",
        ["D8:0D:17"] = "TP-Link",
        ["00:50:56"] = "VMware",  ["08:00:27"] = "VirtualBox",
        ["DC:A6:32"] = "Raspberry Pi", ["B8:27:EB"] = "Raspberry Pi", ["E4:5F:01"] = "Raspberry Pi",
    };

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        foreach (var kv in BuiltIn) Map[kv.Key] = kv.Value;

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "oui.txt");
            if (!File.Exists(path)) return;

            // IEEE format lines look like:  F09FC2   (base 16)  Ubiquiti Inc.
            foreach (var line in File.ReadLines(path))
            {
                var idx = line.IndexOf("(base 16)", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                var hex = line[..idx].Trim().Replace("-", "").Replace(":", "");
                if (hex.Length < 6) continue;

                var name = line[(idx + "(base 16)".Length)..].Trim();
                if (name.Length == 0) continue;

                var key = $"{hex[..2]}:{hex.Substring(2, 2)}:{hex.Substring(4, 2)}";
                Map[key] = name;
            }
        }
        catch { /* best effort */ }
    }

    public static string Lookup(PhysicalAddress mac)
    {
        EnsureLoaded();
        var b = mac.GetAddressBytes();
        if (b.Length < 3) return "";
        var key = $"{b[0]:X2}:{b[1]:X2}:{b[2]:X2}";
        return Map.TryGetValue(key, out var name) ? name : "";
    }
}
