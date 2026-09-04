#define AppName "SnapSort"
#define AppPublisher "Patryk Bąkowski"
#define AppExeName "SnapSort.exe"
#define PublishDir "..\artifacts\SnapSort-win-x64"
#define BrandingDir "..\src\SnapSort.App\Assets\Branding"

#ifndef AppVersion
  #error AppVersion must be passed by build-release.ps1
#endif

[Setup]
AppId={{8E0AF176-173B-42BE-91DE-D1F3BA3EF597}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/P-Bakowski/SnapSort
AppSupportURL=https://github.com/P-Bakowski/SnapSort/issues
AppUpdatesURL=https://github.com/P-Bakowski/SnapSort/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir=..\artifacts\release
OutputBaseFilename=SnapSort_Setup_v{#AppVersion}
SetupIconFile={#BrandingDir}\SnapSort.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dark polar includetitlebar hidebevels
WizardSizePercent=110
WizardImageFile={#BrandingDir}\SnapSort_1024.png
WizardSmallImageFile={#BrandingDir}\SnapSort_128x128.png
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.17763
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Instalator SnapSort
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (C) 2026 {#AppPublisher}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "Utwórz skrót na pulpicie"; GroupDescription: "Dodatkowe opcje:"; Flags: unchecked
Name: "startmenuicon"; Description: "Utwórz skrót w menu Start"; GroupDescription: "Dodatkowe opcje:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\SnapSort"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\SnapSort"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{group}\Odinstaluj SnapSort"; Filename: "{uninstallexe}"; Tasks: startmenuicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Uruchom SnapSort"; Flags: nowait postinstall skipifsilent unchecked

[Messages]
WelcomeLabel1=Witaj w instalatorze SnapSort
WelcomeLabel2=SnapSort v{#AppVersion}%n%nInstalator SnapSort
FinishedHeadingLabel=SnapSort został pomyślnie zainstalowany.
FinishedLabelNoIcons=Kliknij Zakończ, aby zamknąć instalator.
FinishedLabel=Kliknij Zakończ, aby zamknąć instalator.
ButtonFinish=Zakończ

[Code]
var
  ThemeButton: TNewButton;

function HasParameter(Value: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function IsLightMode: Boolean;
begin
  Result := HasParameter('/LIGHTMODE');
end;

procedure SwitchTheme(Sender: TObject);
var
  Params: String;
  ErrorCode: Integer;
begin
  if IsLightMode then Params := '' else Params := '/NOSTYLE /LIGHTMODE';
  ShellExec('', ExpandConstant('{srcexe}'), Params, '', SW_SHOW, ewNoWait, ErrorCode);
  WizardForm.Close;
end;

procedure InitializeWizard;
begin
  WizardForm.Caption := 'SnapSort v{#AppVersion}';
  ThemeButton := TNewButton.Create(WizardForm);
  ThemeButton.Parent := WizardForm;
  ThemeButton.Width := ScaleX(65);
  ThemeButton.Height := ScaleY(25);
  ThemeButton.Left := WizardForm.ClientWidth - ThemeButton.Width - ScaleX(16);
  ThemeButton.Top := ScaleY(10);
  ThemeButton.Anchors := [akTop, akRight];
  if IsLightMode then ThemeButton.Caption := 'Ciemny' else ThemeButton.Caption := 'Jasny';
  ThemeButton.OnClick := @SwitchTheme;
end;
