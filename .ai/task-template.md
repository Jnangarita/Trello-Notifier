# Task

## Requirement
[Descripción, criterios de aceptación y límites del cambio]

## Before coding
* leer `AGENTS.md` y `.ai/context.md`;
* localizar implementación relacionada e instrucciones locales;
* identificar archivos afectados;
* buscar componentes reutilizables;
* localizar tests existentes;
* verificar restricciones arquitectónicas y riesgos del área.

## Implementation
Realizar únicamente el cambio mínimo necesario. Reutilizar antes de crear.
[Archivos previstos y razón del cambio]

## Validation
Ejecutar `powershell -NoProfile -File .\verify.ps1`: formato, lint, análisis
estático, tests, architecture tests, auditoría y build.
[Prueba relacionada o comprobación UI adicional]

## Final review
Revisar duplicación, seguridad, arquitectura, rendimiento y manejo de errores.
Indicar archivos cambiados, comandos, resultados y bloqueos; no declarar completa
una tarea con validaciones relevantes fallidas o pendientes.
