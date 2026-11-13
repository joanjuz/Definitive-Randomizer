# Capacidades de parches: informe de solo lectura

Primer paso hacia un motor de parches que dependa menos de offsets fijos: antes de instalar, cada parche puede decir **qué ve** en los archivos y **hasta dónde está acreditado**. No existe un botón de forzar.

Cubre tres parches: **Player Level Caps** y **Persistent Items** (Ultra Sol / Ultra Luna) y **Shiny Rate** (sexta y séptima generación). Los instaladores no cambian.

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

## Persistent Items

Este parche no usa posiciones fijas: cada uno de sus dos puntos de `Battle.cro` se localiza por una firma con máscara que debe aparecer **exactamente una vez**. Por eso su mejor nivel es «compatible por adaptación»: la estructura se reconoce y la instalación simulada se relee, pero la revisión del archivo no se comprueba.

| Situación | Nivel |
| --- | --- |
| Los dos puntos localizados (originales, instalados o uno de cada) | Compatible por adaptación |
| Una firma no aparece o aparece más de una vez | No acreditada |
| Falta el archivo o no es un CRO | No acreditada |

Al abrir el editor con un archivo no acreditado, el mensaje explica qué punto no se pudo localizar.

## Shiny Rate

Tampoco usa posiciones fijas: la rutina PID de `code.bin` se localiza por su firma y debe ser la única candidata, así que su mejor nivel es «compatible por adaptación».

| Situación | Nivel |
| --- | --- |
| Una sola rutina, con la instrucción original o con rerolls reconocidos | Compatible por adaptación |
| Ninguna rutina, varias candidatas o una instrucción de rerolls desconocida | No acreditada |
| Falta el archivo o no está descomprimido | No acreditada |

«Everything Shiny» es opcional: si su firma no aparece, los rerolls siguen disponibles y el informe lo dice. Al abrir el editor con un archivo no acreditado, el mensaje explica el motivo.

## Interacción entre parches

Player Level Caps y Persistent Items escriben los dos en `Battle.cro`. La suite los instala en los dos órdenes sobre dos módulos sintéticos: uno con espacio libre y otro que obliga a ampliar el segmento de código.

- Cada parche sigue reconociendo su instalación y la del otro después de instalar el segundo.
- El orden no cambia el resultado: los dos órdenes dan el mismo módulo, byte a byte.
- Ampliar el segmento desplaza los puntos de Persistent Items, que se siguen localizando por su firma.
- Reemplazar la tabla de Player Level Caps conserva Persistent Items.

Shiny Rate y Player Level Caps escriben los dos en `code.bin`: instalados en los dos órdenes, ambos siguen reconocidos y el archivo resultante es el mismo.

**Comportamiento observado, sin corregir:** con espacio libre, reinstalar la misma tabla no cambia nada. Cuando la primera instalación tuvo que ampliar el segmento, la primera reinstalación vuelve a mover la rutina (del segmento ampliado al relleno de página) y solo la siguiente deja de cambiar. El resultado sigue siendo válido y conserva el otro parche, pero esa reinstalación no es neutra y el informe de capacidades la muestra como cambios. No se ha modificado el instalador: hacerlo exige evidencia con un módulo real.

## Mega Ability Sync

Usa posiciones fijas de `Battle.cro` (el mismo archivo en Ultra Sol y Ultra Luna) y, además, exige que las rutinas de combate de las que depende tengan su contenido original y que las funciones de `code.bin` se resuelvan por nombre con la forma esperada. Detalle en [MEGA_ABILITY_SYNC_USUM.md](MEGA_ABILITY_SYNC_USUM.md).

| Situación | Nivel |
| --- | --- |
| Cuatro puntos originales o instalados, con espacio libre | Verificada |
| Igual, pero hay que ampliar el segmento de código | Compatible por adaptación |
| Un punto ocupado por otra modificación | Conflicto |
| Instalación a medias, rutinas cambiadas, módulo distinto o faltan `static.crs`/`code.bin` | No acreditada |

## Dónde se usa

- **Vista de Player Level Caps**: la línea de estado muestra el nivel y la evidencia de cada archivo, en lugar de dos palabras técnicas.
- **Rechazo de la instalación**: el mensaje explica el nivel y el motivo.
- **Perfiles compartibles**: el requisito `patch:player-level-caps` se acredita con este informe.

## Archivos

- `pk3DS.Core/Modding/Research/PatchCapability.cs`: modelo común (niveles, estados, informe).
- `pk3DS.Core/Modding/Research/LevelCapPatch.Capability.cs`: informe de Player Level Caps.
- `3DS.Progressive.App/Services/GamePlayerLevelCapService.cs`: informe del proyecto, estado y mensaje de rechazo.
- `3DS.Progressive.App/Services/GamePersistentItemsService.cs`: informe de Persistent Items.
- `3DS.Progressive.Tests/PatchCapabilityChecks.cs`: suite `--patch-capability-only`.

## Validación

`--patch-capability-only`: 129 comprobaciones con binarios sintéticos (65 de Player Level Caps, 21 de Persistent Items, 20 de interacción entre ambos y 23 de Shiny Rate, incluida su convivencia con Player Level Caps). Cubre original, instalación propia, reinstalación idempotente, parche en un solo archivo, tablas distintas, enganche editado, salto a código ajeno, firma desplazada, firma ambigua, archivo truncado, archivo ausente, rutina dañada, rutina sin enganchar, formatos anteriores, falta de espacio, ampliación del segmento (adaptación), acuerdo con el instalador, transacción fallida sin instalación parcial, deshacer y la vista WPF fuera de pantalla.

No se ha comprobado con ROMs reales ni con romhacks: los niveles describen los archivos sintéticos de la suite. No se declara compatibilidad general con ROMs modificadas.

## Siguiente

Llevar el modelo a más parches (mentas cuando se integren sus cambios pendientes, compra única, obediencia). Player Level Caps sigue sin localizar enganches por firma para escribir: eso exigiría evidencia adicional.
