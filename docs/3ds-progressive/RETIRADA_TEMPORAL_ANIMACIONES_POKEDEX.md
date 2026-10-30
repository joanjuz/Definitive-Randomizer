# Retirada temporal de Animaciones de Pokédex

23/10/2026. Se preserva la rama y los cambios locales anteriores.

La comparación jugable F/G comunicada por el usuario aisló `dex_animations.active`: F reproduce la lentitud extrema en Área Silvestre; G funciona con los mismos archivos, AMX, registro de scripts y binario del runtime, cambiando solo el indicador a false. Esto confirma el desencadenante en la configuración probada; no constituye una reparación del defecto nativo exacto.

## Implementación

- La opción ya no aparece en Herramientas. Los otros controles permanecen disponibles.
- Se mantiene el ID serializado 9, sin renumerar las opciones posteriores; el catálogo de contrato conserva la sección TOML y genera explícitamente `[dex_animations] active = false` para configuraciones válidas.
- Normalización, generación de configuración e importación/exportación de recetas rechazan la función retirada con un mensaje en español. No se descartan opciones silenciosamente.
- Global y Batch realizan la comprobación antes de reproducir acciones; Batch no crea salidas ni manifiesto si detecta esa función.
- SetOptions valida antes de cambiar los controles para no cargar parcialmente una receta retirada.
- Se conservan el código, assets y las pruebas de bajo nivel del registro para una futura reparación. Los demás scripts mantienen sus pruebas.
- No se han modificado mods instalados, configuración SD, guardados, extracción original ni templates del usuario. G sigue siendo la configuración de diagnóstico validada previamente. No hay commit ni push.

## Archivos

`pk3DS.Core/NX/PokeFeurOptions.cs`; `3DS.Progressive.App/SwordPokeFeurWindow.cs`; `3DS.Progressive.App/Services/Switch/SwordShieldProject.Global.cs`; pruebas `PokeFeurChecks.cs`, `PokeFeurRealChecks.cs`, `SwitchAutomationChecks.cs`; este informe.

## Validación ejecutada

- Solución Release: 0 errores, 56 emisiones de advertencias conocidas. Proyecto de pruebas Release: 0 errores, 88 emisiones conocidas. Ninguna advertencia nueva frente a la línea base.
- Runner propio: Switch 316, Switch UI 96, paridad/Global/Batch 195, rediseño WPF 95, UI compartida 398. Total: **1100 comprobaciones satisfactorias**.
- Casos concretos: `Poke_UIRetiredDexAbsentOtherFeaturesRemain`, `Poke_UIRetiredRecipeRejectedWithoutApplying`, `Poke_RetiredDexRejectedByNormalization`, `Poke_RetiredDexCannotGenerateConfig`, `Poke_RetiredDexCannotExportRecipe`, `Poke_RetiredDexRejectedInLegacyRecipe`, `Poke_RetiredDexExplicitlyOffAndSerializedIdsPreserved`, `Switch_Global_RetiredDexRejectedBeforeReplay`, `Switch_Global_RetiredDexRejectedByBatchPreflight`, `Switch_Global_RetiredDexPreservesSessionAndCreatesNoBatchOutput`.
- Las comprobaciones UI son de componentes fuera de pantalla, no una interacción real con el escritorio. No se repitió la jugabilidad después de compilar: se conserva la evidencia previa F/G del usuario.
- `git diff --check` correcto. Logs/binlogs: `C:/Users/Joanj/AppData/Local/Temp/DR-retire-dex-20261023`.

Pendiente: reparar y validar el hook nativo antes de volver a ofrecer esta opción. Reiniciar la aplicación usando la compilación actual para ver su retirada; no se publicaron ni reemplazaron distribuciones instaladas.
