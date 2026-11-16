# Autodesk Connector: Claude ↔ AutoCAD 2025

Локальный коннектор, через который Claude Desktop чертит в запущенном AutoCAD 2025: по описанию, эскизу или референсу, с нуля или правками существующего чертежа. Работает только на этом компьютере, без сети, и выполняет только фиксированный список безопасных действий.

```
Claude Desktop ──► autocad-mcp-server.exe ──named pipe──► надстройка в AutoCAD ──► чертёж
```

## С чего начать

| Кто ты | Что читать |
| --- | --- |
| Человек, который будет вести сборку | Инструкция для запуска (ссылку прислал Максим), потом открыть эту папку в Claude Code и написать «Начинаем» |
| Агент (Claude Code) | `CLAUDE.md` → `PLAN.md` |
| Ревьюер | `docs/SECURITY.md`, `docs/ARCHITECTURE.md` |

## Что внутри

| Путь | Что это |
| --- | --- |
| `src/AutocadMcp.Core` | Общие правила: протокол, проверка запросов, безопасные пути, журнал, настройки |
| `src/AutocadMcp.Plugin` | Надстройка для AutoCAD 2025 (.NET 8) |
| `src/AutocadMcp.Server` | MCP-сервер для Claude Desktop + самопроверка `--smoke` |
| `tests/` | Автотесты: логика, скорость, безопасность |
| `packaging/`, `scripts/` | Установщик, расширение `.mcpb`, проверка компьютера, упаковка |
| `docs/` | Инструменты, безопасность, приёмка, решение проблем |
| `.claude/` | Права агента и скиллы `/check-env`, `/build-test`, `/live-test`, `/add-tool`, `/package`, `/rollback`, `/help-request` |

## Команды

```
dotnet build AutocadMcp.sln -c Release
dotnet test AutocadMcp.sln -c Release --no-build
dotnet run --project src/AutocadMcp.Server -- --smoke        # с запущенным AutoCAD и новым чертежом
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
```

В AutoCAD: `MCPSTATUS` — состояние, `MCPSTOP` — немедленно выключить доступ Claude, `MCPSTART` — включить, `MCPLOG` — где журнал.
