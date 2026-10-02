# Consumo de API

No se expone una API de servidor. `Services/TrelloApiClient.cs` es el único
cliente de tarjetas; reutilizarlo en UI y monitor.

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

La UI de prueba de conexión muestra errores en InfoBar; el monitor los publica
en `MonitorSnapshot.HasError`. Un fallo de API no debe consumir el historial
de notificaciones. No agregar un segundo cliente ni retries que multipliquen
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
