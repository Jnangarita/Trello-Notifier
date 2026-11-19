# Plan: integrar Gemini en las vistas de creación

## Objetivo y alcance

Integrar Gemini en las vistas actuales para convertir texto pegado por el usuario
en propuestas de tarjetas de Trello. El usuario revisará y editará las propuestas
antes de crearlas y elegirá el tablero y la lista en cada operación.

Implementación incorporada el 17/11/2026. Los resultados de validación y los controles
pendientes por entorno se detallan al final y en `docs/TESTING.md`.

## Base existente

- `TrelloNotifier/CreateCardPage.xaml` y su code-behind: creación individual con
  Markdown, responsables, etiquetas, vencimiento y checklists.
- `TrelloNotifier/BulkCreateCardPage.xaml` y su code-behind: importación CSV,
  revisión, creación secuencial, detención y resultados por fila.
- `Models/TrelloCardDraft.cs`: borrador y validación de tarjetas.
- `Models/CardImportRow.cs`: estados y resultados de creación por lote.
- `Services/TrelloApiClient.cs`: consulta de destinos y creación de tarjetas y checklists.
- `Services/CardCsvImporter.cs`: lectura, preparación y exportación del flujo CSV.
- `SettingsPage`, `AppSettings`, `SettingsStore` y `AppServices`: configuración,
  persistencia y servicios compartidos.

## 1. Configuración de Gemini

- Añadir campos para la API key y el modelo en la configuración existente.
- Verificar el contrato vigente de Gemini y la disponibilidad del modelo antes
  de elegir el valor predeterminado.
- Extender `AppSettings` y `SettingsStore`, con una migración desde el esquema
  actual que conserve preferencias e historial y respete instalaciones nuevas
  y migraciones anteriores.
- Mantener opcional Gemini: la creación manual y CSV deben funcionar sin configurarlo.
- No incluir claves reales en código, documentación, pruebas o logs.
- Documentar el tratamiento de la credencial: la persistencia actual no cifra
  los secretos y un PasswordBox solo oculta su visualización.

## 2. Servicio compartido de Gemini

- Añadir un cliente dedicado en `Services/`, compartido mediante `AppServices`.
- Seguir el patrón HTTP del proyecto, con timeout y cancelación; verificar si
  basta con `HttpClient` antes de incorporar dependencias.
- Solicitar respuestas estructuradas y validarlas antes de convertirlas en
  borradores. No hacer llamadas HTTP ni analizar JSON en las páginas.
- Limitar el tamaño del texto, el número de propuestas y la respuesta aceptada;
  definir límites explícitos acordes con el modelo y la interfaz.
- Tratar el texto pegado como datos de entrada y los resultados como propuestas,
  nunca como instrucciones para ejecutar operaciones directamente en Trello.
- No inventar vencimientos. Resolver referencias relativas solo con fecha y zona
  horaria explícitas; señalar ambigüedades para revisión.
- No aceptar identificadores inventados de listas, miembros o etiquetas:
  el destino y las asignaciones se validan contra los datos de Trello.
- Manejar respuestas vacías, bloqueadas, truncadas o inválidas, errores de
  autenticación, límites de solicitudes y fallos de red con mensajes comprensibles.
- Informar que el texto se envía a Gemini. No enviar credenciales de Trello ni
  registrar texto, respuestas privadas o claves en logs; reutilizar `AppLog.WriteFailure`.

## 3. Integración en Crear tarjeta

- Añadir una sección «Completar con Gemini» dentro de `CreateCardPage`.
- Permitir pegar instrucciones y generar una propuesta de título, descripción,
  checklists y vencimiento cuando corresponda.
- Permitir aplicar la propuesta al formulario existente sin perder cambios
  manuales silenciosamente.
- Conservar los controles de tablero, lista, responsables y etiquetas.
- Revisar y editar con los campos actuales; crear únicamente mediante el botón
  «Crear tarjeta» y el cliente Trello existente.
- Cancelar o descartar resultados al salir de la vista e impedir generaciones
  concurrentes o aplicación de respuestas obsoletas.

## 4. Integración en Crear tarjetas en lote

- Añadir «Texto con IA» como origen junto al CSV en `BulkCreateCardPage`.
- Permitir pegar pendientes y pulsar «Generar tareas».
- Convertir las propuestas a borradores y filas compatibles con el flujo actual,
  sin generar un CSV intermedio artificial.
- Reutilizar los selectores de tablero, lista y responsables del lote.
- Llevar las propuestas al paso existente «Revisar tarjetas».
- Ampliar la revisión para editar título, descripción y vencimiento, y excluir
  propuestas individualmente antes del primer envío. Actualmente la corrección
  se realiza reemplazando el CSV.
- Revalidar los cambios antes de habilitar la creación.
- Reutilizar las reglas comunes de preparación; si es necesario extraer lógica
  independiente del CSV, hacerlo de forma mínima y sin duplicar validaciones.
- Mantener inicialmente los campos soportados por el lote. Los checklists y
  etiquetas generados corresponden al formulario individual; su creación masiva
  requeriría ampliar expresamente el flujo de resultados parciales.
- Conservar la importación CSV y la exportación de resultados existentes.

## 5. Creación y estados

- Mantener `TrelloApiClient` como único cliente de escritura en Trello.
- Reutilizar el envío secuencial y espaciado, la detención entre solicitudes y
  los estados de `CardImportRow`.
- No reenviar tarjetas creadas ni solicitudes con resultado incierto.
- No reintentar automáticamente una creación tras un timeout.
- Conservar los resultados parciales y su exportación; mantener la indicación
  de que los borradores y resultados permanecen en memoria mientras dure la vista.
- Bloquear modificaciones del lote una vez iniciado el envío, de acuerdo con
  las restricciones actuales.

## Archivos previstos

| Área | Archivos o componentes |
| --- | --- |
| Configuración | `SettingsPage.xaml`, `SettingsPage.xaml.cs`, `Models/AppSettings.cs`, `Services/SettingsStore.cs` |
| Servicios | Nuevo cliente Gemini en `Services/`, registro en `AppServices.cs` |
| Creación individual | `CreateCardPage.xaml`, `CreateCardPage.xaml.cs` |
| Creación masiva | `BulkCreateCardPage.xaml`, `BulkCreateCardPage.xaml.cs` |
| Modelos y preparación | Reutilizar `TrelloCardDraft`, `CardImportRow` y las reglas existentes; extender solo lo necesario |
| Pruebas | Suite existente y dobles locales siguiendo `TrelloNotifier.Tests/AGENTS.md` |
| Documentación | README y guías afectadas de API, seguridad y pruebas |

Los nombres de nuevos tipos y el conjunto definitivo de archivos se concretarán
tras revisar las instrucciones y pruebas aplicables antes de implementar.

## Validación y criterios de aceptación

- [x] Configuración opcional de Gemini y migración sin pérdida de preferencias ni historial.
- [x] Generación individual aplicada a campos editables del formulario actual.
- [x] Generación de múltiples tareas desde texto en la vista de lotes existente.
- [x] Edición y exclusión de propuestas antes de crear.
- [x] Selección de tablero y lista en cada operación.
- [x] Validación de respuestas inválidas, vacías, excesivas y fechas ambiguas.
- [x] Cancelación y descarte de respuestas obsoletas sin perder borradores válidos.
- [x] Flujo manual y CSV conservados, incluyendo validaciones y resultados parciales.
- [x] No reenvío de tarjetas creadas o inciertas; errores sin exposición de secretos.
- [x] Pruebas con credenciales ficticias, HTTP simulado y almacenamiento temporal.
- [ ] Ejecutar `powershell -NoProfile -File .\verify.ps1` desde la raíz del proyecto.
- [ ] Revisión manual WinUI: ambas vistas, edición, navegación, estados de carga,
  errores, temas, escalado y tamaño de ventana.
- [x] Actualizar documentación y registrar resultados reales de validación.

Resultados: build de producción sin advertencias/errores; 67 casos pasaron en un
runner temporal .NET 6 que enlaza las fuentes reales, incluidos los guards de
arquitectura. Host WinUI con HTTP simulado: configuración, generación individual,
confirmación de reemplazo, cancelación/navegación, lote, edición validada, exclusión,
reintento y temas claro/oscuro en ventanas amplia/compacta. La validación completa
con `verify.ps1` se intentó y está bloqueada por falta del SDK .NET 9. Pendientes la
revisión manual de alto contraste y escalado de Windows y una prueba con Gemini
en la cuenta del usuario. Modelo predeterminado verificado en documentación:
`gemini-3.5-flash-lite`; no se hicieron solicitudes a la API real.

Ajuste del 18/11/2026: petición alineada con el ejemplo REST de Postman mediante
`system_instruction`, `generationConfig.response_mime_type` y `response_schema`;
autenticación con `x-goog-api-key`, `Content-Type: application/json` y el mismo modelo.
El esquema añade checklists/observaciones para cubrir el plan y permite `null` en
vencimientos. La validación de claves admite formatos opacos con puntos. Las pruebas
usan exclusivamente valores ficticios.

## Orden de implementación

1. Revisar instrucciones aplicables, contrato Gemini y pruebas relacionadas.
2. Incorporar configuración, persistencia y cliente Gemini con sus pruebas.
3. Integrar generación individual en el formulario existente.
4. Integrar generación masiva y edición de propuestas en la revisión actual.
5. Completar pruebas de regresión, documentación y verificación del proyecto.
