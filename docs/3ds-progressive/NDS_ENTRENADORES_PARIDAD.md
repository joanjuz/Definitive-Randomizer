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

En la entrega inicial del 08/11 no se ejecutó un probe privado de Gen5; esa limitación se actualiza con la ejecución real documentada a continuación. En esa entrega inicial no había validación nueva dentro del juego, con Windows en alto contraste ni con DPI real distintos de 100 %. Los sprites siguen dependiendo del servicio opcional existente. Las pruebas fuera de pantalla y el rebuild no sustituyen una prueba jugable ni una revisión interactiva de foco, alto contraste y DPI.

## Reanudación del PR #56 — 09/11/2026 UTC

Se recuperó la copia de trabajo existente de `feature/nds-trainer-parity`, head `baa0dc56b0d422c282cc89d3aa1c4205d242e2b6`, inicialmente limpia. No se cambió de rama en el árbol principal, que contiene trabajo previo de otros módulos. Esta continuación modifica únicamente el probe de entrenadores y documentación; ningún cambio funcional en App/Core, ningún commit de #55/#57, versión, tag, release o merge.

### Copias privadas y ejecución real

En el árbol privado autorizado de Nintendo DS se localizaron originales de Negro USA/EUR (`IRBO/0`) y Blanco 2 USA/EUR (`IRDO/0`), además de Blanco español (`IRAS`) y varias copias modificadas. No se localizó Negro 2 en ese árbol. Se seleccionaron los dos originales Gen5; Blanco español y los romhacks no se usaron en este probe. También se volvió a comprobar Platino USA rev. 1 (`CPUE/1`) porque se amplió una aserción compartida.

Primero se ejecutó el probe sin cambios en el runner del head: **54 aprobadas**, 27 por ROM Gen5, código 0. No hubo fallo funcional que corregir. Se detectó una carencia de evidencia en el propio probe: no cambiaba la especie ni exigía forma distinta de cero. Se amplió `NdsTrainerParityChecks.Exercise`: conversión de un equipo predeterminado a explícito con cambio de especie (Unown o Deoxys, diferente del original), forma 1 y nivel; al reabrir se comprueban conjuntamente esos valores, objeto y los cuatro movimientos. La validación numérica de formas no constituye un catálogo de legalidad por especie ni una prueba de sus efectos en combate.

Después se compiló Tests/App/Core/LegacyBatch con Release, apphost, `--no-incremental` y binlog: **0 errores y 0 advertencias**. El primer intento quedó bloqueado al leer NuGet.Config por permisos del entorno; el mismo comando completó con el acceso necesario, sin modificar esa configuración.

| Ejecución de esta continuación | Resultado |
|---|---|
| `--nds-trainers-only` | 171 aprobadas, código 0; seis perfiles sintéticos y derivación de movimientos |
| `--nds-trainers-probe <salida nueva> <Platino> <Blanco 2> <Negro>` | 84 aprobadas, código 0; 28 por fuente real |
| `--nds-only`, `--nds-ui-only`, `--requested-ui-only`, renders | No repetidos; se conservan los resultados anteriores, sin cambios funcionales ni visuales |

Comando real, ejecutado desde la copia del PR #56 con rutas privadas resueltas localmente:

```text
dotnet 3DS.Progressive.Tests/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll --nds-trainers-probe <salida nueva aislada> <Platino CPUE/1> <Blanco 2 IRDO/0> <Negro IRBO/0>
```

Este ejemplo documenta los argumentos; los marcadores no son rutas para copiar literalmente. El usuario no necesita ejecutar el runner para hacer su prueba jugable.

| Requisito | Evidencia ejecutada en CPUE, IRBO e IRDO |
|---|---|
| Especie distinta, forma no cero, nivel, objeto y cuatro movimientos tras reconstruir/reabrir | `RebuildReopensEditedSpeciesNonzeroFormLevelItemAndFourMoves` |
| Edición manual y conversión a formato explícito | `ManualEditKeepsEachSupportedField`, `ConvertsDefaultTeamToExplicitWithoutLosingMembers` |
| Los cuatro formatos de equipo y conservación de datos base | `ExplicitFormatRemovalKeepsBaseData`, `ItemOnlyFormatPreservesDefaultMoves`, `MovesOnlyFormatPreservesDefaultItem` |
| Reapertura, recetas históricas/nuevas y Batch | `RebuildReopensFourthMove`, `OldAndNewGlobalOperationsReplayByteIdentically`, `BatchKeepsExplicitManualValues` |
| Archivos ajenos, ARM9, otros entrenadores, cabeceras y sufijos | `UnrelatedFilesAndArm9AreUnchanged`, `OtherTrainerMembersAreUntouched`, `HeaderAiBattleTypeAndOpaqueBytesArePreserved`, `ManualConversionAndRandomizationPreserveSealsAndOpaqueSuffix` |
| Origen intacto | `SourceHashIsUnchanged`, `NoOpRebuildIsByteIdentical` |

Cada nombre de la tabla lleva el prefijo `NdsTrainer_<código>_`. Las fuentes solo se leyeron; las ROM reconstruidas y registros privados se conservan fuera de Git en `artifacts/nds-trainer-real-20261109-final`, con log hermano `.log`. La ejecución anterior Gen5 está en `artifacts/nds-trainer-gen5-20261109-baseline`. El log sintético está en `artifacts/nds-trainers-synthetic-20261109.log`. No se distribuyen ROMs ni rutas privadas en paquetes.

### Criterio de cierre y límites

La verificación técnica focalizada queda aprobada para los dos originales Gen5 probados y Platino. **El propietario confirmó el 09/11/2026 una prueba jugable satisfactoria del editor de entrenadores en Pokémon Platino**: pudo modificar un Pokémon, asignarle cuatro movimientos y un objeto y comprobar el resultado en combate. Esta confirmación corresponde al PR #56, después de abrir su compilación local, y cierra el pendiente jugable de esta paridad. Se conserva [la guía concreta de Platino](NDS_ENTRENADORES_PRUEBA_PLATINO.md) para reproducir la prueba; no se atribuyen al propietario resultados individuales de cada paso de la guía que no haya comunicado.

Siguen sin acreditarse por esta continuación: probe real de Blanco español, juegos/revisiones fuera de los perfiles admitidos, ejecución en juego Gen5, legalidad/efecto jugable de formas arbitrarias y revisión interactiva de alto contraste/DPI. Se conservan las limitaciones documentadas del aprendizaje de formas no base y sprites de especie. No hay un fallo funcional Gen5 reproducido ni pendiente de corrección. El siguiente trabajo sobre eventos/mapa del PR #55 permanece separado y no se inició aquí.

Consolidación autorizada: se incorporan únicamente el refuerzo del probe y estos registros de validación al PR #56. No se repiten suites locales ya aprobadas; el CI del commit final se consulta tras el push. El PR permanece sin fusionar. No se requieren los PR #55/#57 para esta implementación; su integración no depende de ellos.
