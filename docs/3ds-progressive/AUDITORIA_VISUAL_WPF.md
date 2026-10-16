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
