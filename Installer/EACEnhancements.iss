#define AppName "EAC Enhancements"
#define AppPublisher "metaisfacil"
#define AppUrl "https://github.com/metaisfacil/EACEnhancements"

#ifndef AppVersion
  #define AppVersion GetFileVersion("..\Artifacts\EACEnhancements.dll")
#endif

[Setup]
AppId={{B9A9B59D-51DC-46C2-B476-A793B12D6EF7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={code:GetDefaultEacDirectory}
DirExistsWarning=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir=..\Artifacts
OutputBaseFilename=EACEnhancements-Setup
Compression=lzma2/max
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
CloseApplications=no
RestartApplications=no
SetupLogging=yes
UninstallDisplayIcon={app}\EAC.exe
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}

[Files]
Source: "..\Artifacts\EACEnhancements.dll"; DestDir: "{app}"; Flags: ignoreversion

[Code]
function DirectoryFromRegistryValue(Value: String): String;
var
  ClosingQuote: Integer;
  ExecutableEnd: Integer;
begin
  Result := '';
  Value := Trim(Value);
  if Value = '' then
    Exit;

  if Value[1] = '"' then
  begin
    Delete(Value, 1, 1);
    ClosingQuote := Pos('"', Value);
    if ClosingQuote > 0 then
      Value := Copy(Value, 1, ClosingQuote - 1);
  end
  else
  begin
    ExecutableEnd := Pos('.exe', Lowercase(Value));
    if ExecutableEnd > 0 then
      Value := Copy(Value, 1, ExecutableEnd + 3);
  end;

  Value := RemoveQuotes(Trim(Value));
  if CompareText(ExtractFileExt(Value), '.exe') = 0 then
    Value := ExtractFileDir(Value);
  Result := RemoveBackslashUnlessRoot(Value);
end;

function TryEacDirectory(Value: String; var EacDirectory: String): Boolean;
var
  Candidate: String;
begin
  Candidate := DirectoryFromRegistryValue(Value);
  Result := (Candidate <> '') and FileExists(AddBackslash(Candidate) + 'EAC.exe');
  if Result then
    EacDirectory := Candidate;
end;

function FindEacInUninstallRoot(RootKey: Integer; var EacDirectory: String): Boolean;
var
  Keys: TArrayOfString;
  Index: Integer;
  KeyPath: String;
  DisplayName: String;
  Value: String;
begin
  Result := False;
  if not RegGetSubkeyNames(
    RootKey,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall',
    Keys) then
    Exit;

  for Index := 0 to GetArrayLength(Keys) - 1 do
  begin
    KeyPath := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + Keys[Index];
    if RegQueryStringValue(RootKey, KeyPath, 'DisplayName', DisplayName) and
       (Pos('Exact Audio Copy', DisplayName) = 1) then
    begin
      if RegQueryStringValue(RootKey, KeyPath, 'InstallLocation', Value) and
         TryEacDirectory(Value, EacDirectory) then
      begin
        Result := True;
        Exit;
      end;
      if RegQueryStringValue(RootKey, KeyPath, 'DisplayIcon', Value) and
         TryEacDirectory(Value, EacDirectory) then
      begin
        Result := True;
        Exit;
      end;
      if RegQueryStringValue(RootKey, KeyPath, 'UninstallString', Value) and
         TryEacDirectory(Value, EacDirectory) then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

function FindRegisteredEac(var EacDirectory: String): Boolean;
begin
  Result := FindEacInUninstallRoot(HKLM32, EacDirectory) or
    FindEacInUninstallRoot(HKCU32, EacDirectory);
  if Result then
    Exit;

  if IsWin64 then
    Result := FindEacInUninstallRoot(HKLM64, EacDirectory) or
      FindEacInUninstallRoot(HKCU64, EacDirectory);
end;

function GetDefaultEacDirectory(Param: String): String;
begin
  if not FindRegisteredEac(Result) then
    Result := ExpandConstant('{pf32}\Exact Audio Copy');
end;

function IsEacRunning(): Boolean;
var
  Locator: Variant;
  Services: Variant;
  Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Services := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Services.ExecQuery(
      'SELECT ProcessId FROM Win32_Process WHERE Name LIKE "EAC%.exe"');
    Result := Processes.Count > 0;
  except
    Log('Could not query running EAC processes.');
  end;
end;

function ValidateEacDirectory(Directory: String; var ErrorMessage: String): Boolean;
begin
  Result := FileExists(AddBackslash(Directory) + 'EAC.exe');
  if not Result then
    ErrorMessage := 'EAC.exe was not found in the selected folder:' + #13#10#13#10 + Directory;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorMessage: String;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and
     not ValidateEacDirectory(WizardDirValue, ErrorMessage) then
  begin
    MsgBox(ErrorMessage, mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if not ValidateEacDirectory(WizardDirValue, Result) then
    Exit;
  if IsEacRunning() then
    Result := 'Close every Exact Audio Copy window before installing EAC Enhancements.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := not IsEacRunning();
  if not Result then
    MsgBox(
      'Close every Exact Audio Copy window before uninstalling EAC Enhancements.',
      mbError,
      MB_OK);
end;
