; S.T.A.L.K.E.R. Save Editor — one installer for every game (decision D16).
; Components: the application (always), the command-line tool, and the companion mod installed
; into every S.T.A.L.K.E.R. game found on this PC (Steam, GOG, GSC disc installs).
#define MyAppName "S.T.A.L.K.E.R. Save Editor"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppPublisher "Dmitriy-DE"
#define MyAppURL "https://github.com/Dmitriy-DE/S.T.A.L.K.E.R.-Save-Editor"
#define MyAppExeName "StalkerSaveEditor.exe"
#define MyCliExeName "stalker-save-editor-cli.exe"

[Setup]
AppId={{5E973B9B-8344-4821-B86B-25B3E75A384F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\StalkerSaveEditor
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\..\dist
OutputBaseFilename=StalkerSaveEditor-Setup-{#MyAppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.CompApp=Редактор сохранений
english.CompApp=Save editor
russian.CompCli=Консольная утилита (stalker-save-editor-cli)
english.CompCli=Command-line tool (stalker-save-editor-cli)
russian.CompMod=Мод-компаньон во все найденные игры S.T.A.L.K.E.R. (ТЧ, ЧН, ЗП)
english.CompMod=Companion mod into every S.T.A.L.K.E.R. game found (SoC, CS, CoP)
russian.InstallingMod=Установка мода-компаньона в найденные игры…
english.InstallingMod=Installing the companion mod into the games found…

[Types]
Name: "full"; Description: "{code:FullTypeName}"
Name: "custom"; Description: "{code:CustomTypeName}"; Flags: iscustom

[Components]
Name: "app"; Description: "{cm:CompApp}"; Types: full custom; Flags: fixed
Name: "cli"; Description: "{cm:CompCli}"; Types: full
Name: "mod"; Description: "{cm:CompMod}"; Types: full

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "dist\{#MyAppExeName}"; DestDir: "{app}"; Components: app; Flags: ignoreversion
Source: "dist\BUILD_MANIFEST.json"; DestDir: "{app}"; Components: app; Flags: ignoreversion
Source: "dist\Assets\*"; DestDir: "{app}\Assets"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\mods\*"; DestDir: "{app}\mods"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\stalker_ooz.dll"; DestDir: "{app}"; Components: app; Flags: ignoreversion
; The CLI also performs the mod installation, so it is copied whenever either component is chosen.
Source: "dist\{#MyCliExeName}"; DestDir: "{app}"; Components: cli mod; Flags: ignoreversion


[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyCliExeName}"; Parameters: "companion install all"; StatusMsg: "{cm:InstallingMod}"; Components: mod; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\INSTALLER_MARKER"

[Code]
function FullTypeName(Param: String): String;
begin
  if ActiveLanguage = 'russian' then Result := 'Полная установка' else Result := 'Full installation';
end;

function CustomTypeName(Param: String): String;
begin
  if ActiveLanguage = 'russian' then Result := 'Выборочная установка' else Result := 'Custom installation';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\INSTALLER_MARKER'), 'installer', False);
end;
