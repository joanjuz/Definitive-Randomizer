# Implementación de la auditoría visual WPF

## Alcance y conservación

Se aplicaron las diez mejoras de la auditoría con redesign-existing-projects, adaptadas a WPF/XAML. Se conservó la terminología existente. No se añadieron paquetes ni tecnologías web.

No se modificaron los motores de randomización, servicios de edición de ROM, formatos de templates, historial, Batch ni parches de combate. Los cambios C# afectan a presentación, accesibilidad, distribución y pruebas. Las operaciones y los handlers originales se conservan.

Se preservaron los cambios locales ajenos: .gitignore, ErisTemplate/, artifacts/, custom_balance_templates/_state/, TEXTOS_AUDITORIA_20261015.txt y personal-release-notes.md. No se hicieron commits ni push.

## Mejoras implementadas

| Área | Implementación |
|---|---|
| Diseño adaptable | Comportamiento compartido AdaptiveLayout. La ventana principal conserva cuatro columnas en ancho amplio y libera la del inspector en ancho reducido. El inspector pasa a una fila desplegable debajo del editor. La misma solución se usa en movimientos y en el editor genérico. No se recrean controles ni se cambian sus nombres. |
| Acciones principales | Los controles originales Guardar y Restablecer/Descartar se trasladan a zonas fijas fuera del desplazamiento y del inspector. Se conservan Click, estados IsEnabled y la visibilidad asociada al editor. Los botones se redistribuyen con WrapPanel. |
| Diálogos | StudioDialog dispone de desplazamiento para mensajes largos, acciones separadas y dimensiones limitadas al área útil de pantalla. Conserva confirmación, cancelación y eventos. La ventana de comandos y la descripción también se adaptan al espacio. |
| Accesibilidad | Asociaciones LabeledBy en campos estáticos y generados; nombres accesibles para búsquedas, campos de tabla y comandos; rangos numéricos como ayuda accesible. Orden explícito de acciones de cabecera. Cabeceras desplegables enfocables y con estado de expansión vinculado. |
| Tablas | Cabeceras con altura automática y texto multilínea. Controles compactos en filas de 44 DIP. Anchos más contenidos en templates y catálogo flexible. Columna identificadora congelada en templates y reglas de entrenadores. Se conservan todas las columnas originales y la virtualización. Las tablas con muchos campos técnicos siguen permitiendo desplazamiento horizontal cuando es necesario. |
| Navegación | Iconos vectoriales diferenciados, separación visual antes de templates, indicador lateral de destino activo, tipografía de selección y contorno de foco. Los destinos deshabilitados mantienen una apariencia discreta. Se conservan nombres y destinos. |
| Cabecera | El proyecto mantiene su espacio y las acciones pasan a una segunda fila cuando falta ancho. Abrir proyecto precede a las acciones secundarias en el orden de teclado. |
| Botones | ActionRole explícito: Primary, Secondary y Tertiary. Se elimina la deducción de estilos mediante palabras de la etiqueta. Cambiar un texto no cambia el estilo asignado. |
| Tema | Recursos de color centralizados, diccionarios y roles compartidos para paneles, leyendas y cifras. Se eliminan estilos duplicados de tarjetas y tabla de reglas. Los recursos locales restantes usan claves semánticas. Se conservan Segoe UI y el idioma actual. |
| Formularios y contraste | Campos de movimientos agrupados por efectos, golpes y objetivo/flags. Los formularios de dos columnas pasan a una cuando falta espacio. Se muestran rangos numéricos en campos generados. Bordes de entradas reforzados, foco y selección visibles. Paleta vinculada al alto contraste de Windows y actualizable en vistas XAML y construidas en C#, sin sobrescribir bindings ni triggers de deshabilitado. |

## Archivos afectados

En 3DS.Progressive.App/:

- App.xaml y App.xaml.cs: estilos, recursos, inicialización de accesibilidad y paleta.
- AdaptiveLayout.cs: nuevo comportamiento adaptable y asociaciones de accesibilidad.
- AccessiblePalette.cs: nueva integración con alto contraste y recursos dinámicos.
- EditorPresentation.cs: roles explícitos, iconografía y presentación compartida.
- Themes/EditorTokens.xaml: paleta y métricas compartidas.
- MainWindow.xaml: cabecera, distribución, navegación y acciones fijas.
- MoveEditorView.xaml: distribución, grupos, etiquetas y acciones fijas.
- RecordEditorView.xaml y RecordEditorView.xaml.cs: inspector adaptable, acciones fijas, etiquetas y rangos.
- StudioDialog.xaml y StudioDialog.xaml.cs: desplazamiento, dimensiones y recursos.
- TemplateDescriptionWindow.cs: diálogo adaptable, preview escalable y roles de botones.
- TextCommandsWindow.cs: ventana y paneles adaptables, accesibilidad y botones.
- DarkGridTheme.cs: estilo compartido de tablas, cabeceras y foco.
- TrainerRulesWindow.cs: tabla compartida, identificador congelado y acciones.
- TemplateStudioView.cs: columnas, identificación visible y roles de acciones.
- GlobalTemplateView.cs, BatchRomBuilderView.cs y GameTextView.cs: presentación compartida y estilos explícitos.
- BatchToolsView.xaml: recursos de tema compartidos.

En 3DS.Progressive.Tests/:

- VisualRedesignChecks.cs: verificaciones y capturas fuera de pantalla.
- Program.cs: acceso a --visual-redesign-only.

Este informe completa AUDITORIA_VISUAL_WPF.md. La lista incluye la implementación visual pendiente anterior y su ampliación actual; no atribuye los cambios ajenos de .gitignore a este trabajo.

## Validaciones ejecutadas

Compilación de 3DS.Progressive.Tests y sus referencias, incluida la aplicación WPF:

    dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Debug -p:UseAppHost=false --nologo -v:q

Resultado: 0 errores. La última compilación completa registró 90 advertencias en código existente, principalmente nullable y código heredado. No se presenta como una compilación sin advertencias.

| Prueba | Resultado |
|---|---:|
| --visual-redesign-only | 59 comprobaciones aprobadas |
| --template-studio-ui-only | 20 aprobadas |
| --template-latest-ui-only | 152 aprobadas |
| --template-feedback-ui-only | 63 aprobadas |
| --text-classification-ui-only | 8 aprobadas |
| --text-commands-ui-only | 1 aprobada |
| --general-manual-only | 113 aprobadas |
| --manual-edits-only | 100 aprobadas |
| --template-studio-only | 140 aprobadas |
| --master-batch-only | 45 aprobadas |
| --description-preview-fonts, con X/OR/Sol/UltraSol | 173 aprobadas |

La batería visual comprueba cabecera, inspector reducido/amplio y expansión, acciones fijas, asociación de etiquetas, controles generados, estado deshabilitado, toggle enfocable, orden de acciones, tablas originales, bloqueo durante Batch/rebuild y conservación del borrador de descripción. Se registraron los errores de binding de las vistas renderizadas: ninguno.

Se renderizó fuera de pantalla en 1280×720, 1366×768 y 1920×1080, con combinaciones al 100 %, 125 %, 150 % y 200 %. El editor genérico se renderizó en los cuatro escalados. Se comprobaron diálogo largo y alto contraste simulado mediante la paleta del sistema, sin cambiar la configuración de Windows.

Capturas reproducibles en:

    3DS.Progressive.Tests/bin/Debug/net10.0-windows/visual-redesign/

También pasó la comparación de controles nombrados contra HEAD: no faltan nombres originales ni cambiaron sus eventos, textos, Content, Header, Tag, rutas de selección o bindings comprobados. git diff --check no informa errores de espacios; solamente aparece el aviso de finales de línea de .gitignore, que era ajeno a esta implementación.

## Verificaciones manuales pendientes

No se controló el escritorio ni se abrieron ventanas interactivas. El renderizado fuera de pantalla y las comprobaciones de propiedades no equivalen a recorrer una aplicación abierta.

Quedan por validar manualmente:

- Recorrido completo con Tab/Shift+Tab, flechas, Enter y Espacio, incluida edición de celdas y retorno de foco al cerrar diálogos.
- Lectura con Narrador u otro lector de pantalla.
- Cambio real del alto contraste de Windows con ventanas abiertas.
- Cambio de DPI entre monitores físicos y tamaños de área útil reales.
- Apariencia de formularios con nombres y textos excepcionalmente largos en proyectos reales.

No se verificó el comportamiento de juego mediante emulación: los motores y assets del juego no cambiaron en este trabajo. No se modificaron formularios WinForms heredados.

## Distribución

No se actualizó el EXE de release ni la distribución instalada. El SHA256 de D:\Progra\Randomizer\DefinitiveRandomizer\3DS.Progressive.dll sigue siendo:

    644DD5B2A4B374D8BCED12FBBD06D2400F7B9F794F39EF1ECF1180BA48572D3B

## Seguimiento: correcciones de alto contraste, ciclo de vida y CI (16-10-2026)

Esta sección complementa la validación histórica de 05403f6; no sustituye sus resultados ni presenta aquella revisión como nuevamente ejecutada completa.

### Cambios acotados

- **DarkGridTheme.cs**: recursos dinámicos en superficies, alternado de filas, líneas, cabeceras, celdas, selección y foco. Los estados deshabilitados resuelven la paleta vigente. Se conserva un RowStyle previo mediante BasedOn y no se sustituyen bindings existentes de propiedades de color. Se mantienen dimensiones, virtualización, columnas, datos y eventos.
- **AccessiblePalette.cs**: copia independiente de los colores iniciales para restauración del tema oscuro, verificación del Dispatcher y actualización mediante reemplazo de recursos, compatible con pinceles congelados de WPF.
- **VisualRedesignChecks.cs**: host HwndSource sin WS_VISIBLE, comprobado mediante IsWindowVisible; se ejercita Loaded/Unloaded real sin mostrar ni activar ventanas. Las instancias de ciclo nativo se separan de las capturas para no contaminar el layout de DPI. Se conserva la matriz de renderizado 100/125/150/200 %.
- **.github/workflows/3ds-progressive-ci.yml**: compilación explícita del runner Release con apphosts habilitados para conservar el backend LegacyBatch. Nueve suites focalizadas se ejecutan con la DLL del runner propio, propagan errores de proceso y guardan registros. Se adjuntan capturas y binlogs. Se conserva la regresión sintética general y el empaquetado previo WPF/WinForms, sin modificar pk3DS.slnx.
- **Este informe**: resultados y límites actualizados. No se modifican motores, servicios de ROM, templates, historial, Batch ni distribuciones instaladas. Los cambios locales ajenos, incluido AGENTS.md aparecido durante el trabajo, se preservan.

### Evidencia por requisito

| Requisito | Caso ejecutado y resultado |
|---|---|
| Tabla ya cargada cambia a alto contraste | GridTheme_HighContrast_ExistingHeader, GridTheme_HighContrast_ExistingRows y GridTheme_HighContrast_ExistingCellsAndSelection: aprobados. |
| Restauración, selección y deshabilitado | GridTheme_RestoreDark_ExistingControls, GridTheme_HighContrast_SelectionModelRetained, GridTheme_HighContrast_Disabled, GridTheme_FullRow_NormalSelection y GridTheme_FullRow_HighContrastSelection: aprobados. Las comprobaciones de selección por fila verifican las celdas visibles, respetando la precedencia de RowBackground de WPF. |
| Bindings y datos | GridTheme_ResourceSwitch_PreservesDataBindingAndSelection y GridTheme_Apply_PreservesExistingForegroundBinding: aprobados. |
| Registro y ciclo de vida de layout | Lifecycle_Main_LoadedThroughInvisiblePresentationSource, Lifecycle_Loaded_ConfiguresInspectorAndHeader, Lifecycle_Main_DetachmentRaisesUnloaded y Lifecycle_Reload_DoesNotDuplicateLayoutRows: aprobados. También se comprueba Loaded en movimientos y editor genérico. |
| Navegación original | Navigation_TemplateStudioButton_RoutesThroughOriginalHandler y equivalentes de Game Text, Story Text, Global Template y Evoluciones, más Navigation_Moves_OriginalHandler y Navigation_Pokemon_ReturnsToEditor: aprobados. Los destinos se cargan en el host invisible. |
| Navegar no escribe datos | Navigation_ReadOnlyRoutes_PreservePersonalArchive: aprobado sobre fixture sintético. |
| Foco técnicamente comprobable sin escritorio | Focus_LogicalScope_RecordsOpener, Focus_Dialog_HasSeparateLogicalScope y Focus_LogicalScope_OpenerRetainedAfterDialogClosed: aprobados. Se comprueba foco lógico y el evento Closed; no recuperación del foco físico del teclado ni un ShowDialog real. |
| CI explícito | Compilación local Release y las nueve suites incorporadas al workflow: aprobadas. Sintaxis PowerShell de sus ocho bloques run comprobada mediante Parser.ParseInput. La ejecución remota de GitHub Actions sigue pendiente. |

### Regresión reproducida y corregida

Se realizó un contrafactual acotado reponiendo temporalmente solo DarkGridTheme.cs de 05403f6, conservando la prueba nueva y el resto de la implementación. Compiló y el runner falló exactamente en **GridTheme_HighContrast_ExistingHeader**. La corrección propia se restauró byte por byte mediante finally y se recompiló. La versión corregida pasa los casos de tabla y la suite visual completa.

Durante el desarrollo se corrigieron problemas del montaje de pruebas: jerarquía de recursos sin Window, referencias a contenedores de celda reciclados y tamaños heredados del host nativo. No se eliminaron comprobaciones de datos/selección; se consulta el contenedor realizado vigente y se comprueba además la selección del modelo.

### Ejecuciones reales

Compilaciones locales sin publicar:

- pk3DS.slnx, Debug, UseAppHost=true: **0 errores, 57 advertencias**.
- 3DS.Progressive.Tests.csproj y referencias WPF/WinForms, Debug, UseAppHost=true: **0 errores, 90 advertencias**.
- El mismo proyecto, Release, UseAppHost=true: **0 errores, 90 advertencias**.

Cada compilación generó binlog. Las 68 ubicaciones distintas de advertencia de la aplicación y sus dependencias coinciden con líneas existentes en 05403f6; no se encontraron advertencias nuevas en las correcciones. Se mantienen los avisos previos de nullable, código heredado y CA2022.

Suites del runner propio ejecutadas sobre Release:

| Suite | Comprobaciones aprobadas |
|---|---:|
| --visual-redesign-only | 95 |
| --interaction-lock-only | 5 |
| --requested-ui-only | 38 |
| --template-studio-ui-only | 20 |
| --template-latest-ui-only | 152 |
| --template-feedback-ui-only | 63 |
| --text-classification-ui-only | 8 |
| --text-commands-ui-only | 1 |
| --global-options-only | 132 |
| --template-studio-only | 140 |
| --general-manual-only | 113 |
| --manual-edits-only | 100 |
| --master-batch-only | 45 |
| --editor-modules-only | 408 |
| **Total** | **1.320 en 14 suites** |

La suite visual también pasó en Debug. No se registraron errores de binding en las vistas renderizadas. Los registros locales de este seguimiento están en C:\Users\Joanj\AppData\Local\Temp\DR-WpfFix-20261016.

### Límites y pasos manuales

No se mostraron ventanas interactivas ni se controló el escritorio. Se crearon handles invisibles exclusivamente para comprobar el ciclo WPF; no se usó automatización del ratón ni del teclado y no se cambiaron los ajustes de alto contraste de Windows.

Siguen pendientes: Tab/Shift+Tab y recuperación real del foco tras un diálogo modal, Narrador, cambio efectivo del alto contraste del sistema y traslado entre monitores con DPI distintos. El cambio de paleta se simula dentro de la aplicación. No se ejecutó el workflow en GitHub ni se publicaron sus artefactos; los pasos de publicación existentes se conservaron pero no se probaron mediante una publicación local.

No se ejecutó --all-ui-only, porque esa suite muestra ventanas. Tampoco se usaron ROMs originales, emuladores o assets reales para validar jugabilidad. No se hicieron commits ni push y el hash de la distribución instalada sigue coincidiendo con el consignado arriba.
