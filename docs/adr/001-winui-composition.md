# ADR 001 — WinUI sin empaquetar y composición manual

Status: Accepted

## Context
Se necesita una UI Windows con notificaciones y ejecución de escritorio.

## Decision
La implementación actual usa WinUI 3/XAML con code-behind, `WindowsPackageType=None`,
composición estática en `AppServices` y dependencias concretas por constructor
en `DueCardMonitor`. Evidencia: csproj, páginas, AppServices y perfil de publish.
Distribución autocontenida e instalador Inno Setup (`installer.iss`).

## Reason
README confirma que parte de una plantilla WinUI existente. No hay evidencia
del motivo original de elegir composición estática frente a un contenedor DI;
no se infiere una decisión de Clean Architecture o MVVM.

## Consequences
Arranque y plataforma Windows son explícitos; las páginas coordinan servicios.
No agregar un framework DI/MVVM por defecto. El acceso estático y la referencia
de notificaciones a `App.GetAssetPath` limitan el aislamiento de UI.
