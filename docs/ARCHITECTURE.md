# Arquitectura actual

## Unidades y flujo
Hay una aplicación WinUI 3 sin empaquetar y un ejecutable de pruebas independiente.
Las carpetas son agrupaciones de responsabilidades, no ensamblados/capas de
Clean Architecture. Hay una base SQLite local; no hay servidor, ORM ni framework de DI/MVVM.

```text
App → MainWindow / MainPage → DashboardPage / SettingsPage
MainPage → CreateCardPage → SettingsStore / TrelloApiClient
MainPage → BulkCreateCardPage → CardCsvImporter / SettingsStore / TrelloApiClient
CreateCardPage / BulkCreateCardPage → GeminiCardClient → HTTP Gemini (solo propuestas)
BulkCreateCardPage / CardCsvImporter / GeminiCardClient → CardBatchPreparer
App / páginas → AppServices (composición de instancias concretas)
DashboardPage → DueCardMonitor → TrelloApiClient → HTTP Trello/mock
DashboardPage (Historial) → SettingsStore, TrelloApiClient (consulta bajo demanda)
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
ámbito (`TrelloCardScope`), tablero, estado y búsqueda, reutilizando `GetStatus`.
`GetCardPage` reutiliza ese filtrado y orden para devolver una página local, el
total filtrado y los límites de navegación. `DashboardPage` conserva la selección
de página y tamaño para ambas vistas y solo crea los viewmodels de las filas visibles.
Mis tarjetas usa el ámbito pendiente (sin completar ni archivar), también para
sus indicadores. `DashboardPage` se reutiliza mediante un parámetro de navegación
para Historial: consulta todas las asignadas al entrar o actualizar, muestra la
unión de completadas y archivadas y permite filtrar ambos estados independientes.
Esa vista no se suscribe al monitor ni dispara recordatorios; cancela su consulta
al salir y no persiste tarjetas. Ambas vistas reutilizan filas, estilos y
`TrelloCardViewModel`; las archivadas muestran su condición de completado sin
urgencia temporal. El monitor actualiza controles mediante `DispatcherQueue`.
La UI contiene coordinación en code-behind.

`CreateCardPage` implementa el formulario manual, reutiliza estilos y el cliente HTTP
compartido, y coordina la creación de tarjeta, checklists y elementos. Los borradores
`TrelloCardDraft`/`TrelloChecklistDraft` validan campos y normalizan líneas sin IO;
`TrelloMember` y `TrelloLabel` representan las opciones de Trello. Las cargas se cancelan
al navegar o cambiar de tablero; durante una escritura se impide navegar para conservar
su resultado. Los fallos parciales o inciertos se muestran sin repetir el POST inicial.

`BulkCreateCardPage` coordina selector de archivos WinUI, destino, revisión y envío
secuencial. `CardCsvImporter` procesa texto con `TextFieldParser` de .NET y exporta resultados con escape CSV y
protección contra fórmulas. `CardImportRow`/`CardImportState` representan filas y sus
estados sin depender de UI, archivos o HTTP. El progreso es local a la vista; se
exporta a un archivo elegido por el usuario. El HWND del selector procede de `App`.
La clasificación de rechazos confirmados pertenece a `TrelloApiClient` y se comparte
con la creación manual; `TrelloCard.TrelloUri` centraliza sus enlaces HTTPS seguros.

`GeminiCardClient` usa un `HttpClient` compartido con cancelación, timeout y lectura
acotada. Analiza y valida el JSON en el servicio y devuelve propuestas `CardImportRow`
con `TrelloCardDraft` y observaciones de fechas, sin destino ni asignaciones. La vista
individual pide confirmar el reemplazo y reutiliza los campos/checklists existentes.
La vista de lote admite CSV o texto y permite editar/excluir filas antes del primer
envío. `CardBatchPreparer` extrae la preparación antes ubicada en `CardCsvImporter`:
valida fechas, responsables y borradores para ambos orígenes, preservando resultados
creados/excluidos/inciertos. Gemini no recibe servicios de Trello ni accede a SQLite.
El esquema SQLite 3 añade `GeminiApiKey` y `GeminiModel`; migra v1/v2 transaccionalmente.

`App` inicializa `Services/AppLog` antes de usar `AppServices` y vacía Serilog al
cerrar o ante un fallo fatal. El cierre normal de ventana espera `Monitor.StopAsync()`
antes de disponer notificaciones y vaciar el logger; `ProcessExit` es un respaldo.
Solo el cierre ordenado registra «Aplicación cerrada»; el respaldo de salida y un
fallo fatal no se presentan como un cierre normal.
Los servicios y las fronteras de UI usan el logger
compartido; modelos y calendario siguen sin logging. `AppLog` configura texto `.log` en
LocalAppData con Serilog File + Async (cola limitada no bloqueante), limita los
datos de excepción a copias saneadas con trazas nativas sin rutas, mensajes originales
ni Data y observa fallos internos del logger sin copiar su contenido.
`LogContext` propaga `SessionId` al arrancar y `CheckId` dentro de una
comprobación; una propiedad global de sesión respalda callbacks nativos sin contexto.
`CompactLogEnricher` añade el nombre corto y la correlación de presentación sin
modificar las propiedades originales. Los
errores se registran donde se capturan y los servicios añaden resultados/tiempos.
No se incorpora un contenedor DI ni persistencia de logs en SQLite.

La consulta de logs se realiza directamente en los archivos `.log` con herramientas
externas; la aplicación no incluye una vista ni un servicio de lectura de logs.

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
