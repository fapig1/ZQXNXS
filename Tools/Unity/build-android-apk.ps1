<#
.SYNOPSIS
  构建 Android 基础测试 APK，并绕开 sdkmanager 远程清单的无限等待。

.DESCRIPTION
  背景（2026-09-18 定位，日志见 D:\Cache_Temp\opencode\unity_build_opts.log）：

  Unity 的 Android 前置任务 CheckAndroidSDK -> SDKManager.UpdatePackagesList() 会对每个
  SDK 组件（cmdline-tools / platform-tools / platform / build-tools）各调用一次
  `sdkmanager --list`。本机到 dl.google.com 的 TCP 能连上、但没有响应，cmdline-tools 用
  URLConnection 且没有超时，于是永久停在
  "Still waiting for package manifests to be fetched remotely."，
  Unity 也就永远停在 "Detecting Android SDK"，不会自行推进。

  做法：给 sdkmanager 的 JVM 加连接/读取超时。远程清单在几秒内失败后，sdkmanager 会打印
  warning，仍然输出本地已安装包列表并以 0 退出；Unity 把这两条 warning 记为
  DetectErrorsAndWarnings 后继续构建。整个 SDK 检测变成约 8 次 x 约 38 秒，不再挂死。

  为什么 SDKMANAGER_OPTS 有效：Unity 是经 cmd 调用 sdkmanager.bat，bat 内部执行
  "%JAVA_EXE%" %DEFAULT_JVM_OPTS% %JAVA_OPTS% %SDKMANAGER_OPTS% -classpath ...，
  所以该变量会出现在 java 命令行上（2026-09-18 用 Win32_Process 实测确认）。

  实测无效、不要再试的做法：
    - http_proxy / https_proxy 环境变量；
    - -Dhttps.proxyHost / -Dhttps.proxyPort 系统属性；
    - 阿里云 Maven 镜像（只作用于构建后期的 Gradle 依赖，管不到 sdkmanager）。

  只有 SDKMGR 之外的东西都不要改：本方案不改 Unity 安装目录、不放 sdkmanager shim，
  因此 Unity 升级后依然有效。

.PARAMETER Unity
  Unity.exe 的完整路径。

.PARAMETER Project
  Unity 工程目录（含 Assets / ProjectSettings 的那一层）。

.PARAMETER OutputApk
  期望产出的 APK 路径，需与构建方法里的输出一致。

.PARAMETER Method
  构建入口的 executeMethod。

.PARAMETER LogFile
  构建日志落盘位置。

.PARAMETER TimeoutMinutes
  硬超时。超时后结束进程树并报错，避免整夜挂着。

.PARAMETER SkipPreflight
  跳过早检。早检会先单独跑一次 `sdkmanager --list`，确认超时参数真的生效。

.PARAMETER NoPersist
  不把 SDKMANAGER_OPTS 写进用户级环境变量。默认会写，这样从 Unity Hub 直接点的构建
  也不会卡死（注意：Hub 需要重启一次才会拿到新的环境变量）。

.EXAMPLE
  pwsh -File Tools/Unity/build-android-apk.ps1

.EXAMPLE
  pwsh -File Tools/Unity/build-android-apk.ps1 -SkipPreflight -TimeoutMinutes 60
#>
[CmdletBinding()]
param(
    [string]$Unity = 'D:\APP\Unity\6000.0.83f1\Editor\Unity.exe',
    [string]$Project = 'D:\ZengQiangXianShi\ARPet',
    [string]$OutputApk = 'D:\ZengQiangXianShi\Builds\Android\ARPet-phone-test.apk',
    [string]$Method = 'ZQXNXS.ARPet.EditorTools.ProjectSetup.BuildPhoneTestApk',
    [string]$LogFile = 'D:\Cache_Temp\opencode\arpet_android_build.log',
    [int]$TimeoutMinutes = 45,
    [string]$SdkManagerOpts = '-Dsun.net.client.defaultConnectTimeout=5000 -Dsun.net.client.defaultReadTimeout=5000',
    [int]$PreflightTimeoutSeconds = 150,
    [switch]$SkipPreflight,
    [switch]$NoPersist
)

$ErrorActionPreference = 'Stop'

function Write-Step([string]$Text) { Write-Host "== $Text" -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "   $Text" -ForegroundColor Green }
function Write-Warn2([string]$Text) { Write-Host "   $Text" -ForegroundColor Yellow }

function Invoke-Captured {
    param(
        [string]$FilePath,
        [string]$Arguments,
        [int]$TimeoutSec,
        [hashtable]$Environment = @{},
        [string]$WorkingDirectory
    )
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FilePath
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($WorkingDirectory) { $psi.WorkingDirectory = $WorkingDirectory }
    foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = $Environment[$k] }

    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEndAsync()
    $stderr = $proc.StandardError.ReadToEndAsync()
    $finished = $proc.WaitForExit($TimeoutSec * 1000)
    if (-not $finished) {
        try { $proc.Kill($true) } catch { }
        return [pscustomobject]@{ TimedOut = $true; ExitCode = $null; StdOut = $stdout.Result; StdErr = $stderr.Result }
    }
    return [pscustomobject]@{ TimedOut = $false; ExitCode = $proc.ExitCode; StdOut = $stdout.Result; StdErr = $stderr.Result }
}

# ---------------------------------------------------------------- 1. 定位工具
Write-Step '定位 sdkmanager 与 Unity'
if (-not (Test-Path $Unity)) { throw "找不到 Unity.exe：$Unity" }
if (-not (Test-Path (Join-Path $Project 'Assets'))) { throw "找不到 Unity 工程：$Project" }

$editorData = Join-Path (Split-Path $Unity -Parent) 'Data'
$sdkRoot = Join-Path $editorData 'PlaybackEngines\AndroidPlayer\SDK'
$cmdlineToolsRoot = Join-Path $sdkRoot 'cmdline-tools'
if (-not (Test-Path $cmdlineToolsRoot)) { throw "找不到 cmdline-tools：$cmdlineToolsRoot" }

$sdkmanagerBat = Get-ChildItem $cmdlineToolsRoot -Directory |
    Sort-Object { [version]($_.Name) } -Descending |
    ForEach-Object { Join-Path $_.FullName 'bin\sdkmanager.bat' } |
    Where-Object { Test-Path $_ } |
    Select-Object -First 1
if (-not $sdkmanagerBat) { throw "在 $cmdlineToolsRoot 下找不到 sdkmanager.bat" }

Write-Ok "Unity       : $Unity"
Write-Ok "工程        : $Project"
Write-Ok "sdkmanager : $sdkmanagerBat"

# 防御：历史上用过"临时 shim 替换 sdkmanager.bat"的老办法。若是 shim 还在，
# 本脚本的超时参数会被它吞掉，需要先人工还原。
$batText = Get-Content $sdkmanagerBat -Raw
if ($batText -notmatch 'com\.android\.sdklib\.tool\.sdkmanager\.SdkManagerCli') {
    throw "$sdkmanagerBat 看起来被替换过（不是官方脚本），请先还原原始的 sdkmanager.bat 再跑本脚本。"
}
if ($batText -notmatch 'SDKMANAGER_OPTS') {
    throw "$sdkmanagerBat 不接受 SDKMANAGER_OPTS，本方案不适用，请检查 cmdline-tools 版本。"
}

# ---------------------------------------------------------------- 2. 早检
if (-not $SkipPreflight) {
    Write-Step "早检：单独跑一次 sdkmanager --list（上限 $PreflightTimeoutSeconds 秒）"
    $pre = Invoke-Captured -FilePath $sdkmanagerBat -Arguments '--list' -TimeoutSec $PreflightTimeoutSeconds `
        -Environment @{ SDKMANAGER_OPTS = $SdkManagerOpts } `
        -WorkingDirectory (Split-Path $sdkmanagerBat -Parent)
    if ($pre.TimedOut) {
        throw "sdkmanager --list 在 $PreflightTimeoutSeconds 秒内没有结束——超时参数没有生效，先别启动构建。"
    }
    $warnCount = ([regex]::Matches($pre.StdOut + $pre.StdErr, 'Still waiting for package manifests')).Count
    Write-Ok "退出码 $($pre.ExitCode)，耗时小于 $PreflightTimeoutSeconds 秒，'Still waiting' 出现 $warnCount 次（预期为警告级，不阻塞）"
    if ($pre.StdOut -notmatch 'Installed packages') {
        Write-Warn2 '输出里没有 "Installed packages"，Unity 可能拿不到组件版本，请人工看一眼输出。'
    }
}

# ---------------------------------------------------------------- 3. 固化环境变量
if (-not $NoPersist) {
    Write-Step '写入用户级环境变量 SDKMANAGER_OPTS（让 Hub 直接点的构建也不卡）'
    $old = [Environment]::GetEnvironmentVariable('SDKMANAGER_OPTS', 'User')
    if ($old -ne $SdkManagerOpts) {
        [Environment]::SetEnvironmentVariable('SDKMANAGER_OPTS', $SdkManagerOpts, 'User')
        Write-Ok "已写入：$SdkManagerOpts"
        Write-Warn2 'Unity Hub 需要重启一次才会拿到新的环境变量；本脚本自己启动的 Unity 不受影响。'
    } else {
        Write-Ok '用户级环境变量已经是目标值，不重复写。'
    }
}

# ---------------------------------------------------------------- 4. 构建
Write-Step "启动 Unity 批处理构建（硬超时 $TimeoutMinutes 分钟）"
if (Test-Path $LogFile) { Remove-Item $LogFile -Force }
$buildStart = Get-Date

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $Unity
$psi.Arguments = "-batchmode -quit -nographics -projectPath `"$Project`" -executeMethod $Method -logFile `"$LogFile`""
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.EnvironmentVariables['SDKMANAGER_OPTS'] = $SdkManagerOpts
$unityProcess = [System.Diagnostics.Process]::new()
$unityProcess.StartInfo = $psi
[void]$unityProcess.Start()
Write-Ok "Unity PID $($unityProcess.Id)，日志 $LogFile"
$finished = $unityProcess.WaitForExit($TimeoutMinutes * 60 * 1000)
if (-not $finished) {
    try { $unityProcess.Kill($true) } catch { }
    throw "构建超过 $TimeoutMinutes 分钟仍未结束，已结束进程树。最后 40 行日志见下方/文件。"
}
Write-Ok "Unity 退出码 $($unityProcess.ExitCode)，用时 $([math]::Round(((Get-Date) - $buildStart).TotalMinutes, 1)) 分钟"

# ---------------------------------------------------------------- 5. 结果核对
Write-Step '核对构建结果'
$log = if (Test-Path $LogFile) { Get-Content $LogFile -Raw } else { '' }
$waiting = ([regex]::Matches($log, 'Still waiting for package manifests')).Count
$sdkDetectStuck = $log -match 'Detecting Android SDK' -and $log -notmatch 'task "Detecting Android SDK" took'
Write-Ok "'Still waiting for package manifests' 出现 $waiting 次（警告级；只要构建继续推进就正常）"
if ($sdkDetectStuck) { Write-Warn2 '日志里有 "Detecting Android SDK" 但没有对应的 took 行，SDK 检测可能没走完。' }

if (-not (Test-Path $OutputApk)) {
    Write-Warn2 "没有找到 APK：$OutputApk"
    Write-Host '--- 日志最后 40 行 ---'
    ($log -split "`r?`n" | Select-Object -Last 40) -join "`n"
    exit 1
}

$apk = Get-Item $OutputApk
if ($apk.LastWriteTime -lt $buildStart) {
    Write-Warn2 "APK 未被本次构建刷新（最后写入 $($apk.LastWriteTime)），本次实际没有产出。"
    exit 1
}

$hash = (Get-FileHash $OutputApk -Algorithm SHA256).Hash
Write-Ok "APK         : $($apk.FullName)"
Write-Ok "大小/时间   : $([math]::Round($apk.Length / 1MB, 2)) MiB / $($apk.LastWriteTime)"
Write-Ok "SHA256      : $hash"
if ($unityProcess.ExitCode -ne 0) {
    Write-Warn2 "Unity 退出码非 0（$($unityProcess.ExitCode)），请查日志。"
    exit 1
}
Write-Step '完成'
exit 0
