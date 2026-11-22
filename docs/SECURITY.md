# Seguridad

## Secretos y configuración
La app solicita API key y token de Trello: lectura para el monitor y lectura/escritura
para crear tarjetas manualmente o por CSV. No incorpora un flujo
OAuth propio ni autorización local por roles: Trello determina qué tarjetas
puede leer la cuenta. Usar mínimo privilegio; revocar tokens comprometidos.
El ejemplo del README permite tokens sin caducidad, lo cual requiere custodia.

`SettingsStore` guarda `trello-notifier.db` bajo `%LOCALAPPDATA%\Trello Notifier`.
Los anteriores `settings.json` y `notified-cards.json` se importan una sola vez
y se conservan como respaldo. Las credenciales **no están cifradas**; el
PasswordBox solo oculta su visualización. API key y token se muestran ocultos por
defecto; cada campo tiene un botón Mostrar/Ocultar independiente. El historial contiene identificadores
y fechas. No añadir esos archivos a Git, logs, fixtures ni contexto IA.
`.gitignore`/`.ignore` reducen accidentes, pero no protegen el archivo en disco.
Todas las consultas SQLite con valores usan parámetros. Los errores del motor
se traducen sin incluir SQL, valores ni excepciones internas. Excluir también
los archivos auxiliares `.db-journal`, `.db-wal` y `.db-shm`.

## Fronteras externas
- Gemini es opcional y utiliza una clave independiente, también sin cifrar en SQLite.
  Su `PasswordBox` solo oculta el valor en pantalla. El texto pegado se envía a Google
  únicamente al generar, con fecha/zona horaria y contrato estructurado; no se envían
  la configuración ni catálogos de Trello. Los datos pegados pueden ser privados:
  la UI informa del envío antes de generar. No registrar texto, respuestas ni claves.
- `GeminiCardClient` fija host HTTPS, desactiva redirecciones y valida el identificador
  del modelo y la cabecera de autenticación. Limita entrada, bytes de respuesta y
  duración incluso durante la lectura. Valida JSON y rechaza campos adicionales/IDs;
  las propuestas no ejecutan acciones y se revisan antes de escribir en Trello.
  No hay herramientas/function calling, navegación de URLs ni ejecución de Markdown.
  Las pruebas inyectan `HttpMessageHandler` local con credenciales ficticias.
- La URL configurable acepta HTTP/HTTPS. El cliente añade key/token si no están
  vacíos, también para hosts alternativos. Usar HTTPS con Trello; vaciar credenciales
  antes de probar un mock. No desactivar validación TLS.
- `SettingsPage.TryBuildSettings` valida URL e intervalos; `AppSettings.IsConfigured`
  verifica una parte de esas condiciones. La migración rechaza JSON malformado
  o campos obligatorios nulos, pero conserva los valores de intervalos anteriores;
  no incorpora una nueva política de validación de preferencias.
- `TrelloCard` se deserializa desde datos externos. Revisar campos ausentes,
  fechas y URLs en cualquier cambio de ese flujo; no asumir confianza por ser JSON.
- Dashboard permite abrir enlaces HTTP/HTTPS; activación de notificaciones limita
  el host a Trello pero no aplica la misma restricción de esquema. La diferencia
  es deuda previa, no una garantía de validación unificada.

## Información sensible y errores

Los logs se consultan directamente en los archivos generados por `AppLog`.
Las pruebas del logger usan datos ficticios; no cargar logs reales en contexto de IA.

La importación CSV lee solo el archivo elegido por el usuario, comprueba el límite de
2 MiB antes de cargarlo y admite hasta 1000 filas. No se registran ruta, contenido ni
nombres de tarjetas en logs. La exportación escapa comillas/separadores y antepone un
apóstrofo a celdas que podrían interpretarse como fórmulas. Las descripciones se envían
como texto Markdown, sin ejecutarse como HTML o código. Los enlaces de resultados y
de creación manual se limitan a HTTPS de Trello mediante `TrelloCard.TrelloUri`.
No registrar contraseñas, access tokens, query strings autenticadas, nombres de
tarjetas privadas ni dumps de configuración. La API usa credenciales en query:
excluir URL completa en diagnósticos. `AppLog.WriteFailure` usa una lista permitida
de propiedades técnicas (operación constante, tipo y HResult). Entrega a Serilog una
copia saneada de la excepción y sus interiores, incluidas ramas de AggregateException,
limitada a 16 niveles: mensajes sustituidos por tipo/código, sin Data ni propiedades
privadas. Las trazas se generan con `StackTrace(exception, false)`, sin rutas ni líneas
de archivos, y el runtime/Serilog formatea las excepciones. Nunca se retiene la excepción
original en el evento. Serilog escribe texto `.log` en LocalAppData
con rotación solo por tamaño (5 MiB), retención de 3 archivos de la nueva serie y cola limitada; su diagnóstico interno
se sustituye por una advertencia fija para no revelar rutas o excepciones.
Los archivos `*.log` y los antiguos `trello-notifier-*.jsonl` están excluidos de Git y búsquedas.
Ya no se escribe `crash.log`; las copias antiguas pueden contener excepciones
completas y la UI todavía muestra algunos `ex.Message`. No compartir datos reales.
Las notificaciones muestran nombres de tarjetas, visibles en el escritorio.
No agregar telemetría o payloads de log sin necesidad funcional.

## Validaciones y alcance
`verify.ps1` aplica analizadores del SDK para riesgos concretos (TLS/cifrado/SQL)
y consulta vulnerabilidades NuGet incluyendo transitivas; falla si la auditoría
no puede completarse o detecta vulnerabilidades. Una auditoría limpia no garantiza
ausencia de fallos ni soporte vigente del runtime. No hay un escáner de secretos
especializado ni análisis dinámico: revisar el diff manualmente por credenciales,
datos de usuario, logging, URLs y validación externa antes de finalizar.
Si aparece un secreto real, no reproducirlo; retirarlo del cambio y avisar de
la necesidad de revocación. Los hallazgos previos, sin valores sensibles, están en
[KNOWN_ISSUES.md](KNOWN_ISSUES.md).
