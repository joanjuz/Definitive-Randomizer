# Runner de pruebas de 3DS.Progressive.Tests

`3DS.Progressive.Tests` es un **ejecutable con runner propio**. No usa MSTest ni xUnit, así que `dotnet test` no ejecuta sus comprobaciones. La fuente de verdad es `3DS.Progressive.Tests/Program.cs`: este catálogo se generó a partir de ese archivo el 29/10/2026 y puede quedar desfasado. Si una opción no aparece aquí, buscarla en `Program.cs`.

## Uso

`--usum-mints-rebuild <proyecto con mentas> <imagen 3DS nueva> <carpeta nueva de reextracción>` hace una única reconstrucción Full3DS, verifica todos los archivos reextraídos byte a byte y comprueba que el parche siga reconocido. Solo se usa con copias privadas; no valida una partida en el emulador.

`--usum-mints-only` ejecuta el mapa de 21 mentas, bolsillos, rechazo de títulos/huellas/hooks/colisiones, textos sin pérdidas, preparación, transacción con fallo inyectado, historial, reapertura, idempotencia y deshacer con fixtures sintéticos. `--usum-mints-ui-only` verifica fuera de pantalla la advertencia, instalación bloqueada sin propuesta y navegación al editor de tiendas existente. `--usum-mints-arm-fixtures <carpeta>` genera CRO sintéticos para `native/3ds/nature-mints/verify_arm.py`; no contienen datos de juegos. `--usum-mints-real-copies <salida nueva> <Ultra Sol extraído> <Ultra Luna extraído>` copia exclusivamente RomFS/ExeFS y cabeceras necesarias, instala y deshace en las copias, guarda las 21 mentas en una tienda, comprueba precios y fuentes intactas. Requiere material privado; no se ejecuta en CI ni modifica las fuentes. Los resultados jugables se registran aparte según [fase 1](MENTAS_GEN7_USUM_FASE1.md).

`--definitive-game-data-only` verifica el exportador USUM con GARC y textos sintéticos: datos guardados, deshacer, lectura actual pese a cachés antiguas, formas, evoluciones vacías, identidad, destinos, privacidad y acceso WPF. No usa ROMs reales. `--definitive-game-data-fixture <json>` exporta un JSON sintético nuevo para comprobar interoperabilidad con Tracker. La suite de datos se incluye también en la regresión general; no se distribuyen datos extraídos.

`--undo-template-regression-only` verifica UndoOf con historial en memoria, archivos sintéticos temporales y lectura WPF fuera de pantalla. Comprueba exportación manual (ManualEdits y ManualOperation), cadenas de restauraciones, selección de snapshots, separación de hijos GlobalApplication entre recetas con acciones del mismo tipo y opciones recuperadas al reabrir. Informa todas las expectativas antes de fallar conjuntamente si quedan regresiones. No usa ROMs. Queda fuera del recorrido predeterminado; el CI la ejecuta explícitamente y rechaza un código de salida distinto de cero.

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
| `--transaction-races-only` | — | sí |
| `--undo-toctou-only` | — | sí |
| `--undo-template-regression-only` | — | sí |
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

`--transaction-races-only` verifica H2/H3 en Windows/NTFS con binarios sintéticos temporales y oplocks: Commit, rollback y Recover ante escritores externos, procesos hijos interrumpidos o matados a mitad, calibraciones sin escritor, journals anteriores y compatibilidad de metadatos. Imprime bytes, SHA-256, secuencia y estado residual de todos los casos antes de exigir las invariantes conjuntamente. La versión corregida con guardados individuales registra **90 correctas y 0 fallidas**; la versión investigada registraba 18 y 8. El modo interno `--interrupt-transaction-races <raíz> [modo]` exige una raíz sintética propia bajo TEMP. Reproducción: [REPRODUCCION_H2_H3_20261104.md](REPRODUCCION_H2_H3_20261104.md). Corrección, resultados y límites: [CORRECCION_H2_H3_20261104.md](CORRECCION_H2_H3_20261104.md). Guardados sin journal explícito y recuperación: [H2_H3_GUARDADOS_INDIVIDUALES_20261104.md](H2_H3_GUARDADOS_INDIVIDUALES_20261104.md). Los modos internos admitidos son `commit`, `recover`, `commit-vacated`, `standalone-vacated`, `standalone-recover`, `garc`, `garc-reader` y `standalone-writer`; un modo desconocido termina con código 2 antes de acceder a la raíz.

`--undo-toctou-only` es una regresión aislada para Windows y un sistema de archivos local con soporte de oplocks. Usa dos archivos temporales sintéticos y pausa la apertura de una copia de restauración mediante un oplock; modifica el destino después de validar su `AfterSha256` y antes de capturarlo para la transacción. No usa ROMs ni esperas temporizadas. Los límites de espera solo detectan bloqueos del mecanismo. El resultado esperado es rechazo, conservación de ambos archivos (incluida la edición externa) e historial idéntico byte a byte. La prueba informa los cuatro resultados antes de exigirlos conjuntamente. Queda fuera del recorrido predeterminado; el CI la ejecuta explícitamente con esta opción.

`RestorePoints.Undo` compara con `AfterSha256` los mismos bytes capturados que entrega a `FileChange`, antes de llamar a `ProjectHistory.Commit`. Una discrepancia durante esa preparación rechaza el conjunto sin escribir archivos ni historial. Esto cierra la ventana entre el preflight `WhyNot` y la captura. Después, `FileTransaction.Commit` retiene todos los destinos, comprueba el fingerprint por ese mismo handle y los reemplaza sin soltarlos, con relectura y rollback; las garantías y sus límites están en [CORRECCION_H2_H3_20261104.md](CORRECCION_H2_H3_20261104.md). La regresión de esta opción verifica solo la ventana de preparación de Undo.

`--rebuild-marts-ui-only` incluye la regresión de HistoryView: habilitar «Deshacer este cambio» tras la comprobación automática/manual, deshabilitarlo durante bloqueos anidados o ante cambios externos y retirar suscripciones al descargar la vista. Usa un HwndSource WPF fuera de pantalla y archivos sintéticos. Casos y resultados: [HISTORY_UNDO_BUTTON_20261105.md](HISTORY_UNDO_BUTTON_20261105.md).

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
| `--visual-audit` | 1 o 2 (carpeta de capturas, extracción USUM opcional) | sí |
| `--visual-audit-ds` | 2 o más (carpeta de capturas y ROM de DS) |  |

`--global-apply-ui-only` es un alias de `--global-apply-only`.

`--visual-audit <carpeta> [extracción USUM]` abre la ventana principal real fuera de pantalla, con el despachador activo, para X/Y, ORAS, Sol/Luna y Ultra Sol/Ultra Luna. Usa los tamaños lógicos de pantallas habituales: 1280×800 al 100 %, 1920×1080 al 125 % y al 150 %, y 1366×768 al 125 %.

Recorre:
- todas las secciones y sus pestañas;
- las herramientas que se abren desde los hubs («Abrir …»);
- las tablas que se abren desde una sección;
- cada ventana o diálogo que aparece: se captura y se cierra.

Pone en edición una celda de texto y otra de lista de las tablas. Guarda una captura de cada estado, `issues.txt` y `warnings.txt`.

Falla si encuentra:
- controles solapados o botones pegados;
- texto que no cabe o cortado por abajo;
- controles recortados por su contenedor o que se salen por la derecha;
- texto con contraste menor que el de WCAG AA;
- fondos claros del sistema;
- editores de celda con el aspecto del sistema;
- errores al abrir una vista.

El texto acortado con «…» es un aviso y no hace fallar la auditoría. Tiendas y BP de USUM necesita el `Shop.cro` de una extracción real (segundo argumento, que solo se lee); sin él esa sección se omite y queda un aviso.

También abre el panel de opciones de randomizado de cada editor de registros, con todas sus pestañas, y despliega el Inspector cuando una ventana estrecha lo pliega.

`--visual-audit-ds <carpeta> <rom> [rom…]` hace lo mismo con el espacio de Nintendo DS y volcados reales, que solo se leen (se comprueba su SHA-256). Recorre cada sección, sus opciones, Ajustes del juego, Global Template, Historial y las tablas de Level Caps y Trainer Move Rules.

## Comprobaciones con datos reales, copias y probes (requieren rutas)

Necesitan extracciones, ROMs o carpetas de salida. Los argumentos concretos (entrada, salida, semilla…) se definen en la línea correspondiente de `Program.cs` y en la clase de comprobación que invoca. Usar siempre **copias aisladas y rutas de salida nuevas**; nunca la extracción original, la SD ni mods instalados.

| Opción | Argumentos | En CI |
|---|---|---|
| `--arm-audit-export` | exactamente 2 |  |
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
| `--nds-data-probe` | 2 o más |  |
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
| `--story-probe` | 1 o más |  |
| `--trainer-dump` | exactamente 2 |  |
| `--template-load-probe` | exactamente 3 |  |
| `--read-field6` | 1 o más |  |
| `--read-hm6` | 1 o más |  |
| `--read-machine7-recipes` | 1 o más |  |
| `--read-only-roms` | 1 o más |  |
| `--read-static-profiles` | 1 o más |  |
| `--read-text-classification` | 1 o más |  |
| `--read-title-movies` | 1 o más |  |
| `--read-titles6` | 1 o más |  |
| `--read-tm6-compatibility` | 1 o más |  |
| `--relearner-real` | exactamente 3 |  |
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
