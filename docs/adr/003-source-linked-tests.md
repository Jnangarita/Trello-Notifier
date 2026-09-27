# ADR 003 — Runner independiente con fuentes enlazadas

Status: Accepted

## Context
Hay que verificar reglas de recordatorios y persistencia sin consultar Trello
ni emitir notificaciones de Windows.

## Decision
El ejecutable .NET 9 de pruebas compila por enlaces los modelos, `ReminderSchedule`,
`SettingsStore` y `DueCardMonitor`. `MonitorDoubles` sustituye cliente HTTP y
notificaciones. `Program.cs` ejecuta casos y termina por excepción si fallan.
Evidencia: csproj de tests, dobles, runner y comando de README previos a esta tarea.

## Reason
El comentario en `MonitorDoubles` y README explican el aislamiento de red/Windows.
No consta el motivo de elegir runner propio en vez de un framework de testing.

## Consequences
Tests rápidos sin arranque WinUI; `dotnet run` es necesario, `dotnet test` no los
descubre. Los dobles no verifican contratos del servicio real y el build WinUI
sigue siendo obligatorio. Las comprobaciones nuevas reutilizan este runner.
