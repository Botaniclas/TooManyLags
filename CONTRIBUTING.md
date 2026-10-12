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

To publish the verified ZIP with Thunderstore CLI, run `./publish.ps1 -CliDll 'path/to/tcli.dll'`. The default path uses the workspace's local CLI and encrypted shared credential store when available; see `../tools/thunderstore-cli/RELEASE-WORKERS.md`. Otherwise enter a Botaniclas service-account token at the hidden prompt or supply `TCLI_AUTH_TOKEN` in the process environment. The publish script does not save tokens or push to GitHub. Publishing an already uploaded version is refused.
