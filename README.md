# Trello Notifier

Aplicación de escritorio para Windows que consulta las tarjetas abiertas asignadas a tu cuenta de Trello y muestra una notificación nativa cuando una tarjeta está próxima a vencer.

La interfaz y la distribución del proyecto parten de la plantilla WinUI 3 de `client-notifier`, adaptada para consultar directamente la API de Trello.

## Características

- Notificaciones nativas de Windows 10 y Windows 11.
- Consulta periódica de las tarjetas abiertas asignadas al usuario autenticado.
- Vista de todas las tarjetas asignadas con búsqueda por nombre y filtros combinables por tablero y estado, incluidas las que no tienen fecha.
- Anticipación e intervalo de consulta configurables.
- Recordatorios repetidos configurables (Nunca, 15, 30 o 60 minutos), independientes del intervalo de consulta.
- Historial persistente para respetar el intervalo de recordatorio incluso después de reiniciar la aplicación.
- Configuración e historial almacenados en SQLite, con importación automática de los JSON de versiones anteriores.
- Resumen en una sola notificación cuando hay varias tarjetas para avisar.
- Opción para incluir tarjetas vencidas pendientes, con repeticiones cada 2 horas.
- Nuevo aviso si se cambia la fecha de vencimiento de una tarjeta.
- Acceso directo a la tarjeta desde su aviso individual o el botón de la interfaz; los resúmenes abren Trello.
- Temas de Windows, claro y oscuro, con superficies y textos adaptados, controles redondeados y colores de alto contraste del sistema. El tema se previsualiza al seleccionarlo y se conserva al guardar la configuración.
- Sonido de las notificaciones configurable.
- Notificación local de prueba sin conectarse a Trello.
- URL de API configurable para usar Postman Mock Server u otro servidor simulado.
- Inicio automático con Windows opcional desde el instalador.
- Logs locales estructurados con rotación automática para diagnóstico.

## Requisitos

- Windows 10, compilación 17763 o posterior, o Windows 11.
- .NET SDK 9 para compilar desde la línea de comandos.
- Visual Studio 2022 con las herramientas de desarrollo de escritorio para .NET, si se utiliza el IDE.
- Una API key y un token de Trello con permiso de lectura.
- Inno Setup 6 únicamente para generar el instalador.

## Configurar la URL, la API key y el token de Trello

### 1. Obtener la API key

1. Inicia sesión en Trello con la cuenta donde tienes asignadas tus tarjetas.
2. Abre el [panel de administración de aplicaciones de Trello](https://trello.com/apps/admin).
3. Selecciona una aplicación existente o pulsa **Nuevo / New** para crear una con el nombre `Trello Notifier`.
4. Si creas una aplicación, selecciona **My app doesn't use Power-Up capabilities** (mi aplicación no utiliza funciones de Power-Up). Así no necesitas completar la **URL del conector de iframe**. Completa los demás datos solicitados y pulsa **Crear / Create**.
5. Dentro de la aplicación, abre **Trello Auth** (en algunas interfaces, **API Key**) y pulsa **Generate a new API Key** si todavía no tienes una clave.
6. Copia la **API key**.

El enlace `https://trello.com/app-key` puede dirigirte a ese panel. La sección **OAuth 2.0** no necesita configurarse para Trello Notifier: puedes dejar sin configurar la URL de devolución de llamada, el tipo de seguridad del cliente y los scopes de esa sección. El **ID de cliente de OAuth 2.0 no es la API key**.

### 2. Generar un token de solo lectura o de lectura y escritura

1. Elige la URL según las funciones que necesites y sustituye `TU_API_KEY` por la clave obtenida en el paso anterior:

   **Solo lectura (`scope=read`):** permite consultar tus tarjetas y recibir avisos, pero no crear tarjetas.

   ```text
   https://trello.com/1/authorize?expiration=never&scope=read&response_type=token&name=Trello%20Notifier&key=TU_API_KEY
   ```

   **Lectura y escritura (`scope=read,write`):** permite consultar tus tarjetas, recibir avisos y usar **Creación manual** y **Creación masiva**.

   ```text
   https://trello.com/1/authorize?expiration=never&scope=read,write&response_type=token&name=Trello%20Notifier&key=TU_API_KEY
   ```

2. Abre la URL completa en el navegador con tu sesión de Trello iniciada.
3. Comprueba que estás autorizando la cuenta correcta y pulsa **Permitir / Allow**.
4. Copia el **token** que Trello muestra al finalizar.

`expiration=never` indica que el token no caduca automáticamente; puedes revocarlo desde la configuración de tu cuenta de Trello, en **Aplicaciones**. El permiso adicional `account` no es necesario para estas funciones.

Para crear tarjetas, la cuenta también debe poder escribir en el tablero de destino. Si ya tenías un token de solo lectura, genera uno nuevo con lectura y escritura y reemplázalo en Configuración; el token anterior no se actualiza automáticamente.

### 3. Introducir los datos en Trello Notifier

1. Abre **Configuración** en Trello Notifier.
2. Completa estos campos:

   | Campo | Valor |
   | --- | --- |
   | **URL base de la API** | `https://api.trello.com` (solo lectura). |
   | **API key** | La clave copiada de **Trello Auth**. |
   | **Token** | El token de solo lectura o de lectura y escritura obtenido en el paso anterior. |

   La URL base no debe incluir `/1/members/me/cards` ni las credenciales: la aplicación añade la ruta y los parámetros necesarios.

3. Pulsa **Probar conexión** para comprobar los datos.
4. Si la conexión es correcta, pulsa **Guardar cambios**.

Si aparece **Conexión correcta. El servidor devolvió 0 tarjeta(s) abierta(s).**, la consulta funcionó, pero no encontró tarjetas abiertas asignadas a esa cuenta. Tener acceso a un tablero no significa que sus tarjetas estén asignadas a ti: abre una tarjeta, añádete en **Miembros**, comprueba que no esté archivada y repite la prueba.

### 4. Comprobar los avisos de escritorio

1. En **Configuración**, pulsa **Probar notificación** para comprobar que Windows muestra el aviso.
2. En Trello, elige una tarjeta abierta asignada a ti y establece una fecha y hora de vencimiento dentro de los próximos **60 minutos**, si mantienes la anticipación predeterminada. Deja el vencimiento sin marcar como completado.
3. En Trello Notifier, abre **Mis tarjetas** y pulsa **Actualizar**.
4. Mantén la aplicación en ejecución para recibir los avisos. Por defecto consulta Trello cada **5 minutos**.

La aplicación avisa de tarjetas próximas a vencer y, opcionalmente, de vencidas pendientes; no replica las notificaciones de comentarios o menciones de Trello.

Referencia: [documentación oficial de autorización de Trello](https://developer.atlassian.com/cloud/trello/guides/rest-api/authorization/).

## Dashboard y clima

La aplicación inicia en **Dashboard**, una página independiente de **Mis tarjetas**
e **Historial**. Su tarjeta azul muestra temperatura (°C), sensación térmica e
icono y descripción de las condiciones reales, también de noche. El tooltip de
la tarjeta conserva viento (km/h), fuente y fecha de los datos de
[Open-Meteo](https://open-meteo.com/), sin API key. El clima se consulta solo al
abrir esa página o pulsar **Actualizar clima** y no genera acciones en Trello ni
notificaciones de escritorio.

La ubicación predeterminada es **Barranquilla, Colombia** (latitud `10.9685`, longitud `-74.7813`).
Para cambiarla, define estas variables de entorno antes de iniciar la aplicación:

| Variable | Descripción |
| --- | --- |
| `TRELLO_NOTIFIER_WEATHER_LATITUDE` | Latitud entre -90 y 90, con punto decimal. |
| `TRELLO_NOTIFIER_WEATHER_LONGITUDE` | Longitud entre -180 y 180, con punto decimal. |
| `TRELLO_NOTIFIER_WEATHER_CITY` | Etiqueta opcional de la ubicación; no hace geocodificación. |

Ejemplo en PowerShell, iniciando después el ejecutable desde esa misma consola:

```powershell
$env:TRELLO_NOTIFIER_WEATHER_LATITUDE = "40.4168"
$env:TRELLO_NOTIFIER_WEATHER_LONGITUDE = "-3.7038"
$env:TRELLO_NOTIFIER_WEATHER_CITY = "Madrid"
```

Se requieren ambas coordenadas; si faltan o son inválidas se usa Barranquilla. Cambiar
solo la etiqueta no cambia la ubicación. Reinicia la aplicación para aplicar cambios.
No se modifica SQLite. Las coordenadas se envían únicamente a Open-Meteo; la etiqueta
permanece local y no se registran ubicación, URL completa ni respuestas en los logs.

El cliente compartido conserva los datos **1 hora en memoria** al abrir el Dashboard
o volver a él. Tras un fallo muestra **No disponible** y las aperturas normales
respetan una espera de **5 minutos**, para evitar llamadas repetidas. Pulsar
**Actualizar clima** fuerza una nueva consulta, incluso si hay caché vigente o
una espera tras fallo; su resultado actualiza la caché compartida. El botón queda
deshabilitado durante la carga para impedir solicitudes duplicadas. Cada solicitud
tiene un timeout de **10 segundos** y se cancela al salir; la interfaz sigue disponible.
La hora mostrada corresponde a la zona local del equipo. Los datos son condiciones
calculadas por modelos meteorológicos de Open-Meteo.

## Logs y diagnóstico

Los logs se consultan directamente en los archivos `.log` con un editor de texto.
Se guardan automáticamente en:

```text
%LOCALAPPDATA%\Trello Notifier\Logs\trello-notifier.log
```

Cada inicio de la aplicación registra un banner ASCII de **Trello Notifier**, con la versión
del ensamblado y del runtime .NET, junto con WinUI 3 y Serilog. Se escribe como un único evento
Information multilínea, después de configurar el logger y la sesión.

Los eventos usan texto compacto: fecha con zona horaria, nivel (`INFO`, `WARN`,
`ERROR`, `DEBUG`, `FATAL`; `TRACE` para Verbose), correlación, nombre corto del componente y mensaje.
Por ejemplo, con identificadores ficticios:

```text
2026-11-21 14:30:00.010 -05:00 [INFO] [s:45120f6a] App: Aplicación iniciada.

  _______       _ _         _   _       _   _  __ _
 |__   __|     | | |       | \ | |     | | (_)/ _(_)
    | |_ __ ___| | | ___   |  \| | ___ | |_ _| |_ _  ___ _ __
    | | '__/ _ \ | |/ _ \  | . ` |/ _ \| __| |  _| |/ _ \ '__|
    | | | |  __/ | | (_) | | |\  | (_) | |_| | | | |  __/ |
    |_|_|  \___|_|_|\___/  |_| \_|\___/ \__|_|_| |_|\___|_|

Trello Notifier v1.1.1
Powered by .NET 6.0.36 | WinUI 3 | Serilog

2026-11-21 14:30:00.123 -05:00 [INFO] [s:45120f6a] DueCardMonitor: Monitor iniciado
2026-11-21 14:30:01.462 -05:00 [INFO] [s:45120f6a c:9b996914] DueCardMonitor: Evaluadas: 38 | Candidatas: 3 | Avisos enviados: 0 (3 en cooldown) (1339 ms)
```

Se registran inicio, versión, cierre normal, resultados HTTP, comprobaciones del monitor, envío de
avisos y fallos. `s:` identifica la ejecución y `c:` relaciona los eventos de una
comprobación; se muestran ocho caracteres y se omite `c:` fuera del ciclo.
`SessionId`, `CheckId` y `SourceContext` conservan sus valores completos como
propiedades del evento. Los GET correctos y ciclos sin envíos ni cambios
quedan en Debug, también cuando se repite el mismo cooldown. Se comparan conteos
y el conjunto de tarjetas en cooldown; el primer estado, los cambios, los envíos
y la recuperación tras un fallo aparecen en Information. El resumen diferencia notificaciones agrupadas, tarjetas
notificadas, cooldown, deduplicación y preferencias desactivadas.
Que Windows acepte un aviso no demuestra que el
usuario lo haya visto.

- Rotación únicamente por tamaño, al alcanzar aproximadamente **5 MiB** por archivo.
  Se conservan **3 archivos**, incluido el activo. Los siguientes se llaman
  `trello-notifier_001.log`, `trello-notifier_002.log`, etc.; al abrir un cuarto
  archivo se elimina el más antiguo. Reiniciar la app o cambiar de día no crea
  otro archivo si el activo todavía tiene espacio. Un evento completo puede superar
  ligeramente el umbral antes de abrir el siguiente archivo.
  Los archivos diarios anteriores (`trello-notifier-AAAAMMDD.log`) pertenecen al
  esquema antiguo y no se eliminan automáticamente con esta nueva serie.
- Nivel **Information** por defecto. Para habilitar **Debug**, define la variable
  de entorno `TRELLO_NOTIFIER_LOG_LEVEL=Debug` antes de iniciar la aplicación.
  Al quitarla y reiniciar se vuelve al nivel normal. No cambia la base de datos.
- Escritura en segundo plano con cola de 1000 eventos. Si se llena, se descartan
  nuevos eventos para no bloquear la interfaz. Vaciado a disco cada segundo;
  el cierre normal espera al monitor y vacía la cola con `Log.CloseAndFlush()`;
  un cierre forzado puede perder los últimos eventos.
- HTTP registra método, plantilla de ruta sin IDs privados, estado y duración.
  No se registran credenciales, URLs completas, contenido de tarjetas, configuración ni
  mensajes completos de excepciones. Los errores conservan operación, tipo,
  código y trazas nativas sin rutas de archivos. Serilog recibe una copia saneada
  de la excepción y sus interiores (hasta 16 niveles), sin mensajes originales ni Data.
  Los componentes XAML se identifican como `App`, `SettingsPage`, etc., no como `xaml`.
- Un fallo del logger no impide usar la aplicación. Si se detecta al arrancar,
  se muestra un aviso; los fallos internos o de cola también generan una advertencia
  genérica mediante `Trace`, visible con un listener de diagnóstico, sin datos privados.

La plantilla de texto de Serilog y sus salidas File/Async gestionan formato,
rotación y cola. Los logs son locales: no se envían a ningún servidor.
Ya no se escribe `crash.log` junto al
ejecutable; los archivos antiguos no se eliminan automáticamente. No adjuntes
datos locales o logs reales al repositorio ni a herramientas de IA.

## Probar sin Trello

### Probar únicamente la notificación de Windows

1. Abre **Configuración**.
2. Selecciona si deseas reproducir sonido.
3. Pulsa **Probar notificación**.

Esta prueba no realiza solicitudes de red y no necesita API key ni token.

### Probar el flujo completo con Postman Mock Server

1. En Postman, crea una colección con una solicitud `GET` a `/1/members/me/cards`.
2. Agrega un ejemplo de respuesta con código `200` y el encabezado `Content-Type: application/json`.
3. Utiliza como cuerpo una lista de tarjetas:

```json
[
  {
    "id": "tarjeta-prueba-1",
    "name": "Preparar entrega de prueba",
    "due": "2026-09-07T20:00:00Z",
    "dueComplete": false,
    "url": "https://trello.com/"
  }
]
```

4. Ajusta `due` a una fecha futura que esté dentro de la anticipación configurada. Por ejemplo, dentro de los próximos 60 minutos.
5. Crea el Mock Server y copia su URL, con un formato similar a `https://abc123.mock.pstmn.io`.
6. Para usar el mock, la configuración persistida debe tener previamente esa URL en `ApiBaseUrl`; este valor es de solo lectura en la interfaz. No agregues `/1/members/me/cards` al final.
7. Deja vacíos **API key** y **Token**, pulsa **Probar conexión** y después **Guardar cambios**.
8. Regresa a **Mis tarjetas** y pulsa **Actualizar**.

La aplicación añadirá automáticamente la ruta `/1/members/me/cards` y los parámetros de consulta. Para volver al servicio real, restaura la URL `https://api.trello.com` en la configuración persistida e introduce las credenciales de Trello en la interfaz.

Para probar el filtro por tablero, añade `"idBoard": "tablero-prueba"` a las
tarjetas del ejemplo y agrega al mock una respuesta `200` para
`GET /1/members/me/boards`, con cuerpo
`[{"id":"tablero-prueba","name":"Tablero de prueba"}]`.
Las respuestas antiguas sin `idBoard` siguen siendo compatibles y no requieren
esta segunda ruta.

Para mostrar también la lista, añade `"idList": "lista-prueba"` a la tarjeta y
devuelve las listas dentro de cada tablero en esa misma respuesta:
`[{"id":"tablero-prueba","name":"Tablero de prueba","lists":[{"id":"lista-prueba","name":"En progreso"}]}]`.
La consulta de tableros incluye `lists=all`, por lo que el mock puede devolver
también listas archivadas.

## Funcionamiento

Por defecto, la aplicación consulta Trello cada **5 minutos**, avisa sobre tarjetas que vencen durante los próximos **60 minutos** y repite sus recordatorios cada **30 minutos** mientras sigan dentro del periodo de aviso.

### Activar o desactivar las notificaciones de escritorio

En **Configuración → Monitor**, cambia **Notificaciones de escritorio** y pulsa
**Guardar cambios**. La opción está activada por defecto y se conserva al reiniciar.
Al desactivarla, el monitor sigue actualizando **Mis tarjetas**, pero no envía avisos
de vencimientos, repeticiones ni resúmenes, incluso al pulsar **Comprobar ahora**.
El estado del monitor indica que las notificaciones están desactivadas.

Los avisos omitidos no se registran como enviados. Al reactivar la opción se evalúan
las tarjetas actuales respetando la anticipación, la repetición y el historial previo.
El control de sonido y **Probar notificación** se deshabilitan mientras el interruptor
está desactivado. Los avisos ya enviados a Windows no se retiran.

### Configurar la frecuencia de los recordatorios

La consulta y los filtros de **Mis tarjetas** son independientes de qué tarjetas generan avisos.

1. Abre **Configuración**, en la sección **Monitor**.
2. Selecciona **Anticipación** para elegir qué vencimientos próximos se incluyen: **15 minutos**, **30 minutos**, **1 hora**, **2 horas**, **4 horas**, **12 horas**, **1 día**, **2 días**, **3 días** o **1 semana**. El valor predeterminado es **1 hora**. Si tenías guardado un intervalo diferente, selecciona una de estas opciones antes de guardar; no se sustituye automáticamente el valor anterior.
3. Define **Comprobar cada (minutos)** para elegir con qué frecuencia se consulta Trello.
4. Selecciona **Repetir recordatorios cada**: **Nunca**, **15 minutos**, **30 minutos** o **60 minutos**. **Nunca** permite únicamente el primer aviso por tarjeta y fecha de vencimiento.
5. Opcionalmente, activa **Incluir tarjetas vencidas pendientes**. Está desactivado por defecto. Cuando está activo, sus avisos se repiten cada **2 horas** desde el último aviso de esa tarjeta, en lugar del intervalo de las próximas a vencer. **Nunca** desactiva también las repeticiones de vencidas. Las vencidas se pueden consultar en **Mis tarjetas** aunque esta opción esté desactivada.
6. Pulsa **Guardar cambios**.

El primer aviso se envía al detectar una tarjeta que cumple las condiciones. Las repeticiones se envían en la primera comprobación realizada una vez transcurrido el intervalo, no mediante un temporizador separado. Por ejemplo, consultar cada 5 minutos y repetir cada 30 minutos permite detectar cambios rápidamente sin recibir el mismo aviso en cada consulta.

**Comprobar ahora** y las comprobaciones al abrir la pantalla o reiniciar el monitor respetan el mismo intervalo; no fuerzan recordatorios duplicados. **Probar conexión** solo comprueba el acceso a la API; **Probar notificación** envía un aviso local de prueba.

Si hay varias tarjetas cuyo aviso corresponde en la misma comprobación, se envía **un único resumen** con el número de tarjetas próximas a vencer y vencidas, y algunos de sus nombres. Al pulsarlo se abre Trello. Si solo corresponde avisar de una tarjeta, se muestra su nombre y vencimiento, con acceso directo a ella.

Se deja de avisar cuando el vencimiento se marca como completado, la tarjeta se archiva o deja de estar asignada a ti. Sin la opción de incluir vencidas, también deja de avisarse al pasar la fecha de vencimiento. Las tarjetas sin fecha de vencimiento no generan recordatorios.

El monitor funciona mientras el proceso de Trello Notifier está abierto. Para recibir avisos después de iniciar sesión en Windows, deja seleccionada la tarea **Iniciar Trello Notifier con Windows** al instalar.

Se guarda la fecha y hora del último aviso enviado por cada combinación de tarjeta y fecha de vencimiento. Si la fecha de vencimiento cambia, se considera un aviso diferente y se vuelve a evaluar según la anticipación configurada. Reiniciar la aplicación conserva el historial y no reinicia los intervalos.

Al actualizar desde una versión anterior, la repetición predeterminada es de 30 minutos. El historial antiguo se migra automáticamente: las tarjetas que ya figuraban como notificadas comienzan a contar su intervalo desde la migración, para evitar una ráfaga inmediata de avisos. Puedes seleccionar **Nunca** para mantener el comportamiento de un solo aviso.

### Consultar y filtrar tarjetas

En **Mis tarjetas**, **Actualizar** consulta las tarjetas y **Crear tarjeta** despliega las opciones **Creación manual** y **Creación masiva**.

### Creación manual

1. Pulsa **Crear tarjeta → Creación manual**. Se cargan los tableros abiertos y sus listas abiertas desde Trello.
2. Escribe el **título** y selecciona **tablero y lista**. Son obligatorios.
3. Opcionalmente, añade una descripción en Markdown; los botones de negrita, cursiva y lista insertan ese formato. Selecciona uno o varios responsables y etiquetas del tablero.
4. Puedes añadir vencimiento (fecha y hora local de Windows) y varios checklists, con un elemento por línea. Las líneas vacías se omiten. **Quitar vencimiento** permite crear la tarjeta sin fecha.
5. Pulsa **Crear tarjeta**. Se crea primero la tarjeta y luego sus checklists. Al terminar puedes abrirla en Trello o volver a Mis tarjetas, que actualizará la consulta. Solo aparecerá allí si está asignada a tu cuenta.

El formulario conserva lo escrito ante un rechazo explícito de Trello y permite reintentar. Si se creó la tarjeta pero falló un checklist, se informa del resultado parcial y se completa desde Trello. Si no se puede confirmar el resultado de un envío, se bloquea un segundo envío de ese formulario: revisa el tablero antes de iniciar otra creación. No hay reintentos automáticos ni almacenamiento local de borradores; salir de la vista descarta el formulario. Durante el envío se espera a que termine antes de cambiar de vista.

### Creación masiva desde CSV

1. Abre **Crear tarjeta → Creación masiva** y descarga la plantilla si la necesitas.
2. Arrastra un archivo a la zona CSV o pulsa **Elegir archivo** para seleccionar un CSV **UTF-8**, de hasta **2 MiB y 1000 tarjetas**. Admite separadores coma o punto y coma, campos entre comillas, comillas escapadas y descripciones multilínea.
3. Selecciona un **tablero**, una **lista** y, opcionalmente, varios **responsables predeterminados** para el lote.
4. Pulsa **Revisar tarjetas**. Se muestran título, descripción, vencimiento en hora local, responsables y errores por fila. Usa **Editar** para corregir los campos (el vencimiento se introduce en ISO 8601 con zona), **Excluir / Incluir** para cada fila o **Excluir las filas con errores**. También puedes corregir y volver a cargar el CSV. La edición se bloquea al iniciar el envío.
5. Pulsa **Crear tarjetas**. Los envíos son secuenciales, con una pausa de 500 ms entre solicitudes. Puedes **Detener después de esta tarjeta** y continuar después, sin repetir las ya creadas.
6. Consulta el resumen de creadas, pendientes y excluidas, descarga los resultados o abre las tarjetas creadas en Trello. El CSV de resultados incluye fila, título, estado, ID, URL y detalle, con protección frente a fórmulas de hoja de cálculo. **Nueva importación** permite iniciar otro lote.

Columnas admitidas (no se aceptan columnas adicionales ni repetidas):

| Columna | Uso |
| --- | --- |
| `titulo` | Obligatoria; no se aceptan títulos vacíos. |
| `descripcion` | Opcional; admite Markdown y saltos de línea en campos entre comillas. |
| `vencimiento` | Opcional; ISO 8601 con zona explícita, por ejemplo `2026-12-05T09:00:00-05:00` o `2026-12-05T14:00:00Z`. |
| `responsables` | Opcional; nombres de usuario del tablero separados por `;`, con o sin `@`. Si está vacía, se usan los responsables predeterminados. |

```csv
titulo,descripcion,vencimiento,responsables
Revisar inventario,**Importante:** validar existencias,2026-12-05T09:00:00-05:00,ana;carlos
Enviar informe,Resumen semanal,,
```

El lote se detiene ante un error. **Continuar / reintentar** solo envía filas pendientes o rechazadas explícitamente (400/401/403/404/429); espera y corrige permisos/destino según corresponda antes de reintentar. Las creadas, excluidas y de resultado incierto no se reenvían. Un timeout o respuesta inválida puede significar que Trello sí creó la tarjeta: revisa el tablero antes de iniciar otra importación de esa fila.

El progreso se conserva únicamente mientras permanezcas en la vista: descarga los resultados antes de salir. Los checklists y las etiquetas se configuran en la creación manual o directamente en Trello; no son columnas de este formato CSV.

### Crear propuestas con Gemini

Gemini es opcional: la creación manual y CSV siguen funcionando sin configurarlo.

1. En **Configuración → Gemini**, introduce una API key de
   [Google AI Studio](https://aistudio.google.com/apikey) y guarda los cambios.
   El modelo predeterminado es `gemini-3.5-flash-lite`; puedes cambiarlo por un
   modelo disponible en tu cuenta que admita respuestas JSON estructuradas.
   La API tiene cuotas y costes propios, independientes de la suscripción al chat de Gemini.
2. **Una tarjeta:** en **Creación manual → Completar con Gemini**, pega el texto y
   pulsa **Generar propuesta**. Revisa la vista previa: **Aplicar y reemplazar**
   sustituye título, descripción, vencimiento y checklists; **Conservar formulario**
   mantiene la edición anterior. Selecciona responsables y etiquetas en el formulario.
3. **Varias tarjetas:** en **Creación masiva**, selecciona **Origen → Texto con IA**,
   elige tablero, lista y responsables predeterminados y pulsa **Generar tareas**.
   Revisa, edita o excluye las propuestas antes de pulsar **Crear tarjetas en Trello**.
   Este flujo genera título, descripción y vencimiento; no genera checklists ni etiquetas.

Se admiten **20 000 caracteres** y hasta **50 propuestas** por generación. La respuesta
se limita a **1 MiB** y la solicitud a **60 segundos**, sin reintentos automáticos.
Cancelar o recibir una respuesta inválida conserva el borrador/origen anterior.
En el lote, una generación correcta reemplaza el origen cargado. Salir de la vista
cancela la generación y descarta sus resultados tardíos; los borradores no se guardan.

El texto se envía a Google Gemini junto con la fecha y zona horaria de Windows,
sin credenciales ni datos consultados de Trello. Se pide al modelo no inventar fechas
ni asignaciones. Las fechas ambiguas se dejan vacías con una observación para revisión;
comprueba siempre la interpretación y selecciona los responsables en la app. Gemini
solo propone contenido: la creación utiliza el cliente Trello y las reglas de envío
existentes, después de tu revisión. La clave de Gemini se guarda **sin cifrar** en SQLite.

### Consulta de tarjetas

Abre **Mis tarjetas**. Por defecto se muestran **Todas las pendientes** asignadas a tu cuenta: tarjetas sin completar y no archivadas. Usa el filtro **Estado**:

| Filtro | Tarjetas incluidas |
| --- | --- |
| Todas las pendientes | Todas las asignadas sin completar y no archivadas, incluidas las que no tienen fecha. |
| Vencidas | Pendientes con fecha menor o igual a la hora actual. |
| Próximas a vencer | Pendientes que vencen después de ahora y hasta el límite de anticipación configurado, inclusive (60 minutos por defecto). |
| Futuras | Pendientes con vencimiento posterior a ese límite. |
| Sin fecha | Pendientes sin fecha de vencimiento. |

Usa **Filtrar por tablero** para elegir un tablero o **Todos los tableros**.
El selector incluye los tableros con tarjetas pendientes no archivadas asignadas en la última
consulta y se combina con el filtro por estado. Si no se recibe el nombre de un
tablero, se muestra su identificador; las tarjetas sin `idBoard` solo aparecen
en **Todos los tableros**.

La columna **Lista**, a la derecha de **Tablero**, muestra la lista a la que
pertenece cada tarjeta. En la vista compacta aparece debajo del tablero. Si no
se recibe el nombre, se muestra su identificador; si faltan ambos datos,
aparece **Lista no disponible**. Historial reutiliza esta misma presentación.

Escribe en **Buscar tarjeta…** para buscar por parte del nombre, sin distinguir
mayúsculas y minúsculas e ignorando espacios al principio y al final del texto.
La búsqueda se combina con ambos filtros y utiliza las tarjetas ya descargadas.
Al borrar el texto se mantienen los filtros por tablero y estado. El texto se
conserva durante las actualizaciones de esa pantalla y se vacía al volver a abrirla.

La vista amplia presenta una tabla con tarjeta, tablero, vencimiento, estado y
el botón **Abrir en Trello**. En ventanas pequeñas las filas se reorganizan y
el tablero aparece debajo del título. Si no se recibe el nombre,
se muestra su identificador; si tampoco está disponible, **Tablero no disponible**.

Los cuatro indicadores resumen las tarjetas pendientes no archivadas de la última consulta, sin
depender de los filtros: **Pendientes**, **Próximas a vencer**, **Vencidas** y
**Al día**. Este último agrupa futuras y sin fecha; las completadas y archivadas no se cuentan.
La anticipación es la configurada para los avisos. Rojo y ámbar identifican
vencidas y próximas; cada fila también incluye un icono y una etiqueta de estado.
Un error muestra un aviso y guiones en los indicadores, en lugar de ceros que
puedan confundirse con una consulta correcta sin tarjetas.

La tabla muestra las tarjetas que coinciden con la búsqueda y ambos filtros.
Las tarjetas se muestran por vencimiento y después por nombre,
con las tarjetas sin fecha al final.
Cambiar los filtros utiliza la última consulta y recalcula el estado según la hora actual, sin hacer solicitudes de red ni enviar avisos. Los filtros se mantienen durante las comprobaciones de esa pantalla; si el tablero seleccionado deja de tener tarjetas, se vuelve a **Todos los tableros**. Al volver a abrir la pantalla comienza en **Todas las pendientes** y **Todos los tableros**.
**Comprobar ahora** actualiza los datos desde Trello y ejecuta la comprobación habitual de recordatorios. También se actualizan automáticamente con el intervalo configurado.

### Paginación de las tablas

**Mis tarjetas** e **Historial** muestran **25 filas por página** de forma predeterminada.
En el pie de la tabla, selecciona **10, 25 o 50** en el selector de cantidad y utiliza
las flechas **Página anterior** y **Página siguiente**. A su izquierda se indica
el rango visible y el total filtrado, por ejemplo, **26–50 de 63**.

La paginación se aplica después de la búsqueda y los filtros, utilizando las tarjetas
ya descargadas. Cambiar de página no consulta Trello ni envía notificaciones. Los
indicadores siguen resumiendo todas las tarjetas pendientes de la consulta.
Cambiar la búsqueda, un filtro o el tamaño de página vuelve a la primera página.
Al actualizar se conserva la página actual; si desaparece por una reducción de
resultados, se muestra la última disponible. Ante un error, los botones de navegación
se deshabilitan y la página seleccionada se conserva para la siguiente consulta correcta.
Al volver a abrir la pantalla se comienza en la primera página con 25 filas.

### Consultar el historial de tarjetas

Abre **Historial** para consultar las tarjetas completadas o archivadas que siguen
asignadas a tu cuenta. La consulta a Trello se realiza al abrir esta vista o pulsar
**Actualizar historial**, independientemente del monitor de recordatorios.

| Filtro | Tarjetas incluidas |
| --- | --- |
| Todas | Completadas o archivadas; una tarjeta con ambos estados aparece una sola vez. |
| Completadas | Marcadas como completadas (`dueComplete`), archivadas o no. No depende del nombre de la lista en Trello. |
| Archivadas | Archivadas (`closed`), completadas o no. |

La búsqueda por nombre y el filtro por tablero se combinan con el estado sin
realizar nuevas consultas. Los tableros disponibles corresponden a las tarjetas
del historial. Las filas distinguen **Completada y archivada** de **Archivada sin
completar**; no presentan una archivada como pendiente de recordatorio.

Este historial refleja el **estado actual en Trello**, no una copia permanente ni
un registro de eventos o notificaciones. Una tarjeta eliminada o que deje de estar
asignada a ti ya no aparecerá al actualizar; una tarjeta sin completar que se
desarchive volverá a Mis tarjetas. La fecha mostrada es el vencimiento, no la fecha
de completado o archivado. No se guardan nuevas tablas ni datos históricos locales.

## Compilación

### Trabajo con agentes y validación

Empieza por [AGENTS.md](AGENTS.md) y el [contexto mínimo](.ai/context.md).
Consulta solo la documentación específica necesaria para la tarea.
El [estado actual y los problemas conocidos](docs/KNOWN_ISSUES.md) distingue
hallazgos resueltos, parciales y pendientes con evidencia. Contrasta los informes
y planes antiguos con esa referencia y el código antes de asumir que un problema
sigue presente.

El proyecto incluye dos skills de OpenCode en `.opencode/skills/`:

- [`winui3-ui`](.opencode/skills/winui3-ui/SKILL.md): vistas WinUI/XAML, mockups,
  estilos, temas, accesibilidad y validación visual.
- [`trello-api`](.opencode/skills/trello-api/SKILL.md): integración con Trello,
  autenticación, permisos, errores HTTP y reintentos de creación.

OpenCode puede cargarlas cuando la tarea corresponda. También puedes pedirlas
explícitamente con `@winui3-ui` o `@trello-api` en tu mensaje. Sus procedimientos
complementan las reglas de `AGENTS.md` y enlazan la documentación del proyecto.

Para ejecutar formato, lint, análisis estático, pruebas funcionales y
arquitectónicas, auditoría de dependencias y compilación:

```powershell
powershell -NoProfile -File .\verify.ps1
```

Requisitos, comandos individuales y límites de cobertura en
[docs/TESTING.md](docs/TESTING.md). El comando no necesita credenciales de Trello.

Desde la raíz del proyecto:

```cmd
dotnet restore ".\TrelloNotifier\TrelloNotifier.csproj"
dotnet build ".\TrelloNotifier\TrelloNotifier.csproj" -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win10-x64
```

También puedes abrir `TrelloNotifier.sln` en Visual Studio, seleccionar la plataforma `x64` y ejecutar el proyecto.

### Verificar la lógica de recordatorios

Las pruebas de regresión utilizan .NET 9 y dobles de prueba para la API y las notificaciones, sin conectarse a Trello ni mostrar avisos reales de Windows:

```cmd
dotnet run --project ".\TrelloNotifier.Tests\TrelloNotifier.Tests.csproj"
```

## Publicación e instalador

Publica una versión autocontenida:

El perfil `TrelloNotifier/Properties/PublishProfiles/win10-x64.pubxml` incluye
tanto el runtime de .NET (`SelfContained`) como Windows App Runtime
(`WindowsAppSDKSelfContained`). Este último evita que el usuario tenga que
instalar Windows App Runtime 1.6 por separado para iniciar la interfaz WinUI.

```cmd
dotnet publish ".\TrelloNotifier\TrelloNotifier.csproj" -c Release -p:Platform=x64 -p:RuntimeIdentifier=win10-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o ".\artifacts\publish\win-x64"
```

Genera el instalador con Inno Setup 6:

```cmd
"%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "installer.iss"
```

El resultado se crea en `artifacts\installer\Trello-Notifier-Setup-x64.exe`.

Regenera el instalador después de publicar para que incluya los archivos nuevos.
Si distribuyes la aplicación sin instalador, copia toda la carpeta
`artifacts\publish\win-x64`; el ejecutable por sí solo no es suficiente.

## Datos locales

La configuración y el historial se almacenan en:

```text
%LOCALAPPDATA%\Trello Notifier\trello-notifier.db
```

SQLite se incluye con la aplicación y no necesita un servidor ni instalación adicional.
La base se crea al acceder por primera vez a las preferencias o al historial:

- `AppSettings` guarda la conexión, los intervalos, las opciones de avisos, el sonido y el tema en columnas.
- `NotificationHistory` guarda la clave de tarjeta y vencimiento y la fecha del último aviso, conservando su precisión y zona horaria original (offset).
- `PRAGMA user_version` identifica la versión del esquema. Una versión desconocida se rechaza sin sobrescribirla.

El esquema 3 añade la clave y el modelo opcionales de Gemini. Las bases de los esquemas
1 y 2 se actualizan automáticamente en una transacción, conservando preferencias e
historial. Al migrar desde el esquema 1 también se añade la preferencia de avisos
del esquema 2, activada por defecto.

Si existen `settings.json` o `notified-cards.json`, se importan juntos en una transacción. También se admite el historial antiguo de claves sin fechas: recibe una única hora de migración para evitar avisos duplicados inmediatos. Los campos de configuración ausentes conservan los valores predeterminados.

Una vez confirmada la migración, solo se lee y escribe SQLite. Los JSON originales se conservan como respaldo sin modificarlos; editarlos ya no cambia las preferencias. En instalaciones nuevas no se crean esos archivos.

Si un JSON es inválido, se informa del error y se revierte la importación completa. Corrige el archivo y vuelve a abrir Configuración o pulsa **Comprobar ahora** para reintentar. Una base ocupada, dañada o inaccesible produce un error visible; no se reemplaza por una base vacía ni se reinicia silenciosamente el historial. Las esperas por bloqueo se limitan a 5 segundos y el acceso desde la interfaz se realiza en segundo plano.

La API key y el token siguen **sin cifrar**, ahora dentro de SQLite y también en los respaldos JSON que existan. No compartas estos archivos ni los agregues al repositorio. Para respaldar la base, cierra la aplicación antes de copiarla; conserva cualquier archivo auxiliar SQLite si aparece tras un cierre inesperado.

## Estructura

```text
TrelloNotifier/
  Models/                 Configuración y datos de tarjetas
  Services/               API de Trello, monitor, persistencia y avisos
  DashboardPage.*         Consulta de tarjetas y filtros por estado
  SettingsPage.*          Credenciales y preferencias
installer.iss             Instalador para Windows x64
```
