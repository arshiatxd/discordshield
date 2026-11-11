<p align="center">
  <img src="src/app_logo.png" alt="Discord Shield" width="128" />
</p>

<h1 align="center">Discord Shield</h1>

<p align="center">
  High-Performance DPI Evasion and Connection Optimizer for Discord
</p>

---

Discord Shield is a lightweight, open-source Windows utility engineered to restore stable, low-latency connectivity to Discord across networks with stateful Deep Packet Inspection (DPI), DNS interference, and TLS Server Name Indication (SNI) filtering.

Unlike VPN services that route all device traffic through distant third-party servers, Discord Shield works entirely locally. It desynchronizes packet headers, fragments TLS ClientHello handshakes, and safeguards DNS resolution so that Discord traffic connects directly to Discord infrastructure with minimal latency and zero data collection.

---

## Technical Overview

Discord Shield combines dual-layer network desynchronization designed for Discord desktop architecture and Cloudflare edge networks:

1. **Application-Level TLS Record Fragmentation**
   Splits the TLS handshake ClientHello message right before the SNI hostname field. Middlebox inspection filters cannot identify the target domain and allow the connection, while Discord edge servers recombine the segments transparently.

2. **Kernel-Level Packet Desynchronization**
   Applies sequence number desynchronization, reversed TCP segmentation, and unencrypted QUIC/HTTP3 suppression to prevent middlebox connection resets (TCP RST).

3. **Resilient DNS Architecture**
   Provides automated DNS optimization with UDP fallback support, preventing hangs when custom DNS servers (such as Google 8.8.8.8 or Cloudflare 1.1.1.1) or ISP resolvers are configured on the system.

4. **100% Local and Private**
   Operates entirely on the local network stack. Contains no analytics, background tracking, telemetry, or external proxies.

---

## Supported Regions and Network Operators

Discord Shield provides dedicated desynchronization profiles optimized for telecommunications operators and inspection equipment:

### Iran
- **Supported Operators:**
  - Shatel (ADSL, VDSL, Fiber)
  - Asiatech (ADSL, Wireless)
  - Zitel (TD-LTE)
  - Pars Online
  - Rightel (Mobile 3G/4G/LTE)
  - Sabanet
  - Pishgaman
  - Hi-Web
  - Hamrah-e Aval / MCI (Mobile 4G/5G)
  - Irancell (Mobile & TD-LTE)
  - Telecommunication Company of Iran (TCI / Mokhaberat)
- **Inspection Characteristics:** Out-of-band (OOB) TCP desynchronization, Cloudflare-fronted SNI evasion, and multi-vector fragmentation to circumvent stateful filtering and DNS poisoning.

### United Arab Emirates and Middle East
- **Supported Operators:**
  - Etisalat (e&)
  - du (Emirates Integrated Telecommunications Company)
  - STC (Saudi Telecom Company)
  - Ooredoo
  - Omantel
  - Zain
- **Inspection Characteristics:** Bypasses VoIP, WebRTC signaling restrictions, and SNI inspection blocks.

### Turkey
- **Supported Operators:**
  - Turk Telekom (Fixed Broadband and Mobile)
  - Turkcell / Superonline (Fiber and LTE)
  - Vodafone Turkey (Fixed and Mobile)
  - Turksat Kablonet
- **Inspection Characteristics:** Counteracts BTK domain blocking, DNS manipulation, and TCP resets.

### Russia
- **Supported Operators:**
  - Rostelecom
  - MTS
  - Beeline
  - MegaFon
  - ER-Telecom (Dom.ru)
  - Tele2 Russia
- **Inspection Characteristics:** Bypasses RKN TSPU inspection filters via dynamic TTL calculation and reverse packet segmentation.

### Global / Auto
- Automatically tests available profiles against Discord Gateway endpoints and selects the fastest, working route. Defaults to Auto on initial launch.

---

## Installation

Download and run `Discord Shield Setup.exe` from the latest release. The setup wizard installs all necessary components and creates desktop and start menu shortcuts.

---

## Uninstallation

Discord Shield can be removed cleanly at any time:
- **Via Windows Settings:** Open Apps & Features, search for **Discord Shield**, and click **Uninstall**.
- **Via Installation Folder:** Run `Uninstall Discord Shield.exe` located directly in the program directory.

The uninstaller stops running engines, unloads filter drivers, removes program files, and cleans registry entries.

---

## License

Copyright (c) 2026 Discord Shield Contributors. All rights reserved.

Released under the [MIT License](LICENSE).
