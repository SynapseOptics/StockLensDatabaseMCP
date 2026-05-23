; StockLensDatabaseMCP Inno Setup Installer Script
; Requires Inno Setup 6.x
;
; Builds a self-contained Windows installer for the standalone
; stock-lens MCP server + its Claude-registration GUI. No engine
; dependency.

#define MyAppName "StockLensDatabaseMCP"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Synapse Optics"
#define MyAppExeName "ConfigureLensHHStockMcp.exe"
#define MyAppURL "https://github.com/SynapseOptics/StockLensDatabaseMCP"

; Paths relative to this .iss file
#define RepoRoot ".."
#define McpBin    RepoRoot + "\src\LensHH.StockMcp\bin\Release\net8.0"
#define ConfigBin RepoRoot + "\src\ConfigureLensHHStockMcp\bin\Release\net8.0-windows"
#define Assets    RepoRoot + "\assets"

; Optional catalog source. The catalogs directory ships with the
; LensHH-LT install; if a sibling LT repo is present we bundle it,
; otherwise we install without and the user is expected to point
; LENSHH_CATALOGS_DIR at an existing catalogs directory (typically
; their LensHH-LT install).
#define LTCatalogs RepoRoot + "\..\SynapseLensHH-LT\LensHH-LT\catalogs"

[Setup]
; A FRESH GUID — distinct from LensHH-LT's installer so both can be
; installed/uninstalled independently. Regenerate via Inno Setup's
; Tools menu if you ever fork this installer for another product.
AppId={{C9F1A3D2-7B5E-4A6F-9C8D-2E1F4A5B6C7D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=StockLensDatabaseMCP-Setup-{#MyAppVersion}
SetupIconFile={#Assets}\icon.ico
UninstallDisplayIcon={app}\config\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
LicenseFile={#RepoRoot}\LICENSE
PrivilegesRequired=lowest
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; ── Stock-MCP server (LensHH.StockMcp.exe + its .NET deps) ─────────
; Goes to {app}\stock-mcp\ so the configurator can locate it via
; its known-path probe (...\stock-mcp\LensHH.StockMcp.exe).
Source: "{#McpBin}\LensHH.StockMcp.exe"; DestDir: "{app}\stock-mcp"; Flags: ignoreversion
Source: "{#McpBin}\*.dll";              DestDir: "{app}\stock-mcp"; Flags: ignoreversion
Source: "{#McpBin}\*.json";             DestDir: "{app}\stock-mcp"; Flags: ignoreversion
Source: "{#McpBin}\runtimes\*";         DestDir: "{app}\stock-mcp\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; ── Configurator (WPF, registers MCP with Claude) ─────────────────
Source: "{#ConfigBin}\ConfigureLensHHStockMcp.exe";            DestDir: "{app}\config"; Flags: ignoreversion
Source: "{#ConfigBin}\ConfigureLensHHStockMcp.dll";            DestDir: "{app}\config"; Flags: ignoreversion
Source: "{#ConfigBin}\ConfigureLensHHStockMcp.runtimeconfig.json"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "{#ConfigBin}\ConfigureLensHHStockMcp.deps.json";      DestDir: "{app}\config"; Flags: ignoreversion

; ── Documentation ─────────────────────────────────────────────────
Source: "{#RepoRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE";   DestDir: "{app}"; Flags: ignoreversion

; ── Icon ──────────────────────────────────────────────────────────
Source: "{#Assets}\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

; ── OPTIONAL: stock-lens catalog (if a sibling LensHH-LT repo is
;    present at build time). Uses skipifsourcedoesntexist so the
;    installer still builds when LT is not available.
Source: "{#LTCatalogs}\stock-lens-catalog.sqlite"; DestDir: "{app}\catalogs"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#LTCatalogs}\Lenses\*.lhlt"; DestDir: "{app}\catalogs\Lenses"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
; Start Menu
Name: "{group}\Configure Stock-MCP for Claude"; Filename: "{app}\config\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{group}\Catalogs Folder";                Filename: "{app}\catalogs"; Check: CatalogsBundled
Name: "{group}\README";                         Filename: "{app}\README.md"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
; Desktop
Name: "{autodesktop}\{#MyAppName} Configure"; Filename: "{app}\config\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\config\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Configure for Claude}"; Flags: nowait postinstall skipifsilent

[Code]
function CatalogsBundled: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\catalogs\stock-lens-catalog.sqlite'));
end;

function DotNet8DesktopRuntimeExists: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  // .NET 8 Desktop runtime lives under
  // %ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\8.x.y
  // (WPF needs the *Desktop* runtime, not the base .NET runtime).
  if FindFirst(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App') + '\8.*', FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not DotNet8DesktopRuntimeExists then
  begin
    if MsgBox('StockLensDatabaseMCP requires the .NET 8.0 Desktop Runtime.'#13#10#13#10 +
              'Would you like to download it now?'#13#10 +
              '(You can install StockLensDatabaseMCP first and the .NET runtime afterward.)',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open',
        'https://dotnet.microsoft.com/en-us/download/dotnet/8.0',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
  end;
end;
