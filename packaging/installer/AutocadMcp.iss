; Inno Setup 6 script. Built by scripts/package.ps1 (it passes /DAppVersion and /DSourceDir).
; Installs ONLY the AutoCAD plugin, per user, without administrator rights.
; The Claude side is installed separately from the .mcpb file.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\artifacts\bundle\AutocadMcp.bundle"
#endif

[Setup]
AppId={{D510F497-769B-4DC3-B82D-23E443666469}
AppName=AutoCAD MCP Connector
AppVersion={#AppVersion}
AppPublisher=Autodesk-Connector (internal)
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\AutocadMcp.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\..\artifacts
OutputBaseFilename=AutocadMcp-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Never close AutoCAD automatically: the user could lose unsaved work.
CloseApplications=no
Uninstallable=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
russian.WelcomeLabel2=Будет установлен коннектор, через который Claude сможет чертить в AutoCAD 2025.%n%nПЕРЕД УСТАНОВКОЙ ЗАКРОЙ AutoCAD.%n%nПосле установки при первом запуске AutoCAD может спросить про загрузку надстройки: выбери «Всегда загружать».

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
