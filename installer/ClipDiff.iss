; Build through scripts/create-installer.ps1 using the validated self-contained payload.
#ifndef AppVersion
	#error AppVersion is required
#endif
#ifndef AppFileVersion
	#error AppFileVersion is required
#endif
#ifndef PayloadDirectory
	#error PayloadDirectory is required
#endif
#ifndef ExtensionFileName
	#error ExtensionFileName is required
#endif
#ifndef InstallerOutputDirectory
	#error InstallerOutputDirectory is required
#endif

[Setup]
AppId={{75F83916-586C-4D73-B744-AD39917A5039}
AppName=ClipDiff
AppVersion={#AppVersion}
VersionInfoVersion={#AppFileVersion}
AppPublisher=Stuart Dunkeld
AppPublisherURL=https://stuartd.dev
AppSupportURL=https://github.com/stuartd/ClipboardDiff
DefaultDirName={localappdata}\Programs\ClipDiff
DefaultGroupName=ClipDiff
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.17763
OutputDir={#InstallerOutputDirectory}
OutputBaseFilename=ClipDiff-{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\ClipDiff.Windows\Assets\ClipDiff.ico
UninstallDisplayIcon={app}\ClipDiff.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Never register DLLs with Restart Manager: Explorer may have one loaded.
CloseApplications=no
RestartApplications=no
SetupMutex=Local\ClipDiff.Setup

[Files]
Source: "{#PayloadDirectory}\ClipDiff.exe"; DestDir: "{app}"; Flags: ignoreversion
; Immutable filenames: even an unchanged DLL must not be opened for overwrite.
Source: "{#PayloadDirectory}\{#ExtensionFileName}"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{group}\ClipDiff"; Filename: "{app}\ClipDiff.exe"

[Run]
Filename: "{app}\ClipDiff.exe"; Description: "Launch ClipDiff"; Flags: nowait postinstall skipifsilent

[Code]
function OpenEvent(DesiredAccess: LongWord; InheritHandle: Integer; Name: String): THandle;
	external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): Integer;
	external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Integer;
	external 'CloseHandle@kernel32.dll stdcall';

function StopClipDiff(): Boolean;
var
	ShutdownEvent: THandle;
	Attempt: Integer;
begin
	Result := True;

	if not CheckForMutexes('Local\ClipDiff') then
	begin
		Exit;
	end;

	ShutdownEvent := OpenEvent($0002, 0, 'Local\ClipDiff.ShutdownForUpdate');

	if ShutdownEvent <> 0 then
	begin
		SetEvent(ShutdownEvent);
		CloseHandle(ShutdownEvent);

		for Attempt := 1 to 100 do
		begin
			Sleep(100);

			if not CheckForMutexes('Local\ClipDiff') then
			begin
				Exit;
			end;
		end;
	end;

	// Pre-installer portable builds do not support the shutdown signal.
	Result := False;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
	Result := '';

	if not StopClipDiff() then
	begin
		Result := 'ClipDiff could not close automatically. Quit the running copy from its tray menu, then click Next to continue. Your captured text will be discarded when ClipDiff closes.';
	end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
	Command: String;
begin
	// Preserve an existing opt-in even if Launch ClipDiff is unchecked.
	if (CurStep = ssPostInstall) and
		RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ClipDiff', Command) and
		(Command <> '') then
	begin
		RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
			'ClipDiff', '"' + ExpandConstant('{app}\ClipDiff.exe') + '"');
	end;
end;

function InitializeUninstall(): Boolean;
begin
	Result := StopClipDiff();

	if not Result then
	begin
		MsgBox('ClipDiff could not close automatically. Quit ClipDiff and run the uninstaller again.', mbError, MB_OK);
	end;
end;

function IsInstalledPath(Value: String): Boolean;
begin
	Result := Pos(Lowercase(ExpandConstant('{app}\')), Lowercase(Value)) = 1;
end;

function IsExtensionFileName(Name: String): Boolean;
var
	Index: Integer;
	Hash, Prefix: String;
begin
	Result := Name = 'ClipDiff.ShellExtension.dll';

	if Result then
	begin
		Exit;
	end;

	Prefix := 'ClipDiff.ShellExtension.';
	Hash := Copy(Name, Length(Prefix) + 1, 64);

	if (Copy(Name, 1, Length(Prefix)) <> Prefix) or
		(Length(Name) <> Length(Prefix) + 64 + 4) or
		(Copy(Name, Length(Name) - 3, 4) <> '.dll') then
	begin
		Exit;
	end;

	for Index := 1 to Length(Hash) do
	begin
		if Pos(Hash[Index], '0123456789abcdef') = 0 then
		begin
			Exit;
		end;
	end;

	Result := True;
end;

procedure RemoveOwnedKey(Key: String; PathKey: String; QuotedExecutable: Boolean);
var
	Owner, Value: String;
begin
	if RegQueryStringValue(HKCU, Key, 'ClipDiffOwner', Owner) and
		(Owner = 'ClipDiff.ExplorerContextMenu.v1') and
		RegQueryStringValue(HKCU, PathKey, '', Value) then
	begin
		if (QuotedExecutable and (Pos('"' + Lowercase(ExpandConstant('{app}\ClipDiff.exe')) + '"', Lowercase(Value)) = 1)) or
			((not QuotedExecutable) and IsInstalledPath(Value)) then
		begin
			RegDeleteKeyIncludingSubkeys(HKCU, Key);
		end;
	end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
	Command, ExtensionPath, Owner: String;
	FileInfo: TFindRec;
begin
	if CurUninstallStep <> usPostUninstall then
	begin
		Exit;
	end;

	if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ClipDiff', Command) and
		(CompareText(Command, '"' + ExpandConstant('{app}\ClipDiff.exe') + '"') = 0) then
	begin
		RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ClipDiff');
	end;

	RemoveOwnedKey('Software\Classes\*\shell\ClipDiff.CompareWithCurrent',
		'Software\Classes\*\shell\ClipDiff.CompareWithCurrent\command', True);
	RemoveOwnedKey('Software\Classes\CLSID\{4D22FA39-9E5D-42BD-BF0A-8AE885704EC7}',
		'Software\Classes\CLSID\{4D22FA39-9E5D-42BD-BF0A-8AE885704EC7}\LocalServer32', True);

	if RegQueryStringValue(HKCU, 'Software\Classes\CLSID\{6B46A974-40E2-4AD4-9F68-E534202B11E8}\InprocServer32', '', ExtensionPath) and
		IsInstalledPath(ExtensionPath) then
	begin
		RemoveOwnedKey('Software\Classes\CLSID\{6B46A974-40E2-4AD4-9F68-E534202B11E8}',
			'Software\Classes\CLSID\{6B46A974-40E2-4AD4-9F68-E534202B11E8}\InprocServer32', False);

		if RegQueryStringValue(HKCU, 'Software\Classes\*\shellex\ContextMenuHandlers\ClipDiff.CompareSelected', 'ClipDiffOwner', Owner) and
			(Owner = 'ClipDiff.ExplorerContextMenu.v1') then
		begin
			RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\*\shellex\ContextMenuHandlers\ClipDiff.CompareSelected');
		end;
	end;

	// Best effort only. Loaded images remain until Windows releases them; no reboot is requested.
	if FindFirst(ExpandConstant('{app}\ClipDiff.ShellExtension*.dll'), FileInfo) then
	begin
		try
			repeat
				if IsExtensionFileName(FileInfo.Name) then
				begin
					DeleteFile(ExpandConstant('{app}\') + FileInfo.Name);
				end;
			until not FindNext(FileInfo);
		finally
			FindClose(FileInfo);
		end;
	end;

	RemoveDir(ExpandConstant('{app}'));
end;
