# Runner de pruebas de 3DS.Progressive.Tests

`3DS.Progressive.Tests` es un **ejecutable con runner propio**. No usa MSTest ni xUnit, así que `dotnet test` no ejecuta sus comprobaciones. La fuente de verdad es `3DS.Progressive.Tests/Program.cs`: este catálogo se generó a partir de ese archivo el 29/10/2026 y puede quedar desfasado. Si una opción no aparece aquí, buscarla en `Program.cs`.

## Uso

```powershell
# Compilar (el CI mantiene apphost: Global/Batch comprueba el ejecutable de LegacyBatch)
dotnet build .\3DS.Progressive.Tests\3DS.Progressive.Tests.csproj -c Release -p:UseAppHost=true
# Ejecutar una suite
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --synthetic-only
```

- **Sin opciones**: ejecuta la regresión sintética completa (XY, ORAS, SM y USUM) y, en Windows, también las comprobaciones WPF fuera de pantalla.
- `--synthetic-only`: la misma regresión sintética, sin las comprobaciones WPF.
- Cada comprobación imprime `PASS: …`. El primer fallo lanza una excepción y **detiene la suite**, así que un fallo puede ocultar otros posteriores.
- `--help`, `--list` o `-h`: lista las opciones con sus argumentos y si las ejecuta el CI. El listado sale de la tabla fija de `3DS.Progressive.Tests/RunnerCatalog.cs`, que hay que actualizar junto a `Program.cs` y este documento al añadir, renombrar o quitar una opción.
- La mayoría de las opciones exigen exactamente los argumentos indicados. Una opción desconocida o con otro número de argumentos imprime `Opción no reconocida o número de argumentos incorrecto: …`, sugiere `--help` y termina con código 2 sin ejecutar ninguna comprobación.
- Las comprobaciones "UI" renderizan fuera de pantalla: no equivalen a una prueba interactiva ni a una prueba dentro del juego.

## Suites sintéticas y de componentes (sin argumentos)

| Opción | Argumentos | En CI |
|---|---|---|
| `--batch-gen6-only` | — |  |
| `--batch80-only` | — |  |
| `--catch-batch-only` | — |  |
| `--editor-modules-only` | — |  |
| `--gen7-adjust-only` | — |  |
| `--general-manual-only` | — |  |
| `--global-apply-only` | — | sí |
| `--global-options-only` | — | sí |
| `--guidance-only` | — | sí |
| `--history-inspection-only` | — | sí |
| `--item-icons-only` | — |  |
| `--legacy-parity-only` | — | sí |
| `--manual-edits-only` | — |  |
| `--master-batch-only` | — |  |
| `--native-gen6-only` | — |  |
| `--native-wild7-only` | — |  |
| `--nds-only` | — | sí |
| `--performance-only` | — |  |
| `--pickup-only` | — |  |
| `--pokefeur-only` | — |  |
| `--rebuild-cache-only` | — |  |
| `--rebuild-stability-only` | — | sí |
| `--remote-sprites-only` | — |  |
| `--requested-only` | — |  |
| `--shiny-batch-only` | — |  |
| `--single-purchase-only` | — |  |
| `--sm-marts-only` | — |  |
| `--starter-text-only` | — |  |
| `--statics7-batch-only` | — |  |
| `--swsh-expansion-only` | — |  |
| `--swsh-only` | — | sí |
| `--swsh-parity-only` | — | sí |
| `--synthetic-only` | — | sí |
| `--template-feedback-only` | — |  |
| `--template-latest-only` | — |  |
| `--template-studio-only` | — |  |
| `--text-classification-only` | — |  |
| `--text-commands-only` | — |  |
| `--text-search-only` | — |  |
| `--title-movies-only` | — |  |
| `--titles6-only` | — |  |
| `--tm6-compatibility-only` | — |  |
| `--tm7-text-only` | — |  |
| `--trades6-only` | — |  |
| `--tutors6-only` | — |  |

## Suites de interfaz WPF fuera de pantalla (sin argumentos)

| Opción | Argumentos | En CI |
|---|---|---|
| `--all-ui-only` | — |  |
| `--global-apply-ui-only` | — |  |
| `--interaction-lock-only` | — | sí |
| `--rebuild-marts-ui-only` | — | sí |
| `--nds-ui-only` | — | sí |
| `--requested-ui-only` | — | sí |
| `--swsh-ui-only` | — | sí |
| `--template-feedback-ui-only` | — | sí |
| `--template-latest-ui-only` | — | sí |
| `--template-studio-ui-only` | — | sí |
| `--text-classification-ui-only` | — | sí |
| `--text-commands-ui-only` | — | sí |
| `--tm-default-ui-only` | — |  |
| `--utility-ui-only` | — |  |
| `--visual-redesign-only` | — | sí |

`--global-apply-ui-only` es un alias de `--global-apply-only`.

## Comprobaciones con datos reales, copias y probes (requieren rutas)

Necesitan extracciones, ROMs o carpetas de salida. Los argumentos concretos (entrada, salida, semilla…) se definen en la línea correspondiente de `Program.cs` y en la clase de comprobación que invoca. Usar siempre **copias aisladas y rutas de salida nuevas**; nunca la extracción original, la SD ni mods instalados.

| Opción | Argumentos | En CI |
|---|---|---|
| `--audit-catch-templates` | exactamente 2 |  |
| `--check-catch-batch-real-copies` | 2 o más |  |
| `--check-catch-real-copies` | 2 o más |  |
| `--check-full-batch-real` | 2 o más |  |
| `--check-marts6-full-batch-real` | 2 o más |  |
| `--check-marts7-full-batch-real` | 2 o más |  |
| `--check-master-full-batch-real` | 2 o más |  |
| `--check-real-rebuild-roundtrip` | 2 o más |  |
| `--check-shiny-independent-copies` | 2 o más |  |
| `--check-statics7-full-batch-real` | 2 o más |  |
| `--check-title-movie-roundtrip` | exactamente 2 |  |
| `--check-trades6-package-copy` | exactamente 3 |  |
| `--check-tutors6-full-batch-real` | 2 o más |  |
| `--check-tutors6-independent-copies` | 2 o más |  |
| `--check-tutors7-full-batch-real` | 2 o más |  |
| `--description-preview-fonts` | opcionales |  |
| `--diagnose-text-catalogue` | exactamente 1 |  |
| `--export-local-english-for-text-research` | 2 o más |  |
| `--extract-independent-image` | exactamente 2 |  |
| `--gen7-adjust-real` | exactamente 2 |  |
| `--interrupt-save-check` | exactamente 1 |  |
| `--nds-gen5-probe` | 1 o más |  |
| `--nds-gen4-probe` | 2 o más |  |
| `--nds-gen5-items-probe` | 2 o más |  |
| `--nds-gen5-random-probe` | 2 o más |  |
| `--nds-gen5-starters-probe` | 2 o más |  |
| `--nds-gen5-w2-probe` | exactamente 2 |  |
| `--nds-romhack-probe` | 2 o más |  |
| `--nds-roundtrip-probe` | 2 o más |  |
| `--nds-ui-probe` | exactamente 2 |  |
| `--pokefeur-probe` | exactamente 3 |  |
| `--read-facilities` | 1 o más |  |
| `--read-field6` | 1 o más |  |
| `--read-hm6` | 1 o más |  |
| `--read-machine7-recipes` | 1 o más |  |
| `--read-only-roms` | 1 o más |  |
| `--read-static-profiles` | 1 o más |  |
| `--read-text-classification` | 1 o más |  |
| `--read-title-movies` | 1 o más |  |
| `--read-titles6` | 1 o más |  |
| `--read-tm6-compatibility` | 1 o más |  |
| `--requested-real` | 1 o más |  |
| `--review73-real` | 1 o más |  |
| `--single-purchase-backend` | exactamente 1 |  |
| `--single-purchase-copies` | exactamente 2 |  |
| `--sword-surprise-audit` | exactamente 1 |  |
| `--sword-surprise-probe` | exactamente 2 |  |
| `--swsh-divine-probe` | exactamente 1 |  |
| `--swsh-expansion-probe` | exactamente 2 |  |
| `--swsh-gameplay-probe` | exactamente 2 |  |
| `--swsh-global-probe` | exactamente 2 |  |
| `--swsh-parity-probe` | exactamente 1 |  |
| `--swsh-polish-probe` | exactamente 1 |  |
| `--swsh-probe-mod` | exactamente 2 |  |
| `--swsh-random-probe` | exactamente 3 |  |
| `--swsh-read-only` | exactamente 1 |  |
| `--swsh-table-probe` | exactamente 1 |  |
| `--swsh-version-probe` | exactamente 2 |  |
| `--swsh-visual-preview` | exactamente 2 |  |
| `--template-feedback-copies` | 1 o más |  |
| `--template-studio-shops-copies` | 1 o más |  |
| `--verify-existing-rebuild` | exactamente 3 |  |

## Suites incluidas en el CI

`.github/workflows/3ds-progressive-ci.yml` ejecuta las opciones marcadas "sí" y después `--synthetic-only`. Las comprobaciones con datos reales no pueden correr en CI porque no hay ROMs disponibles.
