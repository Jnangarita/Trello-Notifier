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

### 2. Generar el token de lectura

1. Copia la siguiente URL y sustituye `TU_API_KEY` por la clave obtenida en el paso anterior:

   ```text
   https://trello.com/1/authorize?expiration=never&scope=read&response_type=token&name=Trello%20Notifier&key=TU_API_KEY
   ```

2. Abre la URL completa en el navegador con tu sesión de Trello iniciada.
3. Comprueba que estás autorizando la cuenta correcta y pulsa **Permitir / Allow**.
4. Copia el **token** que Trello muestra al finalizar.

El parámetro `scope=read` solicita el permiso de lectura que necesita la aplicación. `expiration=never` indica que el token no caduca automáticamente; puedes revocarlo desde la configuración de tu cuenta de Trello, en **Aplicaciones**.

### 3. Introducir los datos en Trello Notifier

1. Abre **Configuración** en Trello Notifier.
2. Completa estos campos:

   | Campo | Valor |
   | --- | --- |
   | **URL base de la API** | `https://api.trello.com` |
   | **API key** | La clave copiada de **Trello Auth**. |
   | **Token** | El token obtenido al autorizar el acceso de lectura. |

   La URL base no debe incluir `/1/members/me/cards` ni las credenciales: la aplicación añade la ruta y los parámetros necesarios.

3. Pulsa **Probar conexión** para comprobar los datos.
4. Si la conexión es correcta, pulsa **Guardar cambios**.

Si aparece **Conexión correcta. El servidor devolvió 0 tarjeta(s) abierta(s).**, la consulta funcionó, pero no encontró tarjetas abiertas asignadas a esa cuenta. Tener acceso a un tablero no significa que sus tarjetas estén asignadas a ti: abre una tarjeta, añádete en **Miembros**, comprueba que no esté archivada y repite la prueba.

### 4. Comprobar los avisos de escritorio

1. En **Configuración**, pulsa **Probar notificación** para comprobar que Windows muestra el aviso.
2. En Trello, elige una tarjeta abierta asignada a ti y establece una fecha y hora de vencimiento dentro de los próximos **60 minutos**, si mantienes la anticipación predeterminada. Deja el vencimiento sin marcar como completado.
3. En Trello Notifier, abre **Mis tarjetas** y pulsa **Comprobar ahora**.
4. Mantén la aplicación en ejecución para recibir los avisos. Por defecto consulta Trello cada **5 minutos**.

La aplicación avisa de tarjetas próximas a vencer y, opcionalmente, de vencidas pendientes; no replica las notificaciones de comentarios o menciones de Trello.

Referencia: [documentación oficial de autorización de Trello](https://developer.atlassian.com/cloud/trello/guides/rest-api/authorization/).

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
6. En Trello Notifier, reemplaza **URL base de la API** por esa URL. No agregues `/1/members/me/cards` al final.
7. Deja vacíos **API key** y **Token**, pulsa **Probar conexión** y después **Guardar cambios**.
8. Regresa a **Mis tarjetas** y pulsa **Comprobar ahora**.

La aplicación añadirá automáticamente la ruta `/1/members/me/cards` y los parámetros de consulta. Para volver al servicio real, restaura la URL `https://api.trello.com` e introduce las credenciales de Trello.

Para probar el filtro por tablero, añade `"idBoard": "tablero-prueba"` a las
tarjetas del ejemplo y agrega al mock una respuesta `200` para
`GET /1/members/me/boards`, con cuerpo
`[{"id":"tablero-prueba","name":"Tablero de prueba"}]`.
Las respuestas antiguas sin `idBoard` siguen siendo compatibles y no requieren
esta segunda ruta.

## Funcionamiento

Por defecto, la aplicación consulta Trello cada **5 minutos**, avisa sobre tarjetas que vencen durante los próximos **60 minutos** y repite sus recordatorios cada **30 minutos** mientras sigan dentro del periodo de aviso.

### Configurar la frecuencia de los recordatorios

La consulta y los filtros de **Mis tarjetas** son independientes de qué tarjetas generan avisos.

1. Abre **Configuración**, en la sección **Monitor**.
2. Define **Avisar con esta anticipación (minutos)** para elegir qué vencimientos próximos se incluyen.
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

Abre **Mis tarjetas**. Por defecto se muestran **Todas** las tarjetas abiertas asignadas a tu cuenta, sin incluir las archivadas. Usa **Filtrar por estado**:

| Filtro | Tarjetas incluidas |
| --- | --- |
| Todas | Todas las abiertas asignadas, incluidas completadas y sin fecha. |
| Vencidas | Pendientes con fecha menor o igual a la hora actual. |
| Próximas a vencer | Pendientes que vencen después de ahora y hasta el límite de anticipación configurado, inclusive (60 minutos por defecto). |
| Futuras | Pendientes con vencimiento posterior a ese límite. |
| Sin fecha | Pendientes sin fecha de vencimiento. |
| Completadas | Vencimiento marcado como completado en Trello (`dueComplete`), independientemente de la fecha; no es el nombre de una lista del tablero. |

Usa **Filtrar por tablero** para elegir un tablero o **Todos los tableros**.
El selector incluye los tableros con tarjetas abiertas asignadas en la última
consulta y se combina con el filtro por estado. Si no se recibe el nombre de un
tablero, se muestra su identificador; las tarjetas sin `idBoard` solo aparecen
en **Todos los tableros**.

Escribe en **Buscar tarjeta…** para buscar por parte del nombre, sin distinguir
mayúsculas y minúsculas e ignorando espacios al principio y al final del texto.
La búsqueda se combina con ambos filtros y utiliza las tarjetas ya descargadas.
Al borrar el texto se mantienen los filtros por tablero y estado. El texto se
conserva durante las actualizaciones de esa pantalla y se vacía al volver a abrirla.

Cada tarjeta muestra su tablero debajo del título. Si no se recibe el nombre,
se muestra su identificador; si tampoco está disponible, **Tablero no disponible**.

La pantalla indica cuántas tarjetas coinciden con la búsqueda y ambos filtros y el total consultado. Se ordenan por vencimiento y después por nombre, con las tarjetas sin fecha al final.
Cambiar los filtros utiliza la última consulta y recalcula el estado según la hora actual, sin hacer solicitudes de red ni enviar avisos. Los filtros se mantienen durante las comprobaciones de esa pantalla; si el tablero seleccionado deja de tener tarjetas, se vuelve a **Todos los tableros**. Al volver a abrir la pantalla comienza en **Todas** y **Todos los tableros**.
**Comprobar ahora** actualiza los datos desde Trello y ejecuta la comprobación habitual de recordatorios. También se actualizan automáticamente con el intervalo configurado.

## Compilación

### Trabajo con agentes y validación

Empieza por [AGENTS.md](AGENTS.md) y el [contexto mínimo](.ai/context.md).
Consulta solo la documentación específica necesaria para la tarea.
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
