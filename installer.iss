#define MyAppName "Topiary"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Alden JG"
#define MyAppExeName "Topiary.exe"
#define MyAppPublishDir "bin\Release\net8.0-windows\publish"

[Setup]
AppId={{5A8C0357-9B84-4A48-8E72-EA98E631C01A}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=LICENSE.txt
OutputDir=installer
OutputBaseFilename=Topiary_Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
; Add logging
SetupLogging=yes
; Prevent creation of unnecessary directories
CreateAppDir=yes
DisableDirPage=no
DisableProgramGroupPage=yes
; Ensure working directory is set correctly
AppendDefaultDirName=no
UsePreviousAppDir=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main executable and config
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Add a logging file to track installation
Source: "{#MyAppPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Modify the run command to specify working directory
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent; WorkingDir: "{app}"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    Log('Installation completed. Application directory: ' + ExpandConstant('{app}'));
    Log('Executable path: ' + ExpandConstant('{app}\{#MyAppExeName}'));
  end;
end;