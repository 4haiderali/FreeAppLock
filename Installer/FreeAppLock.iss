#ifndef PublishDir
  #define PublishDir SourcePath + "..\publish"
#endif
#ifndef OutputPath
  #define OutputPath SourcePath + "..\dist"
#endif
#ifdef TestRoot
  #define InstallDir TestRoot + "\App"
  #define MenuDir TestRoot + "\Start Menu"
  #define DesktopDir TestRoot + "\Desktop"
  #define StartupRoot "HKCU"
  #define StartupKey "Software\FreeAppLockInstallerTest\Run"
  #define UpgradeKey "Software\FreeAppLockInstallerTest\Metadata"
#else
  #define InstallDir "{autopf}\FreeAppLock"
  #define MenuDir "{commonprograms}\FreeAppLock"
  #define DesktopDir "{commondesktop}"
  #define StartupRoot "HKLM"
  #define StartupKey "Software\Microsoft\Windows\CurrentVersion\Run"
  #define UpgradeKey "Software\FreeAppLock\Installer"
#endif

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef UpgradeVersion
  #define UpgradeVersion GetVersionNumbersString(PublishDir + "\FreeAppLock.exe")
#endif
#ifdef TestRoot
  #define Identity "{61C191B4-B177-431A-A8DE-5270D88A2E57}"
#else
  #define Identity "{E518B93C-91C9-4D18-9CB8-F3CEDBA09082}"
#endif
[Setup]
AppId={{#Identity}
AppName=FreeAppLock
AppVersion={#AppVersion}
AppVerName=FreeAppLock {#AppVersion}
AppPublisher=Haider Ali
DefaultDirName={#InstallDir}
DefaultGroupName=FreeAppLock
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
#ifdef TestRoot
PrivilegesRequired=lowest
CreateUninstallRegKey=yes
#else
PrivilegesRequired=admin
#endif
OutputDir={#OutputPath}
OutputBaseFilename=FreeAppLock-{#AppVersion}-Setup-x64
UninstallDisplayIcon={app}\FreeAppLock.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=no
RestartApplications=no
#ifdef TestRoot
AppMutex=FreeAppLock.IsolatedInstallerTest
#else
AppMutex=FreeAppLock.SingleInstance.CurrentUser
#endif
UsePreviousTasks=yes
UsePreviousAppDir=yes
SetupIconFile=..\Assets\AppLock.ico

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start FreeAppLock at Windows login (all users, quietly in the tray)"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\FreeAppLock.exe"; DestDir: "{app}"; Flags: replacesameversion
Source: "{#PublishDir}\README.md"; DestDir: "{app}"; Flags: replacesameversion
Source: "{#PublishDir}\SECURITY.md"; DestDir: "{app}"; Flags: replacesameversion
Source: "{#PublishDir}\RELEASE-NOTES-v1.1.0.md"; DestDir: "{app}"; Flags: replacesameversion

Source: "{#PublishDir}\FreeAppLock.NativeHost.exe"; DestDir: "{app}"; Flags: replacesameversion
Source: "{#PublishDir}\com.freeapplock.profiles.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\BrowserExtension\*"; DestDir: "{app}\BrowserExtension"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\BROWSER-PROFILES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{#MenuDir}\FreeAppLock"; Filename: "{app}\FreeAppLock.exe"; WorkingDir: "{app}"; IconFilename: "{app}\FreeAppLock.exe"; IconIndex: 0
Name: "{#MenuDir}\Uninstall FreeAppLock"; Filename: "{uninstallexe}"; IconFilename: "{app}\FreeAppLock.exe"; IconIndex: 0
Name: "{#DesktopDir}\FreeAppLock"; Filename: "{app}\FreeAppLock.exe"; WorkingDir: "{app}"; IconFilename: "{app}\FreeAppLock.exe"; IconIndex: 0; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{#DesktopDir}\FreeAppLock.lnk"; Check: not WizardIsTaskSelected('desktopicon')

[Registry]
Root: HKA; Subkey: "{#UpgradeKey}"; ValueType: string; ValueName: "UpgradeVersion"; ValueData: "{#UpgradeVersion}"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: {#StartupRoot}; Subkey: "{#StartupKey}"; ValueType: string; ValueName: "FreeAppLock"; ValueData: """{app}\FreeAppLock.exe"" --tray"; Flags: uninsdeletevalue; Tasks: startup
Root: {#StartupRoot}; Subkey: "{#StartupKey}"; ValueName: "FreeAppLock"; Flags: deletevalue; Check: not WizardIsTaskSelected('startup')

#ifndef TestRoot
Root: HKLM; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.freeapplock.profiles"; ValueType: string; ValueData: "{app}\com.freeapplock.profiles.json"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.freeapplock.profiles"; ValueType: string; ValueData: "{app}\com.freeapplock.profiles.json"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.freeapplock.profiles"; ValueType: string; ValueData: "{app}\com.freeapplock.profiles.json"; Flags: uninsdeletekey
[Run]
Filename: "{app}\FreeAppLock.exe"; Description: "Launch FreeAppLock now"; Flags: nowait postinstall skipifsilent runasoriginaluser
#endif

[Code]
function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
  OldVersion, NewVersion: Int64;
begin
  Result := True;
  if not RegQueryStringValue(HKA, '{#UpgradeKey}', 'UpgradeVersion', InstalledVersion) then
    RegQueryStringValue(HKA, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#Identity}_is1', 'DisplayVersion', InstalledVersion);
  if InstalledVersion <> '' then
    if StrToVersion(InstalledVersion, OldVersion) and StrToVersion('{#UpgradeVersion}', NewVersion) then
      if ComparePackedVersion(OldVersion, NewVersion) > 0 then
      begin
        SuppressibleMsgBox('A newer FreeAppLock version is already installed. Use the same or a newer installer.', mbError, MB_OK, IDOK);
        Result := False;
      end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if FileExists(ExpandConstant('{app}\FreeAppLock.NativeHost.exe')) then Sleep(2500);
  Result := '';
end;
