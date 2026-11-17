#ifndef AppVersion
  #define AppVersion "0.7.56"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\package-" + AppVersion
#endif

[Setup]
AppId={{5F98F556-86DE-48A8-B81B-5296D2A1C9A4}
AppName=LumiBar
AppVersion={#AppVersion}
AppPublisher=Ma Guanqun with Codex
AppCopyright=Ma Guanqun with Codex
VersionInfoDescription=LumiBar Windows x64 Setup
DefaultDirName={localappdata}\Programs\LumiBar
DefaultGroupName=LumiBar
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
DisableProgramGroupPage=yes
AllowNoIcons=yes
DisableWelcomePage=no
WizardStyle=modern
SetupIconFile=..\LumiBar\Assets\LumiBar.ico
UninstallDisplayIcon={app}\LumiBar.exe
OutputDir=..\..\outputs\安装包
#ifdef IncludeAntigravity
OutputBaseFilename=LumiBar-{#AppVersion}-windows-x64-with-antigravity-setup
#else
OutputBaseFilename=LumiBar-{#AppVersion}-windows-x64-setup
#endif
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LumiBar"; Filename: "{app}\LumiBar.exe"; WorkingDir: "{app}"; IconFilename: "{app}\LumiBar.exe"; IconIndex: 0; Check: ShouldCreateShortcuts
Name: "{autodesktop}\LumiBar"; Filename: "{app}\LumiBar.exe"; WorkingDir: "{app}"; IconFilename: "{app}\LumiBar.exe"; IconIndex: 0; Tasks: desktopicon; Check: ShouldCreateShortcuts

[Run]
Filename: "{app}\LumiBar.exe"; Description: "打开 LumiBar"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function ShouldCreateShortcuts(): Boolean;
begin
  Result := (not WizardNoIcons) and (ExpandConstant('{param:NOSHORTCUTS|0}') <> '1');
end;

function StopRunningApp(): Boolean;
var
  ExePath: String;
  ResultCode: Integer;
begin
  ExePath := ExpandConstant('{app}\LumiBar.exe');
  Result := True;
  if FileExists(ExePath) then begin
    Result := Exec(ExePath, '--shutdown', ExpandConstant('{app}'), SW_HIDE,
      ewWaitUntilTerminated, ResultCode);
    if Result then Result := ResultCode = 0;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not StopRunningApp() then
    Result := 'LumiBar 暂时无法退出。请从系统托盘退出后重试。';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  StartupValue: String;
begin
  if CurStep = ssPostInstall then begin
    { Also handles a running portable copy on the first installation. }
    if not StopRunningApp() then
      RaiseException('请从系统托盘退出 LumiBar，再重新打开它。');
    if RegQueryStringValue(HKCU, RunKey, 'LumiBar', StartupValue) then
      RegWriteStringValue(HKCU, RunKey, 'LumiBar', '"' + ExpandConstant('{app}\LumiBar.exe') + '"');
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := StopRunningApp();
  if not Result then MsgBox('LumiBar 暂时无法退出。请从系统托盘退出后重试。', mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupValue: String;
begin
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, RunKey, 'LumiBar', StartupValue) then
      if Pos(Lowercase(ExpandConstant('{app}\LumiBar.exe')), Lowercase(StartupValue)) > 0 then
        RegDeleteValue(HKCU, RunKey, 'LumiBar');
    { User settings, favorites, lyrics and runtime component cache stay intact. }
  end;
end;
