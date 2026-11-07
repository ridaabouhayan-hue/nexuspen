; NexusPen installer (Inno Setup 6). Installeert voor de huidige gebruiker, zonder beheerdersrechten.
#ifndef AppVersion
  #define AppVersion "1.3.1"
#endif

[Setup]
AppId={{8C3E1F2A-6B4D-4E8A-9F1C-3A7B5D2E9C41}
AppName=NexusPen
AppVersion={#AppVersion}
AppVerName=NexusPen {#AppVersion}
AppPublisher=Nexus of Language
DefaultDirName={localappdata}\Programs\NexusPen
DefaultGroupName=NexusPen
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\out
OutputBaseFilename=NexusPen-Setup
SetupIconFile=..\assets\NexusPen.ico
UninstallDisplayIcon={app}\NexusPen.exe
UninstallDisplayName=NexusPen
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Languages]
Name: "nl"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktop"; Description: "Snelkoppeling op het bureaublad"; Flags: unchecked
Name: "autostart"; Description: "Starten met Windows"

[Files]
Source: "..\out\NexusPen.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\LICENSE-Lucide.txt"; DestDir: "{app}\licenties"; Flags: ignoreversion
Source: "..\assets\fonts\OFL-*.txt"; DestDir: "{app}\licenties"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\NexusPen"; Filename: "{app}\NexusPen.exe"
Name: "{userdesktop}\NexusPen"; Filename: "{app}\NexusPen.exe"; Tasks: desktop

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NexusPen"; ValueData: """{app}\NexusPen.exe"" --opstarten"; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "NexusPen"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\NexusPen.exe"; Description: "NexusPen openen"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM NexusPen.exe /F"; Flags: runhidden; RunOnceId: "StopNexusPen"

[CustomMessages]
nl.AskDeleteData=Wil je ook je tekeningen, instellingen en het foutlog van NexusPen verwijderen?%n%nKies Nee als je NexusPen later weer wilt gebruiken, dan staat alles er nog.
en.AskDeleteData=Do you also want to delete your NexusPen drawings, settings and error log?%n%nChoose No if you want to use NexusPen again later: everything will still be there.
fr.AskDeleteData=Voulez-vous aussi supprimer vos dessins, paramètres et le journal des erreurs de NexusPen ?%n%nChoisissez Non si vous voulez réutiliser NexusPen plus tard : tout sera encore là.
es.AskDeleteData=¿Quieres borrar también tus dibujos, ajustes y el registro de errores de NexusPen?%n%nElige No si quieres volver a usar NexusPen más adelante: todo seguirá ahí.

[Code]
// Na het verwijderen: vragen of de tekeningen en instellingen in %APPDATA%\NexusPen ook weg mogen.
// Bij stil verwijderen (bijvoorbeeld bij bijwerken) nooit iets weggooien.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  dataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    dataDir := ExpandConstant('{userappdata}\NexusPen');
    if DirExists(dataDir) and (not UninstallSilent) then
      if MsgBox(CustomMessage('AskDeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(dataDir, True, True, True);
  end;
end;
