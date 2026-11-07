#define MyAppName "Discord Shield"
#define MyAppVersion "1.0"
#define MyAppPublisher "Discord Shield"
#define MyAppURL "https://discord.com"
#define MyAppExeName "DiscordTunnel.exe"
#define SourceDir "..\publish"

[Setup]
AppId={{5C849FA9-1E14-4B28-868C-BD9D0F32E2A8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion=1.0.0.0
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion=1.0
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
Compression=lzma2/ultra64
SolidCompression=yes
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
WelcomeLabel2=This wizard will install {#MyAppName} v{#MyAppVersion} on your computer.%n%n{#MyAppName} provides a Zero-Trust Dual-Layer DPI Bypass & Encrypted DoH Tunnel engineered to restore instant, low-latency Discord connectivity across restricted networks (Turkey, Russia, Iran, UAE, and Global routes).%n%nClick Next to continue.
FinishedHeadingLabel=Completing the {#MyAppName} v{#MyAppVersion} Setup Wizard
FinishedLabelNoIcons=Setup has finished installing {#MyAppName} v{#MyAppVersion} on your computer. Your dual-layer DPI bypass engine is ready.
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
Source: "{#SourceDir}\Core\DiscordShieldEngine.exe"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\WinDivert.dll"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\WinDivert64.sys"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\discord-list.txt"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\ciadpi.exe"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\version.dll"; DestDir: "{app}\Core"; Flags: ignoreversion
Source: "{#SourceDir}\Core\drover.ini"; DestDir: "{app}\Core"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{app}\Uninstall Discord Shield.exe"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\cmd.exe"; Parameters: "/c taskkill /f /im DiscordTunnel.exe & taskkill /f /im DiscordShieldEngine.exe & taskkill /f /im ciadpi.exe & sc stop WinDivert"; Flags: runhidden; RunOnceId: "DiscordShieldUninstallCleanup"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Core"
Type: filesandordirs; Name: "{app}"
