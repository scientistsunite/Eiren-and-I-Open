{ Keep raw DLL queries in the same filesystem view as Inno's64-bit file
  operations. Sysnative is Windows' virtual alias for a WOW64 caller.
  No thread/process redirection setting is changed and no link is followed. }
function GetDriveTypeW(Root: String): LongWord; external 'GetDriveTypeW@kernel32.dll stdcall';
function GetFileAttributesW(Path: String): LongWord; external 'GetFileAttributesW@kernel32.dll stdcall';
function RaiderCurrentProcess: THandle; external 'GetCurrentProcess@kernel32.dll stdcall';
function RaiderIsWow64(Process: THandle; var Wow64: LongWord): LongWord; external 'IsWow64Process@kernel32.dll stdcall';
function AttributeQueryPath(Path: String; Native64: Boolean): String;
var SystemPath: String;
begin
  Result := Path;
  if Native64 then begin
    SystemPath := AddBackslash(ExpandConstant('{win}')) + 'System32';
    if (CompareText(Path, SystemPath) = 0) or
       (CompareText(Copy(Path, 1, Length(SystemPath) + 1), SystemPath + '\') = 0) then
      Result := AddBackslash(ExpandConstant('{win}')) + 'Sysnative' + Copy(Path, Length(SystemPath) + 1, Length(Path));
  end;
end;
function SafeDirectory(Path: String): Boolean;
var Parent, Component, Probe: String; Parts: TArrayOfString;
    Attributes, Wow64, DriveKind: LongWord; Error, Count, I: Integer; Native64: Boolean;
begin
  Result := False;
  if (Length(Path) < 3) or (Copy(Path, 2, 2) <> ':\') then exit;
  DriveKind := GetDriveTypeW(Copy(Path, 1, 3));
  if (Copy(Path, 1, 2) = '\\') or (DriveKind <= 1) or (DriveKind = 4) then exit;
  Native64 := False;
  if Is64BitInstallMode then begin
    if RaiderIsWow64(RaiderCurrentProcess, Wow64) = 0 then begin
      Log('Raider storage validation unavailable: process architecture query failed'); exit;
    end;
    Native64 := Wow64 <> 0;
  end;
  Count := 0;
  while Length(Path) > 3 do begin
    if Count >= 128 then begin Log('Raider storage validation refused excessive path depth'); exit; end;
    SetArrayLength(Parts, Count + 1); Parts[Count] := Path; Count := Count + 1;
    Parent := ExtractFileDir(Path); if Parent = Path then exit; Path := Parent;
  end;
  { Inspect ancestors first: do not query a descendant through a junction. }
  for I := Count - 1 downto 0 do begin
    Component := Parts[I]; Probe := AttributeQueryPath(Component, Native64);
#ifdef RaiderStorageGuardTrace
    Log('RAIDER_GUARD_ATTRIBUTE_QUERY ' + Component);
#endif
    if Probe <> Component then Log('Raider storage native query: ' + Component + ' -> ' + Probe);
    Attributes := GetFileAttributesW(Probe); Error := DLLGetLastError;
    if Attributes = $FFFFFFFF then begin
      { Missing descendants may be legitimate; unreadable/unknown ancestors
        must never be mistaken for absent directories. }
      if (Error <> 2) and (Error <> 3) then begin
        Log('Raider storage validation unavailable: ' + Component + ' error=' + IntToStr(Error)); exit;
      end;
    end else if ((Attributes and $400) <> 0) or ((Attributes and $10) = 0) then begin
      Log('Raider storage validation refused link/non-directory: ' + Component); exit;
    end;
  end;
  Result := True;
end;

procedure NotifyPersonalDataPreserved();
begin
  SuppressibleMsgBox('Data deletion refused because the directory could not be validated or contains a reparse point. Application files were removed; data was preserved for manual review.', mbInformation, MB_OK, IDOK);
end;
