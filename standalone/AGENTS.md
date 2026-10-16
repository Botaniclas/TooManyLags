# Output layout

- Keep current release files directly in `outputs`; archive older versions in `outputs/Versions/<Mod>/<Version>/` and other files in `outputs/Others/`.
- `package.ps1` runs `organize-outputs.ps1` automatically. Preserve archived contents and never overwrite collisions.

# Local Git workflow

- Use Git locally by default. Commit local work as appropriate.
- Authorized Thunderstore releases can use the encrypted local credential helper described in `../tools/thunderstore-cli/RELEASE-WORKERS.md`. Never print or copy tokens into chat, Git, packages or CLI arguments. A stored token does not authorize publication.
- Publishing a version also authorizes pushing its source and finalized changelog to GitHub. Push as part of publication; keep ordinary development local unless the user requests a push. Do not create remote branches or pull requests without an explicit request.

# Changelogs for BotanicPupPatch and TooManyLags

These rules apply only to BotanicPupPatch and TooManyLags projects, including versioned folders. Keep them when preparing a new version. Other projects retain their own rules.

- Maintain a project-root `CHANGELOG.md` during work and before every release. Include it in the install and source ZIPs; include these instructions in the source ZIP.
- Use English, matching the current READMEs. Format like ValheimCommunityPatch: `# Changelog`, newest version first, a bold version number, then a plain fenced block containing `-` bullets.
- Keep entries short: usually 1–3 bullets, one or two sentences each. Avoid long nested lists and repeating the feature list from the README.
- Write normal changelogs: new features, bug fixes and meaningful changes to gameplay or how the mod works, including performance, config, compatibility, dependencies and migrations. Include implementation details when they help explain a fix or integration change. Omit README/wording edits, guides, media, icons, validation reports, release chores and personal folder/build organization. Omit versions with no relevant changes rather than inventing an entry.
- Lead with the observable change: the situation affected and what now happens. Use concrete verbs such as "Fixes", "Adds", "Reduces", "Removes" or "Restores". Avoid vague "improvements" and "optimizations".
- Include concise technical detail when another modder needs it: affected mod/API, relevant dependency version, config key/default, ownership/readiness rule, compatibility fallback or disabled overlap. Mark client/server scope when it matters.
- Explicitly record removed behavior, changed defaults, dependencies and required migration actions. Do not hide these under "cleanup" or "performance".
- Describe only changes verified from the source and relevant checks. Do not invent history, dates, performance measurements, bug guarantees or completed live multiplayer testing. Put detailed test results and implementation analysis in `VALIDATION.md`.
- Keep drafts under **Unreleased**. Before publishing, move completed notes to the actual manifest version; remove empty draft sections from the published copy. Preserve earlier released entries and update notes relative to the previous version.
- Changelog-only work does not require a plugin version bump, a DLL rebuild or modification of installed profiles. Do not rewrite old release archives merely to introduce the new format.

Format:

````markdown
# Changelog

**Unreleased**
```
- Confirmed change, stating the affected situation and resulting behavior.
```

**X.Y.Z**
```
- User-visible fix or feature; add a short compatibility/config detail when relevant.
```
````
