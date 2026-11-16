# Сборка всего, что раздаётся коллегам. Запуск из корня репозитория:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
# Результат в папке artifacts\:
#   AutocadMcp-Setup-<версия>.exe   установщик надстройки для AutoCAD
#   autocad-mcp-<версия>.mcpb       расширение для Claude Desktop
#   SHA256SUMS.txt                  контрольные суммы
# Если тесты красные, упаковка не начинается.

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$artifacts = Join-Path $root 'artifacts'

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Не найдена версия в Directory.Build.props' }
Write-Host "Версия: $version"

function Step([string]$text) { Write-Host ''; Write-Host "==> $text" }

Step '1/6 Тесты (без зелёных тестов упаковки не будет)'
dotnet test AutocadMcp.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Тесты не прошли. Упаковка остановлена.' }

Step '2/6 Очистка папки artifacts'
$full = [IO.Path]::GetFullPath($artifacts)
if (-not $full.StartsWith([IO.Path]::GetFullPath($root))) { throw 'artifacts вне репозитория — остановка' }
if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }
New-Item -ItemType Directory -Path $artifacts | Out-Null

Step '3/6 Сборка надстройки и Autodesk bundle'
dotnet build src/AutocadMcp.Plugin -c Release
if ($LASTEXITCODE -ne 0) { throw 'Надстройка не собралась' }
$bin = Join-Path $root 'src/AutocadMcp.Plugin/bin/Release/net8.0-windows'
$bundle = Join-Path $artifacts 'bundle/AutocadMcp.bundle'
$contents = Join-Path $bundle 'Contents'
New-Item -ItemType Directory -Path $contents -Force | Out-Null
foreach ($f in 'AutocadMcp.Plugin.dll', 'AutocadMcp.Core.dll') { Copy-Item (Join-Path $bin $f) $contents }
# Защита: в пакет не должны попасть DLL самого AutoCAD.
if (Get-ChildItem $contents -Filter 'Ac*.dll') { throw 'В пакет попали DLL AutoCAD — так быть не должно' }
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'),
    (Get-Content 'packaging/bundle/PackageContents.xml' -Raw -Encoding UTF8).Replace('__VERSION__', $version), $utf8NoBom)

Step '4/6 Сборка MCP-сервера (один exe, .NET не нужен на компьютере коллеги)'
$serverOut = Join-Path $artifacts 'mcpb/server'
dotnet publish src/AutocadMcp.Server -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $serverOut
if ($LASTEXITCODE -ne 0) { throw 'MCP-сервер не собрался' }
[IO.File]::WriteAllText((Join-Path $artifacts 'mcpb/manifest.json'),
    (Get-Content 'packaging/mcpb/manifest.json' -Raw -Encoding UTF8).Replace('__VERSION__', $version), $utf8NoBom)

Step '5/6 Установщик (Inno Setup)'
$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Не найден Inno Setup 6 (winget install JRSoftware.InnoSetup)' }
& $iscc "/DAppVersion=$version" "/DSourceDir=$bundle" 'packaging/installer/AutocadMcp.iss'
if ($LASTEXITCODE -ne 0) { throw 'Установщик не собрался' }

Step '6/6 Расширение Claude Desktop (.mcpb)'
# Версия mcpb закреплена, чтобы сборка была повторяемой.
npx -y '@anthropic-ai/mcpb@2.1.2' validate (Join-Path $artifacts 'mcpb/manifest.json')
if ($LASTEXITCODE -ne 0) { throw 'manifest.json не прошёл проверку' }
npx -y '@anthropic-ai/mcpb@2.1.2' pack (Join-Path $artifacts 'mcpb') (Join-Path $artifacts "autocad-mcp-$version.mcpb")
if ($LASTEXITCODE -ne 0) { throw '.mcpb не собрался' }

$outputs = Get-ChildItem $artifacts -File | Where-Object { $_.Extension -in '.exe', '.mcpb' }
$outputs | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } |
    Set-Content -Encoding UTF8 (Join-Path $artifacts 'SHA256SUMS.txt')

Write-Host ''
Write-Host 'ГОТОВО. В папке artifacts:'
$outputs | ForEach-Object { Write-Host ("  {0}  ({1:N1} МБ)" -f $_.Name, ($_.Length / 1MB)) }
Write-Host '  SHA256SUMS.txt'
