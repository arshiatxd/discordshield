#define MyAppName "Discord Shield"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "Discord Shield Contributors"
#define MyAppURL "https://github.com/arshiatxd/discordshield"
#define MyAppExeName "DiscordTunnel.exe"
#ifndef SourceDir
#define SourceDir "..\publish"
#endif

[Setup]
AppId={{5C849FA9-1E14-4B28-868C-BD9D0F32E2A8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion=1.0.1.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} v{#MyAppVersion} Setup Wizard
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir=..\releases
OutputBaseFilename=Discord Shield Setup
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} v{#MyAppVersion}
WizardImageFile=wizard_banner.bmp
WizardSmallImageFile=wizard_small.bmp
WizardImageStretch=yes
WizardSizePercent=105,105
Compression=lzma2/normal
SolidCompression=no
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter=DiscordTunnel.exe,DiscordShieldEngine.exe,ciadpi.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupWindowTitle=Setup - {#MyAppName} v{#MyAppVersion}
WelcomeLabel1=Welcome to the {#MyAppName} v{#MyAppVersion} Setup Wizard
WelcomeLabel2=This wizard will install {#MyAppName} v{#MyAppVersion} on your computer.%n%n{#MyAppName} provides a Zero-Trust Dual-Layer DPI Evasion & Local Tunnel engineered to restore instant, low-latency Discord connectivity for both the desktop app and discord.com web services across restricted networks (Turkey, Iran, UAE / Middle East, Russia, and Global routes).%n%nClick Next to continue.
FinishedHeadingLabel=Completing the {#MyAppName} v{#MyAppVersion} Setup Wizard
FinishedLabelNoIcons=Setup has finished installing {#MyAppName} v{#MyAppVersion} on your computer. Your dual-layer DPI evasion and proxy engine is ready.
FinishedLabel=Setup has finished installing {#MyAppName} v{#MyAppVersion} on your computer. The application may be launched by selecting the installed shortcuts.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "startupicon"; Description: "Launch {#MyAppName} automatically on Windows startup"; GroupDescription: "System Integration:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\DiscordTunnel.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\DiscordTunnel.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\DiscordTunnel.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\DiscordTunnel.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Uninstall Discord Shield.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\flag_ir.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Core\DiscordShieldEngine.exe"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\WinDivert.dll"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\WinDivert64.sys"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\discord-list.txt"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\ciadpi.exe"; DestDir: "{app}\Core"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{app}\Uninstall Discord Shield.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop WinDivert"; Flags: runhidden; RunOnceId: "StopDriver"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Core"
Type: filesandordirs; Name: "{app}"
