# Output layout

- Keep current release files directly in `outputs`; archive older versions in `outputs/Versions/<Mod>/<Version>/` and other files in `outputs/Others/`.
- `package.ps1` runs `organize-outputs.ps1` automatically. Preserve archived contents and never overwrite collisions.
- Keep Object Categories last in generated configs; explain shared category scope, toggles and safeguards once.

# Local Git workflow

- Use Git locally by default. Commit local work as appropriate.
- Authorized Thunderstore releases can use the encrypted local credential helper described in `../tools/thunderstore-cli/RELEASE-WORKERS.md`. Never print or copy tokens into chat, Git, packages or CLI arguments. A stored token does not authorize publication.
- Publishing a version also authorizes pushing its source and finalized changelog to GitHub. Push as part of publication; keep ordinary development local unless the user requests a push. Do not create remote branches or pull requests without an explicit request.

# Optional debug system

- Local builds, verification and test packages default to debug. `build.ps1` defines `TML_DEBUG_SYSTEM` and writes `bin/debug-system/TooManyLags.dll`; `-DebugSystem` remains a compatible explicit spelling.
- Debug builds also define `TML_BRIDGE_DIAGNOSTICS`: AI-test messages, bridge summaries, client diagnostic relay and scoped bridge measurements are enabled independently of EnableLogs. Release builds omit these calls, argument formatting and instrumentation; errors/warnings and explicit console responses remain.
- Only `build.ps1 -Release` compiles without the test system, into `bin/TooManyLags.dll`. Keep test-only code behind its compile symbol. Native simulation bridge uses TML_MOB_BRIDGE in both variants; normal safeguards/visual smoothing/EnableLogs remain independent.
- `verify.ps1` checks the debug development build. `verify.ps1 -Release` prepares and checks both modes, including normal-build omission, before publication. `package.ps1` defaults to debug; `package.ps1 -Release` requires a verified release receipt. The publish helper rejects debug packages/receipts.
- Install one variant per endpoint. Do not publish without explicit user authorization; normal release publication also includes its Git push.

# Changelogs for BotanicPupPatch and TooManyLags

- Maintain English CHANGELOG.md in project root and include it in install/source ZIPs.
- From 2.0 onward, use Markdown version headings, a short user-facing introduction
  when useful, and concise feature/fix bullets. No fenced changelog block for new entries.
- Focus on player-visible outcomes. Use config keys only when they help users find
  the relevant option. Keep implementation and validation details in VALIDATION.md.
- Future versions should be brief. 2.0.0 is an intentional longer overview of the
  rebuilt internals; a short nested list for the simulation bridge is appropriate.
- Include required client/server updates, changed defaults and removed behavior.
- Do not promise bug-free gameplay, zero teleports, exact prediction of arbitrary
  player inputs or live performance measurements without supporting evidence.
- Keep unreleased drafts under Unreleased; use the actual version when preparing
  its package. Preserve historical released entries and archived packages.
- Release preparation does not authorize publishing, installing or pushing.
