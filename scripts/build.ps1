[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$NXInstallDir,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$nxManaged = Join-Path $NXInstallDir 'NXBIN\managed'
foreach ($library in @('NXOpen.dll', 'NXOpenUI.dll', 'NXOpen.UF.dll')) {
    $path = Join-Path $nxManaged $library
    if (-not (Test-Path -LiteralPath $path)) { throw "缺少 NX 2512 引用: $path" }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    Write-Host "$library : $($info.FileVersion) [$path]"
}
$msbuildCommand = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
if ($msbuildCommand) {
    $msbuild = $msbuildCommand.Source
} else {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw '请安装 VS 2022 / Build Tools 和 .NET Framework 4.8 Developer Pack。' }
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw '未找到 MSBuild。' }
}
# Parse XML before compiling so a broken or mismatched asset cannot be deployed.
$document = New-Object System.Xml.XmlDocument
$document.Load((Join-Path $repoRoot 'ClassLibrary1.dlx'))
$logDirectory = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
& $msbuild (Join-Path $repoRoot 'NX_Traverser.csproj') /t:Rebuild /m `
    "/p:Configuration=$Configuration" /p:Platform=x64 "/p:NXInstallDir=$NXInstallDir" `
    /verbosity:minimal "/bl:$logDirectory\build.binlog"
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
$output = Join-Path $repoRoot "bin\$Configuration"
$nxCopies = Get-ChildItem -LiteralPath $output -Filter 'NXOpen*.dll'
if ($nxCopies) { throw '输出目录存在 NX SDK DLL，请清理旧输出；不得随插件部署 NX SDK DLL。' }
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $output 'NX_Traverser.dll'), (Join-Path $output 'ClassLibrary1.dlx')
Write-Host "构建完成: $output。按 LOCAL_AGENT_PROMPT.md 在本地签名、验收后部署。"
