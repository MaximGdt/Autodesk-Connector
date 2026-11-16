# Проверка компьютера перед работой. Ничего не устанавливает и не меняет, только смотрит.
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-env.ps1
# Код выхода 0 = можно работать, 1 = чего-то обязательного не хватает.

$ErrorActionPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$results = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$name, [string]$state, [string]$detail, [string]$fix) {
    $results.Add([pscustomobject]@{ Name = $name; State = $state; Detail = $detail; Fix = $fix })
}

# 1. Windows 64-bit
$os = Get-CimInstance Win32_OperatingSystem
if ($os -and [Environment]::Is64BitOperatingSystem) {
    Add-Check 'Windows 64-бит' 'OK' $os.Caption ''
} else {
    Add-Check 'Windows 64-бит' 'НЕТ' 'Нужна 64-битная Windows 10 или 11' 'Обратись к Максиму'
}

# 2. AutoCAD 2025 (full, not LT)
$acadExe = Join-Path $env:ProgramFiles 'Autodesk\AutoCAD 2025\acad.exe'
$ltExe = Join-Path $env:ProgramFiles 'Autodesk\AutoCAD LT 2025\acadlt.exe'
if (Test-Path $acadExe) {
    Add-Check 'AutoCAD 2025 (полный)' 'OK' $acadExe ''
} elseif (Test-Path $ltExe) {
    Add-Check 'AutoCAD 2025 (полный)' 'НЕТ' 'Установлен AutoCAD LT: у него нет .NET API, коннектор с ним не работает' 'Нужен полный AutoCAD 2025. Обратись к Максиму'
} else {
    Add-Check 'AutoCAD 2025 (полный)' 'НЕТ' "Не найден $acadExe" 'Установи AutoCAD 2025 или скажи агенту, где он установлен'
}

# 3. .NET 8 SDK
$sdks = (& dotnet --list-sdks) 2>$null
if ($sdks -match '^8\.') {
    Add-Check '.NET 8 SDK' 'OK' (($sdks -match '^8\.') | Select-Object -Last 1) ''
} else {
    Add-Check '.NET 8 SDK' 'НЕТ' 'Нужен для сборки' 'winget install Microsoft.DotNet.SDK.8'
}

# 4. Git
$git = (& git --version) 2>$null
if ($git) { Add-Check 'Git' 'OK' $git '' } else { Add-Check 'Git' 'НЕТ' 'Нужен для сохранения шагов и отката' 'winget install Git.Git' }

# 5. Node.js (только для упаковки, этап 6)
$node = (& node --version) 2>$null
if ($node) { Add-Check 'Node.js (этап 6)' 'OK' $node '' } else { Add-Check 'Node.js (этап 6)' 'ПОЗЖЕ' 'Понадобится только для упаковки .mcpb' 'winget install OpenJS.NodeJS.LTS' }

# 6. Inno Setup (только для упаковки, этап 6)
$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) { Add-Check 'Inno Setup 6 (этап 6)' 'OK' $iscc '' } else { Add-Check 'Inno Setup 6 (этап 6)' 'ПОЗЖЕ' 'Понадобится только для установщика' 'winget install JRSoftware.InnoSetup' }

# 7. Claude Desktop (нужен с этапа 2)
$claudeCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'AnthropicClaude\claude.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Claude\Claude.exe')
)
$claude = $claudeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $claude) { $claude = (Get-AppxPackage -Name '*Claude*' | Select-Object -First 1).InstallLocation }
if ($claude) { Add-Check 'Claude Desktop' 'OK' $claude '' } else { Add-Check 'Claude Desktop' 'ПРОВЕРЬ' 'Не найден автоматически' 'Проверь вручную, что приложение Claude установлено (claude.ai/download)' }

# 8. Свободное место
$drive = Get-PSDrive -Name ($PSScriptRoot.Substring(0, 1))
$freeGb = [math]::Round($drive.Free / 1GB, 1)
if ($freeGb -ge 5) { Add-Check 'Свободное место' 'OK' "$freeGb ГБ" '' } else { Add-Check 'Свободное место' 'НЕТ' "$freeGb ГБ, нужно минимум 5" 'Освободи место на диске' }

# Итог
Write-Host ''
Write-Host 'ПРОВЕРКА КОМПЬЮТЕРА'
Write-Host ('-' * 90)
foreach ($r in $results) {
    $mark = switch ($r.State) { 'OK' { '[OK]    ' } 'ПОЗЖЕ' { '[ПОЗЖЕ] ' } 'ПРОВЕРЬ' { '[?]     ' } default { '[НЕТ]   ' } }
    Write-Host ("{0}{1,-26} {2}" -f $mark, $r.Name, $r.Detail)
    if ($r.Fix) { Write-Host ("          как исправить: {0}" -f $r.Fix) }
}
Write-Host ('-' * 90)

$missing = @($results | Where-Object { $_.State -eq 'НЕТ' })
if ($missing.Count -eq 0) {
    Write-Host 'ИТОГ: всё обязательное на месте, можно работать.'
    exit 0
} else {
    Write-Host ("ИТОГ: не хватает обязательного: {0}." -f (($missing | ForEach-Object { $_.Name }) -join ', '))
    exit 1
}
