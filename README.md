<p align="center">
  <img src="src/app_logo.png" alt="Discord Shield" width="128" />
</p>

<h1 align="center">Discord Shield</h1>

<p align="center">
  Zero-Trust Dual-Layer DPI Evasion and Encrypted Tunnel for Discord
</p>

---

Discord Shield is a lightweight, open-source Windows application engineered to restore reliable, low-latency connectivity to Discord across regions with stateful Deep Packet Inspection (DPI), DNS manipulation, and TLS Server Name Indication (SNI) censorship.

Unlike traditional VPN solutions that tunnel entire operating system network traffic through remote servers, Discord Shield functions entirely locally. It neutralizes censorship filters by desynchronizing TCP packet headers, segmenting TLS ClientHello handshakes, and enforcing encrypted DNS-over-HTTPS queries without third-party data collection or external server overhead.

---

## Technical Overview

Discord Shield deploys a multi-layer evasion stack designed specifically for Discord's desktop architecture and Cloudflare edge infrastructure:

1. **Application-Level TLS Record Splitting**
   The application intercepts TLS connection handshakes destined for Discord endpoints (`discord.com`, `gateway.discord.gg`, `cdn.discordapp.com`) and segments the `ClientHello` message immediately prior to the SNI extension. DPI middleboxes cannot inspect the fragmented host value and allow the connection to pass, while remote destination servers reassemble the standard stream without error.

2. **Native Process Bridge**
   When connected, the utility injects a local bridge configuration (`version.dll` and `drover.ini`) into the user's active Discord installation directory (`%LocalAppData%\Discord\app-*`). This directs Discord traffic through the local evasion proxy without altering system-wide network adapters, TAP interfaces, or general web browsers.

3. **Kernel-Level Packet Desynchronization**
   A low-level kernel driver suppresses unencrypted HTTP/3 (QUIC) fallback attempts, filters spoofed TCP RST packets injected by censorship hardware, and applies sequence number desynchronization for strict DPI middleboxes.

4. **Hardened DNS-over-HTTPS (DoH)**
   System DNS templates are configured to query encrypted Cloudflare and Google Anycast resolvers over HTTPS without unencrypted UDP fallback, preventing ISP DNS hijacking, sinkholing, and cache poisoning.

---

## Supported Regions and Network Operators

Discord Shield includes tailored desynchronization profiles optimized for the inspection equipment deployed across major telecommunications providers:

### Turkey
- **Supported Operators:**
  - Turk Telekom (Fixed Broadband and Mobile)
  - Turkcell / Superonline (Fiber Broadband and LTE)
  - Vodafone Turkey (Fixed Broadband and Mobile)
  - Turksat Kablonet
- **Inspection Characteristics:** Operates against BTK-mandated domain blocking, DNS redirection, and stateful SNI TCP resets.

### Russia
- **Supported Operators:**
  - Rostelecom
  - MTS (Mobile TeleSystems)
  - Beeline (PJSC VimpelCom)
  - MegaFon
  - ER-Telecom (Dom.ru)
  - Tele2 Russia
- **Inspection Characteristics:** Bypasses RKN TSPU (Technical Means of Countering Threats) DPI filters through dynamic TTL auto-calculation and out-of-order TCP segmentation.

### Iran
- **Supported Operators:**
  - Shatel (Fixed Broadband & Optical Fiber)
  - Asiatech (ADSL & Enterprise)
  - Zitel (Fixed Wireless Access / TD-LTE)
  - Pars Online (Fixed Broadband)
  - RighTel (3G / 4G / 5G Mobile)
  - Sabanet (ADSL & VDSL)
  - Pishgaman (Fixed Broadband)
  - Hi-Web (ADSL & Fixed Wireless)
  - Telecommunication Company of Iran (TCI / Mokhaberat)
  - MTN Irancell (TD-LTE & Mobile)
  - Hamrah-e Aval / MCI (Mobile & Fixed)
- **Inspection Characteristics:** Employs multi-stage out-of-band (OOB) TCP desynchronization, whitelisted fake SNI injection (`www.cloudflare.com`), TLS record splitting, and non-standard DNS routing to neutralize `10.10.34.34` DNS hijacking and stateful DPI middlebox resets across national and regional transit gateways.

### United Arab Emirates and Gulf Region
- **Supported Operators:**
  - Etisalat (e&)
  - du (Emirates Integrated Telecommunications Company)
  - Saudi Telecom Company (STC)
  - Ooredoo
  - Omantel
- **Inspection Characteristics:** Bypasses VoIP and WebRTC protocol restrictions alongside standard SNI filtering.

### Global / Automatic
- Evaluates latency across multiple evasion profiles against the live Discord Gateway API (`https://discord.com/api/v9/gateway`) and activates the first verified profile. Compatible with restrictive university, corporate, and public Wi-Fi networks.

---

## Installation

Download and execute `Discord Shield Setup.exe` from the latest release to install the application. Administrator privileges are required to configure the local network filter driver and system DNS templates.

---

## Uninstallation

Discord Shield provides clean, single-click uninstallation:
- **Via Control Panel / Settings:** Open Windows Installed Apps, search for **Discord Shield**, and select **Uninstall**.
- **Via Local Installation Folder:** Run `Uninstall Discord Shield.exe` located directly in the installation directory (`%ProgramFiles%\Discord Shield` or standalone folder).

The uninstaller terminates active background engines, unloads the kernel packet filter driver, restores Discord application folders by removing bridge files (`version.dll` and `drover.ini`), and cleans associated registry configurations.

---

## Copyright and License

Copyright (c) 2026 Discord Shield Contributors. All rights reserved.

This software is released under the terms of the [MIT License](LICENSE). Embedded drivers and core evasion engines retain their respective upstream licenses.
