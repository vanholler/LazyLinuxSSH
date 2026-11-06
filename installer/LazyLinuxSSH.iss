#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#define PublishDir "..\artifacts\publish\win-x64"
#if !FileExists(PublishDir + "\LazyLinuxSSH.Desktop.exe")
  #error "LazyLinuxSSH.Desktop.exe is missing. Run build-installer.ps1 instead of compiling this script directly."
#endif

[Setup]
AppId={{7D13DBF2-0A4E-4D4F-9F0F-5B239BDDA0D4}
AppName=LazyLinuxSSH
AppVersion={#AppVersion}
AppVerName=LazyLinuxSSH {#AppVersion}
AppPublisher=LazyLinuxSSH
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\LazyLinuxSSH
DefaultGroupName=LazyLinuxSSH
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=LazyLinuxSSH-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\LazyLinuxSSH.Desktop\Assets\LazyLinuxSSH.ico
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName=LazyLinuxSSH
UninstallDisplayIcon={app}\LazyLinuxSSH.ico
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\LazyLinuxSSH"; Filename: "{app}\LazyLinuxSSH.Desktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\LazyLinuxSSH.ico"; IconIndex: 0
Name: "{autodesktop}\LazyLinuxSSH"; Filename: "{app}\LazyLinuxSSH.Desktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\LazyLinuxSSH.ico"; IconIndex: 0; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\LazyLinuxSSH"; ValueType: string; ValueName: "Language"; ValueData: "{language}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\LazyLinuxSSH.Desktop.exe"; Description: "{cm:LaunchProgram,LazyLinuxSSH}"; Flags: nowait postinstall skipifsilent
