<div align="center">

<img src="assets/logo.png" width="140" alt="AL KATTA logo"/>

# القاطع — AL KATTA

**A modern, open-source LAN bandwidth controller for Windows — a free replacement for SelfishNet.**

Discover every device on your local network, see their real names, and limit or cut each one's speed — with limits that *stay put* instead of silently expiring.

[العربية 🇸🇦](README.ar.md) · [Disclaimer](DISCLAIMER.md) · [Contributing](CONTRIBUTING.md) · MIT License

</div>

---

## Why AL KATTA?

The classic **SelfishNet** is great but abandoned: it rides on the dead **WinPcap** driver, which freezes on Windows 10/11 and has no official support. AL KATTA rebuilds the same simple, fast idea on a modern stack:

- **C# / .NET 8 (WPF)** + **[Npcap](https://npcap.com)** — the maintained successor to WinPcap, stable on Windows 11.
- **Limits that don't expire.** A steady re-ARP loop (every ~1.5 s) keeps the throttle in place, and every limit is **saved by MAC** and re-applied automatically when the device reconnects — fixing SelfishNet's "the limit disappears after a while" bug.
- **NetCut-style device names** via NetBIOS + reverse DNS + OUI vendor lookup.

> ⚠️ Use AL KATTA only on a network you **own or administer**. See [DISCLAIMER.md](DISCLAIMER.md).

## Features

- 🔍 **Device discovery** — fast ARP scan of your subnet.
- 🏷️ **Real names** — NetBIOS name, reverse-DNS, and manufacturer (OUI).
- ✏️ **Custom names** — label a device yourself; saved by MAC, survives IP changes.
- 🎚️ **Per-device limits** — set download/upload caps in Kbps, or **Cut** a device entirely.
- 💾 **Persistent profiles** — limits + names stored in `%AppData%\AlKatta\profiles.json` and auto-restored.
- 📊 **Live rates** — per-device up/down throughput, refreshed every second.
- 🌙 **System tray** — closing (X) minimizes to tray and keeps running; double-click to restore.
- 🌐 **Fully Arabic UI** (RTL).

## How it works

AL KATTA uses **ARP spoofing**: it places your PC between the target device and the gateway, so traffic for a limited device flows through your machine. Each direction passes through a **token-bucket** rate limiter; over-budget packets are dropped so TCP settles near your target rate. Unmanaged devices are never touched.

## Requirements

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Npcap](https://npcap.com) — during install, enable **"Install Npcap in WinPcap API-compatible Mode"**.

## Build & Run

```bash
dotnet build -c Release
```

Run the result **as Administrator** (raw packet capture requires it):

```
src/bin/Release/net8.0-windows/AlKatta.exe
```

## Usage

1. Pick your network **interface** from the list.
2. Click **Scan** (فحص) — devices appear with name / vendor / IP / MAC.
3. Enter a cap in **Kbps** under `↓ حد` / `↑ حد`, tick **تفعيل**, and click **تطبيق** (Apply).
   - `0` = unlimited · **قطع** (Cut) = near-total block · **تحرير** (Release) = restore the device.
4. Double-click **اسم مخصّص** to give a device your own label — saved automatically.

### Fuller vendor names (optional)

Download the IEEE OUI database from <https://standards-oui.ieee.org/oui/oui.txt> and drop it as `oui.txt` next to the exe; it's loaded automatically.

## Project layout

```
src/
 ├─ Core/
 │   ├─ NetworkContext.cs   interface / IP / MAC / gateway / subnet
 │   ├─ CaptureHub.cs       single shared capture handle
 │   ├─ DeviceScanner.cs    ARP device discovery
 │   ├─ NameResolver.cs     NetBIOS + DNS + OUI (NetCut-style names)
 │   ├─ OuiLookup.cs        vendor database
 │   ├─ ArpTools.cs         build / send ARP frames
 │   ├─ TokenBucket.cs      rate limiting
 │   └─ ControlEngine.cs    poison + forward + throttle + clean restore
 ├─ Config/ProfileStore.cs  per-MAC limits & names (persistent)
 ├─ Models/NetDevice.cs     device row bound to the UI
 ├─ ViewModels/MainViewModel.cs
 └─ MainWindow.xaml(.cs)    UI + system tray
```

## Roadmap

- Delay-queue traffic shaping (smoother than drop-based limiting)
- IPv6 forwarding
- Scheduling (auto-limit by time of day)
- Per-group profiles ("study mode")
- Anti-spoof detection

## Contributing

PRs welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE) © 2026 Moustafa
