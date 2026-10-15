# Iteración 34 — Global Template, Batch ROM, entrenadores y estáticos

Prioridad solicitada: integrar estas funciones del sistema original ya validado. Esta iteración conecta configuración y empaquetado; no declara terminada la migración de todos los módulos ni sustituye pruebas en juego.

## Cambios

- Global Template restaura las opciones originales de entrenadores (`SMTE` / `TrainerRand`) y su objeto `Trainer`, incluidos BST Progressive, políticas globales, Level Caps y reglas individuales de movimientos. Guarda las opciones recordadas al salir del editor usando el mismo formato `RandSettings`, sus escalas numéricas reales y el template del motor original.
- La importación valida servicios aislados antes de reemplazar el estado de la interfaz. Un error en estáticos deja intacta la configuración anterior de entrenadores y la receta cargada. Importar y crear snapshots no escribe datos de juego.
- Estáticos Gen7 traduce los controles comunes de generaciones, legendarios/eventos, objetos, habilidades, movimiento especial, niveles, shiny locks, aura y evolución final. Todas las opciones propias del editor Gen6/7 y su alcance se conservan además en `ProgressiveApp`; el motor original ignora esta extensión.
- Se conservan controles y módulos desconocidos, parámetros de acciones, assets y propiedades futuras del JSON, incluidas propiedades dentro de reglas identificadas por `TrainerID`. La fuente se guarda siempre en una ruta distinta.
- Se corrige una pérdida de reglas en `TrainerLogicHost`: las capturas del diálogo original filtraban candidatos habilitados y, en Gen6, omitían metadatos. Ahora las listas completas importadas/editadas sobreviven al reconstruir opciones y capturar templates, incluidos `Use=false`, `CurrentAceLevel`, `NerfTeam` y `ResetUnlistedTrainers`. Esto preserva configuración; no convierte un ID ajeno a los candidatos del motor en un candidato válido.
- Debug compila y empaqueta el ejecutable original de Batch, sus DLL y archivos de runtime, templates legales y manifiestos Catch Zones. Se usa una salida aislada bajo `3DS.Progressive.App/obj/LegacyBatch`, porque pk3DS WinForms estaba abierto y bloqueaba su salida normal. La instancia del usuario se mantuvo abierta.
- Batch rechaza snapshots parciales creados desde historial, aun cuando contengan opciones de entrenadores. También rechaza alcances parciales de entrenadores/estáticos y opciones de estáticos que aún no puede reproducir fielmente: grupos desactivados, suma de niveles y tolerancia BST propia del editor. No genera resultados con opciones silenciosamente descartadas.

## Validación local

La compilación Debug y las regresiones se ejecutaron localmente, sin GitHub Actions: **1.655 comprobaciones aprobadas, 0 errores de compilación y 79 advertencias existentes al incluir también el motor WinForms original**. Resultado en `ITERACION_34_PRUEBAS/build.log` y `regression.log`. El total de advertencias ya no es comparable directamente con las 22 de la compilación anterior de App/Core sin el ejecutor WinForms adicional.

- Fixtures sintéticos XY, ORAS, SM y USUM: importación y exportación de todas las opciones expuestas de entrenadores, precisión decimal shiny, caps y reglas de movimientos, metadatos de reglas deshabilitadas, propiedades futuras y ausencia de escrituras en tablas de juego.
- Estáticos Gen6/7: conservación de opciones y alcance; traducción de controles Gen7; archivos CRO/GARC intactos durante transferencia de configuración.
- WPF: los controles reales recuperan la configuración, exportan el estado recordado y rechazan una importación inválida sin reemplazar el estado anterior. Los snapshots de historial siguen marcados como parciales.
- Batch empaquetado: arranque real de `LegacyBatch/pk3DS.WinForms.exe` con una solicitud sintética `Count=0`; rechazo esperado antes de abrir un proyecto, salida 1 y diagnóstico del motor original. Esta prueba comprueba arranque y dependencias, **no** generación de una ROM completa.
- Los 12 archivos protegidos de Wild y notas personales conservan sus SHA-256 iniciales. Rama `feature/3ds-progressive-ui`; sin commits, push, release ni cambios en Eris-Ultranova.

La revisión automática rechazó inicialmente copiar archivos con cambios locales. Se resolvió respaldando las versiones actuales, revisando los diffs y comprobando sus hashes antes de integrar las adiciones. No quedó una solicitud de permiso pendiente.

## Pendiente antes de declarar este bloque completo

1. Sincronizar con WPF los demás módulos de Global Template. Hoy conservan su JSON original y el motor Batch puede consumir recetas originales completas; no se reconstruyen sus opciones desde historial.
2. Migrar las opciones originales de estáticos aún no expuestas: Mega, restricciones de iniciales, Metronome, Force Totem y los perfiles separados de regalos/estáticos/iniciales Gen6. El round-trip de opciones nativas Gen6 no equivale a importar estos tres perfiles originales.
3. Integrar correctamente acciones especiales de estáticos/Totem y sus parámetros al crear recetas nuevas. Los parámetros de recetas importadas se conservan, pero el historial no puede reconstruir toda la configuración original.
4. Ejecutar un lote Gen7 completo con una receta real en copias de trabajo y verificar outputs, semillas, manifests y rebuild. No se generaron ROMs reales en esta iteración. El ejecutor original sigue limitado a Gen7; Gen6 Batch no está migrado.
5. Validación in-game XY/ORAS/USUM al cerrar la implementación, conforme a la preferencia del usuario. No se solicita todavía.

Fuera de este bloque siguen abiertos Field Items Gen6, compatibilidad Catch Zones con la referencia XY suministrada, verificaciones de extracción/rebuild y pruebas finales de distribución. No se declara una versión estable completa.
