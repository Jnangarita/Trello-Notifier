# Contexto mínimo

Trello Notifier es una aplicación de escritorio Windows que consulta las tarjetas
abiertas asignadas al usuario y muestra avisos nativos de vencimientos. Funciona
mientras el proceso está abierto; no existe un backend ni un servicio Windows.
La API base puede cambiarse para utilizar un servidor simulado sin credenciales.

## Stack y módulos
- `TrelloNotifier/`: C#, WinUI 3 y XAML; Windows App SDK 1.6.241114003,
  CsWinRT 2.0.0, Windows SDK BuildTools 10.0.22621.756. Target
  `net6.0-windows10.0.19041.0`, plataforma x64, RID `win10-x64`.
- `TrelloNotifier.Tests/`: ejecutable `net9.0`, runner propio sin xUnit/NUnit.
  Enlaza las fuentes reales de modelos, calendario, almacenamiento y monitor;
  utiliza SQLite temporal y dobles para HTTP y notificaciones. El SDK aporta Roslyn a las
  comprobaciones arquitectónicas, sin paquetes adicionales.
- Compilación con SDK .NET 9/MSBuild. Distribución sin MSIX, publicación
  autocontenida e instalador Inno Setup. La solución incluye solo la app;
  verificar la solución no ejecuta las pruebas.

## Mapa de responsabilidades
Las páginas usan code-behind, no un framework MVVM. `AppServices` construye
instancias compartidas de servicios concretos. `App` inicia el monitor y gestiona
activación/cierre. `MainPage` proporciona navegación, tema e InfoBar.
`DashboardPage` muestra pendientes no archivadas del snapshot y reutiliza su UI
para Historial (completadas o archivadas, consulta bajo demanda sin avisos);
ambas vistas usan paginación local de 10/25/50 filas (25 por defecto), posterior a
los filtros mediante `MonitorSnapshot.GetCardPage`, sin nuevas consultas ni avisos.
`SettingsPage` valida y guarda preferencias.
`CreateCardPage` crea tarjetas manuales con Markdown, responsables, etiquetas, fecha
y checklists usando el cliente compartido. Valida borradores en modelos; conserva
resultados parciales en la vista y evita reenvíos tras una respuesta incierta. Los
destinos se consultan bajo demanda; no hay borradores persistentes.
`BulkCreateCardPage` importa CSV UTF-8 (hasta 2 MiB / 1000 filas), revisa/edita/excluye
y crea por lotes secuenciales con el mismo cliente. `CardCsvImporter` reutiliza
TextFieldParser de .NET y exporta resultados; `CardBatchPreparer` comparte validación
de fechas/usuarios y preparación con el origen IA. `CardImportRow` guarda estados
en memoria para no reenviar creadas o inciertas. Se puede detener entre solicitudes.
`GeminiCardClient` genera propuestas opcionales en ambas vistas, sin crear en Trello:
JSON estructurado, 20 000 caracteres / 50 tarjetas / 1 MiB de respuesta / 60 s.
La creación individual confirma el reemplazo del formulario; el lote lleva a revisión.
Las páginas cancelan al salir; responsables/etiquetas se eligen con datos de Trello.
`Styles.xaml` contiene los estilos compartidos.
`DiagnosticsPage` consulta logs locales vía `AppLogReader`, con selector de archivo,
filtro por nivel y colores del tema. Lee en segundo plano hasta 1000 líneas completas
de los últimos 2 MiB; actualiza al entrar o bajo demanda y cancela al navegar.

`Models/` contiene `AppSettings`, `TrelloCard` (también DTO JSON; `closed` es
independiente de `dueComplete`; `GetStatus` clasifica vencimientos con
`TrelloCardStatus`), `MonitorSnapshot` (ámbitos `TrelloCardScope`, asignadas y
candidatas a avisos separadas) y `TrelloCardViewModel` (textos, sin WinUI).
`DueCardMonitor` coordina consulta, historial y avisos; protege comprobaciones
con `SemaphoreSlim`. `ReminderSchedule` decide elegibilidad, repetición y clave
de historial. `TrelloApiClient` usa `HttpClient` con timeout de 30 segundos.
`SettingsStore` guarda configuración e historial en `trello-notifier.db` en el
perfil local mediante Microsoft.Data.Sqlite 10.0.12. La primera apertura importa
ambos JSON anteriores en una transacción y conserva los originales como respaldo;
`user_version` impide reimportarlos. El esquema 3 añade `GeminiApiKey`/`GeminiModel`
opcionales y migra los esquemas 1/2 conservando los datos. El esquema 2 incorporó
`NotificationsEnabled` (activado por defecto). Al desactivarlo,
el monitor sigue consultando tarjetas sin enviar ni registrar avisos. Los fallos no reinician datos. Las páginas
y el monitor ejecutan el IO SQLite síncrono fuera del hilo UI. `DesktopNotificationService`
encapsula las notificaciones de Windows y usa `App.GetAssetPath` para el icono.

`AppLog` configura Serilog File + Async: texto `.log` estilo Spring Boot en `%LOCALAPPDATA%\Trello Notifier\Logs`,
rotación diaria/5 MiB, 14 archivos y cola no bloqueante de 1000 eventos. `App`
inicializa y vacía el logger; `CheckId` correlaciona el monitor. Registrar errores
con `AppLog.WriteFailure` (tipo/código/métodos sin mensajes ni rutas), no pasar
excepciones completas a Serilog. `TRELLO_NOTIFIER_LOG_LEVEL=Debug` habilita detalle.

## Restricciones que suelen importar
Reutilizar antes de extender o crear. Mantener modelos sin dependencias de UI o
servicios y calendario sin IO. No duplicar selección de tarjetas, intervalos,
mapeo de textos, llamadas HTTP ni acceso a JSON en las páginas. Conservar
formatos persistidos y contratos de API. No cambiar versiones ni introducir
interfaces/capas para tareas que no lo requieran. Las fechas son
`DateTimeOffset`; usar hora controlada en tests de reglas.

Nunca proporcionar tokens reales a mocks. Las credenciales locales no están
cifradas: no leer ni adjuntar los archivos de datos o logs. No cargar binarios,
generados, `bin/`, `obj/`, `artifacts/` ni configuraciones de IDE como contexto.

## Comandos y lectura selectiva
- Validación completa: `powershell -NoProfile -File .\verify.ps1`.
- Pruebas rápidas: `dotnet run --project .\TrelloNotifier.Tests\TrelloNotifier.Tests.csproj`.
- Build app: `dotnet build .\TrelloNotifier\TrelloNotifier.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win10-x64`.

Leer solo según la tarea: [arquitectura](../docs/ARCHITECTURE.md),
[ubicaciones](../docs/PROJECT_STRUCTURE.md), [código](../docs/CODING_STANDARDS.md),
[tests/CI](../docs/TESTING.md), [seguridad](../docs/SECURITY.md),
[API](../docs/API_GUIDELINES.md), [deuda previa](../docs/KNOWN_ISSUES.md).
Proceso: [workflow](workflow.md); cierre: [checklist](review-checklist.md);
nueva tarea: [plantilla](task-template.md). No cargar todos estos documentos
por defecto.
