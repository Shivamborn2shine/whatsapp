; Scan Drop Windows Setup Script — Inno Setup 6
; Generates ScanDropSetup.exe

#define MyAppName "Scan Drop"
#define MyAppVersion "3.0.0"
#define MyAppPublisher "Scan Drop Technologies"
#define MyAppURL "https://scandrop.local"
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
OutputBaseFilename=ScanDropSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Start Scan Drop Background Sync Service automatically on Windows startup"; GroupDescription: "Windows Background Service:"

[Files]
Source: "..\desktop\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\ScanDropService"; Filename: "{app}\resources\ScanDrop.Service.exe"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Dirs]
; Pre-create standardized Scan Drop local directories (PRD Section 7.1)
Name: "{userdocs}\..\ScanDrop\Incoming"
Name: "{userdocs}\..\ScanDrop\Processing"
Name: "{userdocs}\..\ScanDrop\Completed"
Name: "{userdocs}\..\ScanDrop\Derived"
Name: "{userdocs}\..\ScanDrop\Temporary"
Name: "{userdocs}\..\ScanDrop\Quarantine"

[UninstallDelete]
Type: filesandordirs; Name: "{userdocs}\..\ScanDrop\Temporary\*"

