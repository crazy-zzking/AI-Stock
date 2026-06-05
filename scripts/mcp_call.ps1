param(
    [Parameter(Mandatory=$true)][string]$RequestsJson  # JSON array of {name, args} tool calls
)

# MySQL 连接串：从环境变量注入，勿在脚本里硬编码（避免泄露到版本库）。
# 用法：$env:MCP_MYSQL = "Server=...;Database=aistock;User=...;Password=...;CharSet=utf8mb4;"
if ([string]::IsNullOrWhiteSpace($env:MCP_MYSQL)) {
    Write-Error "请先设置 `$env:MCP_MYSQL（MySQL 连接串）"; exit 1
}
$env:ConnectionStrings__MySQL = $env:MCP_MYSQL

# MCP 可执行路径：默认 Release，可用 $env:MCP_EXE 覆盖（如指向 Debug 构建）。
$exe = if ([string]::IsNullOrWhiteSpace($env:MCP_EXE)) {
    Join-Path $PSScriptRoot "..\AIStock.Mcp\bin\Release\net9.0\AIStock.Mcp.exe"
} else { $env:MCP_EXE }

$calls = $RequestsJson | ConvertFrom-Json

$reqLines = New-Object System.Collections.ArrayList
[void]$reqLines.Add('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"ps","version":"1.0"}}}')
[void]$reqLines.Add('{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}')
$expectedIds = New-Object System.Collections.Generic.HashSet[int]
$id = 100
foreach ($c in $calls) {
    $id++
    [void]$expectedIds.Add($id)
    $payload = @{ jsonrpc="2.0"; id=$id; method="tools/call"; params=@{ name=$c.name; arguments=$c.args } }
    [void]$reqLines.Add(($payload | ConvertTo-Json -Depth 30 -Compress))
}

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8

$results = @{}
try {
    $p = [System.Diagnostics.Process]::Start($psi)
    $errTask = $p.StandardError.ReadToEndAsync()   # drain stderr async (no deadlock)

    foreach ($l in $reqLines) { $p.StandardInput.WriteLine($l) }
    $p.StandardInput.Flush()

    $deadline = (Get-Date).AddSeconds(560)
    while ($results.Count -lt $expectedIds.Count -and (Get-Date) -lt $deadline) {
        $line = $p.StandardOutput.ReadLine()
        if ($null -eq $line) { break }
        $t = $line.Trim()
        if ($t -eq "" -or -not $t.StartsWith("{")) { continue }
        try { $obj = $t | ConvertFrom-Json } catch { continue }
        if ($null -ne $obj.id -and $expectedIds.Contains([int]$obj.id) -and $null -ne $obj.result) {
            $results[[string]$obj.id] = $obj.result.content[0].text
        }
    }
    try { $p.StandardInput.Close() } catch {}
    try { if (-not $p.WaitForExit(5000)) { $p.Kill() } } catch {}
}
catch {
    Write-Output "ERROR: $($_.Exception.Message)"
}

$id = 100
foreach ($c in $calls) {
    $id++
    Write-Output "===CALL $id : $($c.name)==="
    if ($results.ContainsKey([string]$id)) { Write-Output $results[[string]$id] }
    else { Write-Output "(no result)" }
}
