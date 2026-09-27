# Flujo de trabajo

```text
UNDERSTAND → SEARCH → REUSE → PLAN → IMPLEMENT → TEST → REVIEW
```

1. **UNDERSTAND**: leer `AGENTS.md` y `.ai/context.md`; concretar aceptación y
   restricciones. Consultar solo documentación necesaria.
2. **SEARCH**: buscar símbolos/archivos relevantes, después instrucciones locales
   y tests. Por ejemplo: `rg -n "ShouldNotify|GetEligibleCards" TrelloNotifier TrelloNotifier.Tests`.
   Si `rg` no está en PATH, usar la búsqueda de símbolos/contenido del agente o
   editor; no es un requisito de `verify.ps1`. Usar búsqueda acotada, no volcados
   de todo el árbol ni `rg --no-ignore`.
3. **REUSE**: localizar reglas, servicios, modelos y estilos equivalentes.
   Aplicar REUSE → EXTEND → REFACTOR → CREATE y justificar cualquier duplicación.
4. **PLAN**: indicar archivos afectados y pruebas; no agregar capas por anticipación.
5. **IMPLEMENT**: cambio mínimo, compatible con JSON/API y convenciones actuales.
6. **TEST**: correr tests relacionados y `verify.ps1`; si cambia UI/notificación,
   comprobar manualmente en Windows. No usar secretos ni red real en tests.
7. **REVIEW**: usar la checklist, revisar el diff (o archivos cambiados si no hay
   Git), declarar resultados reales y bloqueos. Separar deuda previa de regresiones.

## Exclusiones compatibles
`.ignore` aplica a búsquedas con ripgrep incluso en esta copia sin `.git`;
`.gitignore` protege las rutas habituales al versionar. No hay configuración
previa de un agente/editor con un formato de exclusión propio comprobado.
No se presupone soporte universal de `.aiignore`. En herramientas que no respeten
`.ignore`, limitar explícitamente las búsquedas a fuentes y tests; las reglas
de `AGENTS.md` siguen aplicando a lecturas directas.
