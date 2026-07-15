#define AppName "OKF-Todo"
#define AppPublisher "OKF-Todo"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{8FAE17DA-240D-43E7-876C-7BF9AFB71387}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Okf-Todo
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
MinVersion=10.0
OutputDir=..\artifacts\installer
OutputBaseFilename=Okf-Todo-{#AppVersion}-win-x64-setup
SetupIconFile=..\Okf-Todo\wwwroot\favicon.ico
UninstallDisplayIcon={app}\Okf-Todo.exe
LicenseFile=license-note.txt
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
AppMutex=OkfTodoSingleInstance
CloseApplications=yes
RestartApplications=no
UsePreviousSetupType=yes
AlwaysShowComponentsList=yes

[Types]
Name: "full"; Description: "Full installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "core"; Description: "OKF-Todo GUI and OKF layer"; Types: full custom; Flags: fixed
Name: "mcp"; Description: "Install MCP server"; Types: full

[Files]
Source: "..\artifacts\installer\staging\core\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\installer\staging\mcp\*"; DestDir: "{app}\mcp"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: mcp
Source: "..\artifacts\installer\staging\okf\*"; DestDir: "{app}\okf"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\installer\staging\integration\README.md"; DestDir: "{app}\integration"; Flags: ignoreversion

[Icons]
Name: "{group}\OKF-Todo"; Filename: "{app}\Okf-Todo.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Okf-Todo.exe"

[Run]
Filename: "{app}\Okf-Todo.exe"; Description: "Launch OKF-Todo"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\integration\mcp-config.json"
Type: filesandordirs; Name: "{app}\mcp"
Type: dirifempty; Name: "{app}\integration"
Type: dirifempty; Name: "{app}\okf\todo-database"
Type: dirifempty; Name: "{app}\okf"

[Code]
procedure RemovePreviouslyInstalledMcpFiles;
begin
  DelTree(ExpandConstant('{app}\mcp'), True, True, True);
  DeleteFile(ExpandConstant('{app}\integration\mcp-config.json'));
end;

function JsonEscapePath(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
end;

procedure WriteMcpConfig;
var
  Config: String;
  ConfigPath: String;
  ExecutablePath: String;
begin
  ForceDirectories(ExpandConstant('{app}\integration'));
  ExecutablePath := JsonEscapePath(ExpandConstant('{app}\mcp\Okf-Todo.Mcp.exe'));
  Config :=
    '{' + #13#10 +
    '  "mcpServers": {' + #13#10 +
    '    "okf-todo": {' + #13#10 +
    '      "command": "' + ExecutablePath + '"' + #13#10 +
    '    }' + #13#10 +
    '  }' + #13#10 +
    '}' + #13#10;
  ConfigPath := ExpandConstant('{app}\integration\mcp-config.json');
  SaveStringToFile(ConfigPath, Config, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemovePreviouslyInstalledMcpFiles;

  if CurStep = ssPostInstall then
  begin
    if WizardIsComponentSelected('mcp') then
      WriteMcpConfig
    else
      DeleteFile(ExpandConstant('{app}\integration\mcp-config.json'));
  end;
end;
