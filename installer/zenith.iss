; Zenith installer (Inno Setup 6). Installs the app, the probe and the service in one go, and removes
; them cleanly. The three are published into one folder over one shared .NET runtime (publish\instalacao,
; with -p:PublishSingleFile=false -p:SharedInstall=true, the service last); see .github/workflows/build.yml.
;   iscc /DAppVersion=0.1.0 installer\zenith.iss
; Updates run this same installer with /SILENT /RELAUNCH=1 (see src/Zenith.App/Updates/Updater.cs).
; Output: publish\installer\zenith-setup-<version>.exe

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define ServiceName "Zenith"

[Setup]
; Never change the AppId: it is how Windows finds an installed Zenith to update or remove.
AppId={{7D3C5E2A-4B1F-4C8E-9A6D-2F0B8E1C5A73}
AppName=Zenith
AppVersion={#AppVersion}
AppVerName=Zenith {#AppVersion}
AppPublisher=Zenith
DefaultDirName={autopf}\Zenith
DefaultGroupName=Zenith
DisableProgramGroupPage=yes
DisableDirPage=yes
; The service runs as LocalSystem, so installing it needs administrator rights once.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#PublishDir}\installer
OutputBaseFilename=zenith-setup-{#AppVersion}
SetupIconFile=..\src\Zenith.App\zenith.ico
UninstallDisplayIcon={app}\zenith.exe
UninstallDisplayName=Zenith
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
; The only per-user entries are removed on uninstall (the Run value and the app's preferences).
UsedUserAreasWarning=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
ptbr.DesktopIcon=Criar um atalho na área de trabalho
ptbr.StartupIcon=Iniciar o Zenith com o Windows, discreto na bandeja (dá para desligar depois no ícone)
ptbr.LaunchApp=Abrir o Zenith agora
ptbr.ServiceFailed=O serviço do Zenith não pôde ser iniciado (código %1). O app abre, mas não vai conseguir aplicar nada até o serviço ligar.
ptbr.UndoQuestion=Antes de remover, quer desfazer todas as alterações que o Zenith fez neste PC?%n%nSim: tudo volta como estava e o diário de alterações é apagado.%nNão: as alterações ficam, e o diário é guardado para você poder desfazer se instalar o Zenith de novo.
ptbr.UndoFailed=Algumas alterações não puderam ser desfeitas (código %1). O diário foi guardado em %2 para você tentar de novo depois.

[Tasks]
Name: "startup"; Description: "{cm:StartupIcon}"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Registry]
; The installer's choice; each user's app applies it on its first run and keeps the user's own choice after that.
Root: HKLM; Subkey: "Software\Zenith"; ValueType: dword; ValueName: "StartWithWindows"; ValueData: 1; Tasks: startup; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Zenith"; ValueType: dword; ValueName: "StartWithWindows"; ValueData: 0; Tasks: not startup; Flags: uninsdeletekey
; Only the installed copy updates itself.
Root: HKLM; Subkey: "Software\Zenith"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey
; Written by the app, removed here for the user who uninstalls.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Zenith"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Zenith"; ValueType: none; Flags: uninsdeletekey

[Files]
Source: "{#PublishDir}\instalacao\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Zenith"; Filename: "{app}\zenith.exe"
Name: "{autodesktop}\Zenith"; Filename: "{app}\zenith.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\zenith.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: not Relaunch
; After an update from the app, open it again.
Filename: "{app}\zenith.exe"; Flags: nowait postinstall runasoriginaluser; Check: Relaunch

[Code]
const
  DataDirName = 'Zenith';

{ /RELAUNCH=1: started by the app's "Instalar agora". }
function Relaunch(): Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

function Sc(const Params: String): Integer;
var
  Code: Integer;
begin
  if not Exec(ExpandConstant('{sys}\sc.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Result := Code;
end;

function ServiceExists(): Boolean;
begin
  Result := Sc('query {#ServiceName}') = 0;
end;

{ Stops the service and waits for it, so its exe can be replaced or deleted. }
procedure StopService();
var
  I, Code: Integer;
begin
  if not ServiceExists() then Exit;
  Sc('stop {#ServiceName}');
  for I := 1 to 30 do
  begin
    Exec(ExpandConstant('{cmd}'), '/C sc query {#ServiceName} | find "STOPPED"', '', SW_HIDE, ewWaitUntilTerminated, Code);
    if Code = 0 then Exit;
    Sleep(500);
  end;
end;

{ The app may be sitting in the tray; closing its window only hides it, so end it. }
procedure CloseApp();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im zenith.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Updating: the old app and service must let go of their exes first. }
  CloseApp();
  StopService();
  Result := '';
end;

procedure InstallService();
var
  Exe: String;
  Code: Integer;
begin
  Exe := ExpandConstant('{app}\zenith-service.exe');
  if ServiceExists() then
    Sc('config {#ServiceName} binPath= "\"' + Exe + '\"" start= auto')
  else
    Sc('create {#ServiceName} binPath= "\"' + Exe + '\"" start= auto DisplayName= "Zenith"');
  Sc('description {#ServiceName} "Núcleo do Zenith: aplica e desfaz configurações e guarda o diário de alterações."');
  { Restart after a crash: 5 s, 30 s, then 1 min; the counter resets after a day. }
  Sc('failure {#ServiceName} reset= 86400 actions= restart/5000/restart/30000/restart/60000');
  Code := Sc('start {#ServiceName}');
  { 1056: already running. }
  if (Code <> 0) and (Code <> 1056) and not WizardSilent() then
    MsgBox(FmtMessage(CustomMessage('ServiceFailed'), [IntToStr(Code)]), mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then InstallService();
end;

{ Uninstall: offer to undo everything while the service is still running (it owns the journal),
  then remove the service. The journal is kept unless everything was undone. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  Undo: Boolean;
  Code: Integer;
begin
  if CurUninstallStep <> usUninstall then Exit;
  CloseApp();
  DataDir := ExpandConstant('{commonappdata}\') + DataDirName;

  Undo := UninstallSilent() or
    (SuppressibleMsgBox(CustomMessage('UndoQuestion'), mbConfirmation, MB_YESNO, IDYES) = IDYES);
  if Undo then
  begin
    if not Exec(ExpandConstant('{app}\zenith-probe.exe'), 'desfazer-tudo', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Code := -1;
    if Code <> 0 then
    begin
      Undo := False;
      if not UninstallSilent() then
        MsgBox(FmtMessage(CustomMessage('UndoFailed'), [IntToStr(Code), DataDir]), mbError, MB_OK);
    end;
  end;

  StopService();
  Sc('delete {#ServiceName}');

  if Undo then DelTree(DataDir, True, True, True);
end;
