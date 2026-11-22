# Consumo de API

No se expone una API de servidor. `Services/TrelloApiClient.cs` es el único
cliente de escritura de tarjetas en Trello; reutilizarlo en UI y monitor.
`GeminiCardClient` solo genera propuestas y no tiene acceso al cliente Trello.

| Aspecto | Implementación actual |
| --- | --- |
| Endpoint | `GET {ApiBaseUrl}/1/members/me/cards` |
| Query | `filter=open&fields=id,name,idBoard,idList,due,dueComplete,closed,url` para monitor/conexión; `filter=all` con los mismos campos para Historial |
| Nombres de tableros y listas | `GET {ApiBaseUrl}/1/members/me/boards?filter=all&fields=name&lists=all`, una vez por consulta si hay tarjetas con `idBoard`; incluye listas archivadas |
| Autenticación | `key` y `token` opcionales en query, escapados con `Uri.EscapeDataString` |
| Requisito de credenciales | UI/modelo las requieren para `api.trello.com`; mocks pueden omitirlas |
| HTTP | Una instancia `HttpClient` por `TrelloApiClient`, timeout 30 s |
| Cancelación | `CancellationToken` en solicitud y lectura JSON |
| Serialización | `ReadFromJsonAsync<List<T>>`, `System.Text.Json`; tarjetas y tableros comparten autenticación y manejo HTTP |
| DTO | `Models/TrelloCard.cs` (`id`, `name`, `idBoard`, `idList`, `due`, `dueComplete`, `closed`, `url`), `Models/TrelloBoard.cs` (`id`, `name`, `lists`) y `Models/TrelloList.cs` (`id`, `name`) |
| Fechas | `DateTimeOffset? Due`; presentación convierte a hora local |
| Respuesta vacía JSON | Un resultado deserializado `null` se convierte a lista vacía; JSON inválido lanza excepción |
| Status HTTP | No-2xx produce `HttpRequestException` con código/motivo; no hay tratamiento específico 401/403/429 |
| Retries | No hay reintentos HTTP internos/backoff; un próximo ciclo vuelve a consultar si sigue activo |
| Versionamiento | `/1/` es parte de la ruta existente; no alterar por cambios de documentación |

## Creación manual

`CreateCardPage` reutiliza `TrelloApiClient`; la autenticación, timeout, cancelación,
serialización y logging son compartidos con las consultas. El cliente usa un envío
común para GET y POST. Los errores HTTP conservan `StatusCode` sin exponer cuerpos
ni URLs autenticadas. No hay reintentos automáticos de escrituras.
Las solicitudes exitosas, tanto GET como POST, se registran en Information con método,
ruta permitida sin IDs ni query, código HTTP y duración; son visibles con el nivel predeterminado.

- `GET /1/members/me/boards?filter=open&fields=name&lists=open`: destinos disponibles.
- `GET /1/boards/{id}/members?fields=fullName,username`: responsables del tablero.
- `GET /1/boards/{id}/labels?fields=name,color&limit=1000`: etiquetas del tablero.
- `POST /1/cards`: formulario URL-encoded con `name`, `desc`, `idList`, `idMembers`,
  `idLabels` y `due` opcional en ISO 8601 con offset. Requiere permiso de escritura.
- `POST /1/checklists`: `idCard` y `name`.
- `POST /1/checklists/{id}/checkItems`: `name`, `checked=false`, `pos=bottom`.

Los miembros y etiquetas se consultan en paralelo al seleccionar un tablero y se
descartan al cambiarlo. La tarjeta se crea antes de sus checklists; un fallo posterior
se informa como creación parcial, conservando el enlace de la tarjeta en la vista.
Un timeout/respuesta inválida del POST inicial se trata como resultado incierto y
no permite repetir el envío desde el mismo formulario. 400/401/403/404/429 sin una
tarjeta confirmada permiten corregir y reintentar. No se persisten borradores.

La creación masiva reutiliza estos endpoints de tableros, miembros y tarjetas, sin
otra implementación HTTP. Envía una tarjeta a la vez, espaciando solicitudes 500 ms,
y se detiene al primer fallo. Solo las pendientes o rechazadas explícitamente pueden
reenviarse por acción del usuario; creadas o inciertas no. Detener espera la respuesta
de la tarjeta en curso para evitar incertidumbre por una cancelación a mitad del POST.
El CSV no modifica las reglas de recordatorios ni añade peticiones desde los filtros.

La UI de prueba de conexión muestra errores en InfoBar; el monitor los publica
en `MonitorSnapshot.HasError`. Un fallo de API no debe consumir el historial
de notificaciones. No agregar un segundo cliente Trello ni retries que multipliquen
solicitudes sin evaluar el intervalo del monitor y los límites del proveedor.

`GetAllCardsAsync` reutiliza el mismo flujo HTTP y enriquecimiento de tableros que
`GetOpenCardsAsync`. Solo el historial lo invoca al entrar o actualizar; se cancela
al salir de la página. La selección local conserva completadas o archivadas
asignadas a la cuenta, sin consultar acciones ni persistir una copia histórica.
`closed` y `dueComplete` son independientes; si falta `closed`, su valor es `false`
para conservar compatibilidad con mocks anteriores. Las archivadas no son
candidatas a recordatorios incluso si un servidor simulado las devuelve con `filter=open`.

`TrelloCard` sirve como DTO y modelo interno; no hay mapper de transporte.
`TrelloCardViewModel` transforma a textos visibles. Nuevos campos deben seguir
el naming C# PascalCase y nombres externos exactos con atributos cuando corresponda.
Preservar compatibilidad de payloads, fechas nulas y filtro de completadas.

El cliente asigna `TrelloCard.BoardName` (interno, ignorado por JSON) usando los
tableros consultados por ID. Sin nombre disponible, el selector muestra el ID.
Tarjetas sin `idBoard` siguen visibles en Todos los tableros y no generan una
opción vacía. Los errores HTTP/JSON de cualquiera de las dos rutas se propagan
por el mismo mecanismo existente. Cambiar filtros no llama a la API.

La misma respuesta de tableros incluye `lists` y permite resolver `idList` a
`TrelloCard.ListName` (interno, ignorado por JSON), sin solicitudes por tarjeta.
La columna Lista se muestra después de Tablero; en modo compacto aparece debajo
del tablero. Sin nombre se muestra el ID, y sin ambos, «Lista no disponible».
Los mocks anteriores que omiten `idList` o `lists` siguen siendo compatibles.

El cliente no valida un allowlist de hosts ni impide HTTP; cualquier credencial
presente se envía al host configurado. No registrar URL completa y usar mocks
sin secretos. Consultar [SECURITY.md](SECURITY.md) antes de cambiar autenticación,
configuración o apertura de enlaces. Ejemplo de mock y prueba manual en README.

## Propuestas de Gemini

- `GeminiCardClient` compartido en `AppServices`, basado en `HttpClient`, sin SDK adicional.
- Endpoint fijo HTTPS: `POST https://generativelanguage.googleapis.com/v1beta/models/{modelo}:generateContent`.
  Modelo restringido a identificadores `gemini-*`, sin rutas ni query; redirecciones desactivadas.
- Autenticación por `x-goog-api-key`; nunca utiliza la key/token de Trello.
- `system_instruction` separa el contrato del texto de usuario en `contents`.
  `generationConfig.response_mime_type=application/json` y `response_schema` definen
  `cards[]` con `title`, `description`, `due`, `dateIssue`, `checklists`.
  El esquema usa tipos OpenAPI `OBJECT`/`ARRAY`/`STRING` y `nullable: true` para
  vencimientos ausentes, siguiendo el formato REST del ejemplo de Postman.
  La clave es opaca (admite puntos); se rechazan espacios y caracteres no imprimibles.
  El esquema enviado mantiene tipos, propiedades, campos requeridos y nulabilidad;
  los límites de longitud/cantidad se aplican localmente para reducir complejidad del
  esquema frente al formato simple comprobado en Postman.
- Se exige un candidato terminado con `STOP`; se rechazan bloqueos, truncado, JSON
  inválido, claves adicionales (incluidos IDs), listas vacías y tipos/tamaños incorrectos.
- Límites locales: 20 000 caracteres de entrada, 1 propuesta individual / 50 por lote,
  1 MiB de respuesta (también sin Content-Length), 60 segundos incluyendo la lectura,
  16 384 tokens de salida solicitados. Título 200, descripción 10 000 y observación 500
  caracteres; hasta 10 checklists individuales con 100 elementos de 500 caracteres.
- No se envían catálogos ni credenciales de Trello. Las referencias relativas reciben
  fecha actual con offset y zona Windows; fechas ambiguas o inconsistentes con esa zona
  quedan vacías con una observación. Se requiere revisión humana de la interpretación.
- La respuesta se convierte directamente en borradores/filas. `CardBatchPreparer`
  comparte validación de fechas y preparación de filas con CSV; los responsables se
  validan contra el tablero. El lote no acepta checklists ni etiquetas de Gemini.
- No hay reintentos HTTP automáticos. Los mensajes distinguen clave/permisos, modelo,
  cuota, timeout y respuesta inválida sin incluir cuerpos del proveedor. La UI cancela
  al salir y conserva el borrador anterior al fallar/cancelar; solo los botones de
  creación existentes escriben en Trello.

Para errores HTTP se lee un máximo de 32 KiB, incluso sin Content-Length. Se traducen
solo razones conocidas de `error.details[].reason`, parámetros permitidos de
`fieldViolations`/`error.message` y `FAILED_PRECONDITION` a mensajes constantes.
No se propagan cuerpos, metadatos ni mensajes originales en excepciones o logs. Si el
detalle es desconocido, inválido o excesivo se conserva el código HTTP con un aviso
genérico. La captura de un HTTP 400 por sí sola no confirma un fallo del esquema.

Contrato y modelo consultados el 17/11/2026:
[salida estructurada](https://ai.google.dev/gemini-api/docs/structured-output) y
[Gemini 3.5 Flash-Lite](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite).
Este último figura como estable y admite JSON estructurado. Su disponibilidad y cuota
en una cuenta concreta deben comprobarse con esa cuenta; el modelo es configurable.
