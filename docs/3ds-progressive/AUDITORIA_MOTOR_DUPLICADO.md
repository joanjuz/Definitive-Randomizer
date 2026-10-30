# Auditoría del motor heredado duplicado — 2026-10-30

Análisis sin cambios de código. Compara `3DS.Progressive.App/Services/ProgressiveLegacy/` (motor que la app WPF ejecuta en proceso) con sus originales de `pk3DS.WinForms/` (motor que ejecuta `LegacyBatch` en el Batch). Base: `main` en `b9b1cfd`.

## Conclusión

No se encontró ninguna corrección de lógica de randomización hecha en WinForms después de la copia que falte en la app. Las diferencias grandes son **código que la app no copió** (subconjuntos), no lógica divergente. Hay cuatro diferencias funcionales reales, todas a favor de la app; ninguna produce corrupción silenciosa en el Batch actual. Por eso **no se recomienda consolidar ahora**: primero, pruebas de paridad para los cuatro puntos.

## Inventario

- 34 archivos (~23.400 líneas) en `ProgressiveLegacy`; 31 tienen homónimo en WinForms; 3 son exclusivos de la app (`TrainerLogicHost.cs`, `EngineAdapters.cs`, `LegacyCroLayout.cs`).
- Ignorando espacios, las mayores diferencias son líneas ausentes en la app: `Main.cs` (−2.449), `CustomBalanceTemplates.cs` (−816), `TMEditor7.cs` (−392), `TMHMEditor6.cs` (−373), `GiftEditor6.cs` (−306). La app conserva solo las funciones que necesita.
- Adaptaciones esperadas en casi todos los archivos: espacio de nombres propio, `#nullable disable`, rutas mediante `Main.Workspace` en lugar de `AppDomain.BaseDirectory`, configuración por `AsyncLocal` (`Main.Enter`), formularios sustituidos por `TrainerEngineHooks` y sin recursos gráficos de WinForms.

## Cambios de WinForms posteriores a la copia

Solo cuatro archivos se modificaron en WinForms después de que la app los copiara (`84dae04`):

| Archivo | Commits | Contenido | ¿Falta en la app? |
|---|---|---|---|
| `Main.cs` | `396a8c8`…`4e954bb` | Puente headless del Batch, progreso, Batch Gen6 | No: la app no usa ese código |
| `CustomBalanceTemplates.cs` | `cf16ffd`, `27e185d` | Resolución de la carpeta de templates en `LegacyBatch` y `DEFINITIVE_GLOBAL_PREVIEW_ROOT` | No: la app resuelve rutas con `Main.Workspace` |
| `TrainerHeldItemTemplate.cs`, `TrainerBetterMovesetTemplate.cs` | `27e185d` | Ruta por `DEFINITIVE_GLOBAL_PREVIEW_ROOT` | No: mismo motivo |

## Diferencias funcionales (solo en la app)

| # | Archivo | Diferencia | Efecto en WinForms/Batch | Severidad |
|---|---|---|---|---|
| 1 | `RSTE.cs` (Gen6, entrenadores) | Comprueba `t1 >= 0 && t1 < rTags.Length` antes de `rTags[t1]` | Un índice fuera de rango lanzaría `IndexOutOfRangeException`: fallo explícito, no corrupción | Baja. **Portada a WinForms** (ver seguimiento) |
| 2 | `SMTE.cs` (Gen7, entrenadores) | `ImportantTrainers`, `BossTrainers`, `Legendary`, `Mythical` y `FinalEvo` son de instancia; en WinForms son `static` calculados una vez con `Main.Config.USUM` | El Batch usa un proceso `LegacyBatch` por lote con un solo `ProjectRoot`: no mezcla SM y USUM. Solo afecta a WinForms interactivo si se abre SM y después USUM sin reiniciar (comportamiento heredado) | Baja |
| 3 | Parches Gen7 de `Battle.cro` (`Gen7LuckyChantCrit`, `FairyLockV11`, `MeditateV73`, `NightmareSleepV76` y Wish en `CustomBattleEffectPatcher`) | La app localiza offsets con `LegacyCroLayout` cuando Giro Vil ya reubicó el CRO y recalcula los hashes después | WinForms aplica Giro Vil al final de la misma ejecución, así que en una aplicación única no hay diferencia. Si se reaplican parches sobre un `Battle.cro` que ya tiene Giro Vil, WinForms devuelve −1 y avisa ("Special battle patch skipped"): fallo explícito | Media: puede dar resultados distintos entre app y Batch al reaplicar |
| 4 | `Randomizer.cs` | `GetRandomItemList(GameConfig)` recibe la configuración en lugar de leer la global | Equivalente con una sola ROM por proceso | Ninguna |

**Corrección (30/10/2026):** la versión inicial incluía `Gen7MistOwnerActivePatcher` en el punto 3. Es incorrecto: su código es idéntico en ambos motores (solo cambian dos comentarios de cabecera), no usa `LegacyCroLayout` y solo modifica el segmento de código (offsets < `0xFD000`), que Giro Vil V2 no desplaza. `--legacy-parity-only` comprueba que ambos motores dan el mismo resultado y los mismos bytes.

## Seguimiento (rama `feature/paridad-motor-legacy`)

- Punto 1: comprobación de límites portada a `pk3DS.WinForms/Subforms/Gen6/RSTE.cs`. Con ROMs reales los IDs de `rEnsureMEvo` (máximo 601 en XY y 946 en ORAS) caben en `rTags`, así que el resultado no cambia; solo evita la excepción con tablas de entrenadores más cortas.
- Punto 3: nueva suite `--legacy-parity-only` (13 comprobaciones, incluida en el CI). Sobre el `Battle.cro` sintético con Giro Vil V2 de `RegressionFixes.FullBattle()`: LegacyBatch localiza el archivo y aun así devuelve −1 sin modificarlo en Lucky Chant, Fairy Lock, Meditate y Nightmare; la app los resuelve y conserva V2. Fija el comportamiento actual: si cambia en cualquiera de los dos motores, la suite falla.
- Decisión (30/10/2026): el Batch **mantiene el rechazo explícito** y no se porta `LegacyCroLayout` a WinForms. El Batch parte del `Battle.cro` original y aplica Giro Vil al final, así que el caso solo aparece al reaplicar; el rechazo no corrompe datos y `--legacy-parity-only` impide que cambie sin que se note. Portarlo exigiría pruebas con copias reales y en juego.

## Recomendaciones

1. **Pruebas de paridad** (siguiente paso): para el punto 3, un caso sintético que aplique Giro Vil + otro parche Gen7, reaplique sobre el resultado y compare app frente a WinForms (hash final de `Battle.cro` y código de retorno). Para el punto 1, un caso con `rEnsureMEvo` fuera de rango en ambos motores.
2. **Portar a WinForms** la comprobación de límites del punto 1 (cambio mínimo y aislado).
3. ~~Decidir si el Batch debe admitir la reaplicación del punto 3~~: decidido, se mantiene el rechazo explícito (ver seguimiento).
4. **Consolidación** en un proyecto compartido: solo después de 1–3 y con comparación byte a byte de salidas en XY, ORAS, SM y USUM. El riesgo actual no la justifica por sí sola.

## Método

`git diff --no-index -w` por pareja, líneas exclusivas de cada lado, `git log <copia>..HEAD` del original en WinForms y lectura del modelo de proceso del Batch (`BatchRomExecution.cs`: un proceso `--batch-bridge` por lote). No se ejecutó ningún motor con ROMs reales.
