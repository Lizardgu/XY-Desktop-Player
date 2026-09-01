[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $DotNetArgs
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet-home'
$env:APPDATA = Join-Path $repoRoot '.dotnet-home\AppData\Roaming'
$env:LOCALAPPDATA = Join-Path $repoRoot '.dotnet-home\AppData\Local'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$workspaceSdk = Join-Path (Split-Path -Parent (Split-Path -Parent $repoRoot)) 'work\tooling\dotnet8\dotnet.exe'
$candidates = @(
    $env:XY_DOTNET,
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
    throw '未找到 .NET 8 SDK。请安装 SDK，或把 dotnet.exe 路径放到 XY_DOTNET 环境变量。'
}

& $dotnet @DotNetArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet 命令失败，退出代码：$LASTEXITCODE"
}
