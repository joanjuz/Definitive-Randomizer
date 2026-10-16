# Iteración 30 · Fix Economy Progressive Gen6

La tabla económica WPF de XY/ORAS no cargaba el preset histórico: antes conservaba el precio actual y excluía todas las filas por defecto. Se corrige `GameItemService.PriceRows` para ofrecer el preset real de `EconomyFixer.ApplyLegacy`, conservar la edición/exclusión de filas y registrar su origen correcto en el historial.

`GameEconomy6Defaults` fija los 110 IDs de `MartEditor7.BannedItems` a 1000 y aplica los trece precios específicos del preset histórico: Ultra Ball 200, Great Ball 150, Poké Ball 100; repelentes 50; Escama Corazón 7500 y las seis plumas 10. En total son 123 IDs distintos. Solo se incluyen IDs existentes en el catálogo cargado; el preset original también omitía los inexistentes. Los demás precios se conservan y quedan excluidos. No se aplica la tabla UPR-ZX Gen7 a XY/ORAS.

La vista económica presenta el nombre del preset de la generación y el botón «Restablecer preset». El guardado continúa mediante propuesta verificada e historial; no se guarda al abrir. La representación Core de compra en múltiplos de diez y venta a la mitad se conserva. Gen7 mantiene su tabla previa UPR-ZX + Progressive.

## Pruebas locales

Fixtures XY/ORAS verifican todos los 123 objetivos frente a valores derivados independientemente del código original, exclusiones, precio actual de objetos ajenos, origen Gen6, vista previa sin escrituras, valores tras aplicar, bytes distintos del precio preservados exactamente, historial, idempotencia, edición personalizada, exclusión de una fila, duplicados, precios no representables y cambios externos. Fixtures SM/USUM verifican que conservan los valores Gen7. La prueba WPF comprueba las filas incluidas y Great Ball a 150 y genera una captura de la tabla oscura.

Compilación, regresión, captura y comprobación de SHA-256 de archivos protegidos se guardan en `ITERACION_30_PRUEBAS/`.

## Auditoría acotada de balance de movimientos

Se confirmó que `GameMoveTemplateService` ya distingue Gen6/Gen7, aplica registros Move6, valida el token `Gen6KingShieldMinusOne` y prepara `DLLBattle.cro` con la copia local de `CustomBattleEffectPatcher`, junto con los movimientos en una propuesta. Esta lectura no demuestra paridad exhaustiva del balance ni comportamiento real en juego; se mantiene pendiente una revisión específica de todos los campos/efectos y su evidencia. No se modifica aquí esa ruta ni se declara completa.

La migración completa continúa abierta. La validación real XY/ORAS/USUM se reserva para el cierre acordado. Se mantiene `feature/3ds-progressive-ui`, sin commits, push o publicación y sin cambios en Wild protegido, archivos personales ni Eris-Ultranova.

Resultado final: **1.551 comprobaciones aprobadas; Debug 0 errores / 22 advertencias existentes; 12/12 hashes protegidos conservados.**
