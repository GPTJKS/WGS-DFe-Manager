; Script Inno Setup — WGS DF-e Manager 0.17.5
#define MyAppName "WGS DF-e Manager"
#define MyAppVersion "0.17.5"
#define MyAppPublisher "WGS Assessoria Contábil"
#define MyAppURL "https://wgsassessoria.com.br/"
#define MyAppExeName "WgsDfe.Desktop.exe"

[Setup]
AppId={{8E9D4C52-7A1B-4F39-9E5C-2D1048A70170}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\Instalador
OutputBaseFilename=Setup_WgsDfeManager_0.17.5
SetupIconFile=..\src\WgsDfe.Desktop\Assets\wgs.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na Área de Trabalho (Desktop)"; GroupDescription: "Atalhos adicionais:"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb,appsettings.json"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\win-x64\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "..\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Sempre cria atalho no Menu Iniciar do Windows (pesquisa da tecla Windows)
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Comment: "WGS DF-e Manager 0.17.5 — Plataforma Corporativa de Documentos Fiscais"
; Cria atalho na Área de Trabalho se a opção estiver marcada no instalador
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "WGS DF-e Manager 0.17.5"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
