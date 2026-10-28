$ErrorActionPreference = 'Stop'
try {
    $inputJson = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $limits = $inputJson.rate_limits
    if ($null -ne $limits) {
        $dataDir = Join-Path $env:LOCALAPPDATA 'AIUsageMonitor'
        [System.IO.Directory]::CreateDirectory($dataDir) | Out-Null
        $destination = Join-Path $dataDir 'claude-limits.json'
        $temporary = Join-Path $dataDir ("claude-limits.{0}.tmp" -f [guid]::NewGuid().ToString('N'))
        $payload = [pscustomobject]@{
            captured_at = [DateTimeOffset]::UtcNow.ToString('o')
            rate_limits = [pscustomobject]@{
                five_hour = $limits.five_hour
                seven_day = $limits.seven_day
            }
        }
        try {
            [System.IO.File]::WriteAllText($temporary, ($payload | ConvertTo-Json -Depth 6 -Compress), [System.Text.UTF8Encoding]::new($false))
            if ([System.IO.File]::Exists($destination)) {
                $backup = Join-Path $dataDir ("claude-limits.{0}.bak" -f [guid]::NewGuid().ToString('N'))
                try { [System.IO.File]::Replace($temporary, $destination, $backup) }
                finally { if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue } }
            } else {
                [System.IO.File]::Move($temporary, $destination)
            }
        } finally {
            # A failed swap (file busy) must not leave temporary files piling up in the data folder.
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
        }
    }
    if ($null -ne $limits.five_hour.used_percentage) {
        $remaining = [math]::Round([math]::Max(0, 100 - [double]$limits.five_hour.used_percentage))
        "Claude 5h: $remaining% restante"
    } else {
        'Claude | AI Usage Monitor'
    }
} catch {
    'Claude | AI Usage Monitor'
}
