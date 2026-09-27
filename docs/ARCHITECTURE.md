# Arquitectura actual

## Unidades y flujo
Hay una aplicación WinUI 3 sin empaquetar y un ejecutable de pruebas independiente.
Las carpetas son agrupaciones de responsabilidades, no ensamblados/capas de
Clean Architecture. Hay una base SQLite local; no hay servidor, ORM ni framework de DI/MVVM.

```text
App → MainWindow / MainPage → DashboardPage / SettingsPage
App / páginas → AppServices (composición de instancias concretas)
DashboardPage → DueCardMonitor → TrelloApiClient → HTTP Trello/mock
SettingsPage → SettingsStore, TrelloApiClient, notificaciones, monitor
DueCardMonitor → ReminderSchedule, SettingsStore, DesktopNotificationService
Services → Models
DesktopNotificationService → Windows AppNotifications + App.GetAssetPath
SettingsStore → Microsoft.Data.Sqlite + base local (System.Text.Json solo para migración)
Models → BCL / System.Text.Json.Serialization
```

`AppServices` construye configuración, cliente HTTP, notificaciones y monitor.
El monitor recibe los servicios por constructor, sin interfaces. `Start/Restart`
inicia su bucle asíncrono; cada consulta toma `SemaphoreSlim`, consulta tarjetas,
selecciona candidatas con `ReminderSchedule`, carga/depura historial, agrupa
avisos y persiste las horas solo si se emitieron. Publica `MonitorSnapshot` con
`AssignedCards` (todas las abiertas asignadas), `DueSoonCards` (candidatas a avisos)
y la anticipación usada. `TrelloCard.GetStatus` centraliza los estados temporales
para el calendario y los filtros; no depende de la preferencia de avisar vencidas.
`MonitorSnapshot` obtiene las opciones de tablero y filtra sus tarjetas por
tablero y estado, reutilizando `GetStatus`. El dashboard aplica esos filtros
localmente, actualiza controles mediante
`DispatcherQueue` y transforma tarjetas con `TrelloCardViewModel`, incluidas
completadas y sin fecha. La UI contiene coordinación en code-behind.

## Responsabilidades y dependencias
| Área | Permitido actualmente | No introducir |
| --- | --- | --- |
| `Models/` | Tipos BCL, atributos JSON, modelos entre sí, textos de presentación | UI/WinRT, servicios, IO o HTTP |
| `ReminderSchedule` | Modelos, colecciones, LINQ, fechas recibidas | UI, cliente HTTP, almacenamiento, notificaciones o composición |
| `TrelloApiClient` | `AppSettings`, `TrelloCard`, HTTP/JSON | Controles o reglas de recordatorio |
| `SettingsStore` | Modelos, SQLite y lectura de JSON anteriores para migración | UI, HTTP o reglas de elegibilidad |
| `DueCardMonitor` | Servicios existentes y modelos; eventos de estado | Referencias a páginas o manipulación de controles |
| `DesktopNotificationService` | API Windows, modelos, `App.GetAssetPath` existente | Selección/repetición de tarjetas o persistencia |
| UI | Servicios vía `AppServices`, modelos, recursos compartidos | Copias del cliente HTTP, almacenamiento o calendario |

La referencia `DesktopNotificationService → App` es una excepción real existente;
no se presenta como una separación perfecta. No añadir nuevos ciclos. Las
dependencias compartidas estáticas son parte del diseño actual, no una invitación
a reemplazarlo dentro de una tarea pequeña.

## Ejemplos de ubicación
- Cambiar qué tarjeta puede avisar: `Services/ReminderSchedule.cs` + pruebas.
- Cambiar clasificación por vencimiento: `Models/TrelloCard.cs` (`GetStatus`) y `TrelloCardStatus.cs` + pruebas.
- Ajustar consulta o DTO Trello: `Services/TrelloApiClient.cs` y `Models/TrelloCard.cs`.
- Cambiar persistencia SQLite/migración desde JSON: `Services/SettingsStore.cs` + fixture temporal.
- Cambiar textos de tarjeta: `Models/TrelloCardViewModel.cs`.
- Cambiar controles/preferencias: `SettingsPage.xaml[.cs]`; estilos compartidos en `Styles.xaml`.

## Arquitectura ejecutable
`TrelloNotifier.Tests/ArchitectureTests.cs` compila en aislamiento todos los
modelos y después modelos + calendario con Roslyn del SDK. Rechaza dependencias
de esos archivos fuera de los namespaces permitidos, incluidos accesos IO/HTTP.
Los casos negativos comprueban que el guard detecta violaciones, también aliases
y nombres completamente cualificados. Esto protege las dos fronteras puras
existentes; las restantes filas requieren revisión y build WinUI. No se afirma
comprobar automáticamente todos los ciclos o todo el grafo de la aplicación.

Decisiones comprobadas: [composición/UI](adr/001-winui-composition.md),
[API/JSON anterior](adr/002-json-and-http.md), [pruebas](adr/003-source-linked-tests.md),
[persistencia SQLite](adr/004-sqlite-storage.md).

`SettingsStore` conserva su contrato de configuración y diccionario de historial.
Cada operación abre y libera una conexión sin pooling, con espera por bloqueo de
5 segundos. La inicialización y migración usan una transacción inmediata y
`PRAGMA user_version`; el reemplazo del historial también es transaccional.
Los SQL son parametrizados y los errores del motor se convierten en mensajes
de almacenamiento sin valores ni detalles SQL. Las páginas y las comprobaciones
del monitor realizan este IO síncrono en el pool de hilos; los controles se
actualizan en UI. No se añadieron reglas de horario laboral.
