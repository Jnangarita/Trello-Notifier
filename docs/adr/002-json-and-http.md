# ADR 002 — API directa y estado local JSON

Status: Accepted para API directa; persistencia local sustituida por [ADR 004](004-sqlite-storage.md).

## Context
La app consulta tarjetas de la cuenta y debe conservar preferencias e historial
para no reiniciar los intervalos de aviso al cerrar el proceso.

## Decision
`TrelloApiClient` usa `HttpClient` y `System.Text.Json` directamente.
`TrelloCard` representa el payload. `SettingsStore` guarda JSON en LocalApplicationData
y migra el historial antiguo de lista a diccionario de fechas. No hay backend/ORM.

## Reason
README describe consulta directa, pruebas con URL mock y persistencia del historial.
El comentario de migración explica que evita una ráfaga de avisos. No consta
por qué se eligió JSON en lugar de otra persistencia.

## Consequences
Pocas dependencias, contratos JSON que requieren compatibilidad y archivos locales
sin transacciones. Credenciales actuales sin cifrar. Cambiar formato, almacenamiento
o transporte exige pruebas específicas y no es parte de una preparación documental.
