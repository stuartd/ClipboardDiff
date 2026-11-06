# Windows install and update verification

These checks require an interactive x64 Windows 10/11 desktop. A Linux build is
not evidence that installation or Explorer integration works.

## Automated installer smoke test

On a disposable Windows user profile with no existing installer installation:

```powershell
.\scripts\create-local-release.ps1
.\scripts\test-installer.ps1
```

Requires the .NET 10 SDK, Visual Studio C++ tools/Windows SDK and Inno Setup 6.3+
(the `Inno Setup 6` install location, or `ISCC.exe` on PATH). Close any portable
ClipDiff first. The test temporarily backs up/restores preferences and the Run
entry. It installs into a private directory under `artifacts/installer-smoke`,
disables network checks, starts the real app, and holds its extension open with
`LoadLibrary`. It tests unchanged-DLL and changed-DLL upgrades while the app is
running, verifies registration of the new filename, and uninstalls with both DLLs
loaded. The changed DLL fixture uses a harmless PE overlay and retains the same
COM interface. All installer exits must be zero, with no restart requested.

CI runs this test before uploading release assets. Logs live under
`artifacts/installer-smoke/<id>` and are uploaded on failure. This simulates the
Windows image lock; it does not exercise Explorer's menus or COM cache.

## Interactive installer and Explorer checks

- Run the unsigned Setup asset as a normal user. Confirm no elevation is needed,
  .NET is bundled, a Start menu shortcut appears, and relaunch is offered.
  SmartScreen may warn; that is expected for unsigned downloads.
- On first normal launch, choose **Not now** for update checks. Use a network
  inspector to verify no update request, quit and relaunch, and verify no prompt
  or automatic request. The start-at-login prompt is a separate preference.
- Compare two selected text files through Explorer, including **Show more
  options** on Windows 11, to make Explorer load the native extension.
- Keep ClipDiff running and install the same build again. Confirm it closes
  cleanly, updates without a file-in-use prompt, and offers to relaunch. Existing
  settings and login opt-in must survive. Captured text is discarded on shutdown.
- Upgrade to a build whose extension hash differs. Keep Explorer running. Confirm
  the old loaded DLL remains, the new DLL has a distinct filename, and
  `HKCU\Software\Classes\CLSID\{6B46A974-40E2-4AD4-9F68-E534202B11E8}\InprocServer32`
  points at the new file after relaunch. Exercise both comparison menu commands
  while Explorer can still use its old handler. No Explorer restart is required.
- Relaunch while the old DLL is locked; startup and comparisons must still work.
  After Explorer naturally releases it (for example, after signing out/in), launch
  again and confirm obsolete DLL cleanup. Do not restart Explorer as an update step.
- Quit ClipDiff and deny deletion of an obsolete DLL using a file ACL, then
  relaunch. Cleanup failure must not affect startup, comparisons or the next
  installer update. Restore the ACL afterwards.
- Uninstall with ClipDiff running and after opening the Explorer menu. Confirm
  the tray icon disappears, installed EXE/shortcut/Run entry are removed, and no
  reboot or Explorer restart is requested. A locked DLL/folder may remain; user
  preferences remain. Menu invocations must not launch a removed executable.
- For the one-time migration from a pre-installer portable copy: quit it, install,
  and confirm the saved viewer/shortcut preferences remain. An existing built-in
  startup registration follows the installed EXE. Remove any manually created
  Startup-folder shortcut to the portable copy. Its folder is left untouched.

## Update notification checks

- With automatic checks off, click **Check for updates...**. Confirm a request
  occurs only when clicked and the result is clearly up-to-date/newer/failed.
- Enable **Automatically check for updates**. The consent prompt must identify
  `api.github.com`, describe the purpose, and explain no content is sent. Confirm
  one background request and that clipboard work continues during it.
- Restart repeatedly within 24 hours, including after an offline/failed attempt.
  Confirm no repeat automatic requests. Leave ClipDiff running past the next daily
  deadline to verify periodic checks without restarting.
- Use a build with an installed version below the latest published stable release
  (build locally with `-Version` before packaging) to verify the notification.
  Click it: **Download** opens that release's GitHub page; **Dismiss** closes the
  update window. No file is downloaded by ClipDiff itself. Recheck on the next
  day/restart and confirm the same version does not notify again automatically.
- Disable automatic checks while a slow request is in progress. Confirm it is
  cancelled and produces no later notification. Restart and verify it stays off.
- Test offline and GitHub failure/rate-limit responses. Manual checks explain the
  failure; automatic failures stay quiet. Clipboard/hotkey/Explorer work continues.
- Inspect `%LOCALAPPDATA%\ClipDiff\settings.json`: update data must contain only
  consent, last attempt time, and last notified version. Inspect request metadata:
  no clipboard text, document data, file paths or settings may be sent.

The portable ZIPs should also launch with exactly their matching EXE and hashed
DLL. The `net10` variant additionally needs the x64 .NET 10 Desktop Runtime.
