# Entrenadores Nintendo DS: edición y presentación compartida con 3DS

## Comparación del código de partida

Base: `main` en `462291e`. Comparación directa de `GameTrainerService`, `NdsRecordEditor`, `NdsCells`, `NdsTrainerTools` y los parciales de `RecordEditorView`.

| Clasificación | Diferencia verificada y resolución |
|---|---|
| Ya implementado y funcional | DS usa `RecordEditorView`, sus tarjetas, selección de ranuras, búsqueda, controles, propuesta y confirmación de guardado. Tiene opciones de equipos, IA, BST, Smart Items, Level Caps y Move Rules; historial, Global Template y Batch. Se conservan esas rutas. |
| Motor sin exposición correcta | `Gen4Data.Trainers` y `Gen5Data.Trainers` leen forma, dificultad/IV, género y habilidad; el adaptador no los mostraba. `NdsCells` usa movimientos `Move1`–`Move4`, pero las tarjetas esperan `Move0`–`Move3`. La nueva lectura manual expone las claves de las tarjetas y los campos compatibles. |
| Capacidad compatible que faltaba | Conversión manual de formato, edición de clase, tamaño y objetos de combate, procedencia de movimientos/objetos, derivación informativa por nivel y comparación origen/proyecto/edición. Se incorpora dentro del editor existente. La lista DS también muestra clase, cantidad y niveles. |
| Exclusivo de 3DS | Megas, movimientos Z, EV individuales y selección de naturaleza/shiny de los entrenadores Gen7 no se trasladan a DS. No se reinterpretan los valores de género/habilidad DS como los de Gen7. |

## Comportamiento

- Cada tarjeta DS muestra especie, forma numérica, nivel, objeto y cuatro ranuras de movimiento. Los nombres salen del proyecto abierto, incluidos sus textos de romhack.
- Los sprites opcionales usan el servicio compartido de imágenes por especie. Se indica «imagen de especie»: ese servicio no resuelve formas. Sin imagen se conserva el identificador numérico; no se oculta una imagen ausente. No se empaquetan sprites comerciales.
- Con formato de objetos desactivado, la tarjeta indica **sin objeto · formato del juego**. Con él activado, distingue el objeto explícito y el valor explícito «sin objeto».
- Con movimientos personalizados desactivados, los movimientos mostrados son **derivados**, nunca campos añadidos a la ROM. Se usa el aprendizaje vigente del proyecto, en orden, omitiendo movimientos ya conocidos y sustituyendo el más antiguo al superar cuatro. La caché se invalida al cambiar el proyecto. En romhacks se rotula como estimación: no se garantiza que código modificado respete las tablas.
- No se calcula el aprendizaje de formas distintas de 0 ni cuando la tabla no está disponible. En esos casos se muestra «determinado por el juego · no calculado»; no se usa silenciosamente el aprendizaje de otra forma.
- Para escribir movimientos u objetos se activa el formato correspondiente en **Datos del entrenador**. Afecta a todo el equipo, como en la ROM. La primera activación de movimientos copia los derivados disponibles en las ranuras activas; el usuario puede cambiarlos antes de guardar. Desactivar un formato elimina sus valores explícitos al guardar. Consultar y descartar no convierten formatos.
- Se editan especie, forma, nivel, dificultad/IV combinado, nibbles de género/habilidad, objeto y movimientos; también clase, objetos de combate y tamaño. La forma es el ID representable del juego, no un catálogo de formas legales por especie. Cambiar de especie reinicia su forma a 0.
- No se elimina el último miembro de un equipo existente. No se permite reducir manualmente un equipo de combate doble o especial: cambiar el tipo de combate sigue pasando por las opciones y protecciones DS existentes.
- La comparación de la ranura seleccionada distingue ROM de origen, proyecto guardado y edición actual. La propuesta existente muestra los valores guardados y el resultado randomizado por nombre antes de aplicar. Cuando el formato original no almacena movimientos, su columna anterior dice «por nivel (no almacenado)», sin presentarlo como un movimiento 0 explícito. Al volver al editor se leen los datos aplicados.

## Formatos, conservación y recetas

La lectura/escritura de miembros de `NdsTrainerTools` se comparte entre randomización y edición manual. Gen5 mantiene base de 8 bytes; Gen4, base de 6 con especie/forma empaquetadas. Diamante no lleva los dos bytes adicionales por miembro de Platino/HeartGold. Se conservan esos bytes, los bits superiores de las flags, cabeceras, IA, tipo de combate, miembros no editados y el sufijo opaco posterior al equipo. Se corrigió también la pérdida anterior de ese sufijo al pasar por las reglas de randomización DS.

Las operaciones históricas `Kind=Edit` siguen usando `NdsCells` y **Move1–Move4**. No se cambian ni renumeran sus claves. La edición ampliada usa `Kind=TrainerEdit1`, con **Move0–Move3**, dentro del Global DS versión 1. Ambas operaciones pueden coexistir y reproducirse en orden. Versiones anteriores del programa rechazan la operación nueva; no deben reinterpretarla como una edición histórica.

No se cambian semillas ni opciones de reglas. Las operaciones manuales conservan sus valores al reproducirse en Global/Batch; las aleatorias siguen la política de semillas existente. No se modifican código ARM9/overlays, scripts, perfiles, CRC, eventos ni formatos de otras plataformas. El PR #55 permanece separado: esta rama parte de `main` y no contiene sus commits.

## Validación

Runner propio, con `--nds-trainers-only`, integrado también en `--nds-only`, y pruebas de interacción agregadas a `--nds-ui-only`. `--nds-trainers-probe <salida nueva> <rom>…` usa fuentes privadas de solo lectura y conserva las salidas aisladas. Requiere al menos un equipo explícito y otro predeterminado.

| Requisito | Evidencia focalizada |
|---|---|
| Cuatro movimientos, forma y objeto correctos | `ReadsFourMovesFormAndItem`; `NdsTrainerUi_CardsShowFourExplicitMovesItemAndForm` |
| Consultar no escribe valores derivados | `OpeningAndNoOpSaveDoNotWriteDerivedData`; `SerializerNoOpKeepsEveryByte`; `NdsTrainerUi_DefaultMovesAreDerivedReadOnlyAndDoNotWrite` |
| Derivación y actualización del aprendizaje | `NdsTrainer_DerivedMovesRespectBeforeAndAtLearnLevel`; `NdsTrainer_DerivedMovesSkipKnownDuplicatesAndReplaceOldest`; `NdsTrainer_DerivedMovesInvalidateAfterLearnsetEdits` |
| Edición y cuatro formatos | `ManualEditKeepsEachSupportedField`; `ConvertsDefaultTeamToExplicitWithoutLosingMembers`; `ItemOnlyFormatPreservesDefaultMoves`; `MovesOnlyFormatPreservesDefaultItem`; `ExplicitFormatRemovalKeepsBaseData` |
| Guardado, reapertura y rebuild | `RebuildReopensFourthMove`; `NdsTrainerUi_SaveRefreshesCardsAndComparison` |
| Preservación | `ManualConversionAndRandomizationPreserveSealsAndOpaqueSuffix`; `HeaderAiBattleTypeAndOpaqueBytesArePreserved`; `OtherTrainerMembersAreUntouched`; `UnrelatedFilesAndArm9AreUnchanged`; `SourceHashIsUnchanged` |
| Recetas y semillas | `HistoricalOneBasedRecipesKeepTheirMeaning`; `OldAndNewGlobalOperationsReplayByteIdentically`; `SeededPreviewIsRepeatableAndDoesNotApplyItself`; `BatchKeepsExplicitManualValues` |
| Navegación y comparación del resultado | `NdsTrainerUi_SearchAndTrainerSubtitleUseCurrentProject`; `NdsTrainerUi_SelectingMemberUpdatesComparison`; `NdsTrainerUi_RandomPreviewComparesSavedMovesWithNamedResult`; `NdsTrainerUi_AppliedRandomizationRefreshesCardsAndKeepsOriginalComparison` |

Los nombres sin prefijo en la tabla se ejecutan como `NdsTrainer_<código>_<nombre>` para ADAE, CPUE, IPKE, IRBO, IRAS e IRDO. Los fixtures IRAS/IRDO son sintéticos, con rutas y bancos de texto del perfil correspondiente; no equivalen a validación de esos juegos reales.

Resultados locales del 08/11/2026:

| Ejecución | Resultado |
|---|---|
| Compilación Release no incremental de Tests, App, Core y LegacyBatch, con apphost y binlog | 0 errores, 0 advertencias |
| `--nds-only` | 326 comprobaciones aprobadas; incluye 165 de paridad de entrenadores sobre los seis perfiles sintéticos |
| `--nds-ui-only` | 27 aprobadas; incluye 12 nuevas de tarjetas, conversión, guardado, búsqueda y propuesta/aplicación |
| `--requested-ui-only` | 55 aprobadas, incluidas copiar/pegar y guardar Pokémon de entrenador en las cuatro familias 3DS |
| `--nds-trainers-probe` sobre copia privada original de Platino CPUE/1 | 27 aprobadas: equipo explícito y predeterminado, edición, conversiones, rebuild, reapertura, replay exacto, Batch, preservación de otros archivos/ARM9 y SHA-256 del origen |
| Renders DS inspeccionados | 1100×760 y 760×620, a 96 DPI; texto de procedencia/objeto ajustado, identificación numérica conservada sin imagen |

No se ejecutó un probe privado de Gen5 en esta entrega; su comprobación local es sintética, con sus tres perfiles y diferencias de rutas/textos. No hay validación nueva dentro del juego, con Windows en alto contraste ni con DPI real distintos de 100 %. Los sprites siguen dependiendo del servicio opcional existente. Las pruebas fuera de pantalla y el rebuild no sustituyen una prueba jugable ni una revisión interactiva de foco, alto contraste y DPI.
