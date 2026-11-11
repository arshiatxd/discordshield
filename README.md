<p align="center">
  <img src="src/app_logo.png" alt="Discord Shield" width="128" />
</p>

<h1 align="center">Discord Shield</h1>

<p align="center">
  Zero-Trust Dual-Layer DPI Evasion and Encrypted Tunnel for Discord
</p>

---

Discord Shield is a lightweight, open-source Windows application engineered to restore reliable, low-latency connectivity to Discord across regions with stateful Deep Packet Inspection (DPI), DNS manipulation, and TLS Server Name Indication (SNI) censorship.

Unlike traditional VPN solutions that route all operating system traffic through remote proxy servers, Discord Shield functions entirely on your local machine. It neutralizes censorship filters by desynchronizing TCP packet headers, splitting TLS ClientHello handshakes, and bypassing deep packet inspection middleboxes without third-party data collection, accounts, or external proxy latency.

---

## Technical Overview

Discord Shield deploys an advanced local evasion stack tailored specifically for Discord's desktop architecture and Cloudflare edge infrastructure:

1. **Application-Level TLS Record Splitting and Fake SNI Desynchronization**
   The evasion engine segments outgoing TLS handshakes targeted at Discord endpoints (`discord.com`, `gateway.discord.gg`, `cdn.discordapp.com`). By fragmenting the `ClientHello` message across multiple records and injecting out-of-band fake SNI indicators, censorship middleboxes fail to reconstruct the forbidden hostname, allowing the encrypted stream to pass unobstructed.

2. **Kernel-Level Packet Desynchronization**
   A dedicated local packet filter driver analyzes outbound Discord packets in real-time. It suppresses blocked HTTP/3 (QUIC) attempts, drops censorship-injected TCP RST flags, and desynchronizes packet sequence numbers to prevent stateful tracking by hardware firewalls.

3. **Universal DNS Resolver Compatibility**
   Discord Shield operates seamlessly alongside any existing system DNS configuration. Users with custom resolvers (such as Cloudflare `1.1.1.1`, Google `8.8.8.8`, Quad9 `9.9.9.9`, or standard ISP DNS) will not experience query stalling or handshake timeouts. Discord Gateway endpoints are validated directly against verified Cloudflare Anycast edge IP ranges.

4. **100% Local and Zero-Trust Execution**
   All filtering and packet manipulation take place directly within system memory. No user credentials, tokens, voice packets, or text messages are ever redirected, intercepted, or transmitted to remote servers.

---

## Supported Regions and Network Operators

Discord Shield includes targeted desynchronization profiles optimized for the inspection equipment deployed across major telecommunications providers:

### Iran
- **Supported Operators:**
  - Shatel
  - Asiatech
  - Zitel
  - Pars Online
  - Rightel
  - Sabanet
  - Pishgaman
  - Hi-Web
  - Hamrah-e Aval (MCI)
  - Irancell (MTN)
  - Telecommunication Company of Iran (TCI / Mokhaberat)
- **Inspection Characteristics:** Employs out-of-band (OOB) TCP segmentation, reverse packet fragmentation, and fake SNI injection to neutralize stateful DPI filtering. Fully compatible with both cellular LTE and fixed broadband infrastructure.

### United Arab Emirates and Middle East
- **Supported Operators:**
  - Etisalat (e&)
  - du (Emirates Integrated Telecommunications Company)
  - Saudi Telecom Company (STC)
  - Ooredoo
  - Omantel
  - Zain
- **Inspection Characteristics:** Overcomes VoIP throttling, WebRTC gateway blocks, and SNI domain restrictions across regional telecom networks.

### Turkey
- **Supported Operators:**
  - Turk Telekom (Fixed Broadband and Mobile)
  - Turkcell / Superonline (Fiber and LTE)
  - Vodafone Turkey
  - Turksat Kablonet
- **Inspection Characteristics:** Bypasses BTK-mandated domain blocks, DNS redirection, and stateful TCP resets.

### Russia
- **Supported Operators:**
  - Rostelecom
  - MTS (Mobile TeleSystems)
  - Beeline (PJSC VimpelCom)
  - MegaFon
  - ER-Telecom (Dom.ru)
  - Tele2 Russia
- **Inspection Characteristics:** Bypasses RKN TSPU hardware filters using dynamic TTL shielding and out-of-order TCP record delivery.

### Global / Auto-Detect
- Features automatic multi-profile verification against the live Discord Gateway API (`https://discord.com/api/v9/gateway`). Activates the most responsive verified profile for restricted school, university, enterprise, and public Wi-Fi networks.

---

## Installation

Download and run `Discord Shield Setup.exe` from the latest release. Administrator privileges are required during setup to configure the local packet filter driver.

---

## Uninstallation

Discord Shield provides clean, single-click uninstallation:
- **Via Windows Settings:** Open Windows Installed Apps, search for **Discord Shield**, and click **Uninstall**.
- **Via Installation Folder:** Execute `Uninstall Discord Shield.exe` located directly in the installation directory (`%ProgramFiles%\Discord Shield`).

The uninstallation process stops active background services, safely unloads the kernel filter driver, and removes all program files and configuration keys.

---

## Privacy Policy

Discord Shield operates entirely on the user's local machine:
- **No Data Collection:** Personal data, account tokens, chat history, and voice streams are never collected, logged, or monitored.
- **Local In-Memory Operation:** Packet desynchronization occurs entirely in local system memory without intermediary servers or remote relays.
- **No Telemetry:** The application contains zero analytics beacons, telemetry frameworks, or external trackers.
- **Open Source:** Full source code is open and verifiable under the MIT license.

---

## Copyright and License

Copyright (c) 2026 Discord Shield Contributors. All rights reserved.

This software is released under the terms of the [MIT License](LICENSE). Embedded drivers and circumvention components retain their respective upstream licenses.
