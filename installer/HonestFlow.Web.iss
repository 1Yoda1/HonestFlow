#ifndef MyAppVersion
  #define MyAppVersion "3.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\HonestFlow\current"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer\web"
#endif
#ifndef DotNetRuntimeVersion
  #define DotNetRuntimeVersion "10.0.10"
#endif
#ifndef DotNetRuntimeUrl
  #define DotNetRuntimeUrl "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.10/windowsdesktop-runtime-10.0.10-win-x64.exe"
#endif
#ifndef DotNetRuntimeSha256
  #define DotNetRuntimeSha256 "E82FC901C8F52D716293B2BC0830CE0DD254A06268C457A19E8FC503560A84D1"
#endif

#define MyAppName "HonestFlow"
#define MyAppExeName "HonestFlow.exe"
#define DotNetRuntimeFileName "windowsdesktop-runtime-" + DotNetRuntimeVersion + "-win-x64.exe"

[Setup]
AppId={{D9268653-DB58-4F93-A545-94CB8A2F2A86}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=OOO "Morkovka"
AppCopyright=Copyright (C) Pavel Shadrov 2024-2026
VersionInfoVersion={#MyAppVersion}.0
DefaultDirName={autopf}\HonestFlow
DefaultGroupName=HonestFlow
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=HonestFlow-Web-Setup-{#MyAppVersion}
SetupIconFile=..\Resourses\hf.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=no
UsePreviousAppDir=yes
UsePreviousTasks=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные ярлыки:"; Flags: unchecked

[Dirs]
Name: "{commonappdata}\HonestFlow"
Name: "{commonappdata}\HonestFlow\cache"
Name: "{commonappdata}\HonestFlow\diagnostics"
Name: "{commonappdata}\HonestFlow\logs"
Name: "{commonappdata}\HonestFlow\update"

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion restartreplace

[Icons]
Name: "{autoprograms}\HonestFlow"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\HonestFlow"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить HonestFlow"; Flags: nowait postinstall skipifsilent shellexec

[Code]
var
  RuntimeDownloadPage: TDownloadWizardPage;
  RuntimeInstallPage: TOutputMarqueeProgressWizardPage;

function IsDotNet10DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
  RuntimeRoot: String;
  RuntimeVersionFolder: String;
begin
  Result := False;
  RuntimeRoot := ExpandConstant('{autopf}\dotnet\shared\Microsoft.WindowsDesktop.App');

  if not DirExists(RuntimeRoot) then
    Exit;

  if FindFirst(AddBackslash(RuntimeRoot) + '10.*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          RuntimeVersionFolder := AddBackslash(RuntimeRoot) + FindRec.Name;
          if FileExists(AddBackslash(RuntimeVersionFolder) + 'System.Windows.Forms.dll') then
          begin
            Result := True;
            Exit;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function OnRuntimeDownloadProgress(
  const Url, FileName: String;
  const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

function DownloadAndInstallDotNetRuntime(var NeedsRestart: Boolean): String;
var
  RuntimePath: String;
  ResultCode: Integer;
begin
  Result := '';
  RuntimePath := ExpandConstant('{tmp}\{#DotNetRuntimeFileName}');

  RuntimeDownloadPage.Clear;
  RuntimeDownloadPage.Add(
    '{#DotNetRuntimeUrl}',
    '{#DotNetRuntimeFileName}',
    '{#DotNetRuntimeSha256}');
  RuntimeDownloadPage.Show;
  try
    try
      RuntimeDownloadPage.Download;
    except
      Result := 'Не удалось скачать Microsoft .NET 10 Desktop Runtime: ' +
        GetExceptionMessage;
      Exit;
    end;
  finally
    RuntimeDownloadPage.Hide;
  end;

  RuntimeInstallPage.Show;
  RuntimeInstallPage.Animate;
  try
    if not Exec(
      RuntimePath,
      '/install /quiet /norestart',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) then
    begin
      Result := 'Не удалось запустить установку Microsoft .NET 10 Desktop Runtime.';
      Exit;
    end;
  finally
    RuntimeInstallPage.Hide;
  end;

  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    Result := Format('Установка Microsoft .NET 10 Desktop Runtime завершилась с кодом %d.', [ResultCode]);
    Exit;
  end;

  if ResultCode = 3010 then
    NeedsRestart := True;

  if not IsDotNet10DesktopRuntimeInstalled then
    Result := 'После установки Microsoft .NET 10 Desktop Runtime не обнаружен.';
end;

procedure InitializeWizard;
begin
  RuntimeDownloadPage := CreateDownloadPage(
    'Установка Microsoft .NET 10 Desktop Runtime',
    'Загрузка обязательного системного компонента с сервера Microsoft...',
    @OnRuntimeDownloadProgress);
  RuntimeInstallPage := CreateOutputMarqueeProgressPage(
    'Установка Microsoft .NET 10 Desktop Runtime',
    'Выполняется установка обязательного системного компонента. Пожалуйста, подождите...');

  RuntimeDownloadPage.ShowBaseNameInsteadOfUrl := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not IsDotNet10DesktopRuntimeInstalled then
    Result := DownloadAndInstallDotNetRuntime(NeedsRestart);
end;

function InitializeSetup: Boolean;
begin
  Result := IsWin64;
  if not Result then
    MsgBox('HonestFlow поддерживает только 64-разрядную Windows.', mbError, MB_OK);
end;
