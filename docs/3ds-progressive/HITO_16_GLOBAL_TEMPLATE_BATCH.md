# Hito 16 — Global Template y Batch ROM Builder

Rama: `feature/3ds-progressive-ui`. No hay merge automático.

## Contrato compatible con `DEFINITIVO.json`

Validado sobre el documento facilitado (UTF-8 con BOM; el importador usa `File.ReadAllText`).

- `Version: 1`, `ActionCoverageVersion: 8`, `Game: USUM`, `Generation: 7`.
- 178 entradas `RandSettings`, agrupadas en nueve editores originales.
- 35 `Actions` **con parámetros completos**, inclusive `prices`, `slots`, `operations`, `progression`, `ranges` y `caps`.
- `Trainer.ProgressiveBST` con 8 rangos; `Trainer.LevelCaps.Trainers` con 41 registros; `Trainer.TrainerMoveRules.Trainers` con 114.
- `Wild.ProgressiveBST` con 6 rangos.
- `LevelCaps.Entries` con 21 checkpoints incluyendo condiciones `EventFlagSet` y `EventWorkAtLeast`.
- Cuatro assets con hashes SHA-256. El archivo contiene valores de configuración de Pokémon, economía, evolución, entrenadores, encuentros, tiendas, intercambios, CRO y mejoras de calidad de vida.

**Invariante:** no migrar a un esquema simplificado. Usar `JsonNode.DeepClone` y conservar todo nodo desconocido; únicamente modificar nombre y lista `Actions` cuando el usuario lo solicite. El importador no escribe datos del juego.

## Interfaz

### Global Template
- Navegación directa en `Sistemas` y desde `Automatización`.
- Importación de JSON original v1 (sin modificar ROM).
- Lista de acciones con checks para incluir/excluir en la copia exportada.
- Exportación sin sobrescribir el archivo original importado; se conserva orden, valores, rangos BST, trainer tables, Player Caps, atributos desconocidos y parámetros opacos.
- `Crear desde historial` permite agregar IDs de operaciones documentadas a una receta existente. Si no hay receta anterior, genera un snapshot **parcial**, que no se autoriza para Batch ROM Builder hasta incluir `RandSettings` completos. Los detalles que el historial no ha registrado **no se inventan**.

### Batch ROM Builder
- Vista oscura integrada, no ventana WinForms.
- Archivo global JSON, cantidad (1–50), carpeta destino fuera del proyecto, nombre base, Full/Trimmed .3DS, restaurar backups y conservar proyectos editables.
- Preflight: generación 7, juego, versión 1, ActionCoverageVersion>=2, acciones reconocidas por motor original, sin duplicados, hashes de assets y destino sin colisiones.
- Ejecutor: `LegacyBatch/pk3DS.WinForms.exe --batch-bridge <job.json>`, proceso separado oculto. Reutiliza el motor **original** `BatchGen7ActionExecutor`, staging limpio por cada ROM, nueva semilla por ROM y `CTRUtil.BuildROM`.
- El orden del motor original permanece: movimientos, personal, evoluciones, level-up, huevo, MT, estáticos (con Totems), salvajes, pickup, economía, tiendas, tutores, objetos de campo, shiny, entrenadores al final y parches especiales/CRO.
- El motor conserva las ROM completadas cuando otra falla, y escribe `<base>_batch.txt`. El nuevo WPF genera `<base>_batch-manifest.json` con acciones/receta, SHA-256 de cada archivo de salida, tamaño y semilla cuando se informa en el log. No edita la ROM original.
- Errores del proceso oculto: `3ds-progressive-batch-error.txt` dentro de la salida.

## Historial y recuperación

La nueva receta de construcción **no** se registra como una edición GARC del proyecto de origen (no la hay). Su auditoría es un manifiesto por lote en la carpeta destino, junto al log y archivos resultantes. El historial WPF individual sigue registrando las modificaciones normales. No se debe fingir un rollback global: el motor de lotes aísla cada copia y mantiene archivos anteriores completados.

## Pruebas realizadas

- Regresión de esquema v1/coverage8 con sample anidado de `Trainer`, `Wild`, `LevelCaps`, `Actions`, `Assets` y atributo desconocido.
- Cargar → guardar → volver a cargar conserva `ToJson` en forma canónica, action parameters, rangos y atributos desconocidos.
- Filtrar acciones sin borrar reglas BST u otros campos.
- Rechazo de juego/generación incompatibles; prohibido sobrescribir accidentalmente el original.
- Smoke WPF de navegación Global Template/Batch Builder.
- Empaquetado CI del ejecutable WPF y del backend WinForms autocontenido con templates.

**Pendiente crítico:** ejecutar un lote real usando RomFS+ExeFS+ExHeader y los cuatro assets con SHA-256 que coincidan con `DEFINITIVO.json`, en Ultra Sun y Ultra Moon. Las pruebas con ROMs sintéticas no validan la reconstrucción completa de .3ds ni los parches en runtime.

**Otro pendiente:** sincronizar de forma bidireccional los ajustes *actuales* de todos los editores WPF con RandSettings (importar una receta no reemplaza automáticamente las opciones locales de cada editor). El motor original sí aplica el template al staging de cada ROM durante el batch.
