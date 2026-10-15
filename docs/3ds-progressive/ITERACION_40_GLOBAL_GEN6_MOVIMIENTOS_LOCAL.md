# Iteración 40 — Global Template y movimientos Gen6

Fecha: 2026-10-13. Rama: `feature/3ds-progressive-ui`. Trabajo local, sin publicación.

Se recupera la opción original de incluir MO en movimientos huevo Gen6. Por defecto se excluyen los siete movimientos originales de campo; al habilitar la opción se permiten. Gen6 excluye 165 y 621, como EggMoveEditor6; las exclusiones adicionales de movimientos Gen7 se conservan exclusivamente en Gen7. No se necesita leer code.bin: el código original solo usa las MO estándar cuando la opción está desactivada y no excluye MO cuando está activada.

Global Template ahora importa y exporta `EggMoveEditor6` (Expand, Count, STAB, STABPercent y CHK_HMs) y `TMHMEditor6` (CHK_RandomizeField y CHK_RandomizeHM). La opción nativa de randomizar MT se conserva en ProgressiveApp porque no tiene control equivalente en el formulario original. Se mantienen controles desconocidos y alcance. No se aplica ninguna operación al importar o exportar.

## Validación

- Debug: 0 errores, 79 advertencias.
- 1840 comprobaciones sintéticas XY/ORAS/SM/USUM, incluidas las regresiones WPF.
- Comparación de propuestas de movimientos huevo XY/ORAS contra EggMoveRandomizer con la misma semilla, para ambas opciones MO, y comprobación de que las fuentes no cambian.
- Importación/exportación de los controles Gen6, opciones nativas sin equivalente y controles futuros; preservación del ejecutable al transferir opciones MT/MO.
- Batch continúa rechazando recetas nativas Gen6 de movimientos huevo y máquinas: el ejecutor empaquetado usa formularios Gen7. Habilitarlas ahora produciría una falsa compatibilidad.

La versión estable completa sigue pendiente. Esta iteración no prueba nuevos cambios en juego ni cierra los módulos restantes de Global/Batch, herramientas vanilla, rebuild real y empaquetado final. Wild, ROM originales, archivos personales y Eris-Ultranova permanecen fuera del alcance de estas modificaciones.

La evidencia local se conserva en `ITERACION_40_PRUEBAS`: compilación, regresiones e inventario SHA-256 de archivos protegidos.
