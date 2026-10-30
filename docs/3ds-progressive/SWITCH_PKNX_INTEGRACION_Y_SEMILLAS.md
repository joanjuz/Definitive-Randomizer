# Espada: integración de herramientas, semillas y configuración SD

Fecha: 22/10/2026. Rama: `feature/switch-swsh-prototype`. Sin commit, push, tag ni release en esta actualización. Se conservaron los cambios pendientes anteriores, incluidos iconos y runtime integrado.

## Causa de los encuentros repetidos y corrección aplicada

El mod indicado por el usuario, `Definitive-SWSH-20261022-115430-7fe1b0`, exportaba 664 especies. Sin embargo, el archivo activo `sdcard/config/swsh-mods-exl/config.toml` de Ryujinx seguía usando `battle_pool = [1, 4, 7]` y `seed = 20261022`, heredados del diagnóstico. El runtime lee la SD compartida, no el archivo dentro de la carpeta del mod. Esto explica la limitación observada; no constituye una medición estadística del RNG dentro del juego.

Se verificó el hash del runtime del mod (`AB13EC3B…563331`) y, con Ryujinx cerrado, se instalaron sus opciones completas en la SD, con una nueva semilla criptográfica de 63 bits. No se modificó el mod original ni la extracción. La configuración anterior se conservó en:

`C:/Users/Joanj/AppData/Roaming/Ryujinx/sdcard/config/swsh-mods-exl/config.toml.before-full-pool-92728e38fd.bak`

La SD quedó con 664 especies y semilla `9195966967876602368`. El usuario debe volver a iniciar el juego para comprobar el resultado jugable. Las opciones adicionales que había marcado se conservaron; su comportamiento completo no se considera validado por corregir el pool.

La aplicación ahora compara los hashes de la configuración aplicada y de la SD. Añadir el runtime muestra si coinciden y cuántas especies contiene el pool. Después de exportar, ofrece instalar la configuración si difiere, con confirmación y copia de seguridad. Esa copia de seguridad y el rechazo de configuraciones desactualizadas se conservan.

## Semillas

- Se elimina la semilla fija de las interfaces activas de Switch. El generador usa `RandomNumberGenerator`, 32 bits para herramientas offline y 63 bits para TOML.
- Las semillas se muestran de solo lectura, con acción para generar otra aleatoriamente.
- Una vista previa conserva su semilla al aplicarla: no se vuelve a sortear entre previsualizar y añadir.
- La siguiente randomización genera una semilla nueva; abrir otra extracción también renueva la semilla. Cargar una receta del runtime también genera una nueva.
- Los servicios y manifiestos conservan la semilla para reproducción y diagnóstico. Esto no convierte una semilla conocida en predeterminada de futuras operaciones.

## Interfaz integrada

Inicio → **Espada/Escudo · editor Switch** abre un espacio dentro de la ventana principal, con los estilos e iconos de la aplicación. No abre el diálogo de prototipo. El logo/nombre devuelve a Inicio sin descartar la sesión Switch.

La navegación contiene Pokémon, Entrenadores, Encuentros, Field Items, combate sorpresa, las diez áreas de datos de abajo, Level caps y EXP, Mecánicas y herramientas e Historial. Las secciones reutilizan sus controles y conservan el estado al navegar. Entrar en una sección no activa opciones de randomización.

La cabecera enruta Abrir proyecto al proveedor Switch y muestra Exportar mod. Deshabilita las acciones exclusivas de ROM 3DS mientras está visible este espacio; al volver a Inicio restaura sus estados anteriores. El cierre de la aplicación comprueba la sesión Switch pendiente. Los diálogos de los editores se asocian a la ventana principal visible; los botones Cerrar incrustados no se registran como Cancelar de esa ventana.

Los controladores anteriores de ventana se reutilizan como páginas mediante sus contenidos, sin duplicar motores. Una futura extracción de esos controladores a UserControl puede mejorar la organización interna; no se ha reescrito toda la arquitectura.

## Herramientas de datos incorporadas

Cada área tiene tabla editable, filtro, rangos, columnas adaptables, identificación congelada y virtualización. La vista previa muestra el número de cambios y una muestra; al añadir, el historial registra cada campo y el manifiesto conserva el listado completo. La extracción se mantiene en lectura. La previsualización queda ligada al proyecto y revisión; se rechaza si otra sección cambió la sesión.

| Área | Edición y randomización | Preservación y límites |
|---|---|---|
| Movimientos | Tipo, categoría, potencia, precisión, PP y prioridad; randomizar tipos y categorías | Conserva los demás 49 campos del esquema. Precisión 101 original admitida. Movimientos no utilizables protegidos. Si faltaba un campo con valor predeterminado, se reconstruye la tabla escalar conocida; no se promete identidad byte a byte de su padding |
| Level Up Moves | Movimiento y nivel por ranura; randomización de movimientos | Conserva cantidad, niveles y sentinelas; excluye Z/Max y movimientos no utilizables; primer aprendizaje ofensivo. No incluye movimientos huevo |
| Evoluciones | Método, argumento, destino, forma y nivel de rutas existentes; destinos aleatorios o conversión de intercambio a nivel 37 | Los destinos aleatorios son terminales para evitar ciclos nuevos; conserva condiciones/ranuras. No incorpora altas/bajas de rutas vacías ni parches nuevos de métodos |
| MT y tutores | Compatibilidad MT00–99, TR00–99, ocho tutores de tipos y dieciocho de Armadura; probabilidad 0–100 % | Solo modifica bits conocidos; conserva reservados. No cambia qué movimiento contiene cada MT/TR |
| Objetos y economía | Precios en dinero, Watts y BP; precios aleatorios para objetos comunes | Conserva índices, metadatos y precios gratuitos durante randomización. Rechaza mutaciones indirectas de IDs que comparten registro |
| Tiendas | Objetos por inventario/etapa; reemplazos aleatorios de objetos permitidos | Conserva hashes, etapas de desbloqueo, tablas e inventarios. No implementa compras únicas de 3DS en Switch |
| Encuentros estáticos | Especie, forma, nivel y Gigantamax cuando existen esos campos; especies aleatorias | Protege escenarios especiales de historia y no los expone para editar. No incluye incursiones |
| Regalos | Especie, forma, nivel y Gigantamax disponibles; especies aleatorias | Conserva guiones, identificadores y otros parámetros. La randomización preserva Kubfu, Glastrier y Spectrier hasta validar sus guiones DLC; edición manual explícita disponible. No cambia modelos de regalos en el mapa |
| Intercambios | Pokémon recibido y campos disponibles; especies aleatorias | Conserva requisitos, entrenador, memoria, identidad y resto de campos |
| Entrenadores · equipos | Especie, forma, nivel, objeto, cuatro movimientos, naturaleza, seis EV y seis IV; randomización de especies | Conserva tamaño/equipo, niveles y demás parámetros al randomizar. IV conserva bits adyacentes, shiny y Dynamax. Naturaleza 0–24 o 255. El registro inactivo 000 se conserva aunque contenga un bloque sin usar |

Los editores anteriores de Pokémon, randomización de entrenadores/encuentros/Field Items y todas las opciones del runtime se conservan. La edición manual de niveles de jefes invalida la exportación con caps automáticos antiguos hasta recalcularlos.

## Revisión de pkNX y procedencia

Se revisó [pkNX](https://github.com/kwsch/pkNX) en el commit fijado [`d191cd0e5c05f2af81d9a41c1f1d82e6621b351a`](https://github.com/kwsch/pkNX/tree/d191cd0e5c05f2af81d9a41c1f1d82e6621b351a). Referencias principales: `GameManagerSWSH.cs`, `GameFileMapping.cs`, los esquemas SWSH Waza/Shop/Gift/Static/Trade, `Learnset8.cs`, `EvolutionSet8.cs`, `PersonalInfo8SWSH.cs`, `Item8.cs` y `TrainerPoke8.cs`.

Se adaptaron sus formatos y restricciones al proveedor existente. No es necesario instalar pkNX, importar paquetes ni añadir sus DLL/NuGet a la aplicación. Se añadió su licencia GPL-3.0 en `docs/third-party/pkNX-LICENSE.txt`. El runtime PokeFeur integrado conserva su procedencia y licencia separadas; no se afirma disponer de su código C++ completo.

## Archivos de esta actualización

Nuevos:

- `pk3DS.Core/NX/SwitchSeed.cs`, `SwordShieldDataEditor.cs`.
- `3DS.Progressive.App/Services/Switch/SwordShieldProject.Data.cs`, `SwordShieldProject.SdStatus.cs`.
- `3DS.Progressive.App/SwitchWorkspaceView.cs`, `SwitchDataEditorView.cs`.
- `3DS.Progressive.Tests/SwitchExpansionChecks.cs`.
- Este informe y `docs/third-party/pkNX-LICENSE.txt`.

Ajustados:

- `pk3DS.Core/NX/PokeFeurOptions.cs`.
- `MainWindow.Switch.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `MainWindow.Guidance.cs`, `StartView.cs`.
- `SwordShieldPrototypeWindow.cs`, sus parciales `Randomization` y `Gameplay`, `SwordShieldGameplayWindow.cs`, `SwordPokeFeurWindow.cs`.
- `Services/Switch/SwordShieldProject.cs`, `SwordShieldProject.Gameplay.cs`.
- `3DS.Progressive.Tests/Program.cs`, `SwordShieldChecks.cs`.
- Guía de integración del runtime y hoja de ruta, con enlace a esta actualización.

No se cambió la solución original, los motores Gen6/Gen7, templates del usuario ni la lógica de Batch 3DS. El workflow ya tenía `--swsh-only` y `--swsh-ui-only`; las nuevas comprobaciones se enlazan a esos switches. Se preservó el resto de cambios pendientes del repositorio.

## Validación ejecutada

- Compilación Release no incremental de App/Tests y referencias Core/WinForms: 0 errores. La emisión de advertencias varía por las pasadas WPF; comparación con el registro anterior: las mismas 67 advertencias únicas, ninguna nueva.
- `dotnet build pk3DS.slnx -c Release --no-restore`: 0 errores; 56 advertencias preexistentes.
- `--swsh-only`: 312 aprobadas.
- `--swsh-ui-only`: 53 aprobadas, fuera de pantalla.
- `--swsh-expansion-probe <extracción Espada> <salida temporal>`: 1458 aprobadas en la ejecución final, incluidos SD/caps.
- `--pokefeur-probe <extracción> --builtin <salida temporal>`: 42 aprobadas.
- `--synthetic-only`: 4055 aprobadas con XY, ORAS, SM y USUM sintéticos.
- `--visual-redesign-only`: 95 aprobadas.
- `--guidance-only`: 233 aprobadas, fuera de pantalla.
- Comparación independiente de binarios mediante Python: se comprobaron los campos Waza no seleccionados, sentinelas/niveles de aprendizajes, condiciones/niveles de evolución, bits reservados de personal, metadatos de objetos, hashes/progresión de tiendas, escenarios protegidos y campos no aleatorios de entrenadores.
- `git diff --check` y revisión de whitespace de fuentes nuevas: aprobados.

La prueba real exporta solo a temporales, reabre los recursos, contrasta sus hashes y comprueba que los originales permanecen intactos. Su número incluye verificaciones por recurso: no representa 1458 escenarios jugables distintos. Los binarios de ese mod de validación NO se instalaron en Ryujinx.

| Requirement | Evidence |
|---|---|
| Semilla aleatoria | `Switch_SeedsGeneratedRandomlyWithinTomlRange`, `Switch_UISeedIsGeneratedAndReadOnly` |
| Detectar y corregir configuración de tres especies | `Switch_Real_SdMismatchDetectsThreeSpeciesDiagnosticConfig`, `Switch_Real_SdInstallReplacesDiagnosticPoolWithBackup` |
| Navegación en ventana principal y vuelta a Inicio | `Switch_UIMainWindowHostsSwitchAndRoutesExport`, `Switch_UIHomeRestoresOriginal3dsHeaderState` |
| Edición con vista previa e historial | `Switch_UIManualPreviewDoesNotApply`, `Switch_UIApplyAddsDataHistory`, `Switch_Real_ManifestContainsTenDataEditors` |
| Mantener campos de datos ajenos | `Switch_MoveRebuildPreservesVersionIdAndAvailability`, `Switch_CompatibilityPreservesReservedBits`, `Switch_TrainerIvPatchPreservesAdjacentIvShinyAndDynamaxBits`, `Switch_ItemEditRejectsUnrequestedSharedRecordMutation` |
| Protección de datos/guiones originales | `Switch_TrainerZeroPartyPreservesUnusedStoredMember`, `Switch_RandomGiftsPreserveSpeciesDependentDlcGift`, `Switch_Real_AllSourcesUnchanged` |
| Compatibilidad caps/edición de jefes | `Switch_Real_ManualBossLevelInvalidatesAutomaticCapsExport`, `Switch_Real_UpdatingCapsUsesEditedTrainerLevels` |

Durante el desarrollo se corrigieron errores nuevos de compilación y de presentación detectados por las pruebas. La prueba real detectó precisión 101 y el equipo inactivo 000; no eran defectos de la ROM original. También hubo una compilación bloqueada por un runner que todavía usaba sus DLL; se repitió después de terminarlo. No se ocultó ningún fallo ni se modificaron datos para hacer pasar las pruebas.

## Alcance pendiente y riesgos residuales

No se ejecutó jugabilidad de las diez áreas nuevas ni de las opciones adicionales del runtime. El usuario había validado antes combate sorpresa/captura/menús con el pool reducido. Falta comprobar el pool completo reparado, inicio de partida, combates, equipos manuales, compras, regalos/intercambios y progresión con DLC. La conservación de bytes o propiedades no certifica que cada combinación sea adecuada dentro del juego.

La UI se comprobó mediante componentes y dispatcher fuera de pantalla: falta revisión interactiva de teclado, foco, diálogos y escalados reales 100/125/150/200 %. La raíz de la extracción sigue siendo responsabilidad del usuario: el Build ID no demuestra por sí solo que todos sus archivos RomFS pertenezcan a la misma actualización o DLC.

Escudo no tiene validación real en esta entrega. Su proveedor de formatos anterior se conserva; el runtime nativo sigue limitado a Espada/Ryujinx. Atmosphère no se habilita para PokeFeur sin una variante probada.

No hay todavía paridad total con 3DS: faltan Global Template/Batch propios de Switch, escritura de textos, edición de movimientos huevo, mapeo de contenidos MT/TR, incursiones y parches específicos como compras únicas. No se importan directamente recetas 3DS porque sus recursos, acciones y contratos no son equivalentes. Estos módulos necesitan adaptadores y pruebas propios; no se presentan como implementados por compartir un nombre de sección.
