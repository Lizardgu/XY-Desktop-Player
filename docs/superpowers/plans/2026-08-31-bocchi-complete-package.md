# Bocchi Complete Package Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a clean, immediately runnable Windows package named “孤独摇滚壁纸移植-完整包” with the complete reference player content and reliable launchers.

**Architecture:** `tools/Publish.ps1` publishes the self-contained host beneath `app`, copies one validated content root beneath `content/reference-player`, and writes a five-entry user-facing root. Product strings and assembly output names change without renaming internal namespaces. `tests/Publish.Tests.ps1` exercises the real publish script and real `cmd.exe` parser against a small content fixture.

**Tech Stack:** PowerShell 7, .NET 8 WinForms, Windows batch files, Git.

**Spec:** `docs/plans/2026-08-31-bocchi-complete-package-design.md`

## Global Constraints

- Build only the complete package in this iteration; do not build the later reusable shell package.
- The package root is named `孤独摇滚壁纸移植-完整包`.
- The root contains only two launchers, one instruction file, `app`, and `content`.
- Batch bodies use only ASCII text and paths and are emitted with CRLF endings.
- The complete package includes the current `content/reference-player` tree unchanged.
- Published file names and user-visible application strings contain no `Nikki` token.
- Do not rename source namespaces or source project directories in this iteration.

---

### Task 1: Specify the clean package contract

**Files:**
- Modify: `tests/Publish.Tests.ps1`
- Modify: `packaging/启动-A-普通窗口.cmd`
- Modify: `packaging/启动-B-桌面模式.cmd`
- Modify: `packaging/运行说明.txt`

**Interfaces:**
- Consumes: `tools/Publish.ps1 -Output <path> -Content <path>`.
- Produces: executable assertions for the five-entry root, content copy, CRLF launchers, and `cmd.exe` parsing.

- [ ] **Step 1: Write the failing publish test**

Create a minimal real content fixture with `index.html`, `static`, `assets/covers`, `assets/audios`, and `assets/lyrics`. Invoke `Publish.ps1` with `-Content`. Assert these literal root entries:

```powershell
$expectedRootNames = @(
    'app',
    'content',
    '使用说明.txt',
    '双击这里-启动桌面壁纸.cmd',
    '普通窗口（备用）.cmd'
)
```

Assert that every byte sequence separating launcher lines is `0D 0A`, that `content/reference-player/index.html` exists, that no root DLL exists, and that no published path contains `Nikki`.

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
& '.\tests\Publish.Tests.ps1'
```

Expected: FAIL because `Publish.ps1` has no `-Content` parameter and still creates a flat `NikkiDesktop-win-x64` layout.

- [ ] **Step 3: Add the real batch-parser regression**

In the disposable test package, rename `app` to `app-held`, execute the primary launcher through `cmd.exe /d /c`, restore the directory in `finally`, and assert exit code `2` plus the literal message `Application files are missing.`. This production change would fail if LF-only output again strips command initials.

- [ ] **Step 4: Run the test and confirm the intended failure remains**

Run `& '.\tests\Publish.Tests.ps1'` and confirm the failure is still caused by the missing clean-package behavior, not by test syntax.

- [ ] **Step 5: Commit the RED contract**

```powershell
git add tests/Publish.Tests.ps1
git commit -m "test: define clean complete package contract"
```

### Task 2: Implement packaging and product naming

**Files:**
- Modify: `tools/Publish.ps1`
- Modify: `packaging/启动-A-普通窗口.cmd`
- Modify: `packaging/启动-B-桌面模式.cmd`
- Modify: `packaging/运行说明.txt`
- Modify: `src/NikkiDesktop.App/NikkiDesktop.App.csproj`
- Modify: `src/NikkiDesktop.Core/NikkiDesktop.Core.csproj`
- Modify: `src/NikkiDesktop.App/PlayerForm.cs`
- Modify: `src/NikkiDesktop.App/Program.cs`

**Interfaces:**
- Consumes: existing `--mode`, `--content`, and `--self-test` command-line options.
- Produces: `BocchiWallpaperPort.exe`, `BocchiWallpaperPort.Core.dll`, clean product strings, and the complete package tree.

- [ ] **Step 1: Add clean output and validated content input**

Add a `-Content` parameter defaulting to `content/reference-player`. Resolve it, require `index.html`, and publish the app into `$stagingRoot\app`. Copy the content tree to `$stagingRoot\content\reference-player` without changing source files.

- [ ] **Step 2: Emit portable CRLF launchers**

Use the two packaging templates with only ASCII body text and relative `app\BocchiWallpaperPort.exe` and `content\reference-player` paths. Normalize launcher bytes to CRLF while staging so `cmd.exe` behavior does not depend on checkout line endings.

- [ ] **Step 3: Apply the approved product name**

Set app assembly name to `BocchiWallpaperPort`, core assembly name to `BocchiWallpaperPort.Core`, and replace user-visible `Nikki Desktop` strings in `PlayerForm.cs`, `Program.cs`, and `运行说明.txt` with `孤独摇滚壁纸移植`.

- [ ] **Step 4: Run publish tests and verify GREEN**

Run `& '.\tests\Publish.Tests.ps1'`.

Expected: PASS for root structure, content copy, name hygiene, CRLF bytes, and real `cmd.exe` parsing.

- [ ] **Step 5: Run the existing regression suite**

```powershell
& '.\tools\Invoke-DotNet.ps1' run --project '.\tests\NikkiDesktop.Core.Tests\NikkiDesktop.Core.Tests.csproj' --no-restore
& '.\tests\ImportReferencePlayer.Tests.ps1'
& '.\tools\Invoke-DotNet.ps1' build '.\NikkiDesktop.sln' --configuration Release --no-restore
```

- [ ] **Step 6: Commit implementation**

```powershell
git add tools/Publish.ps1 packaging src/NikkiDesktop.App src/NikkiDesktop.Core
git commit -m "feat: publish complete Bocchi wallpaper package"
```

### Task 3: Build and audit the full deliverable

**Files:**
- Modify: `README.md`
- Modify: `docs/GITHUB-SAVE.md`
- Generate (ignored): `artifacts/publish/孤独摇滚壁纸移植-完整包`
- Generate (ignored): `artifacts/release/孤独摇滚壁纸移植-完整包.zip`

**Interfaces:**
- Consumes: the current 144-file `content/reference-player` source and its SHA-256 manifest.
- Produces: one local full package directory and one verified ZIP containing the same files.

- [ ] **Step 1: Update operator documentation**

Document that users double-click `双击这里-启动桌面壁纸.cmd`, that `普通窗口（备用）.cmd` is diagnostic, and that `app` is not a user entry point. Note that redistribution of bundled media requires appropriate permission.

- [ ] **Step 2: Publish the full package**

```powershell
& '.\tools\Publish.ps1' -Output '.\artifacts\publish\孤独摇滚壁纸移植-完整包' -Content '.\content\reference-player'
```

- [ ] **Step 3: Verify application and content**

Run the packaged app with `--self-test`, verify the existing SHA-256 content manifest, count all root entries, and confirm zero published paths contain `Nikki`.

- [ ] **Step 4: Run a real desktop launch**

Execute `双击这里-启动桌面壁纸.cmd` in the actual Windows desktop session, confirm the process launches from `app\BocchiWallpaperPort.exe`, and read back the process path and product title.

- [ ] **Step 5: Create and extract-check the ZIP**

Create `artifacts/release/孤独摇滚壁纸移植-完整包.zip`, extract it into a new temporary directory under `artifacts`, compare file counts, relative paths, sizes, and SHA-256 values, then retain both the package and ZIP.

- [ ] **Step 6: Run final verification and commit docs**

Run the entire test suite, `git diff --check`, and `git status --short`, then commit documentation with:

```powershell
git add README.md docs/GITHUB-SAVE.md docs/plans/2026-08-31-bocchi-complete-package-design.md docs/superpowers/plans/2026-08-31-bocchi-complete-package.md
git commit -m "docs: explain complete Bocchi wallpaper package"
```

