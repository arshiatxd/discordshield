<p align="center">
  <img src="src/app_logo.png" alt="Discord Shield" width="128" />
</p>

<h1 align="center">Discord Shield</h1>

<p align="center">
  Zero-Trust Dual-Layer DPI Evasion and Encrypted Tunnel for Discord
</p>

---

Discord Shield is a lightweight, open-source Windows application engineered to restore reliable, low-latency connectivity to Discord across networks with stateful Deep Packet Inspection (DPI), DNS manipulation, and TLS Server Name Indication (SNI) filtering.

Unlike traditional VPN solutions that tunnel entire system traffic through remote servers, Discord Shield functions completely locally. It circumvents censorship filters by desynchronizing TCP packet headers, segmenting TLS ClientHello handshakes, and enforcing encrypted DNS-over-HTTPS queries without third-party data collection or external server overhead.

---

## Technical Overview

Discord Shield deploys a multi-layer evasion stack designed specifically for Discord's architecture and Cloudflare edge infrastructure:

1. **Kernel-Level Packet Desynchronization**
   A low-level network packet filter driver intercepts Discord TCP connections (`discord.com`, `gateway.discord.gg`, `cdn.discordapp.com`) directly on the local network adapter, desynchronizing TCP sequence numbers and segmenting the TLS ClientHello packet before the SNI extension reaches inspection middleboxes.

2. **Transparent Out-of-Band (OOB) and Fake SNI Shielding**
   For aggressive stateful firewalls, Discord Shield generates out-of-band data fragments and pre-connection fake SNI packets with shortened Time-To-Live (TTL) values. The inspection middlebox registers the decoy data, while destination edge servers reassemble the genuine handshake.

3. **Hardened DNS-over-HTTPS (DoH)**
   System DNS templates query encrypted Cloudflare and Google Anycast resolvers over HTTPS without unencrypted UDP fallback, neutralizing ISP-level DNS poisoning, sinkholing, and NXDOMAIN manipulation.

4. **Zero-Trust Local Execution**
   All packet filtering runs strictly on the user machine without third-party servers, DLL injection, or persistent proxy configuration.

---

## Supported Regions and Network Operators

Discord Shield includes optimized evasion profiles tailored for specific censorship architectures:

### Iran (Primary Profile)
- **Supported Operators:**
  - Shatel (ADSL, VDSL, Optical Fiber)
  - Asiatech (ADSL, VDSL, OWA)
  - Zitel (TD-LTE)
  - Pars Online
  - Rightel (3G / 4G Mobile)
  - Sabanet
  - Pishgaman
  - Hi-Web
  - Hamrah-e Aval / MCI (Mobile Data and TD-LTE)
  - Irancell / MTN (Mobile Data and TD-LTE)
  - Telecommunication Company of Iran (TCI / Mokhaberat)
- **Profile Characteristics:** Multi-strategy desynchronization utilizing out-of-band TLS record splitting, customized fake SNI injection, and encrypted DoH routing to bypass stateful SNI inspection and DNS manipulation.

### Turkey
- **Supported Operators:**
  - Turk Telekom (Fixed Broadband and Mobile)
  - Turkcell / Superonline (Fiber and LTE)
  - Vodafone Turkey (Fixed Broadband and Mobile)
  - Turksat Kablonet
- **Profile Characteristics:** TLS record splitting and SNI desynchronization targeting BTK-mandated blocks and TCP RST injection.

### Russia
- **Supported Operators:**
  - Rostelecom
  - MTS (Mobile TeleSystems)
  - Beeline (PJSC VimpelCom)
  - MegaFon
  - ER-Telecom (Dom.ru)
  - Tele2 Russia
- **Profile Characteristics:** Dynamic TTL auto-calculation, reverse-order packet fragmentation, and QUIC suppression to bypass RKN TSPU equipment.

### United Arab Emirates and Middle East
- **Supported Operators:**
  - Etisalat by e& (UAE)
  - du / Emirates Integrated Telecommunications (UAE)
  - Saudi Telecom Company / STC (Saudi Arabia)
  - Zain (Saudi Arabia, Kuwait, Bahrain)
  - Mobily (Saudi Arabia)
  - Ooredoo (Qatar and Oman)
  - Omantel (Oman)
- **Profile Characteristics:** Desynchronization tailored for VoIP, WebRTC signaling, and gateway handshakes.

### Global / Auto (Smart Route)
- **Default Behavior:** Automatically evaluates latency against the live Discord Gateway API (`https://discord.com/api/v9/gateway`) across all regional evasion profiles and locks onto the first verified route.
- **Coverage:** University campuses, corporate environments, hotel Wi-Fi, and municipal networks running restrictive perimeter firewalls (Fortinet, Palo Alto Networks, Cisco Umbrella, Sophos).

---

## Installation

1. Download and run `Discord Shield Setup.exe` from the latest release.
2. Complete the setup wizard. Administrator privileges are required to configure the local network filter driver.
3. Launch Discord Shield from your desktop or start menu and click **Connect**.
4. By default, the application is set to **Auto (Smart Route)**, which selects the optimal bypass profile for your network automatically.

---

## Uninstallation

Discord Shield provides a clean uninstaller:
- **Via Settings / Control Panel:** Navigate to Installed Apps, select **Discord Shield**, and click **Uninstall**.
- **Via Installation Folder:** Run `Uninstall Discord Shield.exe` located in the application installation directory (`%ProgramFiles%\Discord Shield`).

The uninstaller stops background engines, removes the driver service, and deletes all program files and shortcuts.

---

## Security and Privacy

- **Zero Data Collection:** Discord Shield does not collect, log, or transmit account tokens, messages, voice packets, or user identifiers.
- **Local Operation:** Traffic connects directly between your computer and Discord servers.
- **Open Source:** The full codebase and build process are auditable in this repository.

---

## License

Copyright (c) 2026 Discord Shield Contributors. All rights reserved.

Released under the terms of the [MIT License](LICENSE).
