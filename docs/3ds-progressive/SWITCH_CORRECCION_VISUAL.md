# Corrección visual del espacio de trabajo Switch

Fecha: 2026-10-22. Rama: feature/switch-swsh-prototype.

## Problema y cambios

Inicio permanecía visible detrás del espacio Switch. Al reutilizar contenidos de ventanas como páginas, sus fondos y etiquetas también perdían parte del tema heredado. Se ocultan explícitamente los hosts de Inicio y 3DS, y se pinta una superficie opaca con recursos dinámicos del tema.

- Cabecera: muestra el proyecto y su estado real; conserva Abrir proyecto y Exportar mod. Oculta las acciones exclusivas de 3DS mientras está en Switch y restaura sus estados al regresar. Exportar se actualiza al cambiar la sesión.
- Navegación: grupos de edición/sistemas, ancho de 232 píxeles y etiquetas multilínea. Conserva los iconos existentes y los destinos.
- Pokémon: lista compacta con nombre y detalles en dos líneas, editor manual antes de las opciones de randomización, historial y opciones plegables, desplazamiento independiente y acciones inferiores accesibles. Identificadores técnicos completos permanecen disponibles en el tooltip.
- Contenidos embebidos: elimina botones duplicados de apertura/exportación y aplica colores de etiquetas coherentes. Las ventanas independientes conservan sus acciones.
- No cambia motores, parámetros de randomización, exportación, datos de origen ni configuración del emulador. Los eventos nuevos solo notifican el estado visual de la cabecera.

## Archivos de esta corrección

- 3DS.Progressive.App/App.xaml: plantilla de navegación específica de Switch.
- 3DS.Progressive.App/MainWindow.Switch.cs: exclusión de hosts y estado de cabecera.
- 3DS.Progressive.App/SwitchWorkspaceView.cs: fondo, etiquetas, navegación y notificaciones.
- 3DS.Progressive.App/SwordShieldPrototypeWindow.cs: distribución, listado y acciones embebidas.
- 3DS.Progressive.App/SwordShieldPrototypeWindow.Randomization.cs: opciones plegadas inicialmente.
- 3DS.Progressive.App/SwordShieldGameplayWindow.cs y SwordPokeFeurWindow.cs: notificación de cambios de sesión.
- 3DS.Progressive.Tests/SwitchVisualChecks.cs y Program.cs: regresión visual y renderizado reproducible.

Se conservan las modificaciones pendientes anteriores; esta lista no representa todo el trabajo pendiente de la rama.

## Validación ejecutada

- Compilación Release de Tests y sus referencias App/Core: cero errores. Persisten advertencias previas de nulabilidad; el primer build registró 88 emisiones, incluidas repeticiones de los proyectos temporales WPF.
- --swsh-ui-only: 57 comprobaciones aprobadas, incluyendo Switch_Visual_HomeAnd3dsShellAreNotPaintedBehindWorkspace, Switch_Visual_WorkspaceHasOpaqueThemeSurface, Switch_Visual_HeaderContainsOnlyRelevantActions y Switch_Visual_HomeRestoresHeaderAndExclusiveSurface.
- --visual-redesign-only: 95 comprobaciones aprobadas, sin errores de binding detectados por ese runner.
- --guidance-only: 233 comprobaciones aprobadas.
- --swsh-only: 312 comprobaciones sintéticas aprobadas.
- --swsh-visual-preview: ocho imágenes renderizadas y examinadas: Pokémon a 900x620, 1280x800 y 1600x900; Movimientos, Field Items, Runtime, Caps y Tools a 1280x800. Datos de la extracción Espada leídos sin modificarlos.

Evidencia local: C:/Users/Joanj/AppData/Local/Temp/DR-Switch-visual-20261022/. Logs, binlogs y renders-v1 están fuera del repositorio.

## Límites y comprobaciones manuales pendientes

Estas son comprobaciones de componentes y renderizados fuera de pantalla, no una sesión interactiva ni una prueba jugable. Falta revisar teclado, foco, alto contraste y escalados 125/150/200 % en Windows; también los flujos de diálogos nativos y exportación durante una sesión real. No se declara validación nueva de jugabilidad. Sin commits ni push.

Compilación final con --no-incremental: 0 errores y 78 emisiones de advertencias; --swsh-ui-only repetido después: 57 aprobadas.
