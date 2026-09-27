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
