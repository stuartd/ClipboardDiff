The compilation succeeded; the failure is in the Windows menu integration test.
I’ve added diagnostics showing:
- The failing case and expected versus actual visibility.
- Direct-handler versus Windows-menu results.
- Decoded Windows errors, menu IDs, and selection attributes.
- Effective registration and read-only Shell restriction checks.
With the updated files at work, close ClipDiff and run:
./scripts/build-shell-extension.ps1 -Test
Send the full output starting at Native build:.

