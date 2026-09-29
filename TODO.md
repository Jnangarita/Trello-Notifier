# Pendientes

- [ ] Añadir la opción **«Añadir desde el correo electrónico»** para crear tarjetas en la bandeja de entrada de Trello, tomando como referencia la captura proporcionada.
  - Mostrar `inbox@app.trello.com` con una opción para copiarla e indicar que el mensaje debe enviarse desde la dirección de correo asociada a la cuenta de Trello.
  - Permitir configurar y copiar la dirección personal de Trello que recibe mensajes desde cualquier dirección de correo.
  - Indicar que cualquier persona con acceso a esa dirección personal puede añadir tarjetas en nombre del usuario.
  - Contemplar las acciones **«Restablecer dirección de correo electrónico»** y **«Enviarme esta dirección por correo electrónico»**, previa verificación del soporte de Trello para integrarlas.
  - No incluir direcciones personales reales en el código, la documentación ni las pruebas.

---

## V1

- [ ] Ejecutar la aplicación en la bandeja del sistema.
  - Mostrar un icono junto al reloj de Windows.
  - Mantener el monitor funcionando al cerrar la ventana.
  - Incluir las opciones **Abrir**, **Comprobar ahora** y **Salir** en el menú del icono.
  - Explicar al usuario que cerrar la ventana mantiene los avisos activos y que **Salir** finaliza la aplicación.

- [x] Mostrar el nombre del tablero en cada tarjeta.
  - Colocarlo debajo del título para identificar su origen, especialmente al seleccionar **Todos los tableros**.

- [x] Agregar búsqueda por nombre de tarjeta.
  - Incluir un cuadro **«Buscar tarjeta…»**.
  - Combinar la búsqueda con los filtros por tablero y estado.
  - Buscar sobre las tarjetas ya descargadas, sin nuevas solicitudes a Trello.

- [ ] Proteger las credenciales guardadas.
  - Cifrar la API key y el token mediante DPAPI de Windows.
  - Migrar las credenciales existentes en SQLite conservando la configuración de los usuarios.

- [ ] Completar la validación y distribución de la primera versión.
  - Actualizar a una versión de .NET con soporte y resolver el bloqueo de `verify.ps1`.
  - Ejecutar la verificación completa del proyecto.
  - Probar visualmente el filtro por tablero y su combinación con el filtro por estado.
  - Comprobar las notificaciones, la apertura de tarjetas y el arranque con Windows.
  - Probar la instalación en un equipo sin herramientas de desarrollo.
  - Generar el instalador con los cambios actuales.

**Prioridad sugerida de los pendientes:** bandeja del sistema → protección de
credenciales → validación e instalador. La creación de tarjetas por correo queda
para una versión posterior.

---

## V2 — Control de notificaciones

La bandeja del sistema, la protección de credenciales y la actualización de .NET
se mantienen en los pendientes de V1; esta propuesta parte de esas mejoras.

- [ ] Incorporar una pausa global de notificaciones.
  - Permitir pausar durante 30 minutos, 1 hora, hasta mañana o hasta reactivarlas manualmente.
  - Añadir el control al menú de la bandeja del sistema y mostrar cuándo se reanudarán los avisos.
  - Mantener la consulta de tarjetas durante la pausa y conservar la preferencia al reiniciar.

- [ ] Permitir posponer recordatorios por tarjeta.
  - Ofrecer opciones como 15 minutos, 1 hora o al día siguiente, sin afectar a las demás tarjetas.
  - Mostrar hasta cuándo está pospuesta una tarjeta y permitir cancelar la posposición.
  - Conservar las posposiciones al cerrar y abrir la aplicación.
  - Definir qué ocurre si cambia el vencimiento en Trello y cómo interactúa con la pausa global y los horarios.

- [ ] Configurar días y horarios de notificaciones.
  - Permitir seleccionar días laborales y franjas horarias.
  - Definir el comportamiento ante cambios de zona horaria y horario de verano.
  - Agrupar los avisos pendientes al comenzar el horario para evitar una acumulación de notificaciones individuales.
  - Definir qué ocurre si la aplicación estaba cerrada durante el horario previsto.

- [ ] Ampliar las preferencias de la bandeja del sistema implementada en V1.
  - Añadir la opción explícita **«Al cerrar la ventana, mantener la aplicación en la bandeja»**.
  - Mostrar el estado de pausa en el menú del icono.

- [ ] Mostrar el estado de sincronización.
  - Indicar la última sincronización correcta y la próxima comprobación prevista.
  - Distinguir credenciales inválidas, falta de conexión, límites de solicitudes y errores de Trello con mensajes claros.

### Base técnica

- [ ] Completar el cierre asíncrono del monitor.
  - Esperar las operaciones activas antes de liberar el semáforo y los recursos.
  - Añadir pruebas del cierre y reinicio durante una comprobación.

- [ ] Reforzar la seguridad de las conexiones y los diagnósticos.
  - Definir una política de HTTPS y de envío de credenciales a servidores alternativos, conservando el uso de mocks con datos ficticios.
  - Evitar que los logs y mensajes de error expongan tokens, URLs autenticadas o datos privados.

- [ ] Hacer reproducible y automática la validación.
  - Seleccionar el SDK mediante `global.json` con una política de actualización definida.
  - Ejecutar `verify.ps1` en CI sobre Windows con las herramientas necesarias para WinUI.

- [ ] Añadir pruebas del cliente HTTP real mediante respuestas simuladas.
  - Cubrir respuestas correctas, datos ausentes o inválidos, errores HTTP, cancelación, timeouts y límites de solicitudes.
  - Usar credenciales ficticias y evitar conexiones reales a Trello.

**Prioridad sugerida:** cierre del monitor y seguridad → pausa global → posponer
por tarjeta → horarios → estado de sincronización. Incorporar CI y pruebas junto
con los cambios relacionados.

## Ideas para versiones posteriores

- [ ] Añadir un historial visible de notificaciones.
  - Mostrar qué avisos se enviaron, cuándo y para qué tarjeta, con acceso a Trello.
  - Definir retención y borrado; el estado actual de deduplicación no representa un historial completo de envíos.

- [ ] Configurar reglas por tablero.
  - Seleccionar qué tableros generan avisos y personalizar anticipación o sonido.
  - Definir la precedencia entre reglas por tablero, preferencias globales, horarios y posposiciones.

- [ ] Mostrar un resumen diario.
  - Incluir tarjetas vencidas y tarjetas que vencen hoy a una hora configurable.
  - Definir si se muestra al abrir la aplicación cuando esta estaba cerrada a la hora programada.

- [ ] Permitir acciones sobre tarjetas desde la aplicación.
  - Marcar el vencimiento como completado o modificar la fecha.
  - Verificar el contrato de la API y solicitar permisos de escritura en Trello para estas acciones.
  - Manejar errores de guardado y actualizar el estado mostrado tras confirmar el cambio.

- [ ] Conservar filtros y búsqueda al reiniciar la aplicación.
- [ ] Permitir ordenar tarjetas por vencimiento, nombre o tablero.
- [ ] Añadir vistas **Hoy** y **Esta semana** para facilitar la planificación.
- [ ] Mejorar la accesibilidad y la navegación por teclado.
  - Revisar foco visible, contraste y navegación entre controles.
