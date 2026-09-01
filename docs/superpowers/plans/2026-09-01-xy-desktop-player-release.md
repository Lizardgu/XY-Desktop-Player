# XY Desktop Player v1.0.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename and slim the completed desktop player, then produce verified GitHub source and Windows Release archives tagged `v1.0.0`.

**Architecture:** Keep the proven WinForms/WebView2/WorkerW behavior unchanged while mechanically renaming the solution, projects, namespaces, product metadata, launchers and release paths. Treat imported media as ignored content, generate source and binary archives through separate paths, and delete only reproducible caches and superseded artifacts after verification.

**Tech Stack:** .NET 8, WinForms, WebView2, PowerShell, Git, Windows Explorer WorkerW APIs.

**Spec:** `docs/plans/2026-09-01-xy-desktop-player-release-design.md`

## Global Constraints

- Preserve current playback and desktop behavior.
- Do not upload, configure a Git remote, or publish copyrighted media.
- Preserve `content/reference-player` and `manifests/reference-player.manifest.json`.
- Use `v1.0.0` only after final verification.
- Delete only paths listed in the recorded cleanup manifest.

---

### Task 1: Commit the design and establish the red naming test

**Files:**
- Create: `docs/plans/2026-09-01-xy-desktop-player-release-design.md`
- Create: `docs/superpowers/plans/2026-09-01-xy-desktop-player-release.md`
- Create: `tests/Branding.Tests.ps1`

**Interfaces:**
- Consumes: current clean commit `73a0932`.
- Produces: an executable branding contract for all later rename steps.

- [ ] **Step 1:** Commit the design documents as a local milestone.
- [ ] **Step 2:** Add `Branding.Tests.ps1` assertions for solution/project names, EXE/product name, virtual host, package default and launcher target.
- [ ] **Step 3:** Run the test and confirm it fails on the old Nikki/Bocchi/孤独摇滚 names.

### Task 2: Rename tracked project structure and branding

**Files:**
- Rename: `NikkiDesktop.sln` to `XYDesktopPlayer.sln`
- Rename: `src/NikkiDesktop.App` to `src/XYDesktopPlayer.App`
- Rename: `src/NikkiDesktop.Core` to `src/XYDesktopPlayer.Core`
- Rename: `tests/NikkiDesktop.Core.Tests` to `tests/XYDesktopPlayer.Core.Tests`
- Modify: all tracked source, scripts, tests, README, license and notices that reference prior host names

**Interfaces:**
- Consumes: relative project references and packaging contract.
- Produces: `XYDesktopPlayer.exe`, `XYDesktopPlayer.Core.dll`, `xydesktop.local` and XY product metadata.

- [ ] **Step 1:** Rename directories and project files with Git-aware moves.
- [ ] **Step 2:** Mechanically replace namespaces, project references, executable names and user-visible product strings.
- [ ] **Step 3:** Update tests and scripts to the new paths and names.
- [ ] **Step 4:** Run `Branding.Tests.ps1` and the core test executable until green.
- [ ] **Step 5:** Commit the rename separately.

### Task 3: Consolidate current context and release documentation

**Files:**
- Create: `docs/PROJECT-STATUS.md`
- Modify: `README.md`
- Modify: `docs/GITHUB-SAVE.md`
- Remove: superseded design/plan/spec files after their still-relevant facts are included in `PROJECT-STATUS.md`

**Interfaces:**
- Consumes: verified feature history and known failed approaches.
- Produces: one canonical handoff summary for later themed forks.

- [ ] **Step 1:** Record current architecture, successful behavior, failed approaches, verification commands, cleanup policy and next-project procedure.
- [ ] **Step 2:** Rewrite GitHub and Release instructions for source/media separation and copyright boundaries.
- [ ] **Step 3:** Remove superseded planning documents and confirm no required operational instruction was lost.
- [ ] **Step 4:** Commit the documentation consolidation.

### Task 4: Build and verify final packages

**Files:**
- Generate ignored: `artifacts/publish/XY桌面播放器-v1.0.0-win-x64`
- Generate ignored: `artifacts/release/XY桌面播放器-v1.0.0-win-x64.zip`
- Generate ignored: `artifacts/release/XY桌面播放器-GitHub源码-v1.0.0.zip`

**Interfaces:**
- Consumes: complete imported media plus renamed source tree.
- Produces: one source archive and one runnable Windows archive.

- [ ] **Step 1:** Run Release build, core tests, source tests, import test, publish test, self-test and content-manifest verification.
- [ ] **Step 2:** Run real startup, fade/resume and WorkerW restoration acceptance tests.
- [ ] **Step 3:** Publish the final complete package and run its own self-test outside the package directory.
- [ ] **Step 4:** Create and extract the Release ZIP; compare every relative path, size and SHA-256.
- [ ] **Step 5:** Commit final release metadata and create annotated local tag `v1.0.0`.
- [ ] **Step 6:** Create the GitHub source ZIP from the tag and verify it contains tracked source only.

### Task 5: Remove reproducible and superseded data

**Files:**
- Create: `CLEANUP-REPORT-2026-09-01.md`
- Delete ignored: old `artifacts/archive`, old publish/release outputs, smoke/verification data, WebView2 data, logs, bin/obj and project-local dependency caches

**Interfaces:**
- Consumes: verified final archives and Git tag.
- Produces: a substantially smaller working directory with an explicit deletion audit.

- [ ] **Step 1:** Record exact deletion targets, file counts, sizes and reasons before deletion.
- [ ] **Step 2:** Verify every resolved target stays under the XY project directory and is not the Git root, `.git`, `content/reference-player`, `manifests`, final publish directory or final release archive.
- [ ] **Step 3:** Delete targets in one native PowerShell workflow.
- [ ] **Step 4:** Recalculate directory sizes and verify final archives, Git status and tag after cleanup.
- [ ] **Step 5:** Commit the cleanup report without modifying the tagged release commit.
