; RHZ LED Controller kurulum betigi (Inno Setup 6). Derleme: installer\build-setup.ps1
; Surum ve yayin klasoru betikten /D ile gelir; elle derlerken asagidaki varsayilanlar kullanilir.

#ifndef AppVersion
  #define AppVersion "0.9.0-beta"
#endif
#ifndef PublishDir
  #define PublishDir "..\bin\publish"
#endif

#define AppName "RHZ LED Controller"
#define AppExe "RLEDC.exe"
#define TaskName "RHZ LED Controller"

[Setup]
; AppId sabit kalmali: sonraki surumler ayni kaydi gunceller.
AppId={{7C3F2A9E-5B1D-4E8A-9F42-2D6B8C1E4A70}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Rhizzu
AppPublisherURL=https://github.com/elgyonr/biostarLEDController
AppSupportURL=https://github.com/elgyonr/biostarLEDController/issues
AppCopyright=© 2026 Rhizzu · GPL-3.0
VersionInfoVersion=0.9.0.0
VersionInfoProductTextVersion={#AppVersion}
DefaultDirName={autopf}\Rhizzu\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Uygulama yonetici olarak calisir ve Program Files'a kurulur.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupIconFile=..\ledsync\Assets\rlc.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
LicenseFile=..\LICENSE
InfoBeforeFile=BETA.txt
OutputDir=..\dist
OutputBaseFilename=RHZ-LED-Controller-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Calisan RLEDC'yi Restart Manager kapatamaz (kapat dugmesi tepsiye gizler); [Code] kapatir.
CloseApplications=no

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Windows açılınca tepside başlat (yönetici yetkisiyle zamanlanmış görev)"; GroupDescription: "Başlangıç:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\ledsync\modules\README.md"; DestDir: "{app}\modules"; DestName: "SmbusPIIX4-LICENSE.md"; Flags: ignoreversion
Source: "BETA.txt"; DestDir: "{app}"; DestName: "BETA-NOTLARI.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} kaldır"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Uygulama yonetici ister; Run anahtari yetki yukseltemedigi icin otomatik baslatma "en yuksek ayricalik" gorevle yapilir.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""{#TaskName}"" /TR ""\""{app}\{#AppExe}\"" --tray"" /SC ONLOGON /RL HIGHEST /DELAY 0000:15"; Flags: runhidden; Tasks: autostart
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#TaskName}"""; Flags: runhidden; RunOnceId: "DelTask"
; CS2 cfg dosyasini uygulamanin kendisi bulup siler.
Filename: "{app}\{#AppExe}"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[Code]
procedure KillApp(const Reason: String);
var
  Code: Integer;
  Log: String;
begin
  Log := ExpandConstant('{userappdata}\LedSync\rlc-hata.log');
  if FileExists(Log) then
    SaveStringToFile(Log, GetDateTimeString('yyyy/mm/dd hh:nn:ss', '-', ':') + '.000 kapaniyor: ' + Reason + #13#10, True);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PawnIOInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not PawnIOInstalled then
    Result := MsgBox('PawnIO sürücüsü bu bilgisayarda bulunamadı.' + #13#10#13#10 +
      'RHZ LED Controller anakart ışıklarına PawnIO üzerinden erişir; PawnIO olmadan ışıklar değişmez.' + #13#10 +
      'PawnIO''yu https://pawnio.eu adresinden indirip kurabilirsin.' + #13#10#13#10 +
      'Kuruluma yine de devam edilsin mi?', mbConfirmation, MB_YESNO) = IDYES;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillApp('kurulum (yeni surum yukleniyor)');
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  KillApp('kaldirma');
  Result := True;
end;
