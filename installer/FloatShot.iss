#define MyAppName "FloatShot"
#define MyAppVersion "0.2.2"
#define MyAppPublisher "FloatShot"
#define MyAppExeName "FloatShot.exe"

[Setup]
AppId={{A5A72559-7863-43F5-A0BB-F41416E4CF9B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=FloatShotSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
SetupIconFile=..\src\FloatShot\Resources\floatshot.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "launch"; Description: "Launch FloatShot after setup"; GroupDescription: "After installation:"; Flags: checkedonce

[Files]
Source: "..\publish\FloatShot.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\FloatShot"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall FloatShot"; Filename: "{uninstallexe}"
Name: "{autodesktop}\FloatShot"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch FloatShot"; Flags: nowait postinstall skipifsilent; Tasks: launch

[UninstallRun]
Filename: "taskkill"; Parameters: "/IM FloatShot.exe /F"; Flags: runhidden; RunOnceId: "StopFloatShot"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
	ResultCode: Integer;
begin
	Exec(ExpandConstant('{cmd}'), '/C taskkill /IM FloatShot.exe /F >NUL 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
	Result := '';
end;