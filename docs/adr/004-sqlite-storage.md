# ADR 004 — Persistencia local SQLite

Status: Accepted

## Context

Se solicita sustituir los JSON de configuración e historial actuales por SQLite,
conservando datos e intervalos de recordatorio. El horario laboral configurable
queda fuera de este cambio.

## Decision

- Reutilizar `SettingsStore` y sus contratos; Microsoft.Data.Sqlite 10.0.12 es el
  proveedor ligero con SQLite nativo incluido. Su destino .NET Standard 2.0
  permite usarlo en la app .NET 6 y en el runner .NET 9, sin ORM ni servidor.
- Guardar columnas en `AppSettings` (fila única) y `NotificationHistory` (clave
  existente de tarjeta/vencimiento y fecha ISO 8601 round-trip con offset/ticks).
- Crear `%LOCALAPPDATA%\Trello Notifier\trello-notifier.db` al primer acceso.
  La creación, importación de ambos JSON y `PRAGMA user_version = 1` se confirman
  en una sola transacción inmediata, volviendo a consultar la versión tras
  adquirir el bloqueo. Un fallo revierte también las tablas; una versión
  desconocida se rechaza. No se sobrescribe ni elimina una base dañada.
- Importar preferencias parciales con sus defaults y los dos formatos de
  historial existentes. El formato de lista recibe una única hora de migración.
  Los JSON originales se conservan sin modificación y no se leen tras confirmar
  la migración, incluso si posteriormente se vacía el historial.
- Usar SQL parametrizado, reemplazo atómico de historial, conexiones por
  operación sin pooling y timeout de bloqueo de 5 segundos. No se mantiene una
  conexión compartida entre hilos. Los consumidores ejecutan el IO fuera de UI.
- Traducir fallos SQLite a errores de almacenamiento sin detalles SQL ni valores;
  la UI y el monitor los muestran y permiten reintento.

## Consequences

Se añade una dependencia administrada/nativa que debe publicarse para Windows
x64 y auditarse junto con sus transitivas. Las pruebas existentes usan SQLite
real en carpetas temporales y mantienen dobles únicamente para HTTP/avisos.
SQLite es la fuente persistente definitiva; volver a una versión JSON no
recupera cambios posteriores a la migración. Las credenciales siguen sin cifrar.
Las transacciones protegen escrituras locales, pero no convierten el envío de
notificaciones Windows y su persistencia en una operación atómica conjunta.
