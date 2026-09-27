# Ejecutar Trello Notifier en JetBrains Rider

Esta guía explica cómo compilar, ejecutar y depurar el proyecto desde Rider en Windows.

## 1. Abrir la solución

1. Abre JetBrains Rider.
2. Selecciona **Open** y abre `TrelloNotifier.sln`, ubicado en la raíz del proyecto.
3. Espera a que Rider cargue la solución y restaure los paquetes NuGet.

Necesitas tener instalado el **SDK de .NET 9**, según los requisitos del proyecto. Puedes comprobar los SDK disponibles en la terminal de Rider:

```powershell
dotnet --list-sdks
```

El proyecto de escritorio tiene como destino `net6.0-windows10.0.19041.0`; el SDK utilizado para compilar y el framework de destino son conceptos distintos.

## 2. Seleccionar la configuración de compilación

En la barra superior de Rider, busca el **selector de configuración de compilación** y selecciona:

```text
Debug | x64
```

Este selector es diferente del selector de configuración de ejecución.

Para revisar la configuración de la solución:

1. Abre el selector de configuración de compilación.
2. Selecciona **Edit Solution Configurations…**.
3. Selecciona la configuración de solución **Debug | x64**.
4. Comprueba que, en la fila del proyecto `TrelloNotifier`:
   - La configuración sea **Debug**.
   - La plataforma sea **x64**.
   - La casilla **Build** esté marcada.
5. Confirma con **OK**.

La ubicación de los controles puede variar según la versión y la interfaz de Rider.

## 3. Compilar el proyecto por primera vez

Abre la terminal integrada de Rider en la raíz del proyecto, donde está `TrelloNotifier.sln`, y ejecuta:

```powershell
dotnet restore ".\TrelloNotifier\TrelloNotifier.csproj"
dotnet build ".\TrelloNotifier\TrelloNotifier.csproj" -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win10-x64
```

Comprueba que la compilación termine correctamente. El ejecutable se genera en:

```text
TrelloNotifier\bin\x64\Debug\net6.0-windows10.0.19041.0\win10-x64\Trello Notifier.exe
```

## 4. Crear la configuración de ejecución

1. Abre **Run → Edit Configurations…**.
2. Pulsa el botón **+** para agregar una configuración.
3. Selecciona **.NET Executable**.
4. En **Name**, escribe:

   ```text
   TrelloNotifier Desktop
   ```

5. En **Executable path**, selecciona el archivo:

   ```text
   C:\Users\jnang\Documentos\Repositorio\util-apps\trello-notifier\TrelloNotifier\bin\x64\Debug\net6.0-windows10.0.19041.0\win10-x64\Trello Notifier.exe
   ```

6. En **Working directory**, selecciona la carpeta que contiene el ejecutable:

   ```text
   C:\Users\jnang\Documentos\Repositorio\util-apps\trello-notifier\TrelloNotifier\bin\x64\Debug\net6.0-windows10.0.19041.0\win10-x64
   ```

7. Deja vacío el campo de argumentos del programa.

Si guardaste el proyecto en otra ubicación, ajusta las rutas para que apunten a tu copia local.

## 5. Compilar automáticamente antes de ejecutar

En la misma ventana **Run → Edit Configurations…**:

1. Selecciona **TrelloNotifier Desktop**.
2. Busca la sección **Before launch**, normalmente en la parte inferior.
3. Pulsa el botón **+ de esa sección**, no el botón para crear otra configuración.
4. Selecciona **Build solution**.
5. Si ya aparece **Build solution**, no lo agregues otra vez.
6. Pulsa **Apply → OK**.

La configuración debe quedar aproximadamente así:

```text
TrelloNotifier Desktop
  Tipo: .NET Executable
  Executable path: ...\win10-x64\Trello Notifier.exe
  Working directory: ...\win10-x64

  Before launch:
    Build solution
```

**Build solution** utiliza la configuración de compilación activa de la solución. Por eso debes mantener seleccionado **Debug | x64**, como se explicó en el paso 2.

## 6. Ejecutar o depurar

1. En el selector de configuración de ejecución de la barra superior, selecciona **TrelloNotifier Desktop**.
2. Comprueba que la configuración de compilación siga siendo **Debug | x64**.
3. Pulsa **Run** para ejecutar o **Debug** para depurar.

Rider compilará la solución antes de iniciar `Trello Notifier.exe`. Si la compilación falla, revisa los errores en la ventana de compilación antes de continuar.

Para depurar, puedes colocar puntos de interrupción en los archivos C# antes de pulsar **Debug**.

## Por qué aparece el error de `.appxrecipe`

Si ejecutas la configuración UWP que Rider generó para `TrelloNotifier`, puedes recibir este error:

```text
Error running 'TrelloNotifier'
Appx recipe file [...\TrelloNotifier.build.appxrecipe] does not exist
```

El proyecto está configurado como una aplicación **WinUI 3 sin empaquetar**. En `TrelloNotifier/TrelloNotifier.csproj` figura:

```xml
<WindowsPackageType>None</WindowsPackageType>
```

Sin embargo, la configuración de ejecución original de Rider es de tipo **UWP** e intenta desplegar un paquete utilizando un archivo `.appxrecipe`.

Para resolver ese error, selecciona la configuración **TrelloNotifier Desktop**, de tipo **.NET Executable**, creada en esta guía. No necesitas crear manualmente el archivo `.appxrecipe` ni cambiar el proyecto a MSIX.

## Si el ejecutable no aparece

1. Revisa que la compilación del paso 3 termine sin errores.
2. Comprueba que hayas utilizado **Debug**, **x64** y `win10-x64`.
3. Verifica que el archivo se llama **`Trello Notifier.exe`**, con un espacio entre `Trello` y `Notifier`.
4. Comprueba que **Executable path** y **Working directory** apunten a la carpeta de salida correcta.

## Referencias de Rider

- [Configuración de ejecución de .NET Executable](https://www.jetbrains.com/help/rider/Run_Debug_Configuration_dotNet_Executable.html)
- [Tareas Before launch](https://www.jetbrains.com/help/rider/Run_Debug_Configurations_dialog.html#before-launch-options)
- [Configuraciones de compilación](https://www.jetbrains.com/help/rider/Build_Configurations.html)