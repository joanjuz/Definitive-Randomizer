# Switch: ajustes de tablas, Global Template y Batch

Fecha: 22 de octubre de 2026. Rama de trabajo: `feature/switch-swsh-prototype`.

## Implementación

- **Tiendas:** «Solo randomizar tiendas especiales» está activado por defecto. El alcance se obtiene del inventario original: tiendas fijas con objetos no básicos. Se conservan suministros básicos, inventarios por progreso y vectores compartidos con ellos. Desmarcarlo conserva la randomización general. Es una clasificación por estructura y contenido, todavía pendiente de contrastar con todos los vendedores dentro del juego; no es un mapa auditado de NPC.
- **Evoluciones:** el argumento cambia de control según el método seleccionado: catálogo de objetos, movimientos, especies o tipos; los métodos numéricos conservan su campo numérico. Cambiar el método conserva el valor actual y reconstruye el control sin guardar automáticamente. Los valores ajenos al catálogo siguen visibles para evitar pérdidas.
- **MT/TR y tutores:** cada casilla muestra MT00, TR00, Tutor01 o Armadura01 y su número correspondiente. No se modifican los índices del backend ni la compatibilidad existente.
- **Global Template:** sección nativa integrada en la navegación de Switch. Permite crear desde operaciones ya aplicadas al historial, cargar/exportar una receta, previsualizar en una sesión aislada y aplicar cada operación al historial. La exportación del mod continúa siendo una acción separada.
- **Batch ROM Builder:** genera entre 1 y 50 mods independientes desde la extracción original y una receta. No reconstruye ROMs de Switch. Hay progreso, cancelación entre operaciones/salidas, protección contra colisiones y manifiesto con estados, hashes y semillas efectivamente utilizadas. Las salidas completadas se conservan si se cancela.

## Contrato de receta y protección de datos

Formato separado `DefinitiveRandomizer.SwitchGlobal`, versión 1, con Title ID y Build ID estrictos. No se convierten ni alteran recetas 3DS. Se conserva metadato raíz desconocido; acciones u opciones no soportadas se rechazan expresamente.

Se registran randomización y edición manual de Pokémon; tablas de movimientos, aprendizajes, evoluciones, máquinas, objetos, tiendas, estáticos, regalos e intercambios; entrenadores, encuentros y objetos de campo; configuración del runtime integrado. Los cambios manuales del mundo se identifican por región/grupo/campo, sin depender de offsets físicos que puedan cambiar al ampliar encuentros.

Al crear una receta se ordenan primero las randomizaciones (movimientos y Pokémon antes de entrenadores), después las ediciones manuales en su orden y finalmente el runtime, cuyos caps se derivan de los equipos finales. Cada operación aleatoria recibe una semilla nueva, incluso si no produce diferencias. Se exportan las opciones resueltas en `switch-recipe-applied.json` por mod. Las recetas con runtime están vinculadas a la versión del módulo integrado.

La previsualización conserva la sesión original ante errores y cancelación. Aplicar comprueba identidad, revisión y generación del historial, incluso si los bytes cambiaron y posteriormente se restauraron. Batch usa sesiones nuevas, exige destinos fuera de la extracción, no sobrescribe salidas existentes ni instala nada en la SD del emulador.

## Archivos de esta implementación

- Core: `pk3DS.Core/NX/SwordShieldSpecialShops.cs`, `SwordShieldDataEditor.cs`.
- Servicios: `3DS.Progressive.App/Services/Switch/SwordShieldProject.Global.cs`, `.Batch.cs`, `.Data.cs`, `.Gameplay.cs`, `.PokeFeur.cs`, `SwordShieldProject.cs`; `SwordShieldRecordEditor.cs`, `.Arguments.cs`, `SwordShieldOptionsStore.cs`.
- Interfaz compartida: `Models/ProjectEditor.cs`, `RecordEditorView.xaml.cs`, `RecordEditorView.Tables.cs`, `SwitchAutomationView.cs`, `SwitchWorkspaceView.cs`.
- Pruebas: `SwitchAutomationChecks.cs`, `SwitchWorldChecks.cs`, `SwitchVisualChecks.cs`, `Program.cs`.

La rama contiene además cambios previos de la integración Switch; no se han descartado ni restaurado. Véanse los informes anteriores [paridad](SWITCH_PARIDAD_CON_3DS.md) y [tablas y entrenadores](SWITCH_TABLAS_Y_ENTRENADORES_IMPORTANTES.md).

## Validación y límites

- Solución Release: 0 errores, 56 emisiones de advertencia. Proyecto de pruebas Release: 0 errores, 88 emisiones. Comparación normalizada: 27 advertencias únicas conocidas y ninguna nueva.
- Runner: `--swsh-parity-only` 168; `--swsh-ui-only` 77; `--swsh-only` 312; `--visual-redesign-only` 95; `--guidance-only` 233; `--all-ui-only` 398; `--synthetic-only` 4055. Total: 5338 comprobaciones satisfactorias.
- Datos reales de Espada: `Switch_Global_RealSpecialShopsPreview` (759 diferencias en el alcance de 161 inventarios especiales); `Switch_Global_RealPersonalTrainerRulesAndRuntimeReplay` (778 diferencias); `Switch_Global_RealBatchExportsTwoIsolatedMods` (2 mods en temporal). Son comprobaciones de datos/exportación, no de jugabilidad.
- 21 capturas WPF a 96 DPI, incluyendo Global y Batch; recorrido renderizado sin errores de binding. Inspección visual de ambas nuevas pantallas. Se ajustó después únicamente la alineación vertical del resumen y se recompiló y repitió la suite Switch UI.
- `git diff --check`: correcto. Registros y binlogs conservados en `C:/Users/Joanj/AppData/Local/Temp/DR-Switch-global-20261022`.

Pruebas de regresión clave: `Switch_Global_OnlySpecialShopsRandomizationPreservesNormalInventories`, `Switch_Global_SpecialScopeProtectsInventoriesSharedWithProgressionShops`, `Switch_Global_ChangingEvolutionMethodUpdatesArgumentControlAndPreservesValue`, `Switch_Global_MachinesShowGameCorrectZeroBasedNumbers`, `Switch_Global_FailedPreviewDoesNotPartiallyApply`, `Switch_Global_RejectsPreviewAfterHistoryChangesEvenWhenBytesMatch`, `Switch_Global_BatchUsesDistinctSeedsPerMod`, `Switch_Global_BatchCancellationKeepsCompletedModsAndManifest`.

Primera integración funcional: crear desde historial exige aplicar antes las opciones; no captura controles todavía sin aplicar. No incluye conversión automática de Global 3DS, edición avanzada de la lista de acciones ni assets externos. Las restricciones del runtime integrado y de sus formatos de exportación permanecen. Espada 1.3.2 se comprueba con datos reales en lectura; Escudo, jugabilidad de los mods exportados, todos los NPC de tiendas, teclado y distintos DPI requieren validación manual. Los renders fuera de pantalla no equivalen a pruebas interactivas.

No se hicieron commits, push, instalaciones en el emulador ni modificaciones de la extracción original.
