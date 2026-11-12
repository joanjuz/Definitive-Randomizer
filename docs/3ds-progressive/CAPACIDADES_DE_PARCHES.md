# Capacidades de parches: informe de solo lectura

Primer paso hacia un motor de parches que dependa menos de offsets fijos: antes de instalar, cada parche puede decir **qué ve** en los archivos y **hasta dónde está acreditado**. No existe un botón de forzar.

Este incremento cubre un parche: **Player Level Caps** (Ultra Sol / Ultra Luna). El instalador no cambia.

## Niveles

| Nivel | Significado | Se puede instalar |
| --- | --- | --- |
| Verificada | Cada recurso está en un estado conocido y la instalación simulada cabe en el espacio existente | sí |
| Compatible por adaptación | Estructura reconocida; la instalación simulada tuvo que ampliar el segmento de código y la reubicación se verificó | sí |
| Conflicto | Otra modificación ocupa un punto de enganche o el espacio que el parche necesita | no |
| No acreditada | Datos insuficientes, firma desplazada o instalación propia que no se puede reemplazar con seguridad | no |

## Estado de cada recurso

Un recurso es un punto de enganche en un archivo (`Battle.cro`: EXP en combate; `code.bin`: Caramelo Raro).

| Estado | Cuándo |
| --- | --- |
| Original | El punto de enganche contiene el código original |
| Instalación propia | Rutina actual enganchada, con tabla válida |
| Instalación propia de formato anterior | Rutina de un formato de tabla anterior |
| Instalación parcial | El enganche salta a una rutina propia incompleta o con tabla no válida, o hay una rutina propia sin enganchar |
| Modificación externa | El enganche no es el original ni de este parche, o salta a código ajeno |
| Desplazado | El código original no está en su sitio y aparece exactamente una vez en otro lugar |
| Truncado / Ausente | El archivo termina antes del enganche o no existe |

**Una firma encontrada en otro lugar es evidencia, no permiso.** El estado «Desplazado» se informa con su posición y el nivel queda en «No acreditada»: una secuencia corta de bytes no demuestra compatibilidad.

## Cómo se decide

1. Se clasifica cada recurso sin escribir.
2. Ausente o truncado → no acreditada. Modificación externa → conflicto. Desplazado → no acreditada.
3. En los demás casos (original o instalaciones propias) se **simula la instalación en copias en memoria** con el instalador real. Si falla: conflicto, o no acreditada si había una instalación propia anterior o parcial. Si funciona: se relee la tabla instalada y se comparan los archivos antes y después.
4. Si algún archivo cambió de tamaño → compatible por adaptación; si no → verificada.

El informe incluye las regiones que cambiarían en cada archivo (o el cambio de tamaño) y una explicación por recurso. Nunca promete más ni menos que el instalador: una prueba lo compara con el instalador en todos los casos.

## Dónde se usa

- **Vista de Player Level Caps**: la línea de estado muestra el nivel y la evidencia de cada archivo, en lugar de dos palabras técnicas.
- **Rechazo de la instalación**: el mensaje explica el nivel y el motivo.
- **Perfiles compartibles**: el requisito `patch:player-level-caps` se acredita con este informe.

## Archivos

- `pk3DS.Core/Modding/Research/PatchCapability.cs`: modelo común (niveles, estados, informe).
- `pk3DS.Core/Modding/Research/LevelCapPatch.Capability.cs`: informe de Player Level Caps.
- `3DS.Progressive.App/Services/GamePlayerLevelCapService.cs`: informe del proyecto, estado y mensaje de rechazo.
- `3DS.Progressive.Tests/PatchCapabilityChecks.cs`: suite `--patch-capability-only`.

## Validación

`--patch-capability-only`: 65 comprobaciones con binarios sintéticos. Cubre original, instalación propia, reinstalación idempotente, parche en un solo archivo, tablas distintas, enganche editado, salto a código ajeno, firma desplazada, firma ambigua, archivo truncado, archivo ausente, rutina dañada, rutina sin enganchar, formatos anteriores, falta de espacio, ampliación del segmento (adaptación), acuerdo con el instalador, transacción fallida sin instalación parcial, deshacer y la vista WPF fuera de pantalla.

No se ha comprobado con ROMs reales ni con romhacks: los niveles describen los archivos sintéticos de la suite. No se declara compatibilidad general con ROMs modificadas.

## Siguiente

Adoptar el mismo modelo en un segundo parche cuando las pruebas lo respalden. El modelo no localiza enganches por firma para escribir: eso exigiría evidencia adicional por parche.
