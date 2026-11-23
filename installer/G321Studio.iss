#ifndef PayloadRoot
  #error Defina PayloadRoot com o diretório dos arquivos a empacotar.
#endif
#ifndef OutputFolder
  #error Defina OutputFolder com o diretório do instalador gerado.
#endif
#if Ver < 0x06070300
  #error Use Inno Setup 6.7.3 ou posterior para proteger a pasta temporária elevada.
#endif

#define StudioExe "G321 Studio.exe"
#define ApoX64 "EqualizerAPO-x64-1.4.2.exe"
#define ApoX86 "EqualizerAPO-x86-1.4.2.exe"
#define ApoArm64 "EqualizerAPO-ARM64-1.4.2.exe"

[Setup]
AppId={{653ED62E-C65F-4D37-BBF7-8B55A029516D}
AppName=G321 Studio
AppVersion=1.0.4
DefaultDirName={localappdata}\Programs\G321 Studio
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#StudioExe}
SetupIconFile=..\Assets\G321.ico
#ifdef AuthenticodeSign
SignTool=G321Authenticode
SignedUninstaller=yes
#endif
VersionInfoDescription=G321 Studio
OutputDir={#OutputFolder}
OutputBaseFilename=G321 Studio Setup
ShowLanguageDialog=yes
Compression=lzma2
SolidCompression=no
MinVersion=10.0.19041
ArchitecturesAllowed=x86os or x64os or arm64
PrivilegesRequired=lowest
AppMutex=Local\G321 Studio
CloseApplications=no
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"; InfoBeforeFile: "SetupInfo.txt"
Name: "en"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "SetupInfo.en.txt"

[LangOptions]
ptbr.LanguageName=Português (Brasil)

[CustomMessages]
ptbr.AppLanguage=pt-BR
en.AppLanguage=en-US
ptbr.StartupTask=Iniciar o G321 Studio na bandeja ao entrar no Windows
en.StartupTask=Start G321 Studio in the notification area when you sign in to Windows
ptbr.InstallApo=Instalar o Equalizer APO (requer administrador)
en.InstallApo=Install Equalizer APO (administrator required)
ptbr.LaunchStudio=Iniciar o G321 Studio na bandeja
en.LaunchStudio=Start G321 Studio in the notification area
ptbr.StartupCheckFailed=Não foi possível verificar o atalho existente da Inicialização.
en.StartupCheckFailed=Could not verify the existing Startup shortcut.
ptbr.ApoProtectedTempRequired=A pasta temporária do Equalizer APO precisa estar em um volume local com permissões de acesso.
en.ApoProtectedTempRequired=The Equalizer APO temporary folder must be on a local volume with access controls.
ptbr.ApoIntegrityFailed=A verificação de integridade do instalador do Equalizer APO falhou.
en.ApoIntegrityFailed=The Equalizer APO installer failed its integrity check.
ptbr.ApoHashVerified=SHA-256 do instalador Equalizer APO verificado.
en.ApoHashVerified=Equalizer APO installer SHA-256 verified.
ptbr.ApoTempConfigFailed=Não foi possível configurar a pasta temporária do instalador do Equalizer APO.
en.ApoTempConfigFailed=Could not configure the Equalizer APO installer temporary folder.
ptbr.ApoLaunchFailed=Não foi possível abrir o instalador do Equalizer APO (erro do Windows %1).
en.ApoLaunchFailed=Could not open the Equalizer APO installer (Windows error %1).
ptbr.ApoElevationRequired=Etapa do Equalizer APO recusada sem elevação.
en.ApoElevationRequired=Equalizer APO installation was refused without elevation.
ptbr.RunSetupAsUser=Abra o instalador normalmente na conta que vai usar o G321 Studio.
en.RunSetupAsUser=Open Setup normally from the account that will use G321 Studio.

[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"

[Files]
Source: "{#PayloadRoot}\app\x64\{#StudioExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: IsX64OS
Source: "{#PayloadRoot}\app\x86\{#StudioExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: IsX86OS
Source: "{#PayloadRoot}\app\arm64\{#StudioExe}"; DestDir: "{app}"; Flags: ignoreversion; Check: IsArm64
Source: "{#PayloadRoot}\vendor\{#ApoX64}"; Flags: dontcopy
Source: "{#PayloadRoot}\vendor\{#ApoX86}"; Flags: dontcopy
Source: "{#PayloadRoot}\vendor\{#ApoArm64}"; Flags: dontcopy
Source: "{#PayloadRoot}\vendor\EqualizerAPO-src-1.4.2.zip"; DestDir: "{app}\Licenças"; Flags: ignoreversion
Source: "{#PayloadRoot}\notices\*"; DestDir: "{app}\Licenças"; Flags: ignoreversion
Source: "SetupInfo.txt"; DestDir: "{app}"; DestName: "G321 Studio.txt"; Flags: ignoreversion; Languages: ptbr
Source: "SetupInfo.en.txt"; DestDir: "{app}"; DestName: "G321 Studio.txt"; Flags: ignoreversion; Languages: en

[Registry]
Root: HKCU; Subkey: "Software\G321 Studio"; ValueType: string; ValueName: "Language"; ValueData: "{cm:AppLanguage}"; Flags: uninsdeletevalue uninsdeletekeyifempty

[Icons]
Name: "{userprograms}\G321 Studio"; Filename: "{app}\{#StudioExe}"; WorkingDir: "{app}"
Name: "{userstartup}\G321 Studio"; Filename: "{app}\{#StudioExe}"; Parameters: "--auto-eq"; WorkingDir: "{app}"; Tasks: startup

[InstallDelete]
Type: files; Name: "{userstartup}\G321 Studio.lnk"; Tasks: not startup; Check: IsManagedStartupShortcut
Type: files; Name: "{app}\Como configurar.txt"; Check: IsUnchangedLegacyGuide

[Run]
Filename: "{srcexe}"; Parameters: "{code:ApoElevationArguments}"; WorkingDir: "{sys}"; Verb: "runas"; Description: "{cm:InstallApo}"; Flags: postinstall unchecked skipifsilent shellexec waituntilterminated; Check: IsApoMissing
Filename: "{app}\{#StudioExe}"; Description: "{cm:LaunchStudio}"; WorkingDir: "{app}"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: CanStartStudio

[Code]
const
  DriveFixed = 3;
  FilePersistentAcls = $00000008;
  LegacyGuideHash = '2D7281BD04E1743B5786ADC86F35188F7F95C34EFDBCA547FA78D3D5D9E399F9';

var
  SourceInstaller: TFileStream;

function GetDriveType(const RootPath: String): Cardinal;
  external 'GetDriveTypeW@kernel32.dll stdcall';
function GetVolumePathName(const Path, VolumePath: String; BufferLength: Cardinal): Boolean;
  external 'GetVolumePathNameW@kernel32.dll stdcall';
function GetVolumeInformation(const RootPath: String; VolumeName, VolumeNameSize,
  SerialNumber, MaximumComponentLength: Cardinal; var FileSystemFlags: Cardinal;
  FileSystemName, FileSystemNameSize: Cardinal): Boolean;
  external 'GetVolumeInformationW@kernel32.dll stdcall';
function SetEnvironmentVariable(const Name, Value: String): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

function IsManagedStartupShortcut: Boolean;
var
  ShortcutPath: String;
  Shell: Variant;
begin
  Result := False;
  ShortcutPath := ExpandConstant('{userstartup}\G321 Studio.lnk');
  if not FileExists(ShortcutPath) then
    Exit;

  try
    Shell := CreateOleObject('WScript.Shell');
    Result := CompareText(Shell.CreateShortcut(ShortcutPath).TargetPath,
      ExpandConstant('{app}\{#StudioExe}')) = 0;
  except
    Log(CustomMessage('StartupCheckFailed'));
  end;
end;

function IsUnchangedLegacyGuide: Boolean;
var
  Guide: String;
begin
  Result := False;
  Guide := ExpandConstant('{app}\Como configurar.txt');
  if not FileExists(Guide) then
    Exit;

  try
    // Remove only the original guide from v1.0.1 and v1.0.2, keeping user edits.
    Result := CompareText(GetSHA256OfFile(Guide), LegacyGuideHash) = 0;
  except
    Result := False;
  end;
end;

function IsApoMissing: Boolean;
var
  ApoRegistryRoot: Integer;
  ConfigDirectory: String;
  InstallDirectory: String;
begin
  if IsWin64 then
    ApoRegistryRoot := HKLM64
  else
    ApoRegistryRoot := HKLM32;

  Result := not (
    RegQueryStringValue(ApoRegistryRoot, 'SOFTWARE\EqualizerAPO', 'ConfigPath', ConfigDirectory) and
    (ConfigDirectory <> '') and FileExists(AddBackslash(ConfigDirectory) + 'config.txt') and
    RegQueryStringValue(ApoRegistryRoot, 'SOFTWARE\EqualizerAPO', 'InstallPath', InstallDirectory) and
    (InstallDirectory <> '') and FileExists(AddBackslash(InstallDirectory) + 'EqualizerAPO.dll'));
end;

function ApoElevationArguments(Param: String): String;
begin
  Result := '/INSTALLG321APO /SP- /LANG=' + ActiveLanguage;
  if ExpandConstant('{log}') <> '' then
    Result := Result + ' /LOG';
end;

function HasProtectedTempVolume(const TempPath: String): Boolean;
var
  DriveRoot, VolumeRoot: String;
  FileSystemFlags: Cardinal;
begin
  Result := False;
  DriveRoot := AddBackslash(ExtractFileDrive(TempPath));
  SetLength(VolumeRoot, Length(TempPath) + 2);
  if not GetVolumePathName(TempPath, VolumeRoot, Length(VolumeRoot)) then
    Exit;
  SetLength(VolumeRoot, Pos(#0, VolumeRoot) - 1);
  if CompareText(VolumeRoot, DriveRoot) <> 0 then
    Exit;
  Result := (GetDriveType(DriveRoot) = DriveFixed) and
    GetVolumeInformation(DriveRoot, 0, 0, 0, 0, FileSystemFlags, 0, 0) and
    ((FileSystemFlags and FilePersistentAcls) <> 0);
end;

procedure InstallBundledApo;
var
  InstallerName, ExpectedHash, TempPath, InstallerPath: String;
  ReturnCode: Integer;
  Installer: TFileStream;
begin
  if IsArm64 then
  begin
    InstallerName := '{#ApoArm64}';
    ExpectedHash := '{#ApoArm64Sha256}';
  end
  else if IsX64OS then
  begin
    InstallerName := '{#ApoX64}';
    ExpectedHash := '{#ApoX64Sha256}';
  end
  else
  begin
    InstallerName := '{#ApoX86}';
    ExpectedHash := '{#ApoX86Sha256}';
  end;

  // Inno restricts writes to this directory for elevated Setup on local volumes with ACLs.
  TempPath := ExpandConstant('{tmp}');
  if not HasProtectedTempVolume(TempPath) then
    RaiseException(CustomMessage('ApoProtectedTempRequired'));

  ExtractTemporaryFile(InstallerName);
  InstallerPath := AddBackslash(TempPath) + InstallerName;
  // Keep the verified file open without write or delete sharing through process launch.
  Installer := TFileStream.Create(InstallerPath, fmOpenRead or fmShareDenyWrite);
  try
    if CompareText(GetSHA256OfStream(Installer), ExpectedHash) <> 0 then
      RaiseException(CustomMessage('ApoIntegrityFailed'));
    Log(CustomMessage('ApoHashVerified'));
    // The vendor installer also extracts its own temporary files.
    if not SetEnvironmentVariable('TEMP', TempPath) or not SetEnvironmentVariable('TMP', TempPath) then
      RaiseException(CustomMessage('ApoTempConfigFailed'));
    if not Exec(InstallerPath, '', TempPath, SW_SHOWNORMAL, ewWaitUntilTerminated, ReturnCode) then
      RaiseException(FmtMessage(CustomMessage('ApoLaunchFailed'), [IntToStr(ReturnCode)]));
  finally
    Installer.Free;
  end;
end;

function InitializeSetup: Boolean;
var
  Argument: Integer;
begin
  Result := False;
  for Argument := 1 to ParamCount do
    if CompareText(ParamStr(Argument), '/INSTALLG321APO') = 0 then
    begin
      if not IsAdmin then
      begin
        Log(CustomMessage('ApoElevationRequired'));
        Exit;
      end;
      try
        if IsApoMissing then
          InstallBundledApo;
      except
        SuppressibleMsgBox(GetExceptionMessage, mbError, MB_OK, IDOK);
      end;
      Exit;
    end;

  if IsAdmin then
  begin
    SuppressibleMsgBox(CustomMessage('RunSetupAsUser'), mbInformation, MB_OK, IDOK);
    Exit;
  end;

  // The optional elevation reopens this same Setup executable. Keep it stable while the wizard is open.
  SourceInstaller := TFileStream.Create(ExpandConstant('{srcexe}'), fmOpenRead or fmShareDenyWrite);
  Result := True;
end;

procedure DeinitializeSetup;
begin
  if SourceInstaller <> nil then
    SourceInstaller.Free;
end;

function CanStartStudio: Boolean;
begin
  Result := not IsAdmin;
end;
