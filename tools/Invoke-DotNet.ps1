[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $DotNetArgs
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$workspaceSdk = Join-Path (Split-Path -Parent (Split-Path -Parent $repoRoot)) 'work\tooling\dotnet8\dotnet.exe'
$candidates = @(
    $env:NIKKI_DOTNET,
    $workspaceSdk,
    (Join-Path $repoRoot '.dotnet\dotnet.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

$dotnet = $candidates | Select-Object -First 1
if (-not $dotnet) {
    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command) {
        $sdkList = & $command.Source --list-sdks
        if ($LASTEXITCODE -eq 0 -and $sdkList) {
            $dotnet = $command.Source
        }
    }
}

if (-not $dotnet) {
    throw '未找到 .NET 8 SDK。请安装 SDK，或把 dotnet.exe 路径放到 NIKKI_DOTNET 环境变量。'
}

& $dotnet @DotNetArgs
exit $LASTEXITCODE

