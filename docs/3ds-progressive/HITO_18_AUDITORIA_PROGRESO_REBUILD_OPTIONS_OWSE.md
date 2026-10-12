# Hito 18 — Auditoría de funciones, progreso general, Type Chart y herramientas ROM

**Rama de desarrollo:** `feature/3ds-progressive-ui`. No merge.

## Auditoría de funciones realmente presentes

Se comprobaron el árbol, commits y servicios WPF. Se **reutilizan** estas funciones ya incorporadas: `GameEggMoveService`, `GameMegaEvolutionService` y Mega unlock USUM, `GameShinyRateService` con restauración, `GameTypeChartService`, `GameCroRecipeService`, `GameUsumRelearnerService`, `GamePlayerLevelCapService`, LZ11, backups e historial. **No se duplican**. El Batch ROM Builder conserva su propia barra con porcentaje por ROM y por lote.

## Navegación nueva

- **Mecánicas y CRO**: Player Level Caps, USUM Relearner y CRO Recipes; parches especiales del juego.
- **Herramientas Pokémon**: Egg Moves, Mega Evolutions, Type Chart y Shiny Rate (los mismos editores existentes); OWSE [Dev].
- **Herramientas ROM**: Rebuild original RomFS, ExeFS, .3DS completo y Trimmed .3DS; LZ11, inventario SHA-256, backup y exportaciones.
- **Opciones e idioma**: idiomas de textos del juego, acerca de pk3DS y listado de GARCs del proyecto.

## Type Chart — defecto real hallado

La nueva `GameTypeChartService` contenía un patrón de **firma ARM de 64 bytes mal copiado** del editor `pk3DS.WinForms/Subforms/Gen7/TypeChart7.cs` (incluía bytes incorrectos). Se sustituyó por la misma secuencia **exacta de 64 bytes** del formulario vanilla. El archivo de pruebas `SystemsExpansionChecks` también contenía el patrón incorrecto y se actualizó al original para impedir regresiones. El editor mantiene lectura, propuesta congelada, fingerprint original y guardado con historial. La verificación definitiva sobre ROM extraída modificada requiere usuario; si un parche personal cambió esa firma, se rechaza sin escribir.

## Progreso general, distinto del Batch ROM Builder

- `UiProgress` ahora publica `OperationTelemetry`: título, fase, porcentaje opcional, completados/total y tiempo transcurrido.
- El footer global muestra **fase, reloj real y porcentaje medido**. Si el proceso no conoce un total, presenta avance indeterminado, **no un porcentaje simulado**.
- **Backup** conserva progreso real por número de archivos.
- **Inventario ROM** añade progreso verificable por archivo, SHA-256 por streaming y ruta actual.
- **Rebuild** informa fases reales terminadas y SHA-256 final. Durante una fase sin métricas internas permanece en ella, no se interpola ficticiamente un avance temporal.
- Las demás operaciones conectadas a `UiProgress.Begin/End` muestran estado y tiempo transcurrido. La instrumentación interna de cada motor continúa de forma incremental.

## Rebuild — estado

Editor WPF en Herramientas ROM con salida nueva y exterior al proyecto, staging temporal y empaquetadores originales:
- RomFS → `RomFS.BuildRomFS`
- ExeFS → `BLZCoder` comprime `code.bin` **solo en una copia temporal** + `ExeFS.PackExeFS`
- .3DS completo / Trimmed → empaquetar ambos + `CTRUtil.BuildROM` y ExHeader extraído.
- Se verifica existencia, tamaño y SHA-256 antes del movimiento final. No se reemplazan destinos existentes.
- Solo se reclama progreso porcentual de **etapas completadas**, no de bytes internos del empaquetador. Los archivos reales deben someterse a validación runtime en consola/emulador antes de distribuir.

No se declaran implementados todavía **Rebuild CRO separado, Patch builder, extracción CXI/3DS, GARC Shuffler ni BLZ/BCLIM fuera de las herramientas ya existentes**.

## Idioma original de pk3DS

Se trasladó el selector de **10 índices** del menú `Options → Language` vanilla, desde japonés/English a Español/Coreano/Chino, con restricción original Gen6 de no admitir los índices 8–9. `GameProjectSession.Open` aplica el idioma al **cargar las cadenas originales del juego**; guardarlo y recargar reconstruye catálogos, sin editar ROM ni crear cuentas. No se debe confundir con traducción de todos los controles fijos del shell WPF. Se incorporaron About pk3DS y About GARCs (lista real y copia).

## OWSE [Dev]

Primera implementación **de solo lectura** que usa GARC `zonedata/encdata`, `ZoneData7.GetArray`, mapeo real de zonas `zone * 11 + 7/8` para ZS/ZI y previsualización hexadecimal de las entradas. **No se edita ni guarda script** en esta etapa porque el OWSE original está marcado [Dev] y aún no se dispone de un motor reversible validado. Pendiente parseado de scripts, vistas completas de mapas y ediciones seguras.

## Pruebas

CI Windows sintetiza datos XY, ORAS, SM y USUM; pruebas de firma TypeChart, protección de salida dentro de fuente, porcentajes reales 2/8, navegación Rebuild/Herramientas Pokémon/Options y selector de idiomas. CI no garantiza compatibilidad runtime sobre código modificado.

**Política:** mantener `feature/3ds-progressive-ui` aislada hasta aprobación explícita, realizar backups del proyecto y validar Type Chart, OWSE y Rebuild sobre extracciones reales.
