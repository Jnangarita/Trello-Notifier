# Convenciones del código existente

- C#: namespaces de archivo `TrelloNotifier`, `.Models`, `.Services`;
  cuatro espacios y llaves en línea propia. Una clase principal por archivo.
- Tipos/métodos/propiedades en PascalCase, parámetros/locales en camelCase,
  campos privados `_camelCase`. Se mezclan tipos explícitos y `var` cuando la
  construcción hace evidente el tipo; mantener el contexto local.
- Modelos `public sealed`, servicios `internal sealed`, utilidades de reglas
  `internal static`, páginas `public sealed partial`. No generalizar accesibilidad.
- Handlers XAML usan `Accion_Evento` (`Save_Click`); métodos async terminan
  en `Async` excepto handlers. No renombrar handlers sin necesidad.
- `Nullable` e `ImplicitUsings` habilitados. Usar `?` para ausencias reales,
  strings inicializados y colecciones vacías. `Due!.Value` solo tras comprobar
  que la tarjeta tiene fecha; el calendario filtra antes de mapear/notificar.
- `MonitorSnapshot` es un record; viewmodel tiene propiedades de solo lectura.
  Configuración y DTO son mutables por serialización/edición; no cambiar contratos.
- Textos visibles, comentarios y nombres de pruebas en español; símbolos C# en inglés.

## Errores y logging
HTTP no exitoso produce `HttpRequestException`. El monitor publica errores en
`MonitorSnapshot`; la UI usa `MainPage.ShowMessage`/InfoBar. La cancelación
esperada no es un fallo de negocio. Persistencia informa errores mediante
`IOException` y no reinicia datos ante fallos de SQLite o de importación JSON.
No agregar catches vacíos generalizados ni cambiar esta política inadvertidamente.
No hay framework de logging; `App.OnUnhandledException` escribe `crash.log`.
No registrar URL autenticada, configuración ni payloads con información privada.
La captura completa de excepciones actual es deuda documentada en `KNOWN_ISSUES.md`.

## Funciones, constantes, DTOs y mapeo
Mantener funciones enfocadas y parámetros explícitos; las reglas temporales
reciben `DateTimeOffset now`. Defaults viven en `AppSettings`; constantes locales
de una responsabilidad no justifican un helper global. Los intervalos existentes
son parte del comportamiento, no una oportunidad de refactor automático.
`JsonPropertyName` conserva nombres de Trello (`dueComplete`, etc.).
`TrelloCard` también se usa internamente; el constructor de `TrelloCardViewModel`
ya realiza el mapeo de presentación. Comentar motivos/invariantes, no cada línea.

## Concurrencia
Usar `Task`, `async/await` y propagar `CancellationToken` en HTTP y espera.
`async void` solo para eventos de UI. No usar `.Result`/`.Wait()` en UI.
Mantener la exclusión de `DueCardMonitor` con `SemaphoreSlim` y el `finally`
que libera el bloqueo. UI se actualiza con `DispatcherQueue`. Suscribirse y
desuscribirse de `Updated` en Loaded/Unloaded. No duplicar timers o bucles.
Revisar explícitamente Stop/Dispose si se cambia el ciclo de vida.

## Duplicate code prevention
Antes de escribir lógica nueva buscar utilities, extensions, validators, mappers,
formatters, repositories, services y components existentes, aunque aquí no
tengan carpetas con esos nombres. Reutilizar o extender lo que cubra la responsabilidad:
`ReminderSchedule` (selección/clave/repetición), `SettingsStore` (SQLite/migración JSON),
`TrelloApiClient` (HTTP), `TrelloCardViewModel` (textos),
`SettingsPage.TryBuildSettings` (validación UI), `Styles.xaml` (presentación).
No extraer abstracciones por coincidencias superficiales. Revisar los duplicados
previos documentados, pero corregirlos solo cuando la tarea lo requiera.

## Herramientas
`.editorconfig` refleja el formato presente; `dotnet format` verifica sin escribir.
Lint comprueba imports (IDE0005). Los analizadores del SDK 9
y reglas concretas de seguridad corren en build; no se instala otro linter.
