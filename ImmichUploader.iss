; ImmichUploader.iss - Inno Setup script for Immich Uploader tray app
; Build with ISCC.exe to produce a single self-contained installer.exe

#define MyAppName "Immich Uploader"
#define MyAppDisplayName "Immich Uploader"
#define MyAppPublisher "Personal"
#define MyAppExeName "ImmichUploader.exe"
; Bump together with <Version> in ImmichUploader.csproj.
#define MyAppVersion "1.1.0"

[Setup]
AppId={{A3F1C8B2-7E4D-4F2A-9B5C-1D3E6F8A9C2B}
AppName={#MyAppDisplayName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ImmichUploader
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=installer\Output
OutputBaseFilename=ImmichUploader-Setup-{#MyAppVersion}
SetupIconFile=
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppDisplayName}
VersionInfoVersion={#MyAppVersion}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Launch Immich Uploader when Windows starts"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "setup.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Upload-Immich.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Immich Uploader"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\Immich Uploader Logs"; Filename: "{app}\Logs"
Name: "{autodesktop}\Immich Uploader"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ImmichUploader"; ValueData: """{app}\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppDisplayName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C choice /C Y /N /D Y /T 1 >nul"; Flags: runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Logs"
Type: filesandordirs; Name: "{app}\state.json"
Type: filesandordirs; Name: "{app}\config.json"

[Code]
function InitializeSetup: Boolean;
begin
  Result := True;
end;
