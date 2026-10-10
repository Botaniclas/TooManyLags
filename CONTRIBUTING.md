# Development

Build on Windows with .NET Framework's C# compiler, an installed Valheim client and BepInEx. Game and dependency DLLs are read from your installation and are not included in this repository.

Run the full build and verification:

```powershell
.\verify.ps1 -ValheimDirectory 'D:\Games\Valheim' -BepInExDirectory 'D:\Games\Valheim\BepInEx'
```

The verification scripts also need the locally cached FiresGhettoNetworking/PUP FPS builds identified in those scripts. See `VALIDATION.md` for the audited versions and test limits.

After verification, create the install, source and config ZIPs:

```powershell
.\package.ps1
```

Build outputs and generated verification reports are ignored by Git. Preserve the existing C# line endings: the imported-source review checks exact file hashes. Follow `AGENTS.md` when recording release changes.
