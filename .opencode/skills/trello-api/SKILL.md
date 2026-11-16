---
name: trello-api
description: Implementar o diagnosticar la integración de Trello Notifier con la API de Trello, incluidos permisos, autenticación, tarjetas, checklists, errores HTTP y reintentos seguros.
---

# Integración con la API de Trello

## Contexto y fuentes

Aplica [AGENTS.md](../../../AGENTS.md) y el
[contexto mínimo](../../../.ai/context.md), sin releer instrucciones ya cargadas
que no hayan cambiado. Lee la operación afectada de
[TrelloApiClient](../../../TrelloNotifier/Services/TrelloApiClient.cs), sus modelos,
consumidores y pruebas relacionados.

Usa [API_GUIDELINES.md](../../../docs/API_GUIDELINES.md) para los contratos del
proyecto y [SECURITY.md](../../../docs/SECURITY.md) para autenticación, datos externos
y diagnóstico. Consulta [arquitectura](../../../docs/ARCHITECTURE.md) si cambia la
ubicación de responsabilidades. Las rutas de comandos parten de la raíz del repositorio.

Cuando la tarea dependa de detalles del proveedor, verifica la documentación
oficial de la operación concreta; no inventes parámetros, scopes ni respuestas:

- [Referencia REST](https://developer.atlassian.com/cloud/trello/rest/).
- [Trello Auth: API key y token](https://developer.atlassian.com/cloud/trello/guides/rest-api/authorization/).
- [Tokens](https://developer.atlassian.com/cloud/trello/rest/api-group-tokens/).
- [Tableros y membresías](https://developer.atlassian.com/cloud/trello/rest/api-group-boards/).
- [Tarjetas](https://developer.atlassian.com/cloud/trello/rest/api-group-cards/).
- [Límites de solicitudes](https://developer.atlassian.com/cloud/trello/guides/rest-api/rate-limits/).

## Procedimiento

1. **Identificar el flujo.** Determina si se trata de consulta, escritura,
   autenticación o diagnóstico. Revisa el endpoint, campos, transporte y manejo de
   respuesta actuales antes de cambiar código.
2. **Reutilizar el cliente compartido.** Extiende `TrelloApiClient` y su mecanismo
   común de envío. Conserva autenticación, escape de parámetros, timeout,
   cancelación y logging existentes. No crees clientes HTTP en las páginas.
3. **Respetar los contratos.** Mantén atributos JSON y compatibilidad con campos
   opcionales o nulos. Conserva `DateTimeOffset` y offsets en el transporte;
   convierte a hora local solo para mostrar. Reutiliza validadores de borradores,
   reglas de importación y `TrelloCard.TrelloUri` donde correspondan.
4. **Interpretar el resultado.** Distingue respuesta HTTP rechazada, fallo de red,
   timeout, JSON inválido y resultado parcial. Preserva el código HTTP sin exponer
   URLs autenticadas ni respuestas crudas. No atribuyas un mensaje genérico a
   permisos sin evidencia del servidor.
5. **Preservar la seguridad de las escrituras.** Reutiliza el clasificador de
   rechazos confirmados y los estados existentes. Un timeout o una respuesta
   inválida tras un POST pueden ocurrir después de crear la tarjeta: no reenvíes
   automáticamente. Conserva tarjetas ya creadas, resultados inciertos y éxitos
   parciales de checklists. Mantén envío secuencial, parada y reanudación del lote.
6. **Evitar consultas innecesarias.** Reutiliza cargas compartidas de tableros,
   listas y miembros. Los filtros locales no deben generar solicitudes nuevas.
   Ante límites de Trello, respeta su respuesta y las reglas actuales de reintento;
   no añadas bucles de reintentos ilimitados ni automáticos para POST.

## Diagnóstico de permisos

- Primero identifica el mecanismo usado realmente. La app utiliza API key y
  token de Trello Auth; una configuración de scopes OAuth 2.0 no demuestra los
  permisos de ese token. No cambies el esquema de autenticación para resolver
  un problema sin comprobar antes el flujo existente.
- Distingue permisos concedidos al token de la membresía y restricciones del
  usuario en el tablero. Poder consultar un tablero no demuestra poder editarlo.
- Para Trello Auth, `scope=read,write` solicita lectura y escritura al autorizar;
  `dateExpires: null` indica ausencia de caducidad, no permiso de escritura.
- Al consultar `/1/tokens/{token}`, el token de la ruta es el inspeccionado.
  Revisa variables resueltas y autenticación heredada si se usa Postman. Comprueba
  el resultado en vez de asumir que cambiar la integración actualiza un token emitido.
- Usa solo evidencias mínimas sin secretos: método, endpoint anonimizado, código
  HTTP y metadatos de autorización necesarios. No cargues configuración privada
  ni logs reales en contexto de IA. No solicites pegar tokens para diagnosticar.
- Si una comprobación repite el mismo resultado, contrasta el flujo y la evidencia
  antes de repetir instrucciones de generación o afirmar una causa no confirmada.

## Validación y entrega

Sigue [TESTING.md](../../../docs/TESTING.md) y las instrucciones de las pruebas
afectadas. Usa fixtures ficticios y dobles locales; nunca envíes credenciales
reales a mocks ni crees tarjetas reales como parte de pruebas automatizadas.

- Verifica transformación de campos, validaciones, cancelación y respuestas de
  error relevantes. Para creaciones, comprueba que los reintentos no dupliquen
  éxitos ni resultados inciertos y que se conserven los resultados parciales.
- Respeta los límites del runner: enlaza solo las fuentes que le corresponden y
  reutiliza sus dobles; no enlaces el cliente HTTP real donde está sustituido.
  Identifica el alcance de cualquier comprobación adicional aislada.
- Compila también la app WinUI cuando cambien fuentes compartidas. Comprueba el
  SDK requerido por cada proyecto; si falta, informa el bloqueo sin retargetear
  el proyecto ni declarar aprobada una suite que no se ejecutó.
- Actualiza la documentación afectada por cambios de contrato o comportamiento.
  Reporta la causa confirmada, el cambio y los resultados reales de validación;
  separa las hipótesis y limitaciones de lo que se pudo comprobar.
