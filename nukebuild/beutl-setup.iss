#define MyAppName "Beutl"
#define MyAppPublisher "b-editor"
#define MyAppURL "https://beutl.beditor.net"
#define MyAppExeName "Beutl.exe"
#define MyAppAssocName MyAppName + " Project File"
#define MyAppAssocExt ".bep"
#define MyAppAssocKey StringChange(MyAppAssocName, " ", "") + MyAppAssocExt

; Target architecture. Defaults to x64; the build passes arm64 values for win-arm64.
#ifndef MyArchitecturesAllowed
  #define MyArchitecturesAllowed "x64compatible"
#endif
#ifndef MyArchitecturesInstallIn64BitMode
  #define MyArchitecturesInstallIn64BitMode "x64compatible"
#endif

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{5312CE8C-3104-4305-B72F-56D652FA76F6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
ChangesAssociations=yes
DisableProgramGroupPage=yes
LicenseFile={#MyLicenseFile}
; The install mode dialog recommends installing for the current user, which needs no administrator rights.
; An update keeps the mode of the installation it replaces without asking (UsePreviousPrivileges).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
SetupIconFile={#MySetupIconFile}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode={#MyArchitecturesInstallIn64BitMode}
ArchitecturesAllowed={#MyArchitecturesAllowed}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MySource}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MySource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files
Source: "{#MyGPLLicenseFile}"; Flags: dontcopy

[Registry]
Root: HKA; Subkey: "Software\Classes\beutl"; ValueType: string; ValueName: ""; ValueData: "URL:Beutl Protocol"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\beutl"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\beutl\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\beutl\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocExt}\OpenWithProgids"; ValueType: string; ValueName: "{#MyAppAssocKey}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}"; ValueType: string; ValueName: ""; ValueData: "{#MyAppAssocName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\{#MyAppAssocKey}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: "{#MyAppAssocExt}"; ValueData: ""

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Check: ShouldOfferLaunch
; postinstall starts Beutl as the user who started Setup, not as the administrator an all-users update runs as.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifnotsilent; Check: ShouldRelaunch

[Code]
const
  SYNCHRONIZE = $00100000;
  WAIT_TIMEOUT = $00000102;
  BeutlExitTimeout = 60000;

var
  GPLLicensePage: TOutputMsgMemoWizardPage;
  GPLAcceptedRadio: TRadioButton;
  GPLNotAcceptedRadio: TRadioButton;

function OpenProcess(DesiredAccess: Cardinal; InheritHandle: Integer; ProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Integer;
  external 'CloseHandle@kernel32.dll stdcall';

// Older versions of Beutl show the wizard and pass /BeutlOnQuit=1 when the user is quitting,
// so the installer leaves it closed.
function ShouldOfferLaunch: Boolean;
begin
  Result := ExpandConstant('{param:BeutlOnQuit|0}') = '0';
end;

// Beutl updates itself silently and passes /BeutlRelaunch=1 when the user chose to restart it.
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:BeutlRelaunch|0}') = '1';
end;

// Beutl passes its process ID with /BeutlPid and quits once the installer has started. A silent
// install closes whatever still uses the files, so it waits for Beutl to finish quitting first.
procedure WaitForBeutlToExit;
var
  ProcessId: Integer;
  Process: THandle;
begin
  ProcessId := StrToIntDef(ExpandConstant('{param:BeutlPid|0}'), 0);
  if ProcessId <= 0 then
    Exit;

  Process := OpenProcess(SYNCHRONIZE, 0, ProcessId);
  if Process = 0 then
    Exit;

  try
    Log(Format('Waiting for Beutl (process %d) to exit.', [ProcessId]));
    if WaitForSingleObject(Process, BeutlExitTimeout) = WAIT_TIMEOUT then
      Log('Beutl is still running.');
  finally
    CloseHandle(Process);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  WaitForBeutlToExit;
  Result := '';
end;

procedure CheckGPLAccepted(Sender: TObject);
begin
  WizardForm.NextButton.Enabled := GPLAcceptedRadio.Checked;
end;

function CloneLicenseRadioButton(Source: TRadioButton): TRadioButton;
begin
  Result := TRadioButton.Create(WizardForm);
  Result.Parent := GPLLicensePage.Surface;
  Result.Caption := Source.Caption;
  Result.Left := Source.Left;
  Result.Top := Source.Top;
  Result.Width := Source.Width;
  Result.Height := Source.Height;
  Result.Anchors := Source.Anchors;
  Result.OnClick := @CheckGPLAccepted;
end;

procedure InitializeWizard();
var
  GPLLicenseFileName: string;
  GPLLicenseFilePath: string;
begin
  GPLLicensePage := CreateOutputMsgMemoPage(
    wpLicense,
    SetupMessage(msgWizardLicense),
    'Beutl.FFmpegWorker - GNU General Public License v3',
    SetupMessage(msgLicenseLabel3),
    '');

  GPLLicensePage.RichEditViewer.Height := WizardForm.LicenseMemo.Height;

  GPLLicenseFileName := ExtractFileName('{#MyGPLLicenseFile}');
  ExtractTemporaryFile(GPLLicenseFileName);
  GPLLicenseFilePath := ExpandConstant('{tmp}\' + GPLLicenseFileName);
  GPLLicensePage.RichEditViewer.Lines.LoadFromFile(GPLLicenseFilePath);
  DeleteFile(GPLLicenseFilePath);

  GPLAcceptedRadio := CloneLicenseRadioButton(WizardForm.LicenseAcceptedRadio);
  GPLNotAcceptedRadio := CloneLicenseRadioButton(WizardForm.LicenseNotAcceptedRadio);
  GPLNotAcceptedRadio.Checked := True;
end;

// A silent install skips the license page, and Next could never leave this one without the user.
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = GPLLicensePage.ID) and WizardSilent;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = GPLLicensePage.ID then
  begin
    CheckGPLAccepted(nil);
  end;
end;
