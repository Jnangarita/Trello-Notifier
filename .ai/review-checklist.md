# Revisión del cambio

## Arquitectura
* [ ] código ubicado en la capa correcta
* [ ] dependencias respetadas
* [ ] no se introdujeron dependencias circulares
* [ ] no se agregaron abstracciones innecesarias

## Duplicación
* [ ] se buscaron implementaciones existentes
* [ ] no se duplicó lógica
* [ ] se reutilizaron componentes existentes

## Código
* [ ] naming consistente
* [ ] código muerto eliminado cuando fue introducido por el cambio
* [ ] imports limpios
* [ ] manejo correcto de errores y cancelación
* [ ] sin magic values evitables
* [ ] contratos JSON y valores por defecto compatibles

## Seguridad
* [ ] sin secrets
* [ ] sin datos sensibles en logs
* [ ] inputs externos validados (URL, JSON, intervalos)
* [ ] acceso a datos seguro
* [ ] mocks sin credenciales reales; auditoría NuGet revisada

## Rendimiento
* [ ] sin trabajo pesado innecesario en UI/main thread
* [ ] sin consultas innecesarias
* [ ] sin llamadas de red innecesarias
* [ ] sin loops o procesos costosos evitables
* [ ] suscripciones y recursos liberados; sin duplicar el monitor

## Tests
* [ ] tests existentes pasan
* [ ] lógica nueva tiene cobertura apropiada
* [ ] bug fixes tienen regression test cuando corresponde
* [ ] `verify.ps1` pasa y se reportó cualquier bloqueo
* [ ] UI/notificaciones comprobadas manualmente cuando corresponde

Marcar como no aplicable con motivo los puntos ajenos a la tarea; no confundir
una revisión manual con una garantía de un analizador automático.
