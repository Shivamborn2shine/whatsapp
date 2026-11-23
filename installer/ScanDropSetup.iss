; Scan Drop Windows Installer Script — Inno Setup 6 (PRD v2.0)
; Generates ScanDropSetup.exe for Windows 10/11 (x64)

#define MyAppName "Scan Drop"
#define MyAppVersion "4.0.2"
#define MyAppPublisher "Scan Drop"
#define MyAppURL "https://scandrop-xi.vercel.app"
#define MyAppExeName "ScanDrop.exe"

[Setup]
AppId={{D37F8E80-99E4-4286-9045-8C1B33B3F882}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\Scan Drop
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=ScanDropSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Run Scan Drop receiver automatically in background when Windows starts"; GroupDescription: "Background Operation:"

[Files]
Source: "..\bin\ScanDrop.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\cloudflared.exe"; DestDir: "{app}\bin"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\index.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\workspace\index.html"; DestDir: "{app}\workspace"; Flags: ignoreversion
Source: "..\version.json"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
; Create PRD Section 6 standardized data directory under C:\ProgramData\ScanDrop with user write permissions
Name: "{commonappdata}\ScanDrop"; Permissions: users-modify
Name: "{commonappdata}\ScanDrop\database"; Permissions: users-modify
Name: "{commonappdata}\ScanDrop\uploads"; Permissions: users-modify
Name: "{commonappdata}\ScanDrop\uploads\temp"; Permissions: users-modify
Name: "{commonappdata}\ScanDrop\uploads\received"; Permissions: users-modify
Name: "{commonappdata}\ScanDrop\uploads\quarantine"; Permissions: users-modify
Name: "{commonappdata}\ScanDrop\converted"; Permissions: users-modify
Name: "{commonappdata}\ScanDrop\previews"; Permissions: users-modify
Name: "{commonappdata}\ScanDrop\logs"; Permissions: users-modify
Name: "{commonappdata}\ScanDrop\config"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ScanDropStation"; ValueData: """{app}\{#MyAppExeName}"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Scan Drop Setup Wizard"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\ScanDrop\uploads\temp"
Type: filesandordirs; Name: "{commonappdata}\ScanDrop\previews"
