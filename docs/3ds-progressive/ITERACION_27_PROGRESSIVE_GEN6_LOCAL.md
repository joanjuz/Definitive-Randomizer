# Iteración 27 · inventario Progressive Gen6 y motor Catch Zones

Rama local: `feature/3ds-progressive-ui`. Sin commits, push ni publicación. No se modifican Eris-Ultranova, archivos personales ni los 12 archivos protegidos de Wild/editor.

## Alcance implementado

Se añade `CatchZonePatchEngine` a la aplicación nueva. Lee el formato 2 utilizado por `pk3DS.WinForms/Misc/CatchZonePatchSync.cs`, verifica familia XY/ORAS y evalúa fragmentos, deltas con copias/literales y eliminaciones sobre bytes suministrados. La evaluación no escribe archivos ni altera el origen. Cada resultado exige longitud y SHA-256 correctos. Reconoce resultados ya aplicados y eliminaciones ya realizadas.

Rechaza rutas relativas inseguras, ámbitos y modos desconocidos, objetivos repetidos, hashes inválidos, originales incompatibles, fragmentos solapados o fuera de límites y deltas que exceden el tamaño destino. Esta versión exige longitudes explícitas; los manifiestos reales las contienen. El lector original aceptaba longitudes omitidas y modos desconocidos como fragmentos; esos casos no se aceptan en el nuevo motor.

Se distribuyen los manifiestos **existentes**, sin alterarlos, desde `pk3DS.WinForms/catch_zone_patches/XY.json` (61 archivos) y `ORAS.json` (174 archivos), mediante el proyecto WPF. Ambos se leen durante las pruebas desde la carpeta de salida. Incluyen ámbitos `encdata`, `mapGR` y `raw`.

**Catch Zones no está completo ni disponible todavía en la interfaz nueva.** Faltan el adaptador de miembros GARC comprimidos/descomprimidos (incluidos nombres `dec_*.bin`), la reconstrucción de encdata/mapGR preservando miembros, los archivos raw, una propuesta conjunta con historial y la vista WPF. El motor representa eliminaciones pero el historial actual solo sustituye archivos existentes: hay que resolver ese caso antes de habilitarlo. Los manifiestos actuales usan únicamente `patch`. No se copian directorios externos ni se aplican parches automáticamente al abrir Wild.

## Inventario Progressive Gen6 que debe conservarse

| Función | Fuente real | Estado de migración |
| --- | --- | --- |
| MO olvidables/restauración | TMHMEditor6; Gen6HmRestriction nuevo | Implementada en iteración 24; prueba real posterior |
| Enable Catch Zones | Main.B_CatchZones_Click; CatchZonePatchSync | Motor y distribución implementados; integración pendiente |
| Plantillas externas Catch Zones | CatchZoneTemplateSync | Pendiente; es una ruta distinta de los manifiestos del botón |
| Trades Accept Any y ofertas aleatorias XY/ORAS | Gen6TradePatcher; Main | Pendiente; ORAS exige además ambos parches del selector PC |
| Fix Economy Gen6 | ItemEditor6; EconomyFixer | Auditar cobertura WPF y equivalencia; no declarar completo |
| Balance de movimientos y efectos de combate | MoveEditor6; CustomBattleEffectPatcher | Auditar tabla y CRO conjuntamente, incluido King's Shield -1 |
| Entrenadores y selección avanzada de movimientos/objetos | SmartTrainerMovePicker; plantillas de entrenadores | Cobertura parcial; revisar opciones y pools Gen6 |
| Caramelos Raros en tiendas | MartEditor6; GameMart6Service nuevo | Implementado en iteración 25; prueba real posterior |

Este inventario complementa la paridad vanilla; no constituye una enumeración exhaustiva de todos los cambios Progressive. Debe seguirse el código real de cada acción, incluidas dependencias de tablas/scripts/CRO, y evitar dar por migrado un botón que solo modifica parte de sus archivos.

## Evidencia local

Compilación Debug sin errores y sin nuevas advertencias de código. Los registros completos de compilación/regresión y comprobación SHA-256 se guardan en `ITERACION_27_PRUEBAS/`. Las pruebas del motor comprueban los dos manifiestos de producción y casos sintéticos de aplicación, idempotencia, longitud variable, ausencia de archivos y rechazos. No demuestran que los parches funcionen en una ROM real.

La migración completa continúa abierta. No se solicita validación en juego todavía; se reserva para el cierre acordado de XY/ORAS/USUM.

Resultado final: **1.451 comprobaciones aprobadas; Debug 0 errores / 22 advertencias existentes; 12/12 hashes protegidos conservados.**
