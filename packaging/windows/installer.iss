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
russian.CompFixes=Исправления игр (безопасные пресеты)
english.CompFixes=Game Fixes (safe presets)
russian.InstallingMod=Установка мода-компаньона в найденные игры…
english.InstallingMod=Installing the companion mod into the games found…
russian.InstallingFixes=Применение выбранного пресета исправлений…
english.InstallingFixes=Applying the selected Game Fix preset…
russian.FixesPresetCaption=Пресет исправлений
english.FixesPresetCaption=Game Fix preset
russian.FixesPresetDescription=Выберите исправления для поддерживаемых установок игры.
english.FixesPresetDescription=Choose fixes for supported game installations.
russian.FixesPresetPrompt=Команда применяется к найденным установкам с проверенной совместимой сборкой.
english.FixesPresetPrompt=The preset is applied to discovered installs with a verified compatible build.
russian.FixesRecommended=Рекомендуемые (по умолчанию)
english.FixesRecommended=Recommended (default)
russian.FixesEssential=Только обязательные
english.FixesEssential=Essential only
russian.FixesAllSafe=Все безопасные
english.FixesAllSafe=All safe
russian.FixesLater=Позже — не применять сейчас
english.FixesLater=Later — do not apply now
russian.FixesApplyFailed=Не удалось применить один или несколько пресетов. Исправления можно проверить и применить в приложении.
english.FixesApplyFailed=One or more presets could not be applied. Review and apply fixes in the app.
russian.CompanionApplyFailed=Не удалось установить мод-компаньон во все найденные игры.
english.CompanionApplyFailed=The companion mod could not be installed into every discovered game.

[Types]
Name: "full"; Description: "{code:FullTypeName}"
Name: "custom"; Description: "{code:CustomTypeName}"; Flags: iscustom

[Components]
Name: "app"; Description: "{cm:CompApp}"; Types: full custom; Flags: fixed
Name: "cli"; Description: "{cm:CompCli}"; Types: full
Name: "mod"; Description: "{cm:CompMod}"; Types: full
Name: "fixes"; Description: "{cm:CompFixes}"; Types: full custom

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "dist\{#MyAppExeName}"; DestDir: "{app}"; Components: app; Flags: ignoreversion
Source: "dist\BUILD_MANIFEST.json"; DestDir: "{app}"; Components: app; Flags: ignoreversion
Source: "dist\Assets\*"; DestDir: "{app}\Assets"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\mods\*"; DestDir: "{app}\mods"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\stalker_ooz.dll"; DestDir: "{app}"; Components: app; Flags: ignoreversion
; The CLI performs Companion and Game Fix operations, so it is copied when either component is chosen.
Source: "dist\{#MyCliExeName}"; DestDir: "{app}"; Components: cli mod fixes; Flags: ignoreversion


[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\INSTALLER_MARKER"

[Code]
var
  FixPresetPage: TInputOptionWizardPage;

procedure InitializeWizard;
begin
  FixPresetPage := CreateInputOptionPage(wpSelectComponents,
    ExpandConstant('{cm:FixesPresetCaption}'),
    ExpandConstant('{cm:FixesPresetDescription}'),
    ExpandConstant('{cm:FixesPresetPrompt}'), True, False);
  FixPresetPage.Add(ExpandConstant('{cm:FixesRecommended}'));
  FixPresetPage.Add(ExpandConstant('{cm:FixesEssential}'));
  FixPresetPage.Add(ExpandConstant('{cm:FixesAllSafe}'));
  FixPresetPage.Add(ExpandConstant('{cm:FixesLater}'));
  FixPresetPage.SelectedValueIndex := 0;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = FixPresetPage.ID) and not WizardIsComponentSelected('fixes');
end;

function RunCliAction(const Parameters: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{app}\{#MyCliExeName}'), Parameters,
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if Result then
    Result := ResultCode = 0;
end;

function FullTypeName(Param: String): String;
begin
  if ActiveLanguage = 'russian' then Result := 'Полная установка' else Result := 'Full installation';
end;

function CustomTypeName(Param: String): String;
begin
  if ActiveLanguage = 'russian' then Result := 'Выборочная установка' else Result := 'Custom installation';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  PresetName: String;
begin
  if CurStep = ssPostInstall then begin
    SaveStringToFile(ExpandConstant('{app}\INSTALLER_MARKER'), 'installer', False);
    if WizardIsComponentSelected('mod') then begin
      WizardForm.StatusLabel.Caption := ExpandConstant('{cm:InstallingMod}');
      if not RunCliAction('companion install all') then
        MsgBox(ExpandConstant('{cm:CompanionApplyFailed}'), mbError, MB_OK);
    end;
    if WizardIsComponentSelected('fixes') and (FixPresetPage.SelectedValueIndex <> 3) then begin
      case FixPresetPage.SelectedValueIndex of
        0: PresetName := 'recommended';
        1: PresetName := 'essential';
        2: PresetName := 'all-safe';
      else
        PresetName := 'recommended';
      end;
      WizardForm.StatusLabel.Caption := ExpandConstant('{cm:InstallingFixes}');
      if not RunCliAction('fixes apply-preset ' + PresetName + ' all') then
        MsgBox(ExpandConstant('{cm:FixesApplyFailed}'), mbError, MB_OK);
    end;
  end;
end;
