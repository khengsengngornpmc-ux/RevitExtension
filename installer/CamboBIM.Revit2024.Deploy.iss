; MHNK Revit deploy tools installer

#ifndef RevitYear
  #define RevitYear "2024"
#endif

#define MyAppName "MHNK_RVT" + RevitYear + "_EXTENSION_v1.00"
#define MyAppVersion "2026.02.20"
#define MyAppPublisher "Mohanokor Engineering Construction"
#define MyAppExeName "deploy-revit" + RevitYear + "-addin.exe"
#define InstallerLogoBmp AddBackslash(SourcePath) + "assets\\MHNK_Logo_55.bmp"
#define InstallerLogoIco AddBackslash(SourcePath) + "assets\\MHNK_Logo.ico"

#ifndef SourceDir
  #error SourceDir define is required. Pass /DSourceDir="path-to-package-folder"
#endif

#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
AppId=MHNK.Revit{#RevitYear}.DeployTools
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\MHNK\Revit{#RevitYear} Deploy Tools
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardSmallImageFile={#InstallerLogoBmp}
SetupIconFile={#InstallerLogoIco}
PrivilegesRequired=admin
ArchitecturesAllowed=x86 x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MHNK Deploy Tool"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\Deploy Readme"; Filename: "{app}\README_DEPLOY.txt"
Name: "{autodesktop}\MHNK Deploy Tool"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--no-ui --configuration Release --platform x64 --revit-year {#RevitYear} --all-users --unlock-test-users --force-license-config"; WorkingDir: "{app}"; Description: "Register MHNK for all users now"; Flags: postinstall skipifsilent
