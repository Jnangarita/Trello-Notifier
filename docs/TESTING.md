# Pruebas y verificación

## Framework y cobertura real
`TrelloNotifier.Tests` es un ejecutable .NET 9 con runner propio en `Program.cs`:
`Run(nombre, Func<Task>)`, `Check(condición, mensaje)`, salida `OK` y excepción
que termina el proceso si falla. **`dotnet test` no ejecuta esta suite.**
No hay xUnit/NUnit, tests UI automatizados ni tests del cliente HTTP real.

Los 11 casos originales cubren calendario/límites, intervalos, preferencias,
migración de historial, agrupación, concurrencia entre comprobaciones,
reinicio, avisos rechazados, vencidas, fallo de API y cambios de fecha.
Los casos del monitor son integración local de fuentes reales con JSON temporal
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

`Fixture` crea una carpeta temporal única y la elimina con `Dispose`.
`MonitorDoubles.cs` aporta los mismos nombres/tipos que los servicios externos
sin compilar sus implementaciones reales. Reutilizar esos dobles y horas
explícitas; no abrir datos del usuario ni notificaciones/red reales.
Los nombres describen comportamiento esperado en español. Nueva lógica de
negocio → tests. Bug fix → regression test cuando sea razonable.

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
Abrir en Windows, navegar dashboard/configuración, guardar y comprobar temas,
usar **Probar notificación** sin red. Para integración HTTP usar un mock con
valores ficticios siguiendo el README; comprobar refresco y errores. Revisar
suscripciones, activación de enlaces y duplicación de recordatorios si se tocaron.
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

En **Historial**, comprobar Todas, Completadas y Archivadas con tarjetas que tengan
ambos estados y archivadas sin completar; estas últimas no deben parecer completadas
ni urgentes. Combinar búsqueda/tablero y comprobar vacíos. Con un mock, verificar
`filter=all` solo al entrar o actualizar; filtros locales y ciclos del monitor no
deben refrescar el historial ni emitir avisos desde esa vista. Navegar entre las
dos vistas mientras carga debe cancelar la consulta saliente y evitar resultados
tardíos. Probar error HTTP, JSON inválido, timeout, configuración ausente y recuperación;
conservar filtros tras error y actualizar tableros por ID tras una consulta correcta.
Comprobar temas, diseño compacto y apertura de enlaces también en Historial.

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
Comprueba que el cierre vacía la cola, que los niveles Information/Debug y los
timeouts se distinguen, que un destino inaccesible no impide usar persistencia
y que el monitor correlaciona fallo/recuperación sin falsas alarmas al cancelar.
Los errores usan secretos ficticios en Message, Data y excepciones internas
para verificar su ausencia en los archivos de texto; también se comprueba la cabecera
tipo Spring Boot, el componente y la correlación. No se leen logs ni configuración del usuario.
No se consulta la API real ni se emiten notificaciones Windows en estas pruebas.

Comprobación manual de diagnóstico: iniciar la app con un mock sin credenciales,
comprobar una consulta correcta y otra fallida, probar notificación y cerrar.
Verificar registros locales, código HTTP, conteos, CheckId y cierre sin contenido
privado; activar Debug mediante la variable de entorno y reiniciar. Comprobar
también el aviso de arranque ante un destino sin permisos en un perfil de prueba.
