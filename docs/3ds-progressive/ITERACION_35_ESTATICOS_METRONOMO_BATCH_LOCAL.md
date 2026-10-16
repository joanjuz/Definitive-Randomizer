# Iteración 35 — Metrónomo de estáticos y acciones reales de Batch

Se integra una opción original pendiente de Gen7 y se corrige la comprobación de compatibilidad de la iteración 34.

- El modo Metrónomo configura los movimientos recordables de estáticos como `[118, 0, 0, 0]`, igual que `StaticEncounterEditor7` original.
- Con la randomización del movimiento especial activada, elimina ese movimiento de los regalos posteriores a los tres iniciales. Conserva el movimiento especial de los iniciales en este modo. Esto no sustituye la migración pendiente de la randomización independiente de iniciales y su protección de movimientos dañinos.
- Global Template importa y exporta el control original `CHK_Metronome`; se conserva además la opción nativa y el alcance como en la iteración anterior.
- La comprobación de Batch usaba incorrectamente `static.randomize`, mientras que el motor validado y la lista de acciones admitidas usan `static-encounters.randomize`. Se corrige el identificador y se comprueban también `starters.randomize` y `static-encounters.modify-levels`. Los ajustes nativos incompatibles ya no omiten la comprobación por usar una acción original.

Pruebas locales en `ITERACION_35_PRUEBAS`: **1.667 comprobaciones aprobadas; Debug 0 errores y 79 advertencias existentes incluyendo el ejecutor WinForms**. Regresiones sintéticas XY/ORAS/SM/USUM, preview sin escrituras, aplicación del preset Metrónomo, conservación de movimiento de iniciales, eliminación de movimiento especial de regalos no iniciales y round-trip del control original. No se modificaron ROMs reales ni se generó un lote completo.

Siguen pendientes Mega, Force Totem, restricciones y randomización completa de iniciales, perfiles originales separados de estáticos Gen6, los demás módulos bidireccionales de Global Template y la generación real de lotes Gen7. La validación en juego se mantiene para cuando finalice la implementación. No se declara paridad funcional completa ni versión estable final.

Sin cambios en los archivos Wild protegidos, archivos personales o Eris-Ultranova; sin commits, push ni release.
