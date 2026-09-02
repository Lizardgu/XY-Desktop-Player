# Theme Skeleton Recovery Implementation Plan

> **For agentic workers:** Execute this recovery inline and verify each checkpoint before deleting the damaged worktree. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recover the reusable theme-pack implementation into the clean GitHub clone while discarding generated caches, duplicate releases, and broken Git metadata.

**Architecture:** Keep `v1.0.0` and `main` untouched. Reconstruct the recovered feature branch from the maintained source, tests, Web player, theme definitions, packaging, and user documentation in the damaged worktree; do not migrate local SDKs, dependency caches, build output, capture data, or media copies that can be regenerated from the original Wallpaper Engine project on `E:`.

**Tech Stack:** .NET 8, Windows Forms, WebView2, React 18, PowerShell, Git.

**Spec:** `docs/plans/2026-09-01-theme-pack-skeleton-design.md`

## Global Constraints

- Work only on `feature/theme-pack-skeleton-recovery`; do not rewrite `main` or `v1.0.0`.
- Do not push or create a release before user acceptance.
- Treat automatic window pause behavior as unverified until the user tests the real desktop build.
- Preserve no generated dependency cache or duplicate publication solely for convenience.
- Use `E:\SteamLibrary\steamapps\workshop\content\431960\2905017768` as the regenerable source for the built-in Bocchi media.

---

### Task 1: Recover maintained project files

**Files:**

- Migrate: `src/**`, `tests/**`, `tools/**`, `packaging/**`, `themes/**`, `web/**`
- Migrate: `README.md`, `THIRD_PARTY_NOTICES.md`, `.gitignore`
- Migrate: theme design, theme guide, verification evidence, and automatic-pause handoff under `docs/**`
- Exclude: `artifacts/**`, `.dotnet-home/**`, `.packages/**`, `.pnpm-store/**`, `data/**`, `build-check.log`, `dotnet-install.ps1`, and the broken `.git` file

- [x] Copy the maintained files from the damaged worktree into the recovery branch.
- [x] Remove the three superseded v1 foreground/fullscreen implementation files that are absent from the recovered architecture.
- [x] Confirm the Git diff contains no binaries, caches, generated release folders, or user media.

### Task 2: Verify recovered source

**Files:**

- Test: `tests/XYDesktopPlayer.Core.Tests/XYDesktopPlayer.Core.Tests.csproj`
- Test: `web/player-src/src/theme-model.test.mjs`
- Test: all non-GUI PowerShell tests in `tests/`

- [x] Build the complete solution in Release mode using the already available SDK and NuGet cache.
- [x] Run the core test executable and record the pass/fail result.
- [x] Run the Web theme-model tests and static PowerShell tests.
- [x] Run `git diff --check` and inspect `git status`.

### Task 3: Record an honest recovered state

**Files:**

- Modify: `docs/PROJECT-STATUS.md`
- Modify: `docs/THEME-SKELETON-VERIFICATION.md`
- Create: `docs/RECOVERY-2026-09-02.md`

- [x] Replace stale claims of user-verified automatic pause with the current known limitation.
- [x] Record the exact old directories excluded and the approximate space they occupied.
- [ ] Commit the recovered source to the local recovery branch without pushing.

### Task 4: Reclaim disk space

**Files:**

- Delete after verification: `C:\Users\Administrator\Documents\Codex\worktrees\XY桌面播放器-theme-skeleton`
- Delete after verification: `C:\Users\Administrator\Documents\Codex\2026-08-30\d-yt-dlp-downloads-bv1yc1gbaekf-mkv\outputs\XY桌面播放器`

- [ ] Resolve both deletion targets to exact absolute paths and re-measure them.
- [ ] Verify the recovery branch commit exists and the working tree is clean.
- [ ] Permanently remove only the two obsolete directories.
- [ ] Verify both old paths are gone and report the recovered disk space.
