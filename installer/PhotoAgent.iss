#ifndef AppVersion
  #error AppVersion must be supplied by Build-Installer.ps1
#endif

[Setup]
AppId={{CC3D58B1-5F24-4BFB-8D51-44C7DEB8704C}
AppName=PhotoAgent
AppVersion={#AppVersion}
AppPublisher=Andrey Tokmachev
AppPublisherURL=https://github.com/atvrs/PhotoAgent
DefaultDirName={localappdata}\Programs\PhotoAgent
DefaultGroupName=PhotoAgent
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
AppMutex=Local\PhotoAgent
CloseApplications=yes
RestartApplications=no
SetupIconFile=..\PhotoAgent\photoagent.ico
UninstallDisplayIcon={app}\PhotoAgent.exe
OutputDir=output
OutputBaseFilename=PhotoAgent_Setup_{#AppVersion}
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}.0
Uninstallable=yes
LicenseFile=..\LICENSE

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"

[Dirs]
Name: "C:\psa_photos"; Flags: uninsneveruninstall
Name: "C:\psa_photos\commands"; Flags: uninsneveruninstall
Name: "C:\psa_photos\responses"; Flags: uninsneveruninstall
Name: "C:\psa_photos\photos"; Flags: uninsneveruninstall
Name: "C:\psa_photos\archive"; Flags: uninsneveruninstall
Name: "C:\psa_photos\logs"; Flags: uninsneveruninstall

[Files]
Source: "..\PhotoAgent\bin\Release\PhotoAgent.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PhotoAgent\bin\Release\PhotoAgent.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PhotoAgent\bin\Release\AForge.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PhotoAgent\bin\Release\AForge.Video.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PhotoAgent\bin\Release\AForge.Video.DirectShow.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PhotoAgent\photoagent.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "INSTALL_RU.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "licenses\*.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "prerequisites\NDP48-x86-x64-AllOS-ENU.exe"; Flags: dontcopy nocompression

[Icons]
Name: "{group}\PhotoAgent"; Filename: "{app}\PhotoAgent.exe"; WorkingDir: "{app}"
Name: "{group}\Удалить PhotoAgent"; Filename: "{uninstallexe}"
Name: "{userdesktop}\PhotoAgent"; Filename: "{app}\PhotoAgent.exe"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "C:\psa_photos\PhotoAgent"; Filename: "{app}\PhotoAgent.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PhotoAgent"; ValueData: """{app}\PhotoAgent.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\PhotoAgent.exe"; Description: "Запустить PhotoAgent"; Flags: nowait postinstall skipifsilent; Check: CanLaunch

[Code]
var
  PrerequisiteNeedsRestart: Boolean;

function HasNet48: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    and (Release >= 528040);
end;

function CanLaunch: Boolean;
begin
  Result := HasNet48 and not PrerequisiteNeedsRestart;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Probe, Folder: String;
  Folders: TArrayOfString;
  I: Integer;
begin
  Result := '';
  { Check the fixed 1C exchange directory before installing prerequisites.
    Never change ACLs or overwrite an existing camera configuration. }
  SetArrayLength(Folders, 6);
  Folders[0] := 'C:\psa_photos';
  Folders[1] := 'C:\psa_photos\commands';
  Folders[2] := 'C:\psa_photos\responses';
  Folders[3] := 'C:\psa_photos\photos';
  Folders[4] := 'C:\psa_photos\archive';
  Folders[5] := 'C:\psa_photos\logs';
  for I := 0 to GetArrayLength(Folders) - 1 do
  begin
    Folder := Folders[I];
    if not ForceDirectories(Folder) then
    begin
      Result := 'Нет доступа к папке ' + Folder + '. Администратор должен предоставить текущему пользователю право изменения этой папки.';
      Exit;
    end;
    Probe := AddBackslash(Folder) + ExtractFileName(ExpandConstant('{tmp}')) + '.write-test';
    if not SaveStringToFile(Probe, 'PhotoAgent setup write test', False) then
    begin
      Result := 'Нет права записи в ' + Folder + '. Администратор должен предоставить текущему пользователю право изменения этой папки.';
      Exit;
    end;
    DeleteFile(Probe);
  end;

  if HasNet48 then
    Exit;

  ExtractTemporaryFile('NDP48-x86-x64-AllOS-ENU.exe');
  if not ShellExec('runas', ExpandConstant('{tmp}\NDP48-x86-x64-AllOS-ENU.exe'),
    '/passive /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, Code) then
  begin
    Result := 'Не удалось запустить установку .NET Framework 4.8. Разрешите запрос Windows на повышение прав и повторите установку. Код: ' + IntToStr(Code);
    Exit;
  end;

  if (Code = 3010) or (Code = 1641) then
    PrerequisiteNeedsRestart := True
  else if Code <> 0 then
  begin
    Result := 'Установка .NET Framework 4.8 завершилась с ошибкой ' + IntToStr(Code) + '. После устранения ошибки запустите установщик PhotoAgent повторно.';
    Exit;
  end;

  if not HasNet48 then
  begin
    NeedsRestart := PrerequisiteNeedsRestart;
    Result := '.NET Framework 4.8 ещё недоступен. Перезагрузите Windows и повторно запустите установщик PhotoAgent.';
  end;
end;

function NeedRestart: Boolean;
begin
  Result := PrerequisiteNeedsRestart;
end;
