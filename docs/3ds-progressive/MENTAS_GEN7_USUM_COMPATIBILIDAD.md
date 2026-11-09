# Compatibilidad del instalador de mentas USUM

El instalador admite combinaciones de cambios acreditados sin registrar el SHA-256 de cada ROM randomizada. Esto no acredita cualquier romhack ni revisiones nuevas. La comprobación es automática; no requiere comparar hashes manualmente ni ofrece instalación forzada.

## Instalación de prueba antes de guardar

La preparación comprueba la identidad NCCH US/UM, el tamaño descomprimido, el mapa de memoria y el estado de Bag.cro y sus hooks. Selecciona el perfil por título, sin exigir inicialmente una huella completa de code.bin. Prepara los 21 objetos, bolsillos, diez idiomas, runtime y CRR con los mecanismos existentes.

Después copia los archivos afectados y todas las dependencias leídas a una carpeta temporal aislada. Aplica allí la transacción existente, relee los archivos y exige el reconocimiento de la instalación completa, las referencias del runtime, las dependencias nativas protegidas y el inventario CRR/CRO. Comprueba los resultados de los archivos afectados y la preservación exacta de las dependencias ajenas. La carpeta se elimina tanto en éxito como en error; esta prueba no escribe en el proyecto ni crea historial.

Solo tras superar la prueba aparece **Listo para instalar** y se habilita **Aplicar y guardar**. El guardado conserva la transacción de `ProjectHistory`, los bloqueos, las huellas de concurrencia y Deshacer. El exheader también se captura y bloquea como dependencia al distribuir las mentas. Los otros estados son **Instalado correctamente** e **Instalación no compatible**, con explicación y diagnóstico técnico desplegable.

La comprobación final de segmentos no se ha debilitado: acepta las variaciones acreditadas de la tabla siguiente, aunque cambie la huella completa. Una diferencia desconocida sigue sin acreditar sus posibles dependencias transitivas; se rechaza antes de guardar incluso si la escritura temporal pudo completarse. La prueba de archivos no demuestra ejecución en emulador.

El fallo CI de `c6b670e` se reprodujo con `--usum-mints-distribution-only`: `Lifecycle()` sustituía el exheader válido de `Setup()` por 2048 ceros para comprobar el copiado Batch y luego continuaba distribuyendo mentas con ese proyecto inválido. Se conserva el fixture válido y se verifica su copia y preservación para US y UM. No se cambió `ValidateMemoryLayout`, los perfiles ni sus huellas.

## Alcance de la validación

Se mantienen los perfiles separados de Ultra Sol y Ultra Luna, el Title ID de NCCH y las protecciones de Bag.cro completo, sus cuatro puntos de instalación, objetos reservados, bolsillos, textos, CRR, transacciones, dependencias concurrentes e historial/deshacer. `UsumNatureMintPatch` se revisó y no necesitó cambios.

El mapa del exheader debe conservar las direcciones, páginas y tamaños de los tres segmentos acreditados. Hay que disponer de `exheader.bin` o `DecryptedExHeader.bin` en la raíz del proyecto; se verifica como dependencia de la transacción. El tamaño del texto difiere en ocho bytes entre US y UM y se comprueba por separado.

En code.bin se comprueban tres segmentos completos: código `0–4B9FFF`, datos de solo lectura `4BA000–566FFF` y datos del runtime `567000–5A3FFF`. Las huellas se obtuvieron de las dos referencias ya reconocidas por `UsumNatureMintCatalog.CodeSha256`. Antes de calcularlas se normalizan exclusivamente:

| Región | Variaciones admitidas y evidencia local |
|---|---|
| MT, `4BB98E`, 200 bytes | 100 IDs entre 1 y 728; tabla escrita por `GameMachineTutorService`. No altera la representación de naturaleza ni sus funciones. |
| Shiny, `220668` y `2205CF` | Solo las instrucciones ya admitidas por el perfil anterior y el editor shiny. No se amplía este contrato. |
| Iconos de mentas | Solo el mapa completo de las 21 referencias producido por `UsumMintIconLayout`; los mapas parciales siguen sin reconocerse. |
| Efectividad de tipos, `4BB558`, 324 bytes | Matriz 18×18 de `GameTypeChartService`, valores 0, 2, 4 u 8. Es la efectividad de ataques entre tipos, separada del cálculo de estadísticas por naturaleza. Se conservan su firma, ubicación y todos los bytes vecinos. |
| Obediencia, `4C4644`, 6 bytes | Solo `[20,35,50,65,80,100]` o seis valores 100, acreditados por `Gen7Obedience`. Se conserva el literal de referencia en `21BD64`; decide obediencia de Pokémon intercambiados, sin cambiar su naturaleza. |

Todo byte restante se sigue protegiendo, incluso datos cuya independencia no se ha demostrado. Quedan incluidos los cuerpos completos y dependencias transitivas de getter, begin/end access, block A, derive/store stat, instrucciones, literales y referencias. No se intenta deducir límites de funciones a partir de unos pocos prólogos ni permitir automáticamente todo lo que esté fuera de cuatro hooks. Los segmentos US y UM no pueden mezclarse.

Si hay diferencias no acreditadas, se indica el segmento concreto afectado; eso no significa que toda diferencia de ese segmento sea un fallo demostrado de las mentas, sino que no se puede acreditar su compatibilidad. Las tablas con valores desconocidos y los mapas de memoria distintos tienen causas específicas. Los detalles están en **Diagnóstico técnico**.

## Validación focalizada del 08/11/2026

Corrección y prueba temporal posteriores al fallo CI: build Release no incremental con apphost y binlog, 0 errores/advertencias; `--usum-mints-distribution-only` 128, `--usum-mints-only` 98, `--usum-mints-ui-only` 28 y `--usum-mints-compatibility-probe` 40 comprobaciones aprobadas. Se añadieron el descarte por fallo de relectura temporal, limpieza en éxito/error sin historial, preservación del mapa en Batch y rechazo de cambios concurrentes del exheader. El probe usa las referencias US/UM reconocidas y Bag real con objetos/textos sintéticos. Una primera ejecución con otra copia local US se detuvo al no coincidir con la huella original requerida por el fixture; se utilizó la referencia US conservada y no se cambiaron los datos ni el requisito. Sin suites generales locales nuevas ni prueba jugable del tester.

Validación anterior a la corrección, conservada como antecedente:

- Build Release de Tests, que compila App/Core/LegacyBatch: 0 errores y 0 advertencias; binlog privado bajo `3DS.Progressive.Tests/bin`.
- `--usum-mints-only`: 92 comprobaciones sintéticas aprobadas, incluidos conflictos, rollback, reapertura y deshacer.
- `--usum-mints-compatibility-probe`: 40 comprobaciones aprobadas con los code.bin de referencia US/UM y Bag.cro real reconocido. Usa objetos/textos sintéticos en proyectos temporales. Acepta la combinación tipos/obediencia/MT/shiny y su mapa de iconos completo; rechaza mapas parciales y alteraciones en cada una de las seis funciones nativas, el literal de obediencia, vecinos de la matriz y datos del runtime. Instala, reabre y restaura los 14 archivos; preserva el ejecutable randomizado y los archivos ajenos.
- `--usum-mints-ui-only`: 28 comprobaciones WPF fuera de pantalla aprobadas, incluidos los tres estados y el diagnóstico cerrado inicialmente.

No se repitieron suites generales ni se probó una nueva ROM completa en juego, foco interactivo, alto contraste o DPI adicionales. Los originales privados solo se leyeron y no se incorporan al repositorio. No se modificaron PR #55/#56, versiones, tags ni releases.

El propietario confirma que pudo randomizar antes de instalar las mentas. El tester usa Ultra Sol: **su caso concreto sigue pendiente de la prueba privada**. En esta prueba se solicita únicamente el estado y, si es incompatible, el diagnóstico textual desplegable. No se solicitan ROMs, partidas, archivos extraídos ni datos binarios. Si persiste el rechazo, se conserva el bloqueo y no se amplían las regiones permitidas sin identificar la diferencia.

## Guía de prueba privada para el tester

1. Abre tu proyecto extraído existente con el ejecutable privado de prueba para Windows x64 y entra en **Mecánicas y CRO → Mentas Gen7**.
2. Pulsa **Comprobar estado y preparar**. Esta consulta no instala las mentas.
3. Si aparece **Listo para instalar**, pulsa **Aplicar y guardar** únicamente en una copia respaldada del proyecto. Si aparece **Instalación no compatible**, despliega **Diagnóstico técnico** y copia su texto; no compartas archivos de la ROM, partidas ni datos binarios.

Después de **Instalado correctamente**, genera la salida desde esa copia y verifica las mentas en el emulador con una partida de prueba respaldada. La prueba temporal y el arranque del ejecutable no sustituyen esa comprobación jugable. Si aparece **Instalación no compatible**, no se instala ni se fuerza el parche.
