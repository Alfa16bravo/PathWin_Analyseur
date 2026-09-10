; Inno Setup script for PathWin Analyzer.
; Built by .github/workflows/build.yml:
;   ISCC.exe /DAppVersion=1.3.1 installer\PathWinAnalyzer.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName      "PathWin Analyzer"
#define AppExeName   "PathWinAnalyzer.exe"
#define AppPublisher "Alfa16bravo"
#define AppUrl       "https://github.com/Alfa16bravo/PathWin_Analyseur"

[Setup]
AppId={{8F3D2A6C-5B41-4E39-9A7D-6C1B0E2F7A54}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

; Ask whether to install for every user or for the current one only.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0

OutputDir=..\out
OutputBaseFilename=PathWinAnalyzer-Setup
SetupIconFile=..\PathAnalyzer\Assets\app.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\standalone\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; Backups and settings live in %LOCALAPPDATA%\PathAnalyzer and are deliberately
; left untouched by the uninstaller.
