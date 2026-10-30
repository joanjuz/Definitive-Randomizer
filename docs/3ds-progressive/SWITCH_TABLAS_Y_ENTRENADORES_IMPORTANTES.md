# Switch · tablas, entrenadores importantes y encuentros vacíos

Implementación sobre los cambios locales de `feature/switch-swsh-prototype`, 22 de octubre de 2026. No se ha realizado commit, push, release ni instalación de mods. La extracción de Espada se utiliza únicamente en lectura.

## Cambios implementados

- **Level Caps y Trainer Move Rules:** tablas a todo el ancho, con ID, grupo, entrenador y Ace actual separados; filtros para importantes y líderes, columnas identificadoras congeladas, seleccionar/deseleccionar visibles, aceptar/cancelar. Cancelar recupera las opciones anteriores. Aceptar configura la próxima vista previa, no modifica los equipos directamente. No se muestran controles de Mega/Z propios de 3DS que no están implementados en este adaptador.
- **Clasificación:** Paul/Hop, Roxy/Marnie y Berto/Bede se distinguen de los líderes. Se reconocen también otros jefes. Se leen los nombres del banco inglés de la propia extracción y se muestran los nombres localizados. Los niveles proceden siempre de los equipos cargados, incluido su estado editado en la sesión.
- **Reglas por combate:** Better Movesets, ataque más fuerte, potencia mínima, tolerancia y permiso de movimientos de estado pueden variar por ID. Las reglas se aplican a las especies y niveles finales y se conservan en las opciones/historial de Switch. Las variantes siguen siendo registros individuales; el cap de campeón conserva su comportamiento existente.
- **Level Up Moves, evoluciones, MT/TR/tutores, objetos y tiendas:** tablas editables que reutilizan los controles, validaciones, eventos y guardado del editor compartido. Los métodos evolutivos tienen nombres e IDs; las tiendas conservan la etapa y la ranura. No se añadieron/borraron rutas evolutivas ni slots manuales mediante estas tablas.
- **Fix Economy:** reutiliza la tabla de precios de 3DS mediante `IEconomyEditor`. Permite seleccionar objetos, editar precios, previsualizar y aplicar con historial. El preset es el heredado Progressive para IDs compartidos, claramente identificado; no pretende ser un balance nuevo validado de Gen8. Los precios en dinero, Watts y BP siguen separados: la tabla de Fix Economy prepara dinero y la ficha del objeto permite editar las tres monedas.
- **Estáticos y regalos:** filtro opcional de BST similar (±20 %, con el propio candidato disponible), aplicado antes de preparar cambios. Se mantienen las protecciones de regalos dependientes de scripts DLC.
- **Iniciales:** filtro de exactamente tres etapas evolutivas usando el grafo actual, con todas las especies de la cadena presentes y sin ciclos. Los registros se identifican desde la extracción original, de modo que conservan su identidad después de randomizar la sesión. Si no se reconocen o no hay candidatos compatibles, se rechaza la vista previa; no se adivinan regalos distintos.
- **Encuentros vacíos:** desactivado por defecto. Materializa campos escalares omitidos de ranuras ya existentes en los vectores de encuentros. Solo reconstruye subtablas del esquema conocido; preserva los bytes originales y redirige las referencias seleccionadas a estructuras añadidas. Los campos desconocidos y los alcances que separan referencias de subtablas compartidas se rechazan. Las probabilidades se reparten hasta sumar 100; las tablas sin niveles usan los valores explícitos de la opción, inicialmente 1–5. No crea zonas ni puntos de aparición nuevos ni garantiza más Pokémon simultáneos en pantalla. Con alcance completo incluye las regiones vacías que antes no eran listables; con un subconjunto conserva el alcance seleccionado. Los IDs de regiones existentes se mantienen al actualizar la lista.
- **Level caps y herramientas:** eliminada la marca del proveedor/emulador del encabezado principal. Se conservan créditos, licencias y restricciones reales de instalación. Las ayudas explican que el PC es el del Centro Pokémon dentro del juego y que sincronización se refiere a naturaleza. Underworld continúa marcado como experimental porque el contrato disponible no permite describir con precisión su efecto; no se presenta como necesario para combate sorpresa.
- Los templates de opciones anteriores pueden omitir las nuevas claves y recibir sus valores predeterminados; los campos desconocidos o fuera de rango siguen rechazándose.

## Excel de referencia

Se leyó `Important Trainers.xlsx` sin modificarlo. Sus hojas son Rival Battles, Gym Leaders, Champions Cup y Other Boss Fights. Identifica las familias de rivales/líderes y otros jefes, pero sus equipos, niveles, movimientos y objetos pertenecen a una ROM de dificultad y **no se importaron**.

La extracción real reconoció 85 registros de rivales, 61 de líderes y 58 de otros importantes. Son registros/variantes, no 204 eventos únicos. Incluyen las alternativas por inicial, revancha y contenido adicional presentes en los archivos; no se asignó una localización narrativa inventada a cada ID.

## Investigación de intercambios · pendiente

El esquema de pkNX distingue el Pokémon entregado (`Species`, campo 8) de los requisitos `RequiredForm` (17), `RequiredSpecies` (18), `RequiredNature` (19) y `UnknownRequirement` (20). Es un punto concreto para continuar la investigación de aceptar cualquier Pokémon. El valor 0 de RequiredSpecies no se ha demostrado como comodín. No se ha parcheado ni añadido un botón que afirme eliminar la comprobación: falta seguir el consumidor del juego/script y validar aceptación/rechazo, cancelación y entrega con una partida de prueba. El parche CRO de Gen6/Gen7 no se puede trasladar literalmente a Switch.

## Archivos de esta ampliación

- `3DS.Progressive.App/Models/ProjectEditor.cs`
- `3DS.Progressive.App/RecordEditorView.xaml.cs` y `RecordEditorView.Tables.cs`
- `3DS.Progressive.App/EconomyPriceView.cs`
- `3DS.Progressive.App/Services/GameItemService.cs` y `Services/IEconomyEditor.cs`
- `3DS.Progressive.App/Services/Switch/SwordShieldRecordEditor.cs`, `.Trainers.cs` y `.Economy.cs`
- `3DS.Progressive.App/Services/Switch/SwordShieldProject.TrainerKinds.cs`, `.Gameplay.cs` y `.Data.cs`
- `3DS.Progressive.App/Services/Switch/SwordShieldWorldEditor.cs`
- `3DS.Progressive.App/SwordPokeFeurWindow.cs`
- `pk3DS.Core/NX/SwordShieldBattlePolicy.cs`, `SwordShieldDataEditor.cs`, `SwordShieldGameplay.cs`, `SwordShieldEncounterExpansion.cs` y `PokeFeurOptions.cs`
- `3DS.Progressive.Tests/Program.cs`, `SwitchTableChecks.cs`, `SwitchParityChecks.cs` y `SwitchVisualChecks.cs`
- Este informe y una referencia a esta ampliación en `SWITCH_PARIDAD_CON_3DS.md`. Los demás archivos pendientes de la rama son anteriores a esta ampliación y se conservaron.

## Evidencia de comportamiento

| Requirement | Evidence |
|---|---|
| Rivales separados de líderes | `Switch_Tables_ClassifiesRivalsSeparatelyFromGymLeaders` y `Switch_Tables_RealTrainerClassification` |
| Reglas por combate y conservación de otros equipos | `Switch_Tables_PerTrainerMoveRuleChangesFinalMoves`, `Switch_Tables_PerTrainerMoveRuleLeavesOtherTrainerUntouched` |
| Tablas editables con guardado existente | `Switch_Tables_UsesEditableGrid_Learnsets`, `Switch_Tables_UsesEditableGrid_Machines`, `Switch_Tables_GridEditsUseExistingValidatedSaveAndHistory` |
| Tabla de reglas opaca y cierre por Cancelar | `Switch_Tables_OpaqueFullWidthRuleTable_TrainerCapsButton`, `Switch_Tables_CancelClosesRuleTable_TrainerMovesButton` |
| Fix Economy limitado a filas/precios seleccionados | `Switch_Tables_FixEconomyPreviewUsesOnlySelectedMoneyPrice`, `Switch_Tables_FixEconomyAppliesAndReopensPrice` |
| BST de regalos | `Switch_Tables_GiftBstFiltersDistantSpecies_0` a `_11`, `Switch_Tables_RealBstPreview_Gifts` |
| Tres etapas y continuidad tras randomizar | `Switch_Tables_StartersUseActualThreeStageEvolutionGraph`, `Switch_Tables_StarterRecordIdentitySurvivesPreviousRandomization` |
| Campos omitidos y conservación de bytes/alcance | `Switch_Tables_MaterializesOmittedFieldsPreservingOriginalBytesExceptSelectedReference`, `Switch_Tables_MaterializationPreservesUnselectedRegions`, `Switch_Tables_FillsPhysicallyOmittedSlotsAndLevelsWithValidReopenedData` |
| Rechazo de perfiles/alcances inseguros | `Switch_Tables_ExpansionRejectsUnknownSlotFieldsBeforeWriting`, `Switch_Tables_ExpansionRejectsScopeSplittingSharedSubtableReferences` |

## Validación y límites

Resultados finales reales:

| Comprobación | Resultado |
|---|---|
| `dotnet build pk3DS.slnx -c Release --no-restore` | PASS, 0 errores, 56 emisiones de advertencias preexistentes |
| `dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release --no-restore` | PASS, 0 errores, 88 emisiones; ninguna advertencia nueva al comparar mensajes normalizados con la validación anterior |
| `--swsh-parity-only` | 146 comprobaciones PASS |
| `--swsh-ui-only` | 72 componentes WPF PASS; fuera de pantalla, no interacción humana |
| `--swsh-only` | 312 comprobaciones PASS |
| `--visual-redesign-only` | 95 comprobaciones PASS |
| `--guidance-only` | 233 comprobaciones PASS; fuera de pantalla |
| `--all-ui-only` | 398 comprobaciones PASS; eventos/componentes programáticos, no recorrido humano |
| `--synthetic-only` | 4055 comprobaciones PASS con datos XY, ORAS, SM y USUM sintéticos |
| `--swsh-visual-preview` con Espada real | 19 renders; sin errores de bindings registrados. Revisados visualmente los nuevos layouts y corregidas superposición/cabeceras. No equivale a DPI real ni prueba interactiva |
| `--swsh-parity-probe` | Clasificación real 85/61/58; 1729 regiones de encuentros y 22 de Field Items antes de expansión. Previews completos BST de encuentros y entrenadores sin modificar el origen |
| `--swsh-table-probe` | BST de estáticos: 618 cambios; regalos: 40 y tres registros de iniciales reconocidos; reglas/cap del rival: 10 cambios; 9520 ranuras omitidas rellenadas. Resultado reabierto con 1798 regiones e identidad de selección preservada; aplicación únicamente en memoria |
| `git diff --check` | PASS |

Total de las siete suites: **5311 comprobaciones superadas**. La última protección de referencias compartidas se volvió a compilar y probar en las suites Switch; las suites generales de 3DS se habían ejecutado satisfactoriamente antes de esa protección exclusiva del módulo NX. No se afirma un recorrido completo de todas las funciones ni una prueba dentro del juego.

Los logs, binlogs, scripts y renders de esta validación están fuera del repositorio, en el directorio temporal `DR-Switch-tables-20261022`.
 Las primeras pruebas detectaron y permitieron corregir un caso de especie 0, la coerción inicial de FrozenColumnCount, un fondo transparente y los campos físicamente omitidos de encuentros. El primer intento de la regresión general se detuvo por la ausencia de LegacyBatch/pk3DS.WinForms.exe al compilar con UseAppHost=false; se repitió con el empaquetado normal, sin modificar la lógica de Batch.

Pendiente de comprobación manual: jugabilidad de los encuentros añadidos, balance de niveles/probabilidades, iniciales y equipos de rivales resultantes, compras con las tres monedas, teclado/foco y accesibilidad interactiva, DPI real y Escudo. Las capturas WPF fuera de pantalla y las pruebas sintéticas no demuestran esos resultados. Siguen pendientes Global Template/Batch de Switch y la paridad avanzada documentada en SWITCH_PARIDAD_CON_3DS.md.

## Referencias

- pkNX fijado a `d191cd0e5c05f2af81d9a41c1f1d82e6621b351a`: [EncounterArchive.fbs](https://github.com/kwsch/pkNX/blob/d191cd0e5c05f2af81d9a41c1f1d82e6621b351a/FlatBuffers/SWSH/Schemas/EncounterArchive.fbs), [EncounterTradeArchive.fbs](https://github.com/kwsch/pkNX/blob/d191cd0e5c05f2af81d9a41c1f1d82e6621b351a/FlatBuffers/SWSH/Schemas/EncounterTradeArchive.fbs) y EvolutionType.cs de la misma copia local.
- [Contrato del módulo integrado](https://github.com/JonatanUHC/PokeFeurRandomSWSH/blob/main/contract/runtime-config.schema.json) y su README, contrastados con la copia local fijada de la investigación anterior. No se reemplazó el payload integrado ni se actualizaron binarios instalados.
