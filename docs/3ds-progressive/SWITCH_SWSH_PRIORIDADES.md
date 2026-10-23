# Espada/Escudo: entrenadores, encuentros y Field Items

21 de octubre de 2026 · `feature/switch-swsh-prototype` · experimental, sin commit/push de esta entrega.

> Este documento conserva la validación de la primera entrega. La ampliación posterior del pool de objetos, la preservación de flags y las cifras nuevas están en [SWITCH_ESPADA_CORRECCIONES.md](SWITCH_ESPADA_CORRECCIONES.md). Prioridad actual: Espada; Escudo pendiente.

## Uso

Inicio → Espada/Escudo · prototipo Switch → Abrir extracción → **Entrenadores, encuentros y Field Items…**. Seleccionar módulos, semilla y si se limita a BST similar. Previsualizar no aplica cambios. «Añadir al mod» añade la propuesta al borrador compartido; después cerrar este panel y usar «Exportar mod…» en la ventana anterior. Se pueden combinar estas operaciones con estadísticas, tipos y habilidades en una sola exportación. Cero opciones es una operación vacía, no un bloqueo.

El alcance es Espada/Escudo 1.3.2 con Build ID reconocido. La extracción original permanece en lectura. Una build desconocida no permite editar/exportar. No se alteraron motores, templates, Batch ni parches de 3DS.

## Funciones y límites

- **Entrenadores:** cambia especies y normaliza forma a 0 usando especies base presentes. Conserva equipos, niveles, movimientos, objetos, IA, IV/EV y demás bytes; retira Gigantamax de los reemplazos. Lee y valida cada pareja trainer_poke/trainer_data; conserva IDs y número de integrantes. No ajusta movimientos a la nueva especie, equipos temáticos ni lógica de combates guionizados: requieren una fase posterior y prueba jugable.
- **Encuentros:** tablas normales visibles y ocultas del juego elegido (`encount_symbol_k`/`encount_k` en Espada, sufijo `t` en Escudo). Conserva áreas, climas, niveles y probabilidades; omite ranuras de especie cero. No incluye estáticos, regalos, incursiones, aventuras Dynamax ni árboles de bayas.
- **Field Items:** reemplaza hashes de Poké Balls, medicinas y bayas por candidatos de esas mismas categorías. Comprueba el mapa de hashes del propio juego. Objetos visibles solo del modelo rojo conocido; MT amarillas, objetos clave, categorías ajenas y hashes desconocidos permanecen intactos. En ocultos conserva cada variante ponderada, cantidades, probabilidades y respawn. Las cantidades de los visibles tampoco cambian.
- **BST similar:** ±20 % del BST de la especie de entrada; si no hay candidatos usa la distancia mínima disponible. No cambia niveles. Se puede desmarcar.
- **Estadísticas:** mezcla los valores existentes, conserva BST y PS=1; mantiene las protecciones de especies con mecánicas de forma. No es una generación de BST nuevos.
- **Catálogos:** lee nombres de especies, objetos y entrenadores de los bancos españoles del juego; usa inglés si falta el banco español y muestra IDs si no hay catálogo. No reescribe textos ni importa nombres de 3DS.

## Integridad, historial y exportación

Los lectores FlatBuffers comprueban tablas, vtables, referencias, vectores y límites. Solo se escriben escalares ya presentes. GFPack conserva directorios, metadatos opacos y todos los payloads originales; agrega los miembros editados al final y actualiza únicamente tamaños/offsets de sus entradas. El codec LZ4 (tipo 2) se mantiene con bloques literales válidos. Esto aumenta el tamaño frente a compresión normal, pero no cambia el tamaño descomprimido ni miembros ajenos. Otra compresión seleccionada se rechaza.

La sesión captura hashes de cada dependencia leída y comprueba cambios de archivos y del listado de equipos. Las vistas previas pertenecen al proyecto y a una revisión concreta del borrador; las obsoletas se rechazan. Exporta solo recursos diferentes al original a un destino nuevo, mediante staging y reapertura; no sobrescribe mods. El manifiesto incluye recursos/hashes, opciones, semilla, algoritmo `swsh-gameplay-fnv-splitmix-v1`, cambios netos e historial. No hay todavía Global Template ni Batch para Switch. La semilla reproduce la operación con idénticas opciones y datos de entrada, no pretende que repetir una operación ya aplicada sea idempotente.

El hash de nombres usa la base específica de pkNX/GFPack `0xCBF29CE484222645`, distinta de la base FNV estándar. Se conservan miembros no seleccionados; Espada nunca usa las tablas de Escudo ni a la inversa.

## Validación ejecutada

- `dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release --no-incremental --nologo -v:q -bl:<temporal>/final-build-{}.binlog`: 0 errores, 88 advertencias. Comparación con build Release de la entrega anterior: mismos 67 diagnósticos distintos tras normalizar el nombre temporal WPF; ninguna advertencia nueva ni en los archivos Switch.
- Runner propio `--swsh-only`: **186 aprobadas**; incluye formatos, compresión, límites, determinismo, cantidades/pesos, UTF-16 español, borradores, origen cambiado y exportación combinada.
- `--swsh-ui-only`: **26 aprobadas**, componentes fuera de pantalla. Incluye propuesta/aplicación, tabla virtualizada e identificadora fija y etiqueta accesible. No es una prueba interactiva real.
- `--synthetic-only`: **3948 aprobadas**, regresiones sintéticas de XY, ORAS, SM y USUM más Switch.
- `--visual-redesign-only`: **95 aprobadas**, incluido chequeo de bindings de las vistas renderizadas de esa suite.
- `--guidance-only`: **233 aprobadas** fuera de pantalla.
- `--single-purchase-only`: **6 aprobadas** de esquema; no es una nueva partida jugable de compras únicas.
- `--item-icons-only`: **17 aprobadas** fuera de pantalla.
- `--swsh-gameplay-probe <extracción Espada> <salida nueva>`: lectura y propuesta de todos los módulos; repetición determinista, diferencias limitadas a campos permitidos, conservación del origen y exportación reabierta. **10.253 ranuras elegibles, 10.681 cambios de campo**: 1396 en entrenadores, 8719 en encuentros y 566 en objetos. **5237 objetos/modelos** conservados por límites de categoría/modelo/hash. 436 equipos tienen cambios; salida de 438 recursos RomFS más manifiesto y LEEME.
- Decodificador independiente `lz4.block` instalado solo en el directorio temporal de comprobación: **267 miembros** validados (13+254), **20 modificados** (2+18), hashes de manifiesto y origen correctos. `data_table.gfpak`: 295904 → 548920 bytes; `placement.gfpak`: 1531712 → 4014504 bytes.
- `git diff --check`: aprobado.

| Requirement | Evidence |
|---|---|
| Conservar información de equipos | `Swsh_Game_TrainerPreservesLevelsMovesItemsAiIvEv`, `Swsh_Game_RejectTeamCountMismatch` |
| Conservar cantidades/pesos/eventos de objetos | `Swsh_Game_ItemsPreserveQuantitiesWeightsRespawnAndEvents`, `Swsh_Game_ItemsPreserveKeyAndTmHashes` |
| Conservar niveles/probabilidades de encuentros | `Swsh_Game_EncounterPreservesLevelsWeightsAndResetsForm`, `Swsh_Game_EncounterOnlySpeciesAndFormBytes` |
| No aplicar antes de confirmar | `Swsh_Game_ProjectPreviewWaitsForApply`, `Swsh_Game_UI_TrainerPreviewWaitsForApply`, `Swsh_Game_UI_ApplyCommitsOnlySessionOverlay` |
| Exportación combinada sin perder cambios | `Swsh_Game_CombinedExportPreservesPersonalAndTrainerChanges`, `Swsh_Game_ManifestIncludesResourceHashes` |
| Rechazar estado obsoleto | `Swsh_Game_ProjectRejectsStalePreview`, `Swsh_Game_RejectChangedSource`, `Swsh_Game_RejectChangedTrainerDirectory` |
| LZ4 y datos opacos | `Swsh_Game_Lz4IndependentLiteral`, `Swsh_Game_Lz4OverlappingMatch`, `Swsh_Game_ArchivePreservesOpaqueSibling`; decodificador independiente real |

## Mod preparado y prueba manual pendiente

Carpeta nueva autorizada:
`C:/Users/Joanj/AppData/Roaming/Ryujinx/mods/contents/0100abf008968000/DR-Prueba-Entrenadores-Encuentros-Items-20261021`

Semilla 20261021, cinco módulos marcados, BST similar activo. No incluye cambios de personal_total.bin: permite probar este bloque por separado. Se copiaron y verificaron SHA-256 de los 440 archivos. No se sustituyeron ni cambiaron configuraciones de los mods anteriores. Cerrar el juego y desactivar otros mods de entrenadores/encuentros/placement (incluido Divine Sword) antes de probar. Para aislar el diagnóstico, dejar únicamente este mod activo.

Pendiente: combate con varios entrenadores (incluidos Dynamax/Gigantamax y eventos guionizados), encuentros visibles y ocultos con varios climas/zonas, recogida de objetos rojos y ocultos y persistencia/respawn. Hacer copia del guardado antes de la prueba. Desactivar el mod y reiniciar revierte el overlay; objetos y Pokémon ya obtenidos en un guardado conservan sus datos. El agente no ha probado jugabilidad, escritorio interactivo, teclado real ni DPI 100/125/150/200 %. No se ha validado una extracción real de Escudo ni licencias DLC; solo el contenido encontrado en la extracción de Espada suministrada. El éxito previo confirmado por el usuario pertenece al mod de tipos/habilidades, no a este nuevo bloque.

## Archivos de esta ampliación

Core: `pk3DS.Core/NX/SwitchBinaryResources.cs`, `SwordShieldGameplay.cs`, `SwordShieldText.cs`, `SwordShieldPersonalRandomizer.cs`, `SwordShieldPersonalTable.cs`.

App: `3DS.Progressive.App/Services/Switch/SwordShieldProject.cs`, `SwordShieldProject.Gameplay.cs`, `SwordShieldPrototypeWindow.cs`, `SwordShieldPrototypeWindow.Randomization.cs`, `SwordShieldPrototypeWindow.Gameplay.cs`, `SwordShieldGameplayWindow.cs`.

Pruebas: `3DS.Progressive.Tests/SwordShieldGameplayChecks.cs`, `Program.cs`. Documentación: este informe, `HOJA_RUTA_SWITCH.md`, `SWITCH_SWSH_RANDOMIZACION.md`. Se conservaron los demás cambios pendientes del prototipo anterior.

## Referencias

Formatos investigados con fuentes primarias de [kwsch/pkNX](https://github.com/kwsch/pkNX), licencia GPL-3.0 compatible con este repositorio: [TrainerPoke8](https://github.com/kwsch/pkNX/blob/master/pkNX.Structures/VsTrainer/GG/TrainerPoke8.cs), [GFPack](https://github.com/kwsch/pkNX/blob/master/pkNX.Containers/Misc/GFPack.cs), [esquemas SWSH](https://github.com/kwsch/pkNX/tree/master/FlatBuffers/SWSH/Schemas), [categorías de objetos](https://github.com/kwsch/pkNX/blob/master/pkNX.Structures/Legality/Tables/Tables8.cs), [EditorSWSH](https://github.com/kwsch/pkNX/blob/master/pkNX.WinForms/MainEditor/EditorSWSH.cs). Referencias consultadas el 21 de octubre; no se añadió pkNX ni un framework FlatBuffers como dependencia de producción.

Skills: pk3ds-progressive-safety, wpf, dotnet-test/code-testing, dotnet-test/run-tests, dotnet-msbuild/binlog-generation. Registros y dependencias de comprobación en temporales, fuera del repositorio. No hay agente test-engineer invocable; implementación y pruebas se realizaron en esta sesión, sin delegación.

## Confirmación del usuario y corrección posterior

El usuario confirmó entrenadores randomizados correctamente. No se interpreta como cobertura de los 437 entrenadores ni de todos los eventos. Informó de objetos visibles sin cambiar y pidió que el Pokémon visible sea distinto del que aparece al empezar el combate. Se corrigió el filtro de Field Items; el comportamiento de combate sorpresa sigue pendiente de un componente runtime. Ver [SWITCH_ESPADA_CORRECCIONES.md](SWITCH_ESPADA_CORRECCIONES.md).
