# Released TooManyLags 1.0.47 with FiresGhettoNetworking 1.5.65

Read-only compatibility review, 2026-10-18. The original DLL was extracted from the prepared/published 1.0.47 package and matched to the release verification SHA256 `CAD677CD4C1289FB518455288ACBBE7D62396457BC05F9429A98AD734EFDC79C`. Unreleased logging changes were not used. Runtime source, installed profiles and release packages were not modified by this review.

## Confirmed setting-dependent startup failure

Dedicated `NoServerSideSimulation=true` calls FgnSimulationStartup.Validate before removing simulation patches. The released validator accepts exactly its seven known simulation patch groups. Actual 1.5.65 ApplyServerSideSimulationPatches also installs SssQuietPass. Invoking the released validator against the actual 1.5.65 installer reproduces `NotSupportedException: FiresGhettoNetworking's simulation patch groups changed; no hooks were removed.` This disables TooManyLags' requested mode safely; changing the manifest dependency number would not fix it.

The standard mode (`NoServerSideSimulation=false`) does not use that removal validator. Therefore a server working in normal simulation mode is consistent with this failure in the other mode; it does not certify every configuration.

## New FGN warning is separate

Actual 1.5.65 contains TmlGuard. After two frames, if `botanic.toomanylags.fgnaddon` is present, it warns that the FGN team has not endorsed/tested TooManyLags and lists replaced config references. The inspected class performs no unpatch, config rollback, owner assignment or addon disable. Its warning alone is not an execution block or proof of a functional incompatibility.

## Actual binary checks and limits

- The Pickable rewrite/provider check passes 865 assertions with FGN 1.5.65 and the original 1.0.47 addon. This is native IL/metadata and modeled composition, not live harvesting.
- The combat IL audit passes 170 assertions for the actual binaries, including timing/budget contracts, handoff/veto order and the existing native integration shapes. Only exact input provenance was extended in an isolated audit copy; production code and checks were not removed.
- The general native audit first needed to follow FGN's relocated AwakeCore startup body rather than assuming Awake directly starts simulation. That is a test-host assumption, not itself an addon incompatibility. After this isolated adjustment, the audit reaches and reproduces the real simulation-group rejection above.
- Running the legacy Harmony audit in a modern dotnet host also raised an AccessTools initializer error. Repeating it in its original .NET Framework host reached the concrete contract failure. That host error is not evidence of an in-game incompatibility.

The existing minimum dependency declaration is not an exact 1.5.36 runtime lock. No hard addon-side 1.5.65 rejection was found. No full live multiplayer certification is claimed. The user's own standard-mode server working and another user's unspecified incompatibility report can both be true. A concrete log/symptom is still needed to assign that particular report to the reproduced mode failure, the new warning or a different interaction.

Reproduction inputs/results are retained locally under `bin/compatibility-fgn-1.5.65/`, including `final-verification.json`. These records describe the original 1.0.47 release, which remains affected.

## Startup fix prepared for 1.0.48

The validator and patch-removal filter now recognize the exact optional `FiresGhettoNetworkMod.SssQuietPass` group from the backend assembly. Its two sector-change postfixes only update simulation bookkeeping and are installed by the same simulation initializer, so they are removed with the existing simulation groups when `NoServerSideSimulation=true`. The seven original groups are still mandatory. Missing groups, unknown additions, foreign namespaces and switching after world creation remain rejected. Normal simulation mode retains the quiet-pass hooks. No ongoing check, timer, ownership rule or config default is added or changed.

The eight-group installer fixture reproduced the reported exception before this change. After the fix, both the original seven-group and new eight-group fixture pass 85 assertions each, including real Harmony removal, preservation of networking/other-mod hooks, unchanged saved configuration, normal simulation mode and rejected unsupported installers. The actual compiled DLL passes the native audits against FGN 1.5.36/1.5.41 (27,986 assertions each) and 1.5.65 (27,991 assertions), including invocation of the production validator against each actual installer. The audit follows `AwakeCore` when present and otherwise retains the original `Awake` check; production startup inspection was not changed.

The actual 1.5.65 Pickable/provider check passes 865 assertions and the combat metadata check passes 133 contracts without optional PUP input. The compiled release diagnostic-counter/allocation check also passes. These are fixture/IL/metadata checks, not a live multiplayer run. These preliminary checks preceded the version bump; the complete 1.0.48 release results are recorded separately in verification.json. Current native results are in `bin/native-audit/verification.json`.

Pre-release implementation-check DLL SHA256 (before version bump): `7BF6451741E67A556942271B52212A27BF9D0A6408D41F31B80D5100FD148D39`.
