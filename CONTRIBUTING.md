# Contributing to AL KATTA

Thanks for your interest! Contributions are welcome.

## Getting started

1. Fork and clone the repo.
2. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download) and
   [Npcap](https://npcap.com) (WinPcap-compatible mode).
3. `dotnet build -c Release` and run `AlKatta.exe` **as Administrator**.

## Guidelines

- Keep the core simple and fast — that's the whole point of this project.
- One focused change per pull request; describe what and why.
- Match the existing code style (nullable enabled, MVVM for UI code).
- UI strings should have an Arabic form (the app ships Arabic-first).
- Don't commit `bin/`, `obj/`, or a downloaded `oui.txt`.

## Ideas / roadmap

See the **Roadmap** section in the README. Good first issues: traffic shaping,
IPv6 forwarding, scheduling, per-group profiles, anti-spoof detection.

## Responsible use

This is a network-admin tool. Please keep contributions aligned with the
[DISCLAIMER](DISCLAIMER.md): it is meant for networks you own or administer.
