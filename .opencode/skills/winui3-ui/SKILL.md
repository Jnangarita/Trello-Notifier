---
name: winui3-ui
description: Crear o modificar vistas WinUI 3 y XAML de Trello Notifier, adaptar mockups, estilos, navegación, temas, accesibilidad y comportamiento de la interfaz.
---

# Interfaz WinUI 3 de Trello Notifier

## Contexto y alcance

Aplica las reglas de [AGENTS.md](../../../AGENTS.md) y el
[contexto mínimo](../../../.ai/context.md). Si ya están cargados y no cambiaron,
no los releas. Esta skill añade un procedimiento específico de interfaz.
Las rutas de comandos se interpretan desde la raíz del repositorio.

Antes de editar, lee la vista afectada, su code-behind y los recursos pertinentes de
[Styles.xaml](../../../TrelloNotifier/Styles.xaml). Busca controles equivalentes en
las páginas existentes. Consulta [arquitectura](../../../docs/ARCHITECTURE.md)
solo si cambian navegación, responsabilidades o dependencias.

## Procedimiento

1. **Concretar el resultado visual.** Identifica ubicación, jerarquía, texto,
   alineación, tamaños y estados solicitados. Si el usuario aporta una imagen o
   mockup, úsalo como referencia y conserva las interacciones necesarias.
2. **Reutilizar el diseño existente.** Usa estilos y `ThemeResource` compartidos
   para paneles, botones, textos, insignias y colores de estado. Conserva las
   plantillas WinUI que proporcionan foco, hover y estados deshabilitados. Reutiliza
   `FontIcon` para iconos y acompáñalos de texto o un nombre accesible.
3. **Editar en la capa propietaria.** Mantén presentación en XAML y coordinación
   en code-behind. La navegación pertenece a
   [MainPage](../../../TrelloNotifier/MainPage.xaml.cs); los servicios se obtienen
   de [AppServices](../../../TrelloNotifier/AppServices.cs). Las reglas de negocio,
   el acceso a archivos y las llamadas HTTP permanecen en sus implementaciones actuales.
4. **Cubrir estados y ciclo de vida.** Representa carga, vacío, validación, error
   y resultado cuando correspondan. Evita doble envío, cancela cargas al salir o
   cambiar de selección y descarta respuestas anteriores antes de actualizar UI.
   Respeta los bloqueos de navegación durante creaciones en curso. Realiza IO y
   trabajo costoso fuera del hilo de interfaz.
5. **Adaptar el diseño.** Comprueba distribución amplia y compacta con los
   `AdaptiveTrigger` pertinentes. Permite envolver textos y desplazar contenido
   largo sin ocultar acciones. En listas virtualizadas, actualiza los estados
   visuales al cargar y reciclar filas, siguiendo `Loaded`/`DataContextChanged`.
   Evita setters en distintos grupos que compitan por una misma propiedad.
6. **Revisar accesibilidad y temas.** Comprueba teclado, orden de foco, nombres
   accesibles y contraste. Un estado debe tener texto o icono además de color.
   Verifica temas claro, oscuro y alto contraste según los controles modificados.

## Validación

Sigue los comandos y comprobaciones aplicables de
[TESTING.md](../../../docs/TESTING.md). Para compilar la interfaz sin sobrescribir
una instancia abierta desde `bin`, usa:

```powershell
dotnet build .\TrelloNotifier\TrelloNotifier.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win10-x64 --output .\artifacts\verify\app -warnaserror
```

- Comprueba las propiedades contra los controles reales de WinUI 3. No asumas que
  una propiedad disponible en WPF existe en WinUI; por ejemplo, `Grid` y
  `StackPanel` no proporcionan `IsEnabled` como los controles.
- Revisa la vista real con datos ficticios: distribución, temas, tamaños de
  ventana y acciones afectadas. Compara el resultado con la referencia visual.
  Una compilación correcta no sustituye esa revisión.
- No abras la app con datos privados para generar capturas. Si necesitas un host
  de prueba, enlaza la vista real y utiliza dobles locales sin red, credenciales,
  configuración ni logs del usuario.
- Añade pruebas solo cuando verifiquen comportamiento nuevo o una regresión
  relevante; los ajustes puramente visuales no necesitan tests que repliquen el XAML.
- Reporta qué se compiló y qué se comprobó visualmente. Si algo quedó bloqueado,
  indícalo sin presentar el mockup o una captura anterior como resultado actual.
