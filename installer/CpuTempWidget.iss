; Inno Setup 6 script for the CPU Temperature Widget (end-user installer).
; Build with scripts\Build-Installer.ps1 (it publishes the app self-contained, so users need no .NET install).
; This installer does NOT contain any AMD files: AMD's Ryzen Master Monitoring SDK is licensed by AMD and
; has to be downloaded by the user. Setup checks for it and explains how to get it.
;
; Testing switch: run Setup with /FAKEMISSING=1 to see the wizard's "missing SDK / runtime" warnings on a PC
; that has both installed.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define AppName "CPU Temperature Widget"
#define AppExe "CpuTempWidget.exe"
#define RepoUrl "https://github.com/eddygk/cpu-temp-widget"
#define SdkPageUrl "https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html"
#define VcRedistUrl "https://aka.ms/vs/17/release/vc_redist.x64.exe"

[Setup]
AppId={{B7E3D9A2-5C41-4F0B-9E6D-2A8C1F7D3E54}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=eddygk
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\CPU Temperature Widget
DisableProgramGroupPage=yes
; The widget needs administrator rights (AMD's SDK loads a kernel driver), and the sign-in task runs elevated.
PrivilegesRequired=admin
; {username} below is deliberate: the sign-in task belongs to the person who runs Setup.
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\dist
OutputBaseFilename=CpuTempWidget-Setup-{#AppVersion}
SetupIconFile=..\app\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Without this, Windows would stop an in-use widget from being replaced; PrepareToInstall closes it instead.
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start the widget automatically when I sign in to Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\Register-StartupTask.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Sign-in task (runs elevated without a UAC prompt each time). Unticking the option removes an older task.
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Register-StartupTask.ps1"" -Exe ""{app}\{#AppExe}"" -User ""{username}"""; Flags: runhidden waituntilterminated; Tasks: startup; StatusMsg: "Setting up start at sign-in..."
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Register-StartupTask.ps1"" -Remove"; Flags: runhidden waituntilterminated; Check: StartupNotSelected
; runascurrentuser: reuse setup's administrator rights, so no second UAC prompt.
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillWidget"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Register-StartupTask.ps1"" -Remove"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveStartupTask"

[Code]
var
  RequirementsPage: TWizardPage;

function FakeMissing: Boolean;
begin
  Result := ExpandConstant('{param:FAKEMISSING|0}') = '1';
end;

function IsAmdCpu: Boolean;
var
  Vendor: String;
begin
  Result := RegQueryStringValue(HKLM, 'HARDWARE\DESCRIPTION\System\CentralProcessor\0', 'VendorIdentifier', Vendor)
            and (Vendor = 'AuthenticAMD');
end;

function SdkInstalled: Boolean;
begin
  Result := (not FakeMissing)
            and RegValueExists(HKLM64, 'SOFTWARE\AMD\RyzenMasterMonitoringSDK', 'InstallationPath');
end;

function VcRuntimeInstalled: Boolean;
begin
  Result := (not FakeMissing)
        and FileExists(ExpandConstant('{sys}\vcruntime140.dll'))
        and FileExists(ExpandConstant('{sys}\vcruntime140_1.dll'))
        and FileExists(ExpandConstant('{sys}\msvcp140.dll'));
end;

function StartupNotSelected: Boolean;
begin
  Result := not WizardIsTaskSelected('startup');
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsAmdCpu then
  begin
    MsgBox('This widget reads the temperature through AMD''s Ryzen SDK, so it only works on computers with an AMD Ryzen processor.'
           + #13#10#13#10 + 'Setup will now close.', mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Close a running widget so its files can be replaced (upgrade / reinstall).
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure OpenLink(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExec('open', TNewStaticText(Sender).Hint, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

{ Adds a wrapped line of text under the previous one; the label sizes itself to its text. }
function AddText(const Caption: String; var Y: Integer; Bold: Boolean): TNewStaticText;
begin
  Result := TNewStaticText.Create(RequirementsPage);
  Result.Parent := RequirementsPage.Surface;
  Result.WordWrap := True;
  Result.AutoSize := True;
  Result.Left := 0;
  Result.Top := Y;
  Result.Width := RequirementsPage.SurfaceWidth;
  if Bold then
    Result.Font.Style := [fsBold];
  Result.Caption := Caption;
  Y := Y + Result.Height + ScaleY(8);
end;

procedure AddLink(const Caption, Url: String; var Y: Integer);
var
  L: TNewStaticText;
begin
  L := AddText(Caption, Y, False);
  L.Font.Style := [fsUnderline];
  L.Font.Color := clBlue;
  L.Cursor := crHand;
  L.Hint := Url;
  L.ShowHint := True;
  L.OnClick := @OpenLink;
end;

procedure InitializeWizard;
var
  Y: Integer;
begin
  RequirementsPage := CreateCustomPage(wpWelcome, 'Before you continue',
    'Setup checked your computer for what the widget needs.');
  Y := 0;

  AddText('[OK]   AMD Ryzen processor', Y, False);

  if VcRuntimeInstalled then
    AddText('[OK]   Microsoft Visual C++ runtime', Y, False)
  else
  begin
    AddText('[ ! ]   The Microsoft Visual C++ runtime is missing (AMD''s SDK needs it). It is a free download from Microsoft:', Y, True);
    AddLink('Download the Visual C++ runtime (x64)', '{#VcRedistUrl}', Y);
  end;

  if SdkInstalled then
    AddText('[OK]   AMD Ryzen Master Monitoring SDK', Y, False)
  else
  begin
    AddText('[ ! ]   AMD''s Ryzen Master Monitoring SDK is not installed yet. It is free, but AMD wants you to accept its licence '
            + 'yourself, so this setup cannot include it. You can finish installing the widget now: it shows "--" until the SDK '
            + 'is installed, then picks it up automatically within a few seconds.', Y, True);
    AddLink('Open the AMD download page', '{#SdkPageUrl}', Y);
    AddText('On that page, follow the download link, unzip the file and run AMDRyzenMasterMonitoringSDK.exe.', Y, False);
  end;
end;
