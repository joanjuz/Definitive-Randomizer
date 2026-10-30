# Inicio, modos y randomizado guiado

Implementación del 19 de octubre de 2026. Sin commit, publicación ni cambios en la distribución instalada.

## Comportamiento

- Inicio con Modo simplificado, Modo completo y un botón inferior de Randomizado guiado. Los modos cambian la navegación sin eliminar editores, valores pendientes ni capacidades. El modo se conserva durante la sesión.
- Vista guiada voluntaria desde Inicio o F1: superposición y contorno sobre controles reales, explicación de secciones y subsecciones, Anterior/Siguiente y Saltar todo. No se abre automáticamente.
- El asistente selecciona primero las secciones. Carga sus opciones al visitarlas y conserva las elecciones al retroceder. No aplica nada durante la configuración ni al cancelar.
- Resumen final con opciones, carpeta, nombre y exportación JSON reutilizable en Global Template/Batch. Generar ROM exige confirmación final y utiliza el Batch existente sobre una copia aislada; conserva el proyecto editable, receta y manifiesto.
- 12 módulos en XY/SM, 13 en ORAS/USUM (tutores). Pokémon, movimientos, entrenadores, encuentros, estáticos e iniciales, aprendizajes, crianza, evoluciones, máquinas, Field Items, tiendas y Pickup. Movimientos usa los tipos y categorías admitidos por el motor actual. Los ajustes especializados siguen disponibles en modo completo.
- Ventanas con tema compartido, límites de pantalla, contenido desplazable, pie de navegación fijo, etiquetas accesibles, rangos visibles y bloqueo de cierre durante la construcción.

## Archivos de esta implementación

Nuevos en 3DS.Progressive.App: StartView.cs, GuidanceCatalog.cs, SpotlightTour.cs, MainWindow.Guidance.cs, GuidedRandomizationWindow.cs y Services/GuidedRandomizationPlan.cs.
Integración: MainWindow.xaml y MainWindow.xaml.cs. El nombre y logo llevan a Inicio; sus cuatro accesos no se repiten en la barra lateral.
Pruebas: GuidanceFollowupChecks.cs, GuidanceChecks.cs y registro en Program.cs de 3DS.Progressive.Tests.
Los cambios previos de historial y estabilidad se conservan; el prototipo de compras únicas permanece pendiente.

## Evidencia

| Requirement | Evidence |
|---|---|
| Aplicación solo al final | Guidance_ConfigurationAndPreflightNeverWriteSource_XY/ORAS/SM/USUM; Guidance_WizardSummaryDoesNotApply_XY/ORAS/SM/USUM |
| Opciones y navegación conservadas | Guidance_ModeSwitchPreservesControlsDraftAndHistory_XY/ORAS/SM/USUM; Guidance_WizardBackRetainsChoices_XY/ORAS/SM/USUM |
| Solo acciones elegidas y replay nativo | Guidance_OnlyChosenActionsAreExported_XY/ORAS/SM/USUM; Guidance_ChosenPersonalRecipeMatchesNativeReplay_XY/ORAS/SM/USUM; Guidance_ChosenMoveReplayPreservesUnselectedArchives_XY/ORAS/SM/USUM |
| Ayuda breve y compatibilidad | Guidance_CatalogHasUniqueShortCompleteTopics; Guidance_TourFiltersUnsupportedTopics_XY/ORAS/SM/USUM |
| Tema y tamaños | Guidance_DialogInheritsReadableTheme_XY/ORAS/SM/USUM; Guidance_DialogTracksHighContrastPalette; Guidance_WizardActionsStayVisible_1/1.25/1.5/2 |

Release del proyecto Tests y sus referencias: 0 errores, 78 advertencias del código heredado. Runner --guidance-only: 144 comprobaciones; --visual-redesign-only: 95; --interaction-lock-only: 19; --rebuild-marts-ui-only: 69. Todas correctas. La batería --synthetic-only finalizó con 3.750 comprobaciones correctas para XY, ORAS, SM y USUM. git diff --check sin incidencias.

## Límites de la validación

Las pruebas WPF y capturas son fuera de pantalla. Los factores 100 %, 125 %, 150 % y 200 % se simulan en el render: no equivalen a cambiar el escalado real de Windows. Pendiente comprobar teclado, foco modal, lector de pantalla, monitores y generación completa con una ROM real. Los tests prueban configuración, preflight, replay nativo y aislamiento con datos sintéticos; no demuestran una partida real ni generación interactiva de ROM.


Los ajustes posteriores del asistente y del recorrido visual están documentados en AJUSTES_ASISTENTE_VISTA_GUIADA.md. Las cifras anteriores corresponden a la primera implementación.
