# Paridad de Espada/Escudo con la experiencia 3DS
> Ampliación posterior: ver [tablas, entrenadores importantes y encuentros vacíos](SWITCH_TABLAS_Y_ENTRENADORES_IMPORTANTES.md). Sus resultados y cambios son adicionales a la validación histórica de este informe.

Fecha: 2026-10-22. Rama: feature/switch-swsh-prototype.

## Alcance real de esta iteración

Se sustituyó la navegación hacia tablas genéricas reducidas por proveedores de datos NX que implementan el contrato IProjectEditor utilizado por la aplicación 3DS. Los datos se mantienen como overlays de sesión y Exportar mod genera su salida; la extracción de entrada permanece en lectura. Esta entrega avanza la paridad funcional y visual, pero no declara implementadas todas las capacidades de 3DS.

Skills aplicadas: pk3ds-progressive-safety, wpf, redesign-existing-projects (criterios adaptados a WPF), dotnet-test/code-testing y run-tests, dotnet-msbuild/binlog-generation. El especialista test-engineer no estaba disponible; se mantuvo el proceso de investigación, planificación, pruebas y revisión en la tarea principal.

## Implementado

### Interfaz y flujos

- RecordEditorView, el mismo control utilizado por 3DS, presenta entrenadores, encuentros, Field Items, movimientos, aprendizajes, evoluciones, compatibilidades, objetos, tiendas, estáticos, regalos e intercambios.
- Conserva lista/búsqueda, formularios, inspector, alcance completo/filtrado/seleccionado, vista previa, aplicación e historial. Las capacidades específicas se ofrecen mediante interfaces de presentación, sin suplantar una sesión CTR.
- Entrenadores reutiliza las pestañas de opciones y las tarjetas de equipo de 3DS, con sprites y acceso a Principal, IV/EV y Movimientos. Se muestran las acciones de niveles/reglas y un acceso a Player Level Caps por entrenadores.
- Textos largos de acciones y entradas se ajustan en varias líneas; se evita el desplazamiento horizontal de los listados. Las etiquetas de templates corresponden a su sección.
- Los cambios pendientes de campos requieren confirmación antes de abandonar la selección/sección. Las páginas limpias se actualizan al volver; una propuesta obsoleta se rechaza al aplicarla. La navegación se bloquea mientras carga un proveedor.

### Pokémon

- Ficha con sprite opcional mediante el mismo SpriteService que usa 3DS, identidad, tipos, BST, estado de cambios y tarjetas del inspector. El BST y los tipos se actualizan mientras se editan campos.
- Editor agrupado de 31 campos: seis estadísticas, dos tipos, captura, tres habilidades, tres objetos, género, eclosión, amistad, crecimiento, dos grupos huevo, color, experiencia base, altura/peso y seis rendimientos EV.
- Guardado atómico de todos los campos editados, con historial individual. Color y rendimientos EV conservan los bits adyacentes; se validan límites antes de aplicar.
- Modificar Pokémon ofrece campo, valor y alcance, con vista previa y aplicación atómica de una entrada o un conjunto.
- Randomización de estadísticas por mezcla o redistribución conservando BST; tipos, habilidades observadas, habilidades normales distintas, Superguarda opcional, prohibición de Sombratrampa/Trampa Arena, captura con mínimo, objetos, grupos huevo y crecimiento. Conserva las protecciones existentes de especies con mecánicas de forma.
- El diálogo de randomización incluye vista previa y aplicación; Exportar permanece como acción de la cabecera.

### Entrenadores, movimientos y objetos

- Progressive BST con tabla editable y rangos que cubren 1–100 sin huecos ni solapamientos; opción libre por tramo. La selección usa el nivel final del Pokémon.
- Escala porcentual de niveles y niveles exactos de los jefes de historia configurados. Los niveles exactos prevalecen sobre el porcentaje. La regla de Lionel 149 incluye sus variantes 189 y 190 dentro del alcance elegido.
- Better Movesets usa aprendizajes de la especie/forma final al nivel final. Incluye STAB, estadística ofensiva dominante, tolerancia mixta, potencia mínima, estados, prohibiciones y selección sin duplicados. Conserva movimientos de especies que legalmente solo disponen de estados; informa restricciones incompatibles antes de aplicar.
- Smart Items se calcula después del equipo y los movimientos finales. Tiene modos Normal/Relevantes/Competitivos y un conjunto conservador de objetos; evita objetos de elección y Chaleco Asalto cuando hay movimientos de estado. No requiere activar randomización de especies ni una segunda casilla general.
- Los Player Level Caps automáticos usan los equipos actuales de los ocho gimnasios y el campeón. El último límite considera las tres variantes de historia de Lionel y registra sus fuentes/hashes. Si cambian los niveles relevantes, se debe actualizar la configuración antes de exportar.

### Encuentros, objetos de campo y templates

- Edición por región de especies, formas, niveles y probabilidades. Edición de Field Items visibles/ocultos por área usando hashes resueltos del juego.
- Randomización de visibles y ocultos por separado, con alcance seleccionado y opciones BST para encuentros. Sin acciones seleccionadas se generan cero cambios.
- Las ediciones de archivos GFPack conservan miembros ajenos y bytes no seleccionados, se reabren para verificar los valores y validan combinaciones de especie/forma y rangos de nivel.
- Importación/exportación de opciones por sección con formato DefinitiveRandomizer.SwitchOptions, versión 1. Se conservan rangos BST dinámicos y se rechazan secciones, formatos y valores incompatibles. Este formato no convierte recetas 3DS ni ejecuta acciones 3DS sobre Switch.
- Las opciones, semillas, alcances y cambios quedan en los historiales/manifiestos de las operaciones correspondientes.

## Errores anteriores encontrados

- waza0000.wazabin contiene una copia de Destructor con MoveID 1. Se excluye el registro ficticio por su nombre/ID canónico; no se muestra ni randomiza como un segundo movimiento.
- La categoría usa 0 Estado, 1 Físico y 2 Especial. Se corrigieron las etiquetas, los filtros ofensivos de aprendizajes y las reglas de equipos/objetos que dependían de esa interpretación.
- La suite UiSmoke comprobaba navegación 3DS inmediatamente después de LoadProject. El código de HEAD ya llevaba al usuario a Inicio y deshabilitaba el editor hasta elegir modo. Se ajustó la prueba para elegir Modo completo antes de comprobar Tiendas/BP; no se alteró ese comportamiento de la aplicación.
- Dos comprobaciones antiguas suponían que la tabla personal terminaba tras las habilidades. Se delimitan ahora esos tres campos explícitamente y se prueba aparte la extensión a 31 campos, sus límites y la preservación de datos.

## Validación

Los logs y binlogs se guardan fuera del repositorio en C:/Users/Joanj/AppData/Local/Temp/DR-Switch-parity-20261022/.

- Compilación Release de pk3DS.slnx: 0 errores.
- Compilación Release de 3DS.Progressive.Tests y referencias App/Core/WinForms, también con --no-incremental: 0 errores. Hubo 78/88 emisiones de advertencias según el paso WPF; la comparación normalizada por archivo/código/mensaje con la línea base visual encontró cero advertencias nuevas.
- --swsh-only: 312 comprobaciones aprobadas.
- --swsh-parity-only: 120 comprobaciones aprobadas.
- --swsh-ui-only: 62 comprobaciones de componentes aprobadas, fuera de pantalla.
- --visual-redesign-only: 95 comprobaciones aprobadas.
- --guidance-only: 233 comprobaciones aprobadas.
- --all-ui-only: 398 comprobaciones aprobadas tras adaptar UiSmoke al flujo existente de Inicio. Esa suite crea ventanas WPF y dispara eventos por código; no representa una revisión interactiva humana ni control del escritorio.
- --synthetic-only: 4055 regresiones aprobadas con fixtures XY, ORAS, SM y USUM.
- --swsh-expansion-probe: 1457 verificaciones reales de lectura, preview, aplicación, exportación temporal, reapertura/hashes e inmutabilidad de los originales.
- --pokefeur-probe con paquete integrado: 42 verificaciones reales aprobadas, con exportación/configuración SD solo dentro de temporales. No se instaló ningún mod ni configuración en Ryujinx.
- --swsh-parity-probe: lectura de 1729 regiones de encuentros y 22 áreas de Field Items; preview global de encuentros Progressive BST (8779 cambios) y de entrenadores con BST/movimientos/Smart Items (7432 cambios), más una aplicación de muestra en memoria. Esas cantidades son resultados de una semilla de prueba, no escenarios jugables independientes.
- Renderizado de páginas a 900x620, 1280x800 y 1600x900; captura adicional de opciones y tabla BST de entrenadores. Se examinaron las imágenes y se comprobó la ausencia de errores de binding registrados por WPF durante esos renders.
- git diff --check: sin errores.

| Requirement | Evidence |
|---|---|
| Reutilizar interfaz 3DS | Switch_UIUsesShared3dsRecordEditor; Switch_Parity_UITrainerUsesOriginalRecordViewAndFieldGroups |
| Ficha editable y estado vivo | Switch_Parity_UIFullPersonalFormSavesThroughSharedSession; Switch_Parity_UIPersonalBstAndDirtyStateUpdateWhileEditing |
| BST por nivel final | Switch_Parity_BstUsesFinalTrainerLevelAcrossBoundary; Switch_Parity_BstRejectsOverlap |
| Caps por jefes y variantes | Switch_Parity_PlayerCapsConsiderAllChampionStarterVariants; Switch_Real_ManualBossLevelInvalidatesAutomaticCapsExport |
| Movimientos legales y únicos | Switch_Parity_MovesRespectFinalSpeciesLevelCategoryAndUniqueness; Switch_Parity_StatusOnlyLegalSpeciesKeepsItsUsableMoves |
| Smart Items coherentes | Switch_Parity_SmartItemsRejectChoiceAndAssaultVestWithStatusMoves; Switch_Parity_SmartItemsCanApplyWithoutSpeciesRandomizationOrExtraEnableGate |
| Modificación masiva atómica | Switch_Parity_BulkModifyAppliesAllSelectedEntriesAtomically; Switch_Parity_BulkModifyRejectsUnavailableScopeWithoutPartialWrite |
| Alcance y preservación | Switch_Parity_WorldRandomizationHonorsSelectedAreaScope; Switch_Parity_WorldManualEditPreservesOtherMemberAndOpaqueSibling |
| Templates de opciones | Switch_Parity_SectionOptionsTemplateRoundTripsWithoutApplying; Switch_Parity_OptionsTemplateRejectsDifferentSection |
| Sin acciones = cero cambios | Switch_Parity_UncheckedTrainerActionsProduceNoChanges; Switch_Parity_UncheckedSharedOptionsProduceNoChanges |

## Archivos de esta iteración

Todos los paths siguientes son relativos al repositorio D:/Progra/Pokemon/Randomizer/pk3DS-Progressive-Dev.

- 3DS.Progressive.App/App.xaml; MainWindow.Switch.cs; Models/ProjectEditor.cs; RecordEditorView.xaml.cs; SwitchWorkspaceView.cs.
- 3DS.Progressive.App/SwordShieldPrototypeWindow.cs; SwordShieldPrototypeWindow.Personal.cs; SwordShieldPrototypeWindow.Modify.cs; SwordShieldPrototypeWindow.Randomization.cs; SwordShieldGameplayWindow.cs; SwordShieldGameplayWindow.Progressive.cs; SwordPokeFeurWindow.cs.
- 3DS.Progressive.App/Services/Switch/SwordShieldProject.cs; SwordShieldProject.Data.cs; SwordShieldProject.Gameplay.cs; SwordShieldProject.World.cs; SwordShieldProject.PokeFeur.cs; SwordShieldRecordEditor.cs; SwordShieldRecordEditor.Trainers.cs; SwordShieldWorldEditor.cs; SwordShieldOptionsStore.cs.
- pk3DS.Core/NX/SwordShieldPersonalTable.cs; SwordShieldPersonalRandomizer.cs; SwordShieldGameplay.cs; SwordShieldBattlePolicy.cs; SwordShieldWorldData.cs; SwordShieldDataEditor.cs.
- 3DS.Progressive.Tests/Program.cs; SwordShieldChecks.cs; SwordShieldRandomizationChecks.cs; SwitchExpansionChecks.cs; SwitchParityChecks.cs; SwitchWorldChecks.cs; SwitchVisualChecks.cs; UiSmoke.cs.
- Este informe.

Se preservaron los cambios locales anteriores. La lista no pretende atribuirse todos los archivos pendientes de la rama. No hubo commit, push ni release.

## Paridad todavía pendiente

- Global Template y Batch propios de Switch, con ejecución/replay de acciones y assets equivalentes; los templates de opciones por sección no sustituyen ese sistema.
- Adaptar los recorridos Inicio/modos simplificado-completo/randomizado guiado y la vista guiada a una sesión Switch completa.
- Smart Items todavía usa un perfil conservador: falta trasladar todo el catálogo de sinergias por habilidad/especie/clima y las reglas específicas/categorías del motor 3DS. Better Movesets aún no incorpora todas sus variantes, ni ampliación por MT/TR y tutores.
- La edición de movimientos presenta los seis campos actuales; faltan los demás efectos, flags, descripciones y sus previews. Faltan movimientos huevo, contenido de MT/TR y algunas reglas de aprendizajes/compatibilidad/evolución (Sanity/Follow, cadenas, filtros avanzados).
- Faltan herramientas específicas equivalentes de IA/tamaño de equipo/combates y otras reglas de entrenadores; textos e idioma editables, balance/mecánicas, incursiones, compras únicas y otros parches requieren implementación por formato/juego.
- Los nombres de áreas de encuentros se muestran por zona/clima y los de Field Items por código de área; queda el mapeo a nombres geográficos localizados.
- Escudo conserva las rutas/formatos comunes existentes y sus fixtures; esta iteración solo validó datos reales de Espada 1.3.2. El runtime nativo y el mapeo de jefes siguen limitados a Espada/Ryujinx.

## Validación manual pendiente

Probar dentro del juego equipos nuevos, progresión, aprendizaje efectivo, objetos, compras y caps, incluyendo las tres ramas del campeón. Revisar los diálogos de modificación/randomización, confirmaciones, teclado/foco, lector de pantalla, alto contraste y escalados de Windows 100/125/150/200 %. El sprite usa el servicio opcional de 3DS y su disponibilidad depende de la conexión; un render fuera de pantalla no prueba todo su ciclo Loaded ni todas las formas.

## Referencias técnicas

Se contrastó el formato con la copia local fijada de pkNX d191cd0e5c05f2af81d9a41c1f1d82e6621b351a: [Waza.fbs](https://github.com/kwsch/pkNX/blob/d191cd0e5c05f2af81d9a41c1f1d82e6621b351a/FlatBuffers/SWSH/Schemas/Waza.fbs), [PersonalInfo8SWSH](https://github.com/kwsch/pkNX/blob/d191cd0e5c05f2af81d9a41c1f1d82e6621b351a/pkNX.Structures/Personal/Info/PersonalInfo8SWSH.cs). Se conservan las referencias/licencias existentes de los adaptadores NX.
