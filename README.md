<p align="center">
  <img src="src/app_logo.png" alt="Discord Shield" width="128" />
</p>

<h1 align="center">Discord Shield</h1>

<p align="center">
  Zero-Trust Local DPI Evasion and Encrypted Tunnel for Discord
</p>

---

Discord Shield is a lightweight, open-source Windows application engineered to restore reliable, low-latency connectivity to Discord across networks affected by stateful Deep Packet Inspection (DPI), DNS manipulation, and TLS Server Name Indication (SNI) filtering.

Unlike VPN providers that route all system traffic through remote proxy servers, Discord Shield operates entirely on the local computer. It neutralizes censorship middleboxes by segmenting TLS ClientHello handshakes, desynchronizing TCP packet headers, and ensuring clean DNS query resolution without intermediary servers, accounts, or privacy risks.

---

## Technical Overview

Discord Shield deploys a multi-layer evasion stack designed specifically for Discord's desktop architecture and Cloudflare edge infrastructure:

1. **Application-Level TLS Record Splitting**
   The application intercepts TLS handshakes destined for Discord endpoints and splits the `ClientHello` message across records right before the SNI extension. DPI middleboxes cannot inspect the fragmented host header and allow the stream through, while remote destination edge servers reassemble the standard TLS stream cleanly.

2. **Kernel-Level Packet Desynchronization**
   A local packet filter driver suppresses unencrypted HTTP/3 (QUIC) attempts, drops censorship-injected TCP RST flags, and applies out-of-order sequence number fragmentation to defeat stateful tracking.

3. **Universal DNS Resolver Compatibility**
   Discord Shield operates seamlessly alongside any existing system DNS configuration. Users with custom resolvers (such as Cloudflare 1.1.1.1, Google 8.8.8.8, Quad9 9.9.9.9, or standard ISP DNS) will not experience query stalling or handshake timeouts.

4. **100% Local and Zero-Trust Execution**
   All evasion and packet manipulation take place directly within local system memory. No user credentials, tokens, voice packets, or text messages are ever redirected, intercepted, or transmitted to remote servers.

---

## Supported Regions and Network Operators

Discord Shield includes targeted desynchronization profiles optimized for the inspection equipment deployed across major telecommunications providers:

### Iran
- **Supported Operators:**
  - Shatel (ADSL, VDSL, Fiber)
  - Asiatech
  - Zitel (TD-LTE)
  - Pars Online
  - RighTel (3G, 4G, 5G)
  - Sabanet
  - Pishgaman
  - Hi-Web
  - Hamrah-e Aval / MCI (Mobile and FWA)
  - Irancell / MTN (Mobile and TD-LTE)
  - Telecommunication Company of Iran (TCI / Mokhaberat)
- **Inspection Characteristics:** Out-of-band (OOB) TCP desync, whitelisted fake SNI injection, TLS record fragmentation, and clean DNS query routing.

### United Arab Emirates and Middle East
- **Supported Operators:**
  - Etisalat by e&
  - du (Emirates Integrated Telecommunications Company)
  - Saudi Telecom Company (STC)
  - Zain
  - Ooredoo
  - Omantel
- **Inspection Characteristics:** Countermeasures for VoIP, WebRTC protocol throttling, and domain-level inspection.

### Turkey
- **Supported Operators:**
  - Turk Telekom (TTNET, VDSL, Fiber, and TT Mobil)
  - Turkcell Superonline (Fiber, VDSL, and LTE)
  - Vodafone Turkey (Fixed Broadband and Mobile)
  - Turksat Kablonet (Cable Internet and Fiber)
  - TurkNet (GigaFiber and VDSL)
  - Millenicom
  - D-Smart Net
  - NetSpeed
  - GibirNet
  - Comnet
- **Inspection Characteristics:** Out-of-order TCP segmentation, SNI offset splitting, reverse packet fragmentation, and selective QUIC suppression to evade BTK-mandated domain blocking and TCP reset injections.

### Russia
- **Supported Operators:**
  - Rostelecom
  - MTS (Mobile TeleSystems)
  - Beeline (PJSC VimpelCom)
  - MegaFon
  - ER-Telecom (Dom.ru)
  - Tele2 Russia
- **Inspection Characteristics:** Bypasses RKN TSPU hardware filters via dynamic TTL shielding and TCP segmentation.

### Global / Auto-Detect
- Automatically evaluates latency across candidate evasion profiles against the live Discord Gateway API (`https://discord.com/api/v9/gateway`) and activates the first verified profile. Compatible with university campuses, corporate environments, and public Wi-Fi networks.

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

## Uninstallation

Discord Shield provides clean, single-click uninstallation:
- **Via Windows Settings:** Open Installed Apps, search for **Discord Shield**, and select **Uninstall**.
- **Via Installation Folder:** Run `Uninstall Discord Shield.exe` located directly in the installation folder.

The uninstaller terminates active background engines, safely unloads the kernel packet filter driver, and removes all program files and configuration keys.

---

## Copyright and License

Copyright (c) 2026 Discord Shield Contributors. All rights reserved.

This software is released under the terms of the [MIT License](LICENSE). Embedded drivers and circumvention components retain their respective upstream licenses.
