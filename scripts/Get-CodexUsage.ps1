param([string]$CodexPath, [Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
function Send-Line($Writer, $Object) {
    $Writer.WriteLine(($Object | ConvertTo-Json -Depth 12 -Compress))
    $Writer.Flush()
}
function Read-Response($Reader, [int]$ExpectedId, [int]$TimeoutSeconds = 18) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $read = $Reader.ReadLineAsync()
        $left = [int][Math]::Max(1, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        if (-not $read.Wait($left)) { throw 'Codex 查询超时，请检查登录或网络。' }
        if ($null -eq $read.Result) { throw 'Codex 连接已关闭，请检查 CLI 版本。' }
        $message = $read.Result | ConvertFrom-Json -ErrorAction Stop
        if ($message.id -eq $ExpectedId) {
            if ($message.error) { throw 'Codex 暂未返回用量，请先使用 ChatGPT 账号登录。' }
            return $message.result
        }
    }
    throw 'Codex 查询超时。'
}
$payload = [ordered]@{ Session = $null; Weekly = $null }
$process = $null
try {
    if ([string]::IsNullOrWhiteSpace($CodexPath)) {
        $command = Get-Command codex.exe -ErrorAction SilentlyContinue
        if ($command) { $CodexPath = $command.Source }
    }
    if (-not $CodexPath -or -not (Test-Path -LiteralPath $CodexPath -PathType Leaf)) {
        throw '未找到 codex.exe，请在设置中选择路径或先安装 Codex CLI。'
    }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo.FileName = $CodexPath
    $process.StartInfo.Arguments = 'app-server'
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    if (-not $process.Start()) { throw '无法启动 Codex。' }
    $stderrTask = $process.StandardError.ReadToEndAsync()
    Send-Line $process.StandardInput ([ordered]@{
        method = 'initialize'; id = 1
        params = @{
            clientInfo = @{ name = 'ai_usage_widget'; title = 'AI Usage Widget'; version = '1.0.0' }
            capabilities = @{ experimentalApi = $true }
        }
    })
    $null = Read-Response $process.StandardOutput 1
    Send-Line $process.StandardInput @{ method = 'initialized'; params = @{} }
    Send-Line $process.StandardInput @{ method = 'account/rateLimits/read'; id = 2; params = @{} }
    $result = Read-Response $process.StandardOutput 2
    $snapshot = $null
    if ($result.rateLimitsByLimitId -and $result.rateLimitsByLimitId.codex) {
        $snapshot = $result.rateLimitsByLimitId.codex
    } elseif ($result.rateLimits -and (-not $result.rateLimits.limitId -or $result.rateLimits.limitId -eq 'codex')) {
        $snapshot = $result.rateLimits
    }
    if (-not $snapshot) { throw '当前账号没有可读取的 Codex 订阅额度。' }
    $values = @($snapshot.primary, $snapshot.secondary) | Where-Object { $null -ne $_ }
    $session = $values | Where-Object { $_.windowDurationMins -eq 300 } | Select-Object -First 1
    $weekly = $values | Where-Object { $_.windowDurationMins -eq 10080 } | Select-Object -First 1
    if (-not $session -and -not $weekly) { throw 'Codex 未返回 5 小时或每周额度。' }
    if ($session) { $payload.Session = @{ usedPercent = [double]$session.usedPercent; resetsAt = $session.resetsAt } }
    if ($weekly) { $payload.Weekly = @{ usedPercent = [double]$weekly.usedPercent; resetsAt = $weekly.resetsAt } }
} catch {
    $payload.CodexError = $_.Exception.Message
} finally {
    if ($process) {
        try { $process.StandardInput.Close() } catch {}
        try { if (-not $process.HasExited) { $process.Kill() } } catch {}
        $process.Dispose()
    }
}
$key = [Environment]::GetEnvironmentVariable('DEEPSEEK_API_KEY', 'Process')
if ([string]::IsNullOrWhiteSpace($key)) { $key = [Environment]::GetEnvironmentVariable('DEEPSEEK_API_KEY', 'User') }
if ([string]::IsNullOrWhiteSpace($key)) {
    $payload.DeepSeek = @{ Configured = $false; BalanceInfos = @() }
} else {
    try {
        $response = Invoke-RestMethod -Method Get -Uri 'https://api.deepseek.com/user/balance' -Headers @{
            Authorization = "Bearer $key"; Accept = 'application/json'
        } -TimeoutSec 15
        $payload.DeepSeek = @{ Configured = $true; IsAvailable = [bool]$response.is_available; BalanceInfos = @($response.balance_infos) }
    } catch {
        $payload.DeepSeek = @{ Configured = $true; Error = '余额查询失败，请检查密钥与网络。'; BalanceInfos = @() }
    }
}
$key = $null
[IO.File]::WriteAllText($OutputPath, ($payload | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
