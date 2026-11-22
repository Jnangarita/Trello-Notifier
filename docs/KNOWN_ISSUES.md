# Estado actual y problemas conocidos

Revisión de fuentes y configuración: **21/11/2026**. Este documento sustituye
el diagnóstico inicial del 24/09/2026 como referencia de pendientes. No se
leyeron configuración, historial ni logs privados del usuario.

## Cómo interpretar este estado

- **Resuelto**: la implementación anterior fue sustituida o corregida; no volver
  a proponer esa corrección sin comprobar una regresión.
- **Parcial**: existe una mejora, pero queda una limitación concreta.
- **Pendiente**: la limitación sigue presente en las fuentes revisadas. Un riesgo
  potencial no equivale a un fallo reproducido.
- Los planes, guías y resultados antiguos no prueban el estado actual. Antes de
  actuar, contrastar el punto con su implementación y sus pruebas. Si cambia,
  actualizar esta referencia en la misma tarea, indicando fecha y evidencia.

La inspección de código no equivale a una validación de ejecución. Los comandos,
la cobertura y los resultados históricos están en [Pruebas](TESTING.md).

## Hallazgos anteriores resueltos

| Hallazgo anterior | Estado actual y evidencia |
| --- | --- |
| JSON síncrono/no atómico y reinicio silencioso ante datos inválidos | **Resuelto para la persistencia habitual.** `Services/SettingsStore.cs` usa SQLite, transacciones y errores visibles. Importa ambos JSON anteriores en una transacción y conserva los originales; el esquema 3 migra v1/v2 sin reiniciar los datos. Las páginas y `DueCardMonitor.CheckNowAsync` sacan el IO SQLite del hilo UI. `Program.cs` cubre migraciones, rollback, bloqueo, corrupción y conservación del historial. Véase [ADR 004](adr/004-sqlite-storage.md). |
| Excepciones completas en `crash.log` | **Resuelto en el logger actual.** `App.xaml.cs` usa `AppLog.WriteFailure`; `Services/AppLog.cs` registra propiedades técnicas permitidas con Serilog File/Async y rotación. Las pruebas de logs usan secretos ficticios para comprobar su exclusión. Ya no se escribe `crash.log`; los archivos antiguos no se eliminan automáticamente. Esto no resuelve todos los mensajes de error de la UI. |
| Fallo de registro de notificaciones sin diagnóstico | **Resuelto en logging.** `DesktopNotificationService.Initialize` registra el fallo con `AppLog.WriteFailure`; los envíos omitidos por falta de registro generan advertencia. No implica que Windows muestre siempre el aviso. |
| Import no utilizado y excepción de lint de `MainPage` | **Resuelto.** Se retiraron el import y la excepción; `.editorconfig` aplica IDE0005 y `verify.ps1` comprueba imports también en ese archivo. |
| Errores HTTP Trello sin `StatusCode` | **Resuelto.** `TrelloApiClient.SendAsync` construye `HttpRequestException` con `response.StatusCode`. No confundirlo con la clasificación por categoría, aún incompleta. |
| Timeout presentado como error técnico genérico | **Resuelto en Mis tarjetas e Historial.** `DueCardMonitor` y `CardsPage.RefreshHistoryAsync` usan `MonitorSnapshot.CreateTimeout`; la cancelación solicitada se trata por separado. El runner incluye recuperación tras timeout del monitor. |

Las rutas de servicios de las tablas son relativas a `TrelloNotifier/`; el runner
está en `TrelloNotifier.Tests/Program.cs`. La suite sustituye el cliente Trello:
sus pruebas no demuestran el comportamiento del HTTP real ni de WinUI.

## Limitaciones vigentes

| Área | Estado | Evidencia / alcance |
| --- | --- | --- |
| Credenciales sin cifrar | Pendiente | `SettingsStore` guarda API key/token de Trello y clave Gemini en SQLite sin cifrado. Los respaldos JSON anteriores también pueden contener credenciales. No leer ni adjuntar datos locales como contexto. |
| URL de API configurable y credenciales en query | Pendiente | `AppSettings.IsConfigured` admite HTTP/HTTPS y `TrelloApiClient` añade key/token a la consulta. La URL es de solo lectura en la UI, pero puede proceder de preferencias persistidas; un destino alternativo puede recibir las credenciales conservadas. Usar únicamente valores ficticios con mocks. |
| Validación desigual de enlaces | Pendiente | `CardsPage.OpenCard_Click` comprueba esquema HTTP/HTTPS; `DesktopNotificationService.OpenTrelloUrl` limita host a Trello, pero no comprueba esquema. No hay una validación uniforme entre esos puntos. |
| Mensajes de error al usuario | Parcial | El monitor controla el fallo de transporte sin `StatusCode` y el timeout; Historial usa un mensaje genérico y uno específico para timeout. `DueCardMonitor` conserva `ex.Message` para otros fallos y `SettingsPage` lo muestra en varios manejadores. La redacción segura del logger no cubre automáticamente la UI. |
| Clasificación HTTP y espera por límite | Parcial | El cliente conserva `StatusCode`, pero no hay clasificación completa 401/403/429/5xx ni tratamiento de `Retry-After` en el cliente Trello/monitor. El bucle conserva su intervalo normal; no hay reintentos automáticos de creación. |
| Cierre/reinicio del monitor | Cierre normal coordinado; pendiente comprobación manual WinUI | `App` espera `DueCardMonitor.StopAsync()` antes de vaciar Serilog. Se conserva el semáforo para que trabajo cancelado pueda liberarlo; no se admiten reinicios tras solicitar cierre. Una prueba simulada verifica espera de comprobaciones manuales y ausencia de nuevos avisos al cerrar. El cierre forzado no garantiza vaciado. |
| Runtime de la app | Pendiente | `TrelloNotifier.csproj` apunta a .NET 6, fuera de soporte. El SDK .NET 9 requerido para verificar y el framework net9.0 de las pruebas no actualizan el runtime de producción. |
| Distribución | Pendiente | Versión de app e instalador en archivos separados. El csproj declara mínimos Windows 17763/18362 en propiedades distintas y README indica 17763; falta conciliar el requisito efectivo. |
| Cobertura y automatización | Pendiente | No hay suite UI ni workflows CI en `.github/workflows`. La solución solo incluye la app; compilarla no ejecuta el runner. Los dobles del monitor no prueban HTTP Trello real, registro/activación Windows ni el binario WinUI. |

## Observaciones de diseño, no correcciones automáticas

- La validación de conexión/opciones se reparte entre `AppSettings`,
  `SettingsPage` y XAML. `ShowTest` y `ShowDueCards` comparten parte de la
  construcción/envío de avisos. Reutilizar o extender solo cuando la tarea lo requiera.
- `AppServices` y `MainPage.Current` usan instancias estáticas; las notificaciones
  llaman a `App.GetAssetPath`. Es el diseño concreto actual, no una orden de migrar
  a MVVM, introducir interfaces o rediseñar capas.
- `ReminderSchedule.GetKey` requiere fecha no nula y sus llamadas del monitor
  filtran previamente. No se ha demostrado un fallo por esa precondición.
- La copia actual sí es un repositorio Git. La afirmación histórica de que no
  existía `.git` ya no describe este entorno. No se ha auditado la historia de Git
  para afirmar ausencia de secretos.

## Validación de esta actualización documental

- `dotnet --list-sdks`: solo SDK **6.0.428** en este entorno (21/11/2026).
- `verify.ps1`: bloqueado por ausencia del SDK .NET 9 requerido; no se presenta
  ningún resultado histórico del runner o build como una validación nueva.
- No se ejecutaron pruebas manuales WinUI ni integración contra Trello para esta
  revisión documental. Revalidar el entorno y los casos afectados en cada tarea.
