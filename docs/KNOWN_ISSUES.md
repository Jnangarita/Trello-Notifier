# Hallazgos previos (análisis 2026-09-24)

Estos puntos estaban presentes antes de preparar el trabajo con IA. No son
correcciones realizadas ni motivos para ampliar silenciosamente una tarea.
Inspección de fuentes/configuración manual; no se leyeron datos locales de usuario.
Build inicial: correcto, 0 warnings. Runner inicial: 11 pruebas correctas.

Actualización posterior: la migración a SQLite sustituye la persistencia JSON
descrita aquí por escrituras transaccionales y errores visibles, con IO fuera
del hilo UI. También retira el import no usado y su excepción de lint en
`MainPage`. Las credenciales siguen sin cifrar en SQLite y en los respaldos JSON.
Los puntos siguientes conservan el diagnóstico histórico; consultar el ADR 004
para el almacenamiento actual.

Actualización de logging: `AppLog` sustituye la escritura de excepciones completas
en `crash.log` por logs de texto con propiedades técnicas permitidas, rotación y escritura
asíncrona. Los fallos de registro de notificaciones ya se registran. Se conservan
el diagnóstico histórico inferior y la deuda de algunos `ex.Message` en la UI;
los antiguos `crash.log` no se eliminan.

## Seguridad
| Hallazgo | Evidencia / consecuencia |
| --- | --- |
| Credenciales en texto plano | `SettingsStore.Save` serializa `AppSettings`; confirmado también en README |
| Destino HTTP/configurable y credenciales en query | `AppSettings.IsConfigured`, `TryBuildSettings`, `TrelloApiClient.GetOpenCardsAsync`; un host alternativo puede recibir key/token si se conservan |
| Validación desigual de enlaces | `DashboardPage.OpenCard_Click` restringe esquema, `DesktopNotificationService.OpenTrelloUrl` restringe host pero no esquema |
| Logs/errores sin redacción | `App.OnUnhandledException` escribe excepción completa, monitor/UI exponen `ex.Message`; riesgo de datos sensibles, no fuga comprobada |
| Runtime antiguo | App apunta a .NET 6, fuera de soporte; actualizar requiere una tarea de compatibilidad propia, no cambiar versiones aquí |

No se identificaron credenciales reales hardcodeadas en las fuentes revisadas.
No hay SQL ni evidencia de bypass TLS. Esto no equivale a un escaneo de historia
Git: esta copia no contiene `.git`.

## Duplicación, acoplamiento y robustez
- Validación de URL/credenciales parcialmente repetida en `AppSettings` y
  `SettingsPage`; rangos/opciones/defaults repetidos entre XAML, validación y modelo.
- Construcción y envío de notificación parcialmente repetidos en `ShowTest` y
  `ShowDueCards`; no se extrajeron helpers sin necesidad.
- `AppServices` y `MainPage.Current` acoplan la UI a instancias estáticas;
  notificaciones llaman a `App.GetAssetPath`. El diseño es pequeño y concreto.
- `DueCardMonitor.Stop` cancela sin esperar `_monitorTask`; `Dispose` libera
  el semáforo mientras puede quedar trabajo. Posible carrera no cubierta por
  el caso actual de concurrencia entre comprobaciones.
- IO de JSON es síncrono y escritura no atómica; JSON inválido reinicia defaults
  o historial. Preferencias editadas a mano no pasan todas las validaciones UI.
- `DesktopNotificationService.Initialize` absorbe todos los errores; algunos
  fallos solo se manifiestan como notificación no mostrada.
- `ReminderSchedule.GetKey` requiere fecha no nula y depende del filtrado previo.
  `TrelloCardViewModel` ya admite fechas nulas al incorporar la consulta de todas
  las tarjetas asignadas.
- `MainPage.xaml.cs` tiene un `using TrelloNotifier.Models` no utilizado.
  IDE0005 permanece como sugerencia solo en ese archivo; no se eliminó código
  de producto para introducir lint. El resto mantiene el control activo.
- No se identificaron grandes bloques duplicados, repositories equivalentes ni
  ciclos entre proyectos de producción (solo hay un proyecto). No hay cobertura
  suficiente para afirmar ausencia de código muerto en toda la aplicación.

## Validación y distribución
- Tests enlazan fuentes y usan dobles con los mismos nombres concretos: no
  prueban HTTP real, registro/activación de Windows ni binario WinUI.
- Solución solo incluye app; ejecutar únicamente build de solución no ejecuta tests.
- Sin CI previa ni suite UI; el nuevo comando agregado cubre validaciones locales.
- Versiones de app e instalador se mantienen en archivos distintos. Los mínimos
  de Windows del csproj/README no son idénticos; revisar en una tarea de distribución.
