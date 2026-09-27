# Dónde ubicar el código

| Tipo de código | Ubicación real / criterio |
| --- | --- |
| UI y handlers | `TrelloNotifier/*Page.xaml` y `.xaml.cs`; ventana en `MainWindow.*` |
| Recursos UI reutilizables | `TrelloNotifier/Styles.xaml`; imágenes/iconos en `Assets/` |
| Arranque y composición | `App.xaml.cs`, `AppServices.cs` |
| Modelos de datos/dominio | `Models/TrelloCard.cs`, `AppSettings.cs`, `MonitorSnapshot.cs` |
| DTOs | `TrelloCard` ya representa el contrato JSON; no duplicarlo por defecto |
| Entidades persistidas | Sin ORM: `AppSettings` y diccionario de historial se mapean a tablas SQLite en `SettingsStore` |
| Repositories y sus implementaciones | No existen; `Services/SettingsStore.cs` resuelve persistencia |
| Services | `TrelloNotifier/Services/`; reutilizar los cinco servicios existentes |
| Controllers | No hay HTTP servidor; eventos de UI en los code-behind existentes |
| Use cases | No hay capa separada; coordinación en `DueCardMonitor`, reglas en `ReminderSchedule` |
| ViewModels/mappers | `Models/TrelloCardViewModel.cs` transforma tarjeta a textos; no framework MVVM |
| Utilities/extensions/validators | No carpetas genéricas; buscar primero en la clase responsable, p. ej. `TryBuildSettings` |
| Constants | Defaults en `AppSettings`, límites en validación/XAML y reglas en `ReminderSchedule`; no catálogo global |
| Tests | `TrelloNotifier.Tests/Program.cs`, `MonitorDoubles.cs`, `ArchitectureTests.cs` |
| Configuración de build | `.csproj`, perfil `Properties/PublishProfiles/win10-x64.pubxml`, `Directory.Build.props` |
| Validación | `verify.ps1`, `.editorconfig`, runner existente de tests |
| Distribución | `installer.iss`; salida en `artifacts/` (no fuente) |
| Documentación IA | `AGENTS.md`, `.ai/`, `docs/`; instrucciones locales solo para tests |

No crear carpetas/capas para categorías que el proyecto no tiene. Una nueva
responsabilidad solo amerita un archivo después de buscar equivalentes y
justificar su necesidad. `TrelloNotifier.sln` contiene únicamente la app;
el runner se invoca explícitamente desde `verify.ps1`.

`bin/`, `obj/`, `Generated Files/`, `.vs/`, `.idea/` y `artifacts/` son salidas o
estado local. No editar WinRT/XAML generado. La base SQLite y los respaldos JSON
de usuario viven fuera del repositorio, en `%LOCALAPPDATA%\Trello Notifier`, y no
se usan como fixtures.
