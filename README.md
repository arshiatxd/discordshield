<p align="center">
  <img src="src/app_logo.png" alt="Discord Shield" width="128" />
</p>

<h1 align="center">Discord Shield</h1>

<p align="center">
  Zero-Trust Local DPI Evasion and Encrypted Tunnel for Discord
</p>

---

Discord Shield is a lightweight, open-source Windows desktop utility engineered to restore reliable, low-latency connectivity to Discord across networks affected by stateful Deep Packet Inspection (DPI), DNS poisoning, and TLS Server Name Indication (SNI) filtering.

Unlike conventional VPN providers that route all machine traffic through remote relays, Discord Shield runs entirely on the local computer. It bypasses censorship middleboxes by splitting TLS ClientHello handshakes, desynchronizing TCP packet headers, suppressing censored QUIC handshakes, and enforcing clean DNS resolution without intermediate proxies, account registration, or traffic redirection.

---

## Technical Overview

Discord Shield deploys a multi-layer evasion stack tailored specifically for Discord's desktop client, voice gateway infrastructure, and Cloudflare edge network:

1. **Application-Level TLS Record Splitting**
   Intercepts outbound TLS handshakes destined for Discord endpoints and splits the `ClientHello` frame across records directly before the SNI extension. Stateful inspection middleboxes fail to reconstruct the fragmented host header and let the connection pass, while Cloudflare destination endpoints reassemble the standard TLS stream seamlessly.

2. **Kernel-Level Packet Desynchronization**
   A kernel packet filter driver intercepts and suppresses unencrypted HTTP/3 (QUIC) fallbacks, drops censorship-injected TCP RST packets, and injects out-of-band (OOB) packets and out-of-order sequence fragmentation to defeat stateful TCP stream tracking.

3. **In-Flight Connection Cancellation**
   Users can immediately abort in-flight connection attempts via the integrated cancel control. Cancellation cleanly releases network resources, halts background probing routines, and flushes local resolver cache entries without lingering processes.

4. **Fluid Discord-Native Interface**
   Built with a modern WPF fluid motion interface featuring a high-contrast dark theme, animated lock status indicator, responsive spring easing, real-time gateway latency telemetry, and an adaptive region dropdown designed to prevent clipping on constrained displays.

5. **Universal Resolver Compatibility**
   Operates transparently alongside any existing system DNS configuration, including public DoH resolvers (Cloudflare 1.1.1.1, Google 8.8.8.8, Quad9 9.9.9.9) and standard ISP-provided nameservers without query stalls or connection hangs.

6. **100% Local Zero-Trust Architecture**
   All evasion logic runs in local memory. No user credentials, tokens, voice packets, or text messages are ever intercepted, logged, or sent to external servers.

---

## Supported Regions and Network Operators

Discord Shield provides targeted evasion parameter sets calibrated for the specific inspection hardware and middlebox behaviors across major telecommunications providers:

### Turkey
- **Supported Operators:**
  - Turkcell Superonline (Fiber, VDSL, LTE, and Superbox FWA)
  - Turk Telekom (TTNET, VDSL, Fiber, and TT Mobil)
  - Vodafone Turkey (Fixed Broadband, Fiber, and 4.5G Mobile)
  - Turksat Kablonet (Cable Internet and Uydunet Fiber)
  - TurkNet (GigaFiber, VDSL, and Active Infrastructure)
  - Millenicom (Fiber and VDSL)
  - NetSpeed (VDSL and Fiber)
  - GibirNet (Fiber and VDSL)
  - Comnet (Datacenter and Residential Fiber)
  - D-Smart Net (ADSL and Fiber)
- **Inspection Characteristics:** Out-of-order TCP segmentation, SNI offset splitting, reverse packet fragmentation, and selective QUIC suppression to evade BTK-mandated domain blocking and TCP reset injections.

### Iran
- **Supported Operators:**
  - Hamrah-e Aval / MCI (Mobile 3G/4G/5G and FWA)
  - Irancell / MTN (Mobile, TD-LTE, and Fiber)
  - RighTel (3G, 4G, and LTE)
  - Shatel (ADSL, VDSL, and Shatel Mobile)
  - Telecommunication Company of Iran (TCI / Mokhaberat)
  - Asiatech (ADSL, VDSL, and ODU)
  - Zitel (TD-LTE and Unlimited FWA)
  - Pars Online (ADSL and VDSL)
  - Sabanet (ADSL and Wireless)
  - Pishgaman (ADSL, VDSL, and Wireless)
  - Hi-Web (ADSL, 4G Rural, and Wireless)
- **Inspection Characteristics:** Out-of-band (OOB) TCP desync, whitelisted fake SNI injection, TLS record fragmentation, and clean DNS query routing.

### United Arab Emirates and Middle East
- **Supported Operators:**
  - Etisalat by e& (Elife Fiber, Freedom 5G, and Fixed Wireless)
  - du / Emirates Integrated Telecommunications Company (Home Fiber and 5G)
  - Saudi Telecom Company (STC / stc pay / 5G)
  - Mobily (Etihad Etisalat)
  - Zain (Saudi Arabia, Kuwait, Bahrain, and Jordan)
  - Ooredoo (Qatar, Oman, and Kuwait)
  - Omantel (Oman Telecommunications Company)
  - Batelco (Bahrain Telecommunications Company)
- **Inspection Characteristics:** VoIP and WebRTC protocol unblocking, SIP filtering countermeasures, and TLS domain inspection bypass.

### Russia
- **Supported Operators:**
  - Rostelecom (GPON and Broadband)
  - MTS / Mobile TeleSystems (Mobile and Fixed)
  - Beeline / PJSC VimpelCom (Home Internet and Mobile)
  - MegaFon (Yota and Mobile 4G/5G)
  - ER-Telecom / Dom.ru (Broadband and Fiber)
  - Tele2 Russia (Rostelecom Mobile)
- **Inspection Characteristics:** Bypasses RKN TSPU hardware filters via dynamic TTL shielding and TCP segmentation.

### Global / Auto-Detect
- Automatically detects the user's geographic network origin via low-overhead latency probes, reorders evasion profiles accordingly, and activates the first verified profile against the live Discord Gateway endpoint (`https://discord.com/api/v9/gateway`). Compatible with university campuses, corporate firewalls, and public Wi-Fi hotspots.

---

## Privacy Policy

Discord Shield operates entirely on the user's local machine:
- **Zero Data Collection:** Personal information, Discord tokens, voice communications, and chat logs are never monitored or recorded.
- **Local In-Memory Processing:** Packet desynchronization occurs strictly in system memory without remote proxy servers.
- **No Telemetry:** The application contains zero analytics trackers, telemetry beacons, or external reporting mechanisms.
- **Open Source:** Full source code is open, inspectable, and verifiable under the MIT license.

---

## Installation

Download and run `Discord Shield Setup.exe` from the latest release to install the application. Administrator privileges are required to configure the local network filter driver.

---

## Building from Source

### Prerequisites
- Windows 10 / 11 (64-bit)
- .NET 10.0 SDK
- Inno Setup 6 (optional, required only for building the installer)

### Compile Application
```powershell
dotnet build src\DiscordTunnel.csproj -c Release
```

### Build Installer
```bat
build.bat
```

---

## Uninstallation

Discord Shield provides clean, single-click uninstallation:
- **Via Windows Settings:** Open Installed Apps, search for **Discord Shield**, and select **Uninstall**.
- **Via Installation Folder:** Run `Uninstall Discord Shield.exe` located directly in the installation folder.

The uninstaller terminates active background engines, safely unloads the kernel packet filter driver, and removes all program files and configuration keys.

---

## Copyright and License

Copyright (c) 2026 Discord Shield Contributors. All rights reserved.

This software is released under the terms of the [MIT License](LICENSE). Embedded drivers and circumvention components retain their respective upstream licenses.
