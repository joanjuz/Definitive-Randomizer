# 3DS Progressive — Hito 02: diálogo oscuro y editor de movimientos

## Alcance de esta entrega
- Versión de desarrollo: v0.2.0.
- Rama: feature/3ds-progressive-ui.
- Sin modificaciones de pk3DS.WinForms ni pk3DS.Core.
- Respeta la validación realizada por el usuario: el cambio de una estadística se reflejó en pk3DS Progressive.

## Cambios visuales
Se incorpora StudioDialog (WPF) con tema Dark Graphite, botones de acción y cancelación, mensajes explicativos y modal con propietario. Sustituye los cuadros MessageBox del editor de Pokémon para errores, validaciones, cambios sin guardar, confirmación y éxito de guardado. La selección de carpeta del sistema sigue siendo nativa de Windows.

## Segundo módulo funcional: Movimientos
- Navegación lateral: Movimientos, habilitado tras cargar un proyecto.
- Pantalla WPF propia, con búsqueda, lista de movimientos, tarjetas, inspector y estado de edición.
- Campos editables: tipo, categoría (estado/físico/especial), potencia, precisión, PP y prioridad.
- Sincronización del GARC y relectura del registro guardado.
- X/Y: registro GARC directo.
- ORAS, Sol/Luna y USUM: contenedor mini WD, como hace Main.B_Move_Click en pk3DS.
- Preserva los demás bytes de cada movimiento, incluidos efectos, flags y Z-Moves.
- Verificación de todas las entradas antes de reemplazar el GARC; validación del SHA-256 original para detectar cambios externos.
- Reemplazo atómico de archivo; no crea backups automáticamente. No se extraen ni suben recursos de ROM.

## Paridad inicial

| Función | Estado |
| --- | --- |
| Listar/buscar movimientos | Implementado, sin prueba de juego aún |
| Leer tipo, categoría, potencia, precisión, PP y prioridad | Implementado, requiere prueba manual |
| Guardar estos campos y preservar otros | Implementado, requiere prueba manual |
| Efectos, multigolpe, flags, Z-Moves, estados, targets y randomización | Aún pendientes de interfaz |
| Balance Moves, CRO patches, textos y demás Progressive | Aún pendientes |
| Interfaz integrada en una única vista del shell | Pendiente de refinamiento; ventana propia provisional |
| Ventanas de confirmación 100 % Dark Graphite | Implementado, requiere revisión visual |

## Pruebas solicitadas
1. Abrir la nueva preview con una copia del proyecto de Ultra Sol.
2. Guardar una stat de Pokémon y verificar que el nuevo diálogo combine con la app y que se siga guardando.
3. Abrir Movimientos, editar la potencia o los PP de un movimiento, guardar y abrir el mismo GARC desde pk3DS Progressive.
4. Confirmar que se conservaron los demás campos del movimiento.
5. Buscar y seleccionar otro movimiento, comprobando los avisos por cambios sin guardar.
6. Si alguna prueba falla, compartir mensaje de error y captura; no hacer merge todavía.

## Próximos hitos
- Completar los campos vanilla del editor Personal.
- Ampliar el Move Editor con efectos, multigolpe, flags, Z-Moves y funciones Progressive.
- Integrar el segundo editor en la composición persistente con barra lateral + inspector.
- Continuar entrenadores, encuentros y objetos según la matriz de paridad V4.
