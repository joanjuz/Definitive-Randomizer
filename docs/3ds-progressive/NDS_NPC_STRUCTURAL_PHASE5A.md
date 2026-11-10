# Nintendo DS — edición estructural de NPC, fase 5A

Añadir, duplicar, copiar, cortar/trasladar y pegar **NPC existentes** en **Platino USA CPUE/1**, con un checkpoint nuevo (versión 2) para las operaciones que cambian el número de registros. Se apoya en las fases 1–4 ([NDS_EVENT_EDITOR_PHASE1.md](NDS_EVENT_EDITOR_PHASE1.md), [NDS_NPC_DRAG_PHASE4.md](NDS_NPC_DRAG_PHASE4.md)). **No hay validación dentro del juego de ninguna de estas operaciones.** No cambia el motor de aleatorización ni otros juegos.

## Qué se averiguó (copia privada CPUE/1, solo lectura)

| Hecho | Valor |
|---|---|
| `zone_event.narc` | 534 miembros, 157 800 bytes; la ranura de la ROM mide 158 208 bytes (408 de holgura) |
| Estructura de un miembro | cuatro bloques sucesivos, cada uno con su contador u32 (fondo 20 B, NPC 32 B, warp 12 B, activador 16 B) |
| Cabeceras por archivo de eventos | 533 archivos con una sola cabecera; **1** compartido por 2 cabeceras |
| NPC por mapa | de 0 a **51** (469 archivos tienen NPC; 3555 NPC en total) |
| ID de NPC (+0) | consecutivos en 439 de 469 archivos; **28 archivos repiten IDs** (el ID no es único en el juego original) |
| Scripts | 3353 de 3555 NPC tienen script distinto de 0; no existe un analizador de scripts en el proyecto |
| Reconstrucción | `Narc.Write` rebuilds FAT/GMIF; `NdsRom.Save` admite archivos de otro tamaño (en su sitio si caben o reubicados en espacio libre) |

Consecuencias de diseño:

- El índice de un NPC **no** es su identificador: el índice solo ordena el bloque; el ID es la referencia local que usan los scripts. Por eso un NPC nuevo se añade **al final** del bloque (no se reindexa nada) con un ID libre (el mayor del archivo + 1), nunca repetido.
- Como no se pueden analizar los scripts, **no se copian ni scripts ni flags**: un NPC nuevo lleva script 0, flag 0 y tipo 0 (valores que existen en el juego original). Sí se copian de la plantilla los bytes desconocidos, el rango de visión, los rangos y la altura bruta.
- Gráfico y movimiento solo pueden ser valores que aparecen en NPC del juego original (listas acreditadas). La dirección es 0–3. Posición dentro de los límites del editor.
- Un mapa no supera **51** NPC (el máximo observado); el límite real del motor no se conoce y no se intenta rebasar el del juego original.

## Corrección posterior (rediseño)

Tras la prueba del propietario se corrigió la experiencia de uso y se amplió el alcance; el diagnóstico está en [NDS_EDITOR_REDESIGN_PLAN.md](NDS_EDITOR_REDESIGN_PLAN.md). En resumen:

- **Duplicar / Pegar / Añadir** se hacen eligiendo la casilla en el mapa (fantasma). Una casilla sin conflictos previsualiza directamente; una advertencia, «Añadir» o una copia desde otro mapa abren el formulario, que ahora vive en el inspector (el mapa sigue visible) y muestra la **plantilla**, que se puede cambiar.
- El NPC previsualizado se dibuja en el mapa como **«NPC nuevo · pendiente de guardar»** hasta guardarlo o descartarlo.
- El **clic derecho** selecciona el NPC bajo el cursor y el menú ofrece «Pegar aquí», «Duplicar aquí» y «Añadir NPC aquí» en esa casilla.
- **Copiar entre mapas** está permitido como **copia básica** (modo A): NPC nuevo con el gráfico, el movimiento, la dirección, la visión y los rangos del original; script y flag a 0; ID libre del destino; altura del NPC más cercano del destino. No se trasladan diálogos ni comportamiento. Cortar entre mapas sigue rechazado.
- **Eliminar** quita cualquier NPC **añadido por el editor** (no solo el último), con una propuesta reversible; los NPC del juego original siguen protegidos con su explicación.
- Guardar o restaurar **conserva el zoom y la posición** del mapa.

Las secciones siguientes describen la entrega original y siguen siendo válidas salvo en esos puntos.

## Operaciones

Botones **[Añadir] [Duplicar] [Copiar] [Cortar] [Pegar] [Eliminar]** junto al inspector, menú contextual del lienzo y atajos (Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+D, Supr). Cada operación termina en una **propuesta revisable**: nada se escribe al pulsar un botón ni al elegir una casilla.

| Operación | Qué hace | Estado |
|---|---|---|
| Añadir | formulario (ID, gráfico, movimiento, dirección, X, Y) a partir del NPC seleccionado como plantilla | implementada |
| Duplicar | elegir la casilla con un NPC fantasma; formulario con ID libre | implementada |
| Copiar | portapapeles interno con el registro tipado y su procedencia | implementada |
| Pegar | como duplicar, dentro del mismo archivo de eventos (incluye una cabecera que lo comparta); entre archivos distintos **se rechaza** | implementada |
| Cortar | marca el NPC; **no elimina nada**. Al pegar en el mismo archivo de eventos es un **traslado** (movimiento existente de la fase 4, reversible); entre archivos distintos se rechaza | implementada |
| Eliminar | **siempre rechazada** para NPC del juego original, con las razones (scripts no analizables, ID, reindexación, archivo compartido, dependencias) | bloqueada |
| Selección múltiple | no implementada | pendiente |

Evaluación de la casilla con `NdsSpatialQuery`: **Rechazado** (colisión, fuera de límites o de la matriz, ID duplicado, valores no acreditados), **Indeterminado** con advertencia explícita (sin datos de terreno, otra cabecera, otro NPC/objeto/warp, rango de activador) y **Sin conflictos estáticos conocidos** (nunca «seguro»; se lista lo no evaluado).

**Por qué no se elimina.** Eliminar un NPC del juego original exigiría demostrar que ningún script se refiere a su ID, que ninguna otra dependencia (aparición por flags, escenas, combates) lo necesita y que reindexar a los NPC posteriores no rompe nada. Con los datos actuales eso no se puede demostrar. La función muestra el informe y no ofrece forzarla. Un NPC añadido por la última operación se quita con **Restaurar último cambio…** (exacto y reversible). Si se logra un analizador de scripts, la eliminación podrá revisarse.

## Checkpoint versión 2

Formato `DefinitiveRandomizer.NdsEvents`, **versión 2**, solo cuando la sesión incluye una adición. Una sesión que únicamente mueve NPC **sigue escribiendo la versión 1** (mismo JSON que antes, sin campo nuevo), de modo que esos archivos se siguen leyendo en versiones anteriores. La versión 1 se lee y restaura igual que siempre.

- Campos de la v2: los de la v1 (`Format`, `Version`, `Game`, `Revision`, `SourcePath`, `SourceSha256`, `Audit`), `Edits` vacío y **`Operations`** ordenadas. Cada operación tiene `Kind` (`MoveNpc`, `AddNpc` o `RemoveAddedNpc`) y una carga: `Move` (mapa, índice, antes/después), `Add` (mapa, índice al que entra y el registro de 32 bytes en hexadecimal) o `Remove` (mapa, índice y registro esperado). `RemoveAddedNpc` solo es válida para índices iguales o mayores que el número de NPC del juego original y si el registro coincide; nunca elimina un NPC original.
- La reconstrucción repite las operaciones sobre la fuente acreditada: el resultado es determinista y se **vuelve a leer y reproducir antes de publicar** el archivo (staging, vaciado a disco, relectura, sin sobrescritura), como en la v1.
- Se rechazan: versión desconocida; v2 sin operaciones o con `Edits`; v1 con `Operations`; operaciones de tipo desconocido o con dos cargas; adiciones con índice que no coincide con el estado reconstruido, ID repetido, valores no acreditados, hexadecimal ilegible o truncado, mapa inexistente; fuente distinta (SHA-256).
- Propuestas ligadas a sesión y revisión; una propuesta obsoleta o concurrente se rechaza.
- La identidad de la ROM no se debilita: la **fuente** sigue exigiendo el perfil CPUE/1 y el hash normalizado de la tabla original. Los datos **derivados** (con NPC nuevos) no se aceptan como fuente: se verifican por reproducción desde la fuente y por invariantes (cada miembro ajeno idéntico byte a byte, el bloque de NPC crece exactamente en el registro añadido). Una ROM exportada con NPC nuevos **no se reconoce como origen** del editor de eventos; se continúa desde el checkpoint y su fuente.
- Deshacer una adición quita el último registro y deja los **bytes originales exactos**; si no quedan operaciones, el checkpoint resultante es v1.

## Reconstrucción de la ROM

`PlatinumEvents.AddNpc` reconstruye el miembro (contador +1, registro añadido al final del bloque de NPC), reescribe el NARC con `Narc.Write` y comprueba que todos los demás miembros son idénticos. `NdsRom.Save` coloca el archivo en su ranura o lo reubica en espacio libre. Prueba real (copia privada): +32 bytes en el archivo de eventos de T01, **en su sitio**, ROM exportada con todos los demás archivos idénticos byte a byte, reapertura del checkpoint v2 y restauración con SHA-256 idéntico al de la fuente.

## Pruebas

- `--nds-npc-structure-only` (75): construcción del registro, reglas de validación, reconstrucción del NARC, archivo compartido, tope de 51, sesiones, checkpoint v2/v1, documentos inválidos, propuestas obsoletas, fallos de escritura, concurrencia, exportación y política de pegado/cortado/eliminación.
- `--nds-npc-structure-ui-only` (52): botones y menú, colocación con fantasma, formulario y validaciones, propuesta → guardado → restauración, pegar y cortar (mismo archivo y rechazo entre archivos), eliminar, cancelaciones, aviso ante cambios pendientes, diseño a 1100/760 y archivo compartido.
- `--nds-npc-structure-probe <carpeta nueva> <ROM privada>`: recorrido real añadir → checkpoint v2 → exportar → reabrir → restaurar; compara cada archivo ajeno y el SHA-256 de la fuente antes y después.
- Regresión: `--nds-events-*`, `--nds-npc-drag-*`, `--nds-map-*`, `--nds-ui-only`.

## Prueba manual para el propietario

Copia de la ROM CPUE/1 y partida de respaldo; salida en otra carpeta.

1. Eventos → Mapa, busca **411 · T01** y selecciona un NPC normal (p. ej. el 0).
2. **Duplicar**: elige una casilla libre; revisa el formulario (ID nuevo, estado, «No evaluado»); **Previsualizar propuesta**.
3. **Guardar cambios…** en un checkpoint nuevo y **Exportar ROM…**.
4. Abre la ROM en el emulador: el NPC nuevo aparece en esa casilla (sin script: no habla). Comprueba que el juego no falla y que los demás NPC siguen igual.
5. En el editor, **Restaurar último cambio…**, guarda otro checkpoint, exporta y verifica que desaparece.
6. Prueba también **Copiar → Pegar** en el mismo mapa y **Cortar → Pegar** (traslado). Comprueba que **Eliminar** se rechaza con sus razones.

Hasta que lo confirmes en el emulador, la función no se declara validada en juego.

## Pendiente

Selección múltiple y copia de grupos (cuando haya guardado atómico de varias adiciones y reglas de IDs), eliminación (requiere un analizador de scripts), pegar entre archivos de eventos distintos, scripts y flags propios del NPC nuevo, objetos/warps/activadores, y validación en juego.
