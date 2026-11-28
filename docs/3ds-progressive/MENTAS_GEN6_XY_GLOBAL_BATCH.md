# XY-MINT: candidato Y y opciones Global Template / Batch

Continuación del PR #147, base `182f3a1c`. No habilita automáticamente las mentas en ningún proyecto, no publica una versión y no modifica los servicios USUM. X-04 y sus iconos tienen aceptación del propietario; la comprobación jugable independiente de Y sigue pendiente.

## Candidato Y-02

Se generó `Pokemon-Y-XY-MINT-EXPERIMENTAL.3ds` en una carpeta aislada, usando el perfil de título `0004000000055E00`, sin escribir en la ROM fuente ni en guardados. SHA-256:

```text
9E2E69E475D8853CC6D1427FEC15A6ACD7F8DF9DF7C476BD6B132845DDDE689D
```

Resultado técnico: 656 recursos coinciden después de reconstruir y reextraer; 133 CRO válidos y CRR coincidente con su inventario completo. La comprobación del CRR conserva exactamente los bytes ajenos a su tabla de hashes; no genera una firma RSA. Se conservan segmentos, relocaciones y nombres físicos, incluidos `DllBag.cro` y `DllBattle.cro`.

La retirada de iconos → tiendas de prueba → mentas devuelve todos los recursos originales. Reinstalar mentas → tiendas → iconos recupera exactamente el candidato final, incluso después de reabrir el proyecto. Las instalaciones repetidas son idempotentes. Las nueve tablas generales contienen cada una las 21 mentas una sola vez; las tiendas especiales conservan sus inventarios. Esta distribución garantizada pertenece exclusivamente al candidato de prueba.

Los resultados privados reproducibles están en `evidence/verify-full-cycle-v1.json` y `evidence/rebuild-reextract-v1.json`, junto al candidato. No se ejecutó un emulador para estas comprobaciones.

Protocolo del propietario, con una copia aislada de su guardado Y:

1. Abrir la ROM candidata; comprar una menta en una tienda general (precio experimental 20000) y comprobar nombre e icono. Las 21 están disponibles en las nueve tablas generales.
2. Anotar naturaleza, estadísticas y cantidad; usar una naturaleza distinta y comprobar el cambio real y el descuento de una unidad.
3. Repetir la misma naturaleza y cancelar otro uso: sin consumo. Comprobar rechazo de huevos y un objeto medicinal normal como control.
4. Guardar, cerrar y reabrir; confirmar naturaleza, estadísticas e inventario. Revisar las 21 imágenes, incluida Seria. No hacen falta combates.

## Opciones reales de receta

El formato sigue siendo Version 1 / ActionCoverageVersion 8. Ninguna instalación implica randomización ni inclusión en pools.

| Selección | Identificador o ruta JSON |
|---|---|
| Persistent Items X/Y | `Actions[].Id = "xy.persistent-items"`, `Parameters = {"Enable":"1"}` |
| Instalar 21 mentas | `Actions[].Id = "xy.nature-mints.install"`, `Parameters = {}` |
| Instalar 21 iconos corregidos | `Actions[].Id = "xy.nature-mints.icons.install"`, `Parameters = {}` |
| Incluir mentas en tiendas | `ProgressiveApp.Gen6MintItems.IncludeMarts` (booleano) |
| Incluir mentas en suelo | `ProgressiveApp.Gen6MintItems.IncludeFieldItems` (booleano) |

Los dos booleanos son independientes y están desactivados por defecto. El segundo conserva los checks existentes de visibles y ocultos: no fuerza ambos alcances. Solo afecta al pool aleatorio; no convierte un barajado de objetos existentes en randomización por catálogo. Una whitelist explícita sigue limitando los IDs admitidos y las prohibiciones conservan precedencia. No se modifica Pickup ni sus tasas en esta entrega.

El nodo antiguo `Gen6MintItems.Include` sigue leyéndose como ambos booleanos; una contradicción con los nuevos campos se rechaza. La captura guarda los campos independientes y conserva los campos desconocidos. Las opciones activas para ORAS, SM o USUM se rechazan.

La interfaz Global Template ofrece las tres instalaciones como acciones seleccionables, además de los dos checks de pool. Cargar y guardar opciones no instala nada. Los iconos requieren mentas previamente instaladas o su instalación explícitamente seleccionada; el preflight rechaza una dependencia ausente.

## Orden e integridad

Antes de los randomizadores se ejecuta un bootstrap determinista: mentas → iconos → Persistent Items. No consume RNG ni añade índices a la secuencia de semillas de los módulos existentes. La receta conserva el orden original de sus demás acciones; los puentes mantienen su orden de ejecución establecido.

La preview privada 1.1.0-preview.3 admite instalar sobre datos ya randomizados. Conserva las huellas originales por título como referencia; `Gen6MintRuntimeDependencies` valida un contrato finito de composición:

- Identidad NCCH y mapa de memoria ExHeader por título; admite firma, tamaños y hashes regenerados del contenedor y el indicador de compresión del código. `static.crs` permanece fijado.
- Código nativo fijado mediante SHA-256 del complemento de rangos de datos conocidos: tablas MT/MO y Pickup, función MO original/parcheada reconocida, layout de tiendas validado, solicitudes/ofertas de intercambio e índices de los 21 iconos. Los offsets y longitudes de los cuatro rangos de tiendas, incluido el puntero nativo, están fijados al perfil original; un descriptor alternativo con checksum coherente no permite mover el rango excluido. No se excluye un segmento completo de código.
- Inventario físico completo de CRO; payload de Bolsa reconocido; patrón de Persistent Items, tabla de tipos y Escudo Real mediante su validador existente; solo los campos tipados de encuentros/regalos/iniciales pueden variar. Instrucciones y bytes opacos permanecen fijados.
- Hashes CRO y CRR correspondiente a todos los módulos. Antes de instalar se admite reparar hashes obsoletos de cambios tipados reconocidos dentro de la misma transacción; instrucciones desconocidas siguen rechazadas. Normalizar las cabeceras acreditadas al perfil original debe recuperar el SHA-256 completo original del CRR.
- Formato de objetos y los 21 slots reservados: deben estar originales o contener la instalación propia. Las demás propiedades y textos randomizados se conservan. Naturalezas, mensajes propios, archivo original de iconos y las 21 texturas corregidas se verifican por separado.

El historial no acredita bytes externos. Dependencias desconocidas, identidad cruzada, instalación parcial, hash obsoleto o cambio concurrente se rechazan. Los SHA capturados se verifican y bloquean durante las transacciones; dos capturas contradictorias del mismo archivo se rechazan. Persistent Items y su CRR se guardan juntos. Los encuentros estáticos se sellan con integridad antes de que el siguiente módulo capture las dependencias.

No se reemplazan CRO completos por una copia vanilla al deshacer: se utiliza el historial transaccional y el orden inverso. Global aplica los bytes congelados de la vista previa en una transacción; Batch trabaja sobre un staging limpio por ROM y verifica el resultado final antes de publicarlo. La fuente no se randomiza.

El soporte de combinación está acreditado sobre copias limpias mediante ese orden de bootstrap. Una instalación externa previa que no cumpla el perfil sigue bloqueada; no se promete admitir cualquier romhack ni todos los órdenes de instalación manual.

## Caso real PokeJuz

Se tomó la receta facilitada como `TemplateGlobal.json` y se exportó una copia independiente llamada `PokeJuz_XY_Batch.json`, con sus CSV adjuntos. No se modificó la fuente.

SHA-256 original: `AE4FBEEC8214C72D3C7E77AD1E6D4E7FE8EBCCF426D15A9B17D5F6C8D97ADE69`.

La comparación profunda acredita todas las secciones originales, las 15 acciones y su orden, los parámetros, los rangos Progressive BST y los dos CSV. Solo se añaden las tres acciones y el nodo de las dos inclusiones (ambas activas en esta copia solicitada). No se introduce una semilla fija en la receta entregada.

| CSV | SHA-256 conservado |
|---|---|
| `moves_gen6.csv` | `3E89D293589C839B77DC7202C2CE7C58C3B5A0FF67699E60F382381E0C92506F` |
| `evolutions_gen6.csv` | `DE50A781283258A9EC4CCD618C57BE0BAF9AA2688E885F712DBE7209A87E6C49` |

Se resuelven los assets adjuntos por nombre y SHA-256 incluso cuando la receta histórica no tiene `Role`. Los CSV adjuntos acreditados prevalecen sobre una ruta antigua de instalación, sin reescribir los parámetros guardados ni sus hashes.

La receta original tiene `Marts.SpecialOnly=1`; se conserva. Incluir mentas añade candidatos a esas tiendas elegibles, sin sustituir los demás objetos y sin garantizar las 21 en cada stock. No confundir este pool con las nueve tiendas garantizadas del candidato Y-02.

## Reproducción y evidencia técnica

El CLI de investigación permite `preview-global ROOT RECIPE SEED NEW_CASE` y `batch-global ROOT RECIPE SEED NEW_CASE`, exige una raíz aislada X/Y, comprueba cabeceras y usa salidas nuevas. El segundo ejecuta dos ROMs, acredita el staging editable mediante las 148 dependencias nativas, reextrae cada salida y exige igualdad exacta de los 656 recursos con ese staging. También comprueba SHA-256 del contenedor, identidad inmutable, ExHeader idéntico, Persistent Items y conservación de la fuente. No ejecuta el juego.

La cabecera NCCH del contenedor reconstruido tiene firma vacía, tamaños y hashes regenerados y el indicador esperado del descifrado de clave fija. La preview3 admite esas diferencias verificables sin retirar las comprobaciones de identidad y código nativo. La instalación no exige que los datos randomizados coincidan con sus hashes vanilla.

La acción existente `rng.seed`, con `Parameters.value`, permite fijar una semilla XY. La primera ROM utiliza el valor exacto; las siguientes suman `0x9E3779B9` módulo `2^31`. Sin esa acción se conserva la generación aleatoria habitual. Las instalaciones no alteran esta secuencia. USUM conserva su comportamiento anterior.

El replay de estas nuevas opciones XY reinicia el pool estático de primeros movimientos desde su orden canónico, sin obtener números aleatorios adicionales. Así no arrastra el orden barajado de una operación anterior a los equipos de entrenadores del siguiente replay. El reinicio no se invoca en USUM ni en las recetas anteriores sin las nuevas opciones XY.

`runtime-code-guards ROOT NEW_JSON` ejecuta controles en memoria sobre el código autorizado de cada título: instrucciones desconocidas, MT inválida y descriptor de tiendas con puntero falsificado. Los tres se rechazan en X e Y sin escribir el ejecutable. `runtime-validate` volvió a acreditar las 148 dependencias del proyecto Y-02.

Comprobaciones automatizadas principales:

| Requirement | Evidence |
|---|---|
| Checks independientes y carga/guardado WPF | `Gen6MintUi_CombinesThreeInstallsAndIndependentPools_*`, `Gen6MintUi_SaveLoadPreservesSelections_*` |
| Preservar secciones y parámetros | `Gen6 mint recipe preserves synthetic BST CSV assets unknown sections *`, comparación profunda privada del caso real |
| Semillas reproducibles | `Gen6MintSeed_ExactSavedSeed_*`, `Gen6MintSeed_DistinctNonnegativeBatch_*`, `Gen6MintSeed_ReplayIndependentOfGeneratedSeed_*` |
| Aislar el pool de primeros movimientos | `Gen6MintRecipe_FirstMovePoolDoesNotLeakBetweenBatchRoms`, `Gen6MintRecipe_FirstMovePoolUsesRequestedSeed`, `Gen6MintRecipe_FirstMoveResetDrawsNoRng` |
| No admitir instrucciones/bytes opacos como datos | `Gen6MintRuntime_InstructionsAndOpaqueFieldsRemainPinned_*`, `Gen6MintRuntime_InvalidLevelRejected`, `Gen6MintRuntime_StaleHashesRejected` |
| Capturas concurrentes incompatibles | `Gen6MintRecipe_ConcurrentDependencyFingerprintsRejectBeforeWrite`, `Gen6MintRecipe_ConflictingEvidenceLeavesSourceIntact` |
| Mantener CSV y metadatos | `Gen6MintRecipe_BundledHashMatchedCsvWinsOverInstalledPath`, `Gen6MintRecipe_CsvResolutionDoesNotRewriteStoredOptions`, `XYMachine6_UntouchedMintRowFlagsPreserved` |
| Cambio de identidad después de preview | `GlobalApply_XyChangedTitleHeaderRejectsStalePreview`, `GlobalApply_XyIdentityRejectionWritesNothing` |

Las suites son del runner ejecutable propio: `--gen6-mints-only`, `--global-apply-ui-only`, `--global-apply-only`, `--batch80-only`, `--native-gen6-only`, `--batch-gen6-only`, `--global-options-only`, `--gen6-persistent-items-only`, `--map3ds-reminder-only`, `--usum-mints-only` y `--usum-mints-distribution-only`. CI incorpora la suite WPF de Global junto a las existentes. Ninguna prueba sintética ni reextracción se presenta como aceptación jugable.

## Instalador WPF privado preview3

En Mecánicas y CRO → Mentas Gen6 se comprueba el perfil, se prepara una vista previa y se instalan por separado las 21 mentas y los iconos. Una tercera operación añade las 21 mentas faltantes a las nueve tiendas generales, conservando sus objetos existentes y validando capacidad. Cada operación usa Historial; se deshace en orden inverso. No activa los pools de Global automáticamente.

Regresiones locales: mentas Gen6 521 comprobaciones, Global WPF 17 (incluye navegación al instalador real), USUM 98 y distribución USUM 128, Persistent Items 122, recordador XY 72, Batch Gen6 7 y extracción 64. Son comprobaciones técnicas; la prueba jugable de Y con el botón queda a cargo del propietario. El paquete es privado y no modifica la Preview instalada.

Se acreditó instalar mentas e iconos después de las 15 acciones de randomización de la receta original de X: 656 archivos, integridad CRO/CRR, retirada exacta y reinstalación; evidencia privada `preview3-randomized-install.json`.

## Compatibilidad de los instaladores manuales (fix3)

El recordador del PR #144 estaba incluido en preview3. Su botón se ocultaba cuando su acreditación exigía code.bin y DllField.cro vanilla, aunque solo hubieran cambiado tablas reconocidas. Persistent Items manual mantenía una comprobación equivalente más estricta que el bootstrap de Global/Batch. Se unifica la admisión con los perfiles independientes X/Y y el complemento nativo fijado: tablas MT/MO, Pickup, tiendas, intercambios, iconos, encuentros/regalos y los cambios ARM reconocidos mantienen validaciones tipadas; el resto de código y bytes opacos continúa fijado. No se modifica el script del recordador ni las dos instrucciones de Persistent Items.

Algunas operaciones de randomización dejan hashes CRO/CRR obsoletos. Los instaladores preparan su reparación en la misma propuesta e historial, después de acreditar el inventario nativo completo. Se conservan nombres físicos, tamaños, segmentos, relocaciones y los bytes criptográficos del CRR. Ninguna reparación se guarda si falla la validación. Deshacer recupera los bytes anteriores exactos, incluso si esos bytes tenían hashes obsoletos; retirar Persistent Items conserva los demás datos y sus hashes actualizados.

Sobre una copia aislada del proyecto randomizado de Y se reprodujo el rechazo anterior por code.bin. Se acreditaron después instalación e idempotencia de Persistent Items, recordador en la colocación conocida de Ruta 2 (zona 259, 463/458), ambos órdenes de instalación, integridad CRO/CRR durante la instalación y deshacer LIFO exacto de los 659 recursos de entrada. Una mutación desconocida del ejecutable se rechazó en ambos instaladores. El primer ensayo de convivencia tuvo un error del arnés: deshacía el asiento de restauración en vez de la operación anterior; se corrigió usando LatestUndoable y se repitió sobre otra copia limpia. No se atribuye ese resultado a una prueba dentro del juego.

Regresiones locales: Persistent Items 122, recordador 72, WPF recordador 15, WPF Persistent Items 20, mentas Gen6 528, Global WPF 17 y mentas USUM 98; todas aprobadas. Evidencias privadas: `compat-before.log`, `compat-cycle-v2.log` y los logs `compat-*-only.log`, fuera de Git y sin recursos comerciales versionados.
