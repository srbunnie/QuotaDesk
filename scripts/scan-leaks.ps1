<#
.SYNOPSIS
    Bloqueia chaves, tokens, arquivos de credencial e dados pessoais antes de chegarem ao GitHub.

.DESCRIPTION
    Sem parâmetros, verifica o que está no stage (é o que o hook .githooks/pre-commit roda).
    -All verifica todos os arquivos versionados; -History, as linhas adicionadas em todos os commits.
    -Revs a,b (usado pelo pre-push) verifica só os commits que ainda não estão em nenhum remoto.

    Termos pessoais (nome, usuário do Windows, empresa) não ficam no repositório: cada pessoa
    coloca os seus, um por linha, em .git/info/leak-scan-denylist.txt. No CI eles vêm do
    segredo LEAK_SCAN_DENYLIST. Esses termos nunca são impressos, só o arquivo e a linha.

    Uma linha com o marcador leak-scan:allow é ignorada (use só para exemplos falsos).
#>
[CmdletBinding()]
param(
    [switch]$All,
    [switch]$History,
    [string]$Revs
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$root = (git rev-parse --show-toplevel).Trim()
Set-Location -LiteralPath $root

$secretRules = @(
    @{ Name = 'chave privada';        Pattern = '-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP |ENCRYPTED )?PRIVATE KEY' }
    @{ Name = 'chave da AWS';         Pattern = '\b(?:AKIA|ASIA)[0-9A-Z]{16}\b' }
    @{ Name = 'token do GitHub';      Pattern = '\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{50,})' }
    @{ Name = 'chave da Anthropic';   Pattern = '\bsk-ant-[A-Za-z0-9_\-]{20,}' }
    @{ Name = 'chave da OpenRouter';  Pattern = '\bsk-or-v1-[A-Fa-f0-9]{32,}' }
    @{ Name = 'chave da OpenAI';      Pattern = '\bsk-(?:proj-|svcacct-|admin-)?[A-Za-z0-9_\-]{32,}' }
    @{ Name = 'chave do Google';      Pattern = '\bAIza[0-9A-Za-z_\-]{35}' }
    @{ Name = 'token do Slack';       Pattern = '\bxox[abprs]-[A-Za-z0-9\-]{10,}' }
    @{ Name = 'chave do Stripe';      Pattern = '\b[rs]k_live_[A-Za-z0-9]{20,}' }
    @{ Name = 'JWT';                  Pattern = '\beyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}' }
    @{ Name = 'Bearer literal';       Pattern = '(?i)\bbearer\s+[A-Za-z0-9._~+/\-]{24,}=*' }
    @{ Name = 'segredo atribuído';    Pattern = '(?i)\b(?:api[_-]?key|apikey|client[_-]?secret|secret[_-]?key|password|passwd|pwd|access[_-]?token|refresh[_-]?token|auth[_-]?token)\b["'']?\s*[:=]\s*["''][^"''\s]{12,}["'']' }
)

$personalRules = @(
    @{ Name = 'caminho de usuário do Windows'; Pattern = '(?i)\b[A-Z]:[\\/]+Users[\\/]+(?!(?:Public|Default|All Users)\b)[^\\/\s"''<>|:*?%$\{\}]+' }
    @{ Name = 'caminho de usuário Unix';       Pattern = '(?<![\w.~\-])/(?:home|Users)/(?!runner/)[A-Za-z0-9._\-]+/' }
    @{ Name = 'e-mail';                        Pattern = '(?i)\b(?!no-?reply@)[A-Z0-9._%+\-]+@(?!(?:[A-Z0-9\-]+\.)*(?:example\.(?:com|org|net)|users\.noreply\.github\.com)\b)[A-Z0-9.\-]+\.(?!(?:png|jpg|jpeg|gif|svg|webp|ico)\b)[A-Z]{2,}\b' }
)

$blockedFiles = @(
    '(^|/)\.env(\.(?!example$|template$)[^/]+)?$'
    '(^|/)(claude-)?settings\.json$'
    '(^|/)settings\.local\.json$'
    '\.(dat|pfx|p12|pem|key|snk|ppk|kdbx|jks|keystore|publishsettings|dmp|mdmp|har)$'
    '(^|/)id_(rsa|dsa|ecdsa|ed25519)(\.pub)?$'
    '(^|/)\.(npmrc|pypirc|netrc)$'
    '(^|/)error\.log$'
    '\.(exe|dll|pdb|msi|zip|7z|nupkg)$'
)

# Termos pessoais: arquivo local em .git/info e/ou segredo do CI. Comparação literal, sem diferenciar maiúsculas.
$denyTerms = New-Object System.Collections.Generic.List[string]
$denyFile = (git rev-parse --git-path info/leak-scan-denylist.txt).Trim()
if (Test-Path -LiteralPath $denyFile) { Get-Content -LiteralPath $denyFile -Encoding UTF8 | ForEach-Object { $denyTerms.Add($_) } }
if ($env:LEAK_SCAN_DENYLIST) { $env:LEAK_SCAN_DENYLIST -split "`r?`n" | ForEach-Object { $denyTerms.Add($_) } }
$denyRules = @($denyTerms | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } | Sort-Object -Unique |
    ForEach-Object { [regex]::new([regex]::Escape($_), 'IgnoreCase') })

$findings = New-Object System.Collections.Generic.List[string]

function Mask([string]$value) {
    if ($value.Length -le 8) { return '***' }
    return $value.Substring(0, 6) + '…' + '***'
}

function Test-Line([string]$file, [int]$line, [string]$text, [bool]$textOnly) {
    if ($text -match 'leak-scan:allow') { return }
    $where = if ($line -gt 0) { "${file}:$line" } else { $file }
    if ($textOnly) {
        foreach ($rule in $secretRules + $personalRules) {
            $m = [regex]::Match($text, $rule.Pattern)
            if ($m.Success) { $findings.Add("$where  $($rule.Name): $(Mask $m.Value)") }
        }
    }
    else {
        foreach ($rule in $personalRules | Select-Object -First 2) {
            if ([regex]::IsMatch($text, $rule.Pattern)) { $findings.Add("$where  $($rule.Name) dentro de arquivo binário") }
        }
    }
    for ($i = 0; $i -lt $denyRules.Count; $i++) {
        if ($denyRules[$i].IsMatch($text)) { $findings.Add("$where  termo pessoal da lista local (#$($i + 1))") }
    }
}

function Test-FileName([string]$file) {
    foreach ($pattern in $blockedFiles) {
        if ($file -match $pattern) { $findings.Add("$file  tipo de arquivo que não pode ser versionado") }
    }
    foreach ($deny in $denyRules) {
        if ($deny.IsMatch($file)) { $findings.Add("$file  termo pessoal no nome do arquivo") }
    }
}

function Test-Identity([string]$label, [string]$email) {
    if ($env:QUOTADESK_ALLOW_EMAIL -eq '1') { return }
    # noreply@github.com é o committer dos merges feitos pela interface do GitHub.
    if ($email -notmatch '@users\.noreply\.github\.com$|^noreply@github\.com$') {
        $findings.Add("$label  e-mail do autor não é o noreply do GitHub ($(Mask $email))")
    }
}

# Lê um diff unificado e testa só as linhas adicionadas.
function Test-Diff([string[]]$diff) {
    $file = $null; $line = 0; $inHeader = $false
    foreach ($row in $diff) {
        if ($row.StartsWith('diff --git ')) { $inHeader = $true; $file = $null; continue }
        if ($inHeader) {
            if ($row.StartsWith('+++ ')) { $file = if ($row -eq '+++ /dev/null') { $null } else { $row.Substring(6) } }
            elseif ($row.StartsWith('@@')) { $inHeader = $false }
            if ($inHeader) { continue }
        }
        if ($row.StartsWith('@@')) {
            $m = [regex]::Match($row, '\+(\d+)')
            $line = if ($m.Success) { [int]$m.Groups[1].Value } else { 0 }
            continue
        }
        if ($file -and $row.StartsWith('+')) { Test-Line $file $line $row.Substring(1) $true; $line++ }
    }
}

if ($History -or $Revs) {
    $range = if ($Revs) { @($Revs -split ',' | Where-Object { $_ }) + '--not' + '--remotes' } else { @('--all') }
    $log = git -c core.quotepath=off log @range -p -U0 --no-color --no-ext-diff --format='commit %h %ae %ce'
    $chunk = New-Object System.Collections.Generic.List[string]
    foreach ($row in $log) {
        if ($row -match '^commit (\w+) (\S+) (\S+)$') {
            Test-Diff $chunk; $chunk.Clear()
            Test-Identity "commit $($Matches[1]) (autor)" $Matches[2]
            Test-Identity "commit $($Matches[1]) (committer)" $Matches[3]
            continue
        }
        if ($row.StartsWith('+++ b/')) { Test-FileName $row.Substring(6) }
        $chunk.Add($row)
    }
    Test-Diff $chunk
    $mode = if ($Revs) { 'commits a enviar' } else { 'histórico completo' }
}
elseif ($All) {
    $text = '\.(cs|csproj|ps1|cmd|py|md|json|html|css|js|txt|yml|yaml|xml|gitignore|gitattributes|nojekyll)$|(^|/)[^./]+$'
    foreach ($file in git -c core.quotepath=off ls-files) {
        Test-FileName $file
        if (-not (Test-Path -LiteralPath $file)) { continue }
        if ($file -match $text) {
            $n = 0
            foreach ($row in [IO.File]::ReadAllLines((Join-Path $root $file))) { $n++; Test-Line $file $n $row $true }
        }
        else {
            Test-Line $file 0 ([Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes((Join-Path $root $file)))) $false
        }
    }
    $mode = 'arquivos versionados'
}
else {
    foreach ($file in git -c core.quotepath=off diff --cached --name-only --diff-filter=ACMR) {
        Test-FileName $file
        $size = [long](git cat-file -s ":$file")
        if ($size -gt 10MB) { $findings.Add("$file  arquivo com $([math]::Round($size / 1MB)) MB; artefatos grandes vão para a Release, não para o Git") }
    }
    Test-Diff (git -c core.quotepath=off diff --cached -U0 --no-color --no-ext-diff)
    foreach ($kind in 'GIT_AUTHOR_IDENT', 'GIT_COMMITTER_IDENT') {
        $ident = git var $kind
        if ($ident -match '<([^>]*)>') { Test-Identity $kind $Matches[1] }
    }
    $mode = 'stage'
}

$unique = @($findings | Sort-Object -Unique)
if ($unique.Count -eq 0) {
    Write-Host "leak-scan: nada encontrado ($mode, $($denyRules.Count) termos pessoais)." -ForegroundColor Green
    exit 0
}

Write-Host "leak-scan: $($unique.Count) problema(s) em $mode" -ForegroundColor Red
$unique | ForEach-Object { Write-Host "  $_" }
Write-Host ''
Write-Host 'Remova o dado, troque a chave exposta e rode de novo. Exemplo falso em teste: marque a linha com leak-scan:allow.'
exit 1
