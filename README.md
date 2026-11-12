<p align="center">
  <img src="src/app_logo.png" alt="Discord Shield" width="128" />
</p>

<h1 align="center">Discord Shield</h1>

<p align="center">
  Zero-Trust Dual-Layer DPI Evasion and Local Tunnel for Discord
</p>

---

Discord Shield is a lightweight, open-source Windows application engineered to restore reliable, low-latency connectivity to Discord across networks with stateful Deep Packet Inspection (DPI), DNS manipulation, and TLS Server Name Indication (SNI) filtering.

Unlike traditional VPN services that route entire operating system traffic through remote servers, Discord Shield runs locally on your machine. It mitigates censorship filters through TCP packet desynchronization, TLS ClientHello splitting, desktop bridge routing, and scoped browser PAC routing without third-party servers, telemetry, or system-wide DNS overrides.

---

## Technical Overview

Discord Shield combines four coordinated local mechanisms tailored for both the Discord desktop client and web browsers:

1. **Application-Level TLS Record Splitting**
   TLS `ClientHello` handshakes destined for Discord endpoints (`discord.com`, `gateway.discord.gg`, `cdn.discordapp.com`) are segmented before the SNI extension over a local SOCKS5 proxy (`127.0.0.1:1080`), preventing DPI middleboxes from inspecting the host header while remote servers reassemble the stream cleanly.

2. **Native Desktop Bridge**
   While connected, Discord Shield deploys `version.dll` and `drover.ini` into active desktop installations (`%LocalAppData%\Discord*\app-*`), routing Discord desktop traffic directly through the local evasion proxy without virtual network adapters. These files are automatically removed upon disconnecting or exiting.

3. **Scoped Web Client Routing**
   A lightweight local Proxy Auto-Config (PAC) server (`http://127.0.0.1:1085/discord-shield.pac`) temporarily routes browser requests for Discord web and CDN domains through the local SOCKS5 tunnel while leaving all other websites direct. Any pre-existing `AutoConfigURL` setting is preserved and restored cleanly on disconnect.

4. **Kernel-Level Packet Desynchronization & Custom-DNS Preservation**
   A local WinDivert-based packet engine suppresses QUIC fallback interference, filters injected RST packets, and applies TTL-aware / out-of-band segmentation. System DNS settings are left intact so custom user DNS providers (Cloudflare, Google, or local DoH) continue operating without conflicts, alongside multi-strategy fallback profiles per region.

---

## Geo-Aware Auto-Detect and Supported Regions

On first launch, **Auto-Detect** inspects Windows regional settings and network localization metadata to prioritize the closest matching country profile automatically, then cycles through tuned per-region strategies against the live Discord Gateway API (`https://discord.com/api/v9/gateway`).

### Iran
- **Supported Operators:**
  - Telecommunication Company of Iran (TCI / Mokhaberat)
  - Shatel
  - Asiatech
  - Zitel
  - Pars Online
  - Rightel
  - Sabanet
  - Pishgaman
  - Hi-Web
  - Irancell (TD-LTE Fixed Wireless and Mobile)
  - Hamrah-e Aval / MCI
- **Profile Architecture:** Cycles through primary OOB + Yandex DNS (`1253`), adaptive disorder + OpenDNS (`5353`), and direct DNS-safe OOB/fake-SNI modes for restrictive ISP middleboxes.

### Turkey
- **Supported Operators:**
  - Turk Telekom (Fixed Broadband and Mobile)
  - Turkcell / Superonline (Fiber Broadband and LTE)
  - Vodafone Turkey
  - Turksat Kablonet
- **Profile Architecture:** Combines SNI segmentation, TTL-bounded packet tricks, and adaptive disorder fallback against BTK inspection filters.

### Russia
- **Supported Operators:**
  - Rostelecom
  - MTS
  - Beeline
  - MegaFon
  - ER-Telecom (Dom.ru)
  - Tele2
- **Profile Architecture:** Auto-TTL calculation (`1-4-10`) paired with wrong-sequence/wrong-checksum reverse fragmentation and out-of-band TLS record splitting against TSPU filters.

### UAE / Middle East
- **Supported Operators:**
  - Etisalat (e&)
  - du
  - Saudi Telecom Company (STC)
  - Ooredoo
  - Omantel
- **Profile Architecture:** Balances WebRTC/VoIP desynchronization with adaptive fake-SNI fallback.

### Global
- Provides multi-profile deep packet splitting and OOB fallback suitable for restrictive institutional, university, or public networks worldwide.

---

## Installation and Requirements

- **Runtime:** Requires the official [Microsoft .NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
- Download and execute `Discord Shield Setup.exe` from the [Releases](https://github.com/arshiatxd/discordshield/releases) page. Administrator privileges are required for the local WinDivert packet driver.

---

## Privacy and Transparency

- Zero telemetry, zero analytics, and zero external relay servers. All traffic stays strictly between your machine and Discord's official infrastructure.
- Because Discord Shield distributes unsigned local packet-filtering binaries (`WinDivert64.sys`, `DiscordShieldEngine.exe`, `ciadpi.exe`), Windows SmartScreen or Defender heuristics may display a prompt on unsigned executables. Every component is inspectable in this repository.

---

## Uninstallation

Discord Shield supports full, clean removal:
- **Via Windows Settings:** Open **Installed Apps**, locate **Discord Shield**, and select **Uninstall**.
- **Via Local Folder:** Execute `Uninstall Discord Shield.exe` inside the installation directory (`%ProgramFiles%\Discord Shield` or standalone folder).

Uninstallation terminates background helper processes, stops the `WinDivert` driver service, restores `AutoConfigURL` browser proxy settings, removes bridge files (`version.dll` and `drover.ini`) from Discord installation folders, and clears registry entries and shortcuts.

---

## Copyright and License

Copyright (c) 2026 Discord Shield Contributors. All rights reserved.

This software is released under the terms of the [MIT License](LICENSE). Third-party core utilities and drivers retain their respective upstream licenses.
