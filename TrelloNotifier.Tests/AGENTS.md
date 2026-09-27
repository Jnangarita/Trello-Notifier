# Reglas específicas de pruebas

- Ejecutable .NET 9, no proyecto xUnit/NUnit: usar `dotnet run --project
  TrelloNotifier.Tests/TrelloNotifier.Tests.csproj` desde la raíz.
- Reutilizar `Run`, `Check`, `Card` y `Fixture` de `Program.cs`.
- El csproj enlaza fuentes de producción; no copiar su implementación aquí.
- `MonitorDoubles` reemplaza API y notificaciones por nombre/namespace. No
  enlazar sus implementaciones reales ni agregar llamadas a red o UI.
- Fixtures en directorio temporal propio con limpieza; fechas fijas para reglas.
- Arquitectura usa Roslyn del SDK de compilación, no un paquete externo.
  Mantener casos negativos del guard; no relajar reglas para acomodar un fallo.
- Después de cambiar fuentes enlazadas, compilar también la app WinUI: los
  tests corren bajo net9.0, la app bajo net6.0-windows.
