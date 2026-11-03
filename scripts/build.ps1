[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$NXInstallDir,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('v4.8', 'v4.7.2')][string]$TargetFrameworkVersion = 'v4.8'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$nxManaged = Join-Path $NXInstallDir 'NXBIN\managed'
Write-Host "编译目标: .NET Framework $TargetFrameworkVersion / x64"
if ($TargetFrameworkVersion -eq 'v4.7.2') {
    Write-Host '正在尝试旧工程的 4.7.2 编译目标；仍使用 NX 2512 引用，并保留框架兼容性检查。'
}
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
    if (-not (Test-Path -LiteralPath $vswhere)) { throw "未找到 MSBuild；需要 VS / Build Tools（支持 C# 7.3）及 $TargetFrameworkVersion 的 Developer/Targeting Pack。" }
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
    "/p:TargetFrameworkVersion=$TargetFrameworkVersion" '/warnaserror:MSB3274,MSB3275' `
    /verbosity:minimal "/bl:$logDirectory\build-$Configuration-$TargetFrameworkVersion.binlog"
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码 $LASTEXITCODE。MSB3644 表示缺少目标框架开发包；MSB3274/MSB3275 表示引用要求更高框架。请按 LOCAL_AGENT_PROMPT.md 处理。"
}
$output = Join-Path $repoRoot "bin\$Configuration\$TargetFrameworkVersion"
$nxCopies = Get-ChildItem -LiteralPath $output -Filter 'NXOpen*.dll'
if ($nxCopies) { throw '输出目录存在 NX SDK DLL，请清理旧输出；不得随插件部署 NX SDK DLL。' }
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $output 'NX_Traverser.dll'), (Join-Path $output 'ClassLibrary1.dlx')
Write-Host "构建完成: $output。按 LOCAL_AGENT_PROMPT.md 在本地签名、验收后部署。"
