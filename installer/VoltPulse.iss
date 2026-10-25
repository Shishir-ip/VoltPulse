; VoltPulse installer (Inno Setup 6). Per-user install: no administrator rights required.
; Build: ISCC.exe /DVersion=1.0.0 installer\VoltPulse.iss (or via tools\build.ps1)

#define AppName "VoltPulse"
#ifndef Version
#define Version "1.0.0"
#endif
#define AppPublisher "VoltPulse Project"
#define AppExe "VoltPulse.exe"

[Setup]
AppId={{7A1F4C8E-2D3B-4E5F-9A8B-1C2D3E4F5A6B}
AppName={#AppName}
AppVersion={#Version}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=VoltPulse-{#Version}-installer
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
ChangesEnvironment=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start {#AppName} with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VoltPulse"; \
    ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "reg.exe"; Parameters: "DELETE ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /V VoltPulse /F"; Flags: runhidden; RunOnceId: "DelAutostart"

[UninstallDelete]
; user data in %APPDATA%\VoltPulse is intentionally kept so history survives reinstalls
Type: files; Name: "{app}\*.log"
