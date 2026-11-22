# Pruebas y verificación

## Framework y cobertura real
`TrelloNotifier.Tests` es un ejecutable .NET 9 con runner propio en `Program.cs`:
`Run(nombre, Func<Task>)`, `Check(condición, mensaje)`, salida `OK` y excepción
que termina el proceso si falla. **`dotnet test` no ejecuta esta suite.**
No hay xUnit/NUnit ni tests UI en el runner. El cliente HTTP Trello se sustituye;
el cliente Gemini real se enlaza con un `HttpMessageHandler` simulado, sin red.

Los 11 casos originales cubren calendario/límites, intervalos, preferencias,
migración de historial, agrupación, concurrencia entre comprobaciones,
reinicio, avisos rechazados, vencidas, fallo de API y cambios de fecha.
Los casos del monitor son integración local de fuentes reales con SQLite temporal
y dobles, no integración contra Windows ni Trello.
La consulta por estados añade cobertura de clasificación y límites temporales,
presentación de completadas/sin fecha y publicación de todas las asignadas sin
ampliar los avisos. Se comprueba también la recuperación tras fallo y lista vacía.
El filtro por tablero cubre combinación con todos los estados, nombres repetidos,
identificadores/nombres ausentes, compatibilidad JSON, orden y consultas vacías.
La búsqueda por nombre cubre coincidencias parciales, mayúsculas, espacios,
nombres nulos y combinación con ambos filtros. La presentación del tablero cubre
tarjetas completadas/sin fecha y alternativas cuando faltan nombre o identificador.
La presentación de listas cubre ambos diseños y datos ausentes; el contrato JSON
cubre `idList`, listas anidadas en tableros y compatibilidad con mocks anteriores.
Las insignias del dashboard cubren la anticipación personalizada, su límite exacto,
el paso a vencida y los textos de todos los estados, sin dependencias WinUI.
Los ámbitos pendiente/historial cubren completadas y archivadas independientes,
su unión sin duplicados, filtros combinados, tableros, listas vacías, restauración
y compatibilidad de `closed` ausente. Se comprueba que las archivadas no generan
avisos y se retiran del historial de repetición aunque un mock las devuelva como abiertas.
El interruptor de notificaciones cubre persistencia, migración concurrente de SQLite
v1 a v2 sin pérdida de datos, pausa de primeros avisos y repeticiones, tarjetas
consultables, reactivación respetando el historial y desactivación durante una consulta.
La paginación cubre tamaños de 10/25/50, límites y páginas parciales o vacías,
recorrido sin omisiones ni duplicados, desempate estable por ID, filtros combinados
en pendientes e historial y ajuste de la última página al reducirse los resultados.

`Fixture` crea una carpeta temporal única y la elimina con `Dispose`.
`MonitorDoubles.cs` aporta los mismos nombres/tipos que los servicios externos
sin compilar sus implementaciones reales. Reutilizar esos dobles y horas
explícitas; no abrir datos del usuario ni notificaciones/red reales.
Los nombres describen comportamiento esperado en español. Nueva lógica de
negocio → tests. Bug fix → regression test cuando sea razonable.

## Clima del Dashboard

El runner enlaza `WeatherClient` real y reutiliza el handler HTTP programable de
las pruebas con respuestas meteorológicas ficticias. Cubre endpoint sin claves,
unidades y timestamp UTC, sensación térmica, condiciones WMO y noche, campos
opcionales o desconocidos, caché y expiración exacta, refresco manual que fuerza
HTTP y renueva la caché (también tras fallo), llamadas concurrentes,
cancelación tardía, coordenadas inválidas, fallos HTTP/red/timeout/JSON, límite de
respuesta, recuperación tras cinco minutos y logging correlacionado y saneado:
GET, ruta `/v1/forecast`, estado y duración, sin query ni datos de ubicación.
Verifica INFO al recibir datos, WARN ante HTTP/red/timeout/JSON, cancelación en
Debug y ausencia de eventos HTTP al devolver la caché.
No consulta Open-Meteo real ni usa preferencias del usuario.

Revisión WinUI con dobles: abrir Dashboard sin configurar Trello, comprobar carga,
temperatura/viento/hora, aperturas con caché, refresco manual con nueva solicitud
y «No disponible». Pulsar refrescar debe actualizar también tras un fallo sin esperar
cinco minutos; durante la carga, el botón queda deshabilitado. Navegar fuera mientras
carga y regresar; verificar que no aparecen resultados tardíos. Comprobar que el
clima no aparece en Mis tarjetas, Historial ni Configuración. Revisar textos largos,
ventanas amplia/compacta, teclado, temas claro/oscuro y alto contraste.

## Gemini y preparación de lotes

El runner enlaza `GeminiCardClient` con `GeminiHttpDouble`: comprueba el endpoint y
cabecera, separación de credenciales, referencia temporal, contrato estructurado,
checklists individuales, límites de entrada/salida/cantidad, respuestas vacías,
bloqueadas, truncadas, JSON inválido, IDs inventados, fechas ambiguas/zonas incorrectas,
cancelación tardía y errores 400/401/403/404/429/500 sin reintentos ni cuerpos privados.
`CardBatchPreparer` reutiliza los casos de responsables/CSV y conserva estados que no
pueden volver a enviarse. SQLite cubre instalaciones nuevas, JSON, v1 y v2, migración
concurrente, preferencias e historial conservados y persistencia de Gemini al reiniciar.

Revisión WinUI con dobles: probar **Completar con Gemini** aplicando y descartando
la propuesta sobre campos ya editados; comprobar que no crea hasta pulsar Crear.
En lote, alternar CSV/Texto con IA, generar, editar título/descripción/fecha/usuarios,
corregir una fecha inválida, excluir/incluir y volver al origen conservando cambios.
Comprobar clave ausente, error HTTP, generación cancelada, navegación durante carga,
doble clic, no reenvío de éxitos/inciertos y bloqueo de edición tras iniciar envíos.
Revisar configuración, diálogos, scroll, foco, temas, contraste y escalado.

### Resultados históricos (no describen una ejecución actual)

Los resultados siguientes corresponden a sus fechas y entornos. Antes de reutilizar
un bloqueo o dar una comprobación por superada, volver a ejecutar los comandos
pertinentes. El [estado y los problemas conocidos](KNOWN_ISSUES.md) distingue las
limitaciones vigentes de las ya corregidas; estos resultados no son una lista de deuda.

Validación de esta implementación (17/11/2026): el build WinUI pasó sin errores ni
advertencias. Un runner temporal enlazó las mismas fuentes y ejecutó 67 casos bajo
.NET 6, incluidos los tres guards arquitectónicos; no sustituye la suite .NET 9.
Ese runner reportó dos advertencias nullable preexistentes en el test del contrato
de listas. Un host WinUI aislado comprobó las vistas reales con HTTP simulado, temas
claro/oscuro, ventana amplia/compacta y edición/reintento. `verify.ps1` sigue bloqueado
por faltar el SDK .NET 9 (solo está instalado 6.0.428); auditoría y validación completa
deben ejecutarse con el SDK documentado. Quedan las comprobaciones manuales de alto
contraste y escalado de Windows. Formato e imports de la app y `git diff --check`
pasaron; el formato del runner aún señala espacios preexistentes en los fixtures
de exportación CSV y migración v1. No se usaron cuentas, claves ni logs reales.

El ajuste REST del 18/11/2026 añade comprobaciones de claves ficticias con punto,
Content-Type, ruta exacta de `gemini-3.5-flash-lite`, `system_instruction`,
`response_mime_type` y `response_schema` con tipos OpenAPI y vencimiento anulable.
Se verifica también que los cuerpos de error privados no entren en excepciones.

Diagnóstico del HTTP 400 (18/11/2026): dos regresiones adicionales distinguen claves
inválidas/restringidas, API deshabilitada, parámetros rechazados y condiciones de uso
mediante respuestas ficticias; comprueban ausencia de cuerpos privados en excepciones
y límite de 32 KiB también para errores sin longitud conocida. El esquema enviado se
simplifica sin retirar la validación local. La causa del rechazo de la cuenta real
queda pendiente de confirmar con el detalle sanitizado de la aplicación recompilada.
Resultado tras el ajuste: 69 casos correctos en el runner temporal .NET 6, build WinUI
con 0 errores/advertencias, formato de la app y `git diff --check` correctos. Se mantienen
los bloqueos de SDK y las incidencias preexistentes del runner descritos arriba.

## Arquitectura
`ArchitectureTests.cs` usa Roslyn que ya viene con el SDK para verificar modelos
aislados y modelos + calendario. Los tres casos arquitectónicos incluyen un
autotest con dependencias prohibidas. Esos checks leen solo las fuentes concretas
de esas áreas, excluyen generados y no necesitan cargar el proyecto WinUI.
No sustituir un fallo del guard por ampliar la lista de namespaces permitidos
sin una decisión arquitectónica explícita.

## Comandos
Requisitos: Windows x64, SDK .NET 9, herramientas/SDK Windows del proyecto y
acceso a NuGet para restauración/auditoría. No requiere Inno Setup para validar.
Desde la raíz:

```powershell
# Todos los controles (sin editar/formatear fuentes automáticamente)
powershell -NoProfile -File .\verify.ps1

# Desarrollo: suite funcional + arquitectura
dotnet run --project .\TrelloNotifier.Tests\TrelloNotifier.Tests.csproj

# Formato y lint de un proyecto; repetir para TrelloNotifier.Tests
dotnet format whitespace .\TrelloNotifier\TrelloNotifier.csproj --verify-no-changes --no-restore
dotnet format style .\TrelloNotifier\TrelloNotifier.csproj --diagnostics IDE0005 --severity warn --verify-no-changes --no-restore

# Build con análisis estático (el script también valida el proyecto de tests)
dotnet build .\TrelloNotifier\TrelloNotifier.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win10-x64

# Inspección adicional de dependencias (la puerta de seguridad está en verify)
dotnet list .\TrelloNotifier\TrelloNotifier.csproj package --vulnerable --include-transitive
```

Si la política local de PowerShell bloquea scripts, se puede ejecutar con una
excepción solo para ese proceso (sin cambiar la política persistente):
`powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1`.
La verificación coloca la salida WinUI en `artifacts/verify/app` para evitar
conflictos con una instancia de desarrollo abierta desde `bin/`. No ejecutar
la app desde esa carpeta mientras se verifica.

`verify.ps1` restaura ambos proyectos y ejecuta formato, lint, compilación con
analizadores, runner y auditoría NuGet de dependencias directas/transitivas.
Se detiene con código no cero ante errores, advertencias de build, vulnerabilidades
o auditoría incompleta. El build precede al runner porque hay que compilarlo;
no se compila una segunda vez con `dotnet run --no-build`.
No basta con que `dotnet list package` devuelva código cero: el script inspecciona
el resultado JSON y sus errores/vulnerabilidades.

El lint se ejecuta como `dotnet format style`, separado del build: activar
`EnforceCodeStyleInBuild` para IDE0005 en este SDK exige generar documentación
XML adicional. No hace falta habilitarla para el control de imports existente.
El control de imports se aplica también a `MainPage.xaml.cs`; su excepción
anterior se retiró al modificar la carga de preferencias para SQLite.

## Comprobación manual cuando cambie UI

En **Creación masiva**, usar un CSV de ejemplo sin datos privados y un mock local:
elegir/cancelar archivos, arrastrar un único CSV (rechazar varios archivos o carpetas),
guardar plantilla/resultados, probar UTF-8 con BOM, comillas,
saltos de línea, fechas con offset, responsables desconocidos y cambio rápido de tablero.
Comprobar revisión/exclusión de inválidas, progreso, doble clic, detener/continuar,
rechazo 403/429 y respuesta incierta. Los reintentos no deben reenviar creadas/excluidas/
inciertas; navegar durante el envío requiere detener primero. El runner enlaza el parser
real (sin UI ni HTTP) y cubre formatos, límites, validación de usuarios/fechas, estados,
enlaces seguros y escape/protección de exportación.
Revisar los tres pasos (archivo y destino, revisión, resultado), los colores de las
filas al desplazarlas, el resumen final y Nueva importación en temas claro/oscuro
y ventanas amplias/estrechas, comparándolos con los mockups de creación masiva.

En **Crear tarjeta → Creación manual**, usar un servidor simulado y credenciales
ficticias para comprobar carga vacía/fallida, cambio rápido de tablero, selección
múltiple de miembros/etiquetas, título/lista obligatorios, fecha opcional, Markdown y
varios checklists con líneas vacías. Comprobar un POST de tarjeta seguido de sus
checklists, doble clic, rechazo 403/429, timeout con resultado incierto y fallo parcial
tras crear la tarjeta. La vista debe impedir reenvíos inciertos y abrir solo enlaces
HTTPS de Trello. Revisar adaptación a ventana estrecha y navegación durante el envío.
El runner cubre validación de borradores, elementos de checklist y DTOs de selección;
no enlaza el cliente HTTP real ni automatiza WinUI.
Abrir en Windows, navegar dashboard/configuración, guardar y comprobar temas,
usar **Probar notificación** sin red. Para integración HTTP usar un mock con
valores ficticios siguiendo el README; comprobar refresco y errores. Revisar
suscripciones, activación de enlaces y duplicación de recordatorios si se tocaron.
En **Configuración → Monitor**, desactivar **Notificaciones de escritorio**, guardar
y comprobar que **Comprobar ahora** actualiza las tarjetas sin emitir avisos y muestra
el estado desactivado. Verificar que sonido y **Probar notificación** se deshabilitan
al apagar el interruptor; reactivarlo, guardar y comprobar la reanudación según el
historial. Reiniciar para confirmar que la preferencia se conserva. Comprobar el
interruptor con teclado y en los temas claro, oscuro y de contraste.
En **Mis tarjetas**, comprobar los cinco filtros, recuentos, estado vacío y error;
incluir tarjetas sin fecha y verificar que completadas y archivadas quedan fuera.
Desactivar avisos de vencidas debe
mantenerlas visibles. Cambiar filtro no debe consultar HTTP; una comprobación
debe conservar el filtro seleccionado y reflejar cambios de fecha/estado.
Combinar el selector de tablero con cada estado; comprobar Todos los tableros,
recuentos y resultados vacíos. Al refrescar, conservar el tablero por ID aunque
cambie de nombre; si ya no tiene tarjetas, volver a Todos los tableros. Un fallo
temporal no debe borrar la selección. Cambiar de tablero no debe consultar HTTP.
Escribir y borrar la búsqueda combinada con ambos filtros; comprobar recuentos,
estado vacío y conservación del texto al refrescar. Verificar el nombre del
tablero y de la lista en columnas contiguas (debajo del título en la vista compacta),
también con nombres largos y datos ausentes. Mover una tarjeta de lista y renombrar
la lista en el mock; comprobar el nombre actualizado al refrescar y las listas
archivadas en Historial. Comprobar los cuatro indicadores: no cambian al filtrar y
Al día incluye solo futuras y sin fecha pendientes no archivadas. Un fallo debe mostrar un aviso y
guiones en los indicadores. Comprobar el orden por vencimiento y después por
nombre, con las tarjetas sin fecha al final, también al filtrar y refrescar.
Verificar colores e iconos de estado al filtrar y desplazar la lista (filas recicladas).
El pipeline no demuestra que Windows haya mostrado un toast correctamente.

En ambas tablas, usar más de 50 tarjetas del mock y recorrer las flechas **Página anterior/Página siguiente**:
comprobar rango y total filtrado (por ejemplo, **26–50 de 63**), última página parcial,
**0–0 de 0** sin resultados y flechas deshabilitadas en los extremos. Revisar el texto
a la izquierda de las flechas, los tooltips y los estados de foco, hover y deshabilitado.
Alternar **10/25/50 filas por página**, búsqueda, tablero y estado desde una página
intermedia: deben volver a la primera página y mantener los demás filtros. Los
indicadores globales no deben cambiar al paginar. Verificar que los enlaces de las
filas siguen abriendo la tarjeta correcta y que cada página comienza arriba.
Actualizar debe conservar la página actual o ajustarla a la última disponible si
se eliminan tarjetas. Comprobar resultados vacíos, error con botones deshabilitados
y recuperación en la página previa. Paginar no debe generar solicitudes HTTP ni
avisos. Revisar el pie de tabla en diseño compacto y amplio, temas y navegación por teclado.

En **Historial**, comprobar Todas, Completadas y Archivadas con tarjetas que tengan
ambos estados y archivadas sin completar; estas últimas no deben parecer completadas
ni urgentes. Combinar búsqueda/tablero y comprobar vacíos. Con un mock, verificar
`filter=all` solo al entrar o actualizar; filtros locales y ciclos del monitor no
deben refrescar el historial ni emitir avisos desde esa vista. Navegar entre las
dos vistas mientras carga debe cancelar la consulta saliente y evitar resultados
tardíos. Probar error HTTP, JSON inválido, timeout, configuración ausente y recuperación;
conservar filtros tras error y actualizar tableros por ID tras una consulta correcta.
Comprobar temas, diseño compacto y apertura de enlaces también en Historial.

En ambas vistas, un timeout debe mostrar «Tiempo de espera agotado» y
«El servidor tardó demasiado en responder. Inténtalo nuevamente en unos momentos.»;
una consulta correcta posterior debe retirar el título y el error.

Si cambian los estilos, alternar **Claro**, **Oscuro** y **Usar configuración de
Windows** desde Apariencia y volver al dashboard. Comprobar fondos, texto secundario,
insignias, desplegables, InfoBar y estados de foco, hover y deshabilitado. Revisar
también un tema de contraste de Windows. Redimensionar a ambos lados de 1000 px
(cabecera e indicadores compactos) y 1100 px (panel de navegación), y comprobar el escalado
de Windows al 150 % y el cambio tabla/filas compactas a 1250 px: títulos largos,
tableros, controles y botones deben seguir
siendo legibles y accesibles por teclado. Guardar el tema y reiniciar para verificar
que se conserva. Esta comprobación visual requiere ejecutar la app WinUI.

## CI
No existía CI en esta copia. No se añade infraestructura de despliegue.
Un futuro job Windows con SDK 9 y herramientas WinUI debe invocar `verify.ps1`,
sin secretos, antes de publicar. Orden lógico: FORMAT → LINT → STATIC ANALYSIS
→ UNIT/INTEGRATION TESTS → ARCHITECTURE TESTS → SECURITY CHECKS → BUILD/artefacto.
El script reúne análisis y build para evitar duplicarlos. Si se distribuye una
versión, añadir después el publish Release e Inno Setup documentados en README.
No sustituir la suite por `dotnet test TrelloNotifier.sln`.

## Logging

El runner enlaza `AppLog` real y usa Serilog File/Async en carpetas de `Fixture`.
Comprueba reutilización del archivo entre arranques y rotación al alcanzar 5 MiB,
manteniendo solo los tres archivos más recientes de la serie, incluido el activo.
Comprueba que el cierre vacía la cola, que los niveles Information/Debug y los
timeouts se distinguen, que un destino inaccesible no impide usar persistencia
y que el monitor correlaciona fallo/recuperación sin falsas alarmas al cancelar.
Los errores usan secretos ficticios en Message, Data y excepciones internas
para verificar su ausencia en los archivos de texto; también se comprueba la cabecera
compacta escrita en el archivo, el componente corto y la correlación.
Se verifica aislamiento de scopes paralelos, conservación de propiedades completas,
conteos de avisos agrupados/cooldown/deduplicación/desactivación/rechazo, reducción
de rutina a Debug y espera de una comprobación manual durante el cierre.
Las regresiones verifican contextos XAML, entrega de excepciones saneadas a Serilog,
trazas e interiores/ramas agregadas sin mensajes privados, y cambios del conjunto
en cooldown con conteos idénticos. También cubren repeticiones, cambio de orden,
transición tras un envío y avisos desactivados.
No se leen logs ni configuración del usuario.
No se consulta la API real ni se emiten notificaciones Windows en estas pruebas.

Comprobación manual de diagnóstico: iniciar la app con un mock sin credenciales,
comprobar una consulta correcta y otra fallida, probar notificación y cerrar.
Verificar registros locales, código HTTP, conteos, CheckId y cierre sin contenido
privado; activar Debug mediante la variable de entorno y reiniciar. Comprobar
también el aviso de arranque ante un destino sin permisos en un perfil de prueba.
