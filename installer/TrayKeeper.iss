#ifndef AppVersion
#define AppVersion "1.3.0"
#endif

[Setup]
AppId={{C482A1B7-9D12-42DE-8C84-EFD830B58E91}
AppName=Tray Keeper
AppVersion={#AppVersion}
AppPublisher=alexlvcom
AppPublisherURL=https://github.com/alexlvcom/TrayKeeper
AppSupportURL=https://github.com/alexlvcom/TrayKeeper/issues
DefaultDirName={autopf}\Tray Keeper
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=TrayKeeper-Setup
SetupIconFile=..\src\TrayKeeper\Resources\app.ico
UninstallDisplayIcon={app}\TrayKeeper.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
Name: startup; Description: "Start with Windows"; Flags: unchecked

[Files]
Source: "..\publish\installer\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{autoprograms}\TrayKeeper.lnk"
Type: files; Name: "{autodesktop}\TrayKeeper.lnk"
Type: files; Name: "{autostartup}\TrayKeeper.lnk"
Type: files; Name: "{userprograms}\TrayKeeper.lnk"
Type: files; Name: "{userdesktop}\TrayKeeper.lnk"
Type: files; Name: "{userstartup}\TrayKeeper.lnk"
Type: files; Name: "{commonprograms}\TrayKeeper.lnk"
Type: files; Name: "{commondesktop}\TrayKeeper.lnk"
Type: files; Name: "{commonstartup}\TrayKeeper.lnk"

[Icons]
Name: "{autoprograms}\Tray Keeper"; Filename: "{app}\TrayKeeper.exe"
Name: "{autodesktop}\Tray Keeper"; Filename: "{app}\TrayKeeper.exe"; Tasks: desktopicon
Name: "{autostartup}\Tray Keeper"; Filename: "{app}\TrayKeeper.exe"; Tasks: startup

[Run]
Filename: "{app}\TrayKeeper.exe"; Flags: nowait shellexec; Check: RestartAfterUpgrade
Filename: "{app}\TrayKeeper.exe"; Description: "Start Tray Keeper"; Flags: nowait postinstall skipifsilent shellexec; Check: OfferFirstLaunch

[Code]
var
  WasRunning: Boolean;

function RestartAfterUpgrade: Boolean;
begin
  Result := WasRunning;
end;

function OfferFirstLaunch: Boolean;
begin
  Result := not WasRunning;
end;

function StopApplication: Boolean;
var
  AppWindow: HWND;
  Attempt: Integer;
  ErrorCode: Integer;
begin
  Result := (not CheckForMutexes('TrayKeeper.Singleton')) and (not CheckForMutexes('Local\TrayKeeper.SingleInstance'));
  if Result then
    Exit;

  Log('Requesting application shutdown.');
  AppWindow := FindWindowByWindowName('TrayKeeperListener');
  if AppWindow <> 0 then
    PostMessage(AppWindow, $0010, 0, 0); { WM_CLOSE: clean exit }

  for Attempt := 1 to 50 do
  begin
    if (not CheckForMutexes('TrayKeeper.Singleton')) and (not CheckForMutexes('Local\TrayKeeper.SingleInstance')) then
    begin
      Result := True;
      Exit;
    end;
    Sleep(100);
  end;

  if CheckForMutexes('TrayKeeper.Singleton') or CheckForMutexes('Local\TrayKeeper.SingleInstance') then
  begin
    Exec('taskkill.exe', '/F /IM TrayKeeper.exe', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
    Sleep(200);
  end;

  Result := (not CheckForMutexes('TrayKeeper.Singleton')) and (not CheckForMutexes('Local\TrayKeeper.SingleInstance'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  WasRunning := CheckForMutexes('TrayKeeper.Singleton') or CheckForMutexes('Local\TrayKeeper.SingleInstance');
  if WasRunning and not StopApplication then
    Result := 'Tray Keeper is running. Please close it before running setup.';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usAppMutexCheck then
  begin
    if not StopApplication then
      RaiseException('Tray Keeper is running. Please close it before uninstalling.');
  end;
end;
