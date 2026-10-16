# Iteración 41 — visibilidad según el juego

Fecha: 2026-10-13. Rama local `feature/3ds-progressive-ui`.

Se comparte una política de compatibilidad entre navegación y presentación. Las tarjetas incompatibles se excluyen del árbol de interfaz, en lugar de permanecer visibles con botones deshabilitados:

- Gen6: intercambios Progressive, Catch Zones y O-Powers.
- Gen7: parcheos de estáticos y explorador OWSE.
- USUM: Player Level Caps, Relearner y CRO Recipes.
- Tiendas: editor Gen6 y editor USUM; se oculta su acceso en SM.

La navegación directa y los clicks programáticos rechazan estos destinos incompatibles antes de abrir el editor. MT/MO Gen6 usa una etiqueta propia. Automatización permite abrir estáticos Gen6, que ya estaban implementados pero tenían un acceso deshabilitado. El selector de idiomas solo ofrece ocho entradas en Gen6; Gen7 conserva diez. Las funciones compartidas como Mega Evolutions, Pickup y Type Chart continúan disponibles en ambas generaciones. Los controles personales Gen7 y de Z-Moves ya tenían visibilidad condicionada y se conservan.

## Validación local

Debug compila sin errores, con 79 advertencias. Pasan 1900 regresiones con datos sintéticos XY, ORAS, SM y USUM. Las nuevas pruebas WPF verifican presencia/ausencia de botones, idiomas, etiquetas y bloqueo de navegación directa y por evento. Las pruebas no solicitan validación en juego ni modifican ROM reales.

No se declara terminada la migración: siguen pendientes proyecciones y ejecutor Batch Gen6, herramientas vanilla restantes y pruebas finales de rebuild, empaquetado y juego. Esta iteración cierra la presentación y navegación de las secciones revisadas; no demuestra por sí sola paridad de todos los controles internos de todas las herramientas.
