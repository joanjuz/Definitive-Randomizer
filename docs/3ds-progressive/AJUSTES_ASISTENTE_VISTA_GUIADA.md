# Ajustes del asistente y vista guiada

Fecha: 20 de octubre de 2026. Código fuente actualizado; sin commit, push, publicación ni cambios en la distribución instalada. Se conservan los cambios pendientes de historial y estabilidad y el prototipo de compras únicas.

## Cambios

- Rangos BST: al activar BST progresivo en Entrenadores o Encuentros, Siguiente abre un paso separado con ProgressiveBstTable, compartida con los editores actuales. Se pueden editar, dividir y fusionar rangos. Anterior conserva las modificaciones; desactivar BST omite el paso y no borra sus valores. Los intervalos inválidos mantienen al usuario en la tabla. El JSON usa las claves BST e interfaces originales, sin motores nuevos.
- Tiendas: alcance inicial Solo tiendas especiales activado y Proteger objetos X desactivado en Gen6/Gen7. Las acciones de inventario siguen siendo acumulables.
- Secciones sin acciones: se omiten al construir la receta y aparecen como Sin cambios en el resumen. No bloquean otras secciones. Si todas están vacías, el resumen ofrece Cerrar sin cambios y no genera una ROM. Seleccionar una operación como Pickup o MT tiene un control explícito para desactivarla.
- Inicio: sus accesos no se repiten en el lateral. Un botón accesible engloba el logo y el nombre y lleva a Inicio por ratón o teclado; se mantienen los handlers e identificadores anteriores.
- Vista guiada: se reemplaza el diálogo de lectura por SpotlightTour, superpuesto a la interfaz. Oscurece el resto y dibuja un contorno de tema alrededor del control real. El cuadro incluye Anterior, Siguiente/Terminar y Saltar todo; Escape termina el recorrido. Se explican el inicio, las secciones disponibles y los grupos, pestañas y acciones reales del editor. Ver esta sección usa la navegación original y abre su recorrido cuando ha cargado. La guía no pulsa Guardar/Aplicar ni ejecuta randomización. También se usa sobre las opciones del asistente. El cuadro se adapta al tamaño de la ventana y el contorno sigue el layout.

## Archivos de esta revisión

- 3DS.Progressive.App/MainWindow.xaml: marca clicable, accesos duplicados ocultos y host de superposición.
- 3DS.Progressive.App/MainWindow.Guidance.cs: recorrido contextual y exploración mediante navegación existente.
- 3DS.Progressive.App/SpotlightTour.cs: componente compartido del recorrido visual.
- 3DS.Progressive.App/GuidedRandomizationWindow.cs: pasos BST, persistencia, resumen de omitidas y ayuda superpuesta.
- 3DS.Progressive.App/Services/GuidedRandomizationPlan.cs: alcance de tiendas y filtrado de acciones vacías.
- 3DS.Progressive.Tests/GuidanceChecks.cs y GuidanceFollowupChecks.cs: regresiones del modelo y controles reales.
- docs/3ds-progressive/INICIO_MODOS_ASISTENTE.md y este informe: documentación.

Se retiró SectionTourWindow.cs, creado en la implementación anterior aún no publicada, porque su ayuda modal quedó sustituida. No se modificaron los motores, servicios binarios, ROMs, distribuciones instaladas ni templates personales.

## Matriz de evidencia

Los sufijos de familia se ejecutan para XY, ORAS, SM y USUM. Las comprobaciones WPF son fuera de pantalla, no interacción humana real.

| Requirement | Evidence |
|---|---|
| Paso BST condicional | Guidance_BstEnabledOpensNextTableStep_USUM_Trainers y Guidance_BstEnabledOpensNextTableStep_USUM_Wild; mismas comprobaciones en XY/ORAS/SM |
| No mostrar campos sueltos, conservar rangos y validar | Guidance_BstFieldsExcludedFromOptions_USUM_Trainers; Guidance_BstDisabledSkipsTable_USUM_Trainers; Guidance_BstBackRetainsEditedRanges_USUM_Trainers; Guidance_BstAddedRangeIsCapturedInRecipe_USUM_Trainers; Guidance_InvalidBstRangeStaysOnTable_USUM_Trainers; también Wild y demás familias |
| Alcance de tiendas solicitado | Guidance_MartScopeDefaultsSpecialWithoutXProtection_XY/ORAS/SM/USUM |
| Omitir secciones sin bloquear otras | Guidance_EmptyMovesAndMartsDoNotBlockOtherSections_XY/ORAS/SM/USUM; Guidance_AllEmptySummaryDoesNotGenerate_XY/ORAS/SM/USUM; Guidance_NoCheckedOptionsSkipsSection_USUM_FieldItems y restantes módulos/familias |
| Inicio y marca | Guidance_DuplicateSidebarEntriesAreHidden; Guidance_BrandLogoAndNameReturnHome |
| Controles reales, subsecciones y salida | Guidance_TourHighlightsActualControlBounds; Guidance_TourExplainsRealHomeView; Guidance_TourCoversRealSectionsAndSubsections; Guidance_SkipTourRemovesOverlayWithoutApplying; Guidance_TourCardRemainsInsideSmallWindow |

## Resultados

- Compilación Release del proyecto Tests y sus referencias: 0 errores. La compilación que regeneró WPF notificó 88 advertencias del código existente; ninguna localizada en los componentes nuevos de esta revisión.
- --guidance-only: 233 comprobaciones correctas.
- --visual-redesign-only: 95 correctas.
- --interaction-lock-only: 19 correctas.
- --rebuild-marts-ui-only: 69 correctas.
- --synthetic-only: 3.750 comprobaciones correctas con datos sintéticos de XY, ORAS, SM y USUM.
- git diff --check: sin incidencias.
- Capturas revisadas: contorno sobre la marca real y tabla BST de Entrenadores.

## Pendiente manual

Recorrer la guía con ratón/teclado y lector de pantalla, verificar el foco al salir en una ventana real, navegar con proyectos reales y distintas resoluciones/escalados físicos de Windows. La adaptación 100/125/150/200 se prueba mediante renderizado fuera de pantalla; no sustituye un monitor real. La generación completa con una ROM real y su ejecución en emulador quedan pendientes. La batería sintética no demuestra jugabilidad.

Skills aplicadas: pk3ds-progressive-safety, wpf, redesign-existing-projects, dotnet-test/code-testing, dotnet-test/run-tests y dotnet-msbuild/binlog-generation. No está disponible un agente especializado test-engineer en esta sesión; el trabajo de pruebas se realizó directamente.
