[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testId = [Guid]::NewGuid().ToString('N')
$outputRoot = Join-Path $repoRoot "artifacts\test-publish-$testId"
$fixtureRoot = Join-Path $repoRoot "artifacts\test-content-$testId"

try {
    $requiredContentDirectories = @(
        'static',
        'assets\covers',
        'assets\audios',
        'assets\lyrics'
    )
    foreach ($relativePath in $requiredContentDirectories) {
        New-Item -ItemType Directory -Force -Path (Join-Path $fixtureRoot $relativePath) | Out-Null
    }
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'index.html') -Value '<!doctype html><title>fixture</title>' -NoNewline
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'static\fixture.txt') -Value 'complete-package-fixture' -NoNewline

    & (Join-Path $repoRoot 'tools\Publish.ps1') -Output $outputRoot -Content $fixtureRoot

    $expectedRootNames = @(
        'app',
        'content',
        '使用说明.txt',
        '双击这里-启动桌面壁纸.cmd',
        '普通窗口（备用）.cmd'
    ) | Sort-Object
    $actualRootNames = @(
        Get-ChildItem -LiteralPath $outputRoot -Force |
            Select-Object -ExpandProperty Name |
            Sort-Object
    )
    if (($actualRootNames -join "`n") -cne ($expectedRootNames -join "`n")) {
        throw "Published root entries differ.`nExpected:`n$($expectedRootNames -join "`n")`nActual:`n$($actualRootNames -join "`n")"
    }

    $requiredFiles = @(
        'app\XYDesktopPlayer.exe',
        'content\reference-player\index.html',
        'content\reference-player\static\fixture.txt',
        '双击这里-启动桌面壁纸.cmd',
        '普通窗口（备用）.cmd',
        '使用说明.txt'
    )
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputRoot $relativePath) -PathType Leaf)) {
            throw "Published package is missing $relativePath."
        }
    }

    $applicationVersion = (Get-Item -LiteralPath (Join-Path $outputRoot 'app\XYDesktopPlayer.exe')).VersionInfo
    if ($applicationVersion.ProductName -cne 'XY桌面播放器') {
        throw "Published product name is '$($applicationVersion.ProductName)' instead of XY桌面播放器."
    }
    if ($applicationVersion.FileDescription -cne 'XY桌面播放器') {
        throw "Published file description is '$($applicationVersion.FileDescription)' instead of XY桌面播放器."
    }

    $rootDlls = @(Get-ChildItem -LiteralPath $outputRoot -File -Filter '*.dll')
    if ($rootDlls.Count -ne 0) {
        throw "Published root contains $($rootDlls.Count) DLL files instead of keeping dependencies under app."
    }

    $publishedEntries = @(Get-ChildItem -LiteralPath $outputRoot -Force -Recurse)
    $legacyNames = @($publishedEntries | Where-Object Name -Match 'Nikki')
    if ($legacyNames.Count -ne 0) {
        throw "Published package still exposes $($legacyNames.Count) Nikki-named entries."
    }

    $launcherPaths = @(
        (Join-Path $outputRoot '双击这里-启动桌面壁纸.cmd'),
        (Join-Path $outputRoot '普通窗口（备用）.cmd')
    )
    foreach ($launcherPath in $launcherPaths) {
        $launcherBytes = [System.IO.File]::ReadAllBytes($launcherPath)
        $launcherText = [System.Text.Encoding]::ASCII.GetString($launcherBytes)
        if ($launcherText -notmatch "`r`n") {
            throw "Launcher has no CRLF line endings: $launcherPath"
        }
        if ([regex]::IsMatch($launcherText, "(?<!`r)`n")) {
            throw "Launcher contains bare LF line endings: $launcherPath"
        }
    }

    $appRoot = Join-Path $outputRoot 'app'
    $heldAppRoot = Join-Path $outputRoot 'app-held'
    Move-Item -LiteralPath $appRoot -Destination $heldAppRoot
    try {
        $primaryLauncher = Join-Path $outputRoot '双击这里-启动桌面壁纸.cmd'
        $launcherOutput = @('') | & $env:COMSPEC /d /c "`"$primaryLauncher`"" 2>&1
        $launcherExitCode = $LASTEXITCODE
    }
    finally {
        Move-Item -LiteralPath $heldAppRoot -Destination $appRoot
    }

    if ($launcherExitCode -ne 2) {
        throw "Launcher returned $launcherExitCode instead of 2 when app files were missing. Output: $launcherOutput"
    }
    if (($launcherOutput -join "`n") -notmatch 'Application files are missing\.') {
        throw "Launcher did not report the missing app directory through cmd.exe. Output: $launcherOutput"
    }

    Write-Host 'PASS complete publish is clean, portable, CRLF-safe, and content-complete'
}
finally {
    $artifactBoundary = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
    foreach ($candidate in @($outputRoot, $fixtureRoot)) {
        $resolvedCandidate = [System.IO.Path]::GetFullPath($candidate)
        if ($resolvedCandidate.StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolvedCandidate)) {
            Remove-Item -LiteralPath $resolvedCandidate -Recurse -Force
        }
    }
}
