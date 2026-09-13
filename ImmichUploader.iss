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
; Upgrade hardening:
;  - CloseApplications lets Restart Manager close the running tray app so the
;    locked exe can be replaced.
;  - RestartApplications=no prevents a double launch (the [Run] entry relaunches
;    it exactly once after install).
;  - AppMutex matches Program.SingleInstanceMutexName in the app.
CloseApplications=yes
RestartApplications=no
AppMutex=ImmichUploader.SingleInstance

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Launch Immich Uploader when Windows starts"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "setup.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "Upload-Immich.ps1"; DestDir: "{app}"; Flags: ignoreversion
; Bundle immich-go.exe when it is present next to the project or one folder up.
#if FileExists(AddBackslash(SourcePath) + "immich-go.exe")
Source: "immich-go.exe"; DestDir: "{app}"; Flags: ignoreversion
#elif FileExists(AddBackslash(SourcePath) + "..\immich-go.exe")
Source: "..\immich-go.exe"; DestDir: "{app}"; Flags: ignoreversion
#endif

[Icons]
Name: "{autoprograms}\Immich Uploader"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\Immich Uploader Logs"; Filename: "{app}\Logs"
Name: "{autodesktop}\Immich Uploader"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ImmichUploader"; ValueData: """{app}\{#MyAppExeName}"" --tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppDisplayName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C choice /C Y /N /D Y /T 1 >nul"; Flags: runhidden; RunOnceId: "DelayDelete"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\Logs"
Type: filesandordirs; Name: "{app}\state.json"
Type: filesandordirs; Name: "{app}\config.json"

[Code]
{ Compare two dotted version strings numerically. Returns -1, 0, or 1. }
function VersionPart(const V: String; Index: Integer): Integer;
var
  I, Part, Start: Integer;
begin
  Result := 0;
  Part := 0;
  Start := 1;
  for I := 1 to Length(V) + 1 do
  begin
    if (I > Length(V)) or (V[I] = '.') then
    begin
      if Part = Index then
      begin
        Result := StrToIntDef(Copy(V, Start, I - Start), 0);
        Exit;
      end;
      Inc(Part);
      Start := I + 1;
    end;
  end;
end;

function CompareVersionStr(const A, B: String): Integer;
var
  I, VA, VB: Integer;
begin
  for I := 0 to 3 do
  begin
    VA := VersionPart(A, I);
    VB := VersionPart(B, I);
    if VA < VB then
    begin
      Result := -1;
      Exit;
    end;
    if VA > VB then
    begin
      Result := 1;
      Exit;
    end;
  end;
  Result := 0;
end;

function InitializeSetup: Boolean;
var
  Installed: String;
  Key: String;
begin
  Result := True;
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1';

  if not (RegQueryStringValue(HKLM, Key, 'DisplayVersion', Installed)
          or RegQueryStringValue(HKCU, Key, 'DisplayVersion', Installed)) then
    Exit;

  { Block accidental downgrades unless the user explicitly confirms. }
  if CompareVersionStr(Installed, '{#MyAppVersion}') > 0 then
  begin
    if MsgBox('A newer version of {#MyAppDisplayName} (' + Installed + ') is already installed.' + #13#10 + #13#10 +
              'Install this older version (' + '{#MyAppVersion}' + ') anyway?',
              mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
