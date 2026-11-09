# Definitive Randomizer — v0.15.0-preview.2

Pre-release para Windows x64, autocontenida: no necesita instalar .NET por separado. Extrae el ZIP completo en una carpeta nueva y ejecuta `DefinitiveRandomizer.exe`, conservando sus dependencias y `LegacyBatch`.

## Instalador de mentas Gen7 con prueba previa

**El instalador prepara y valida el parche de las 21 mentas en una copia temporal aislada antes de guardar.** Relee el resultado y comprueba sus dependencias y la preservación de los archivos ajenos. Si la preparación falla, descarta la copia temporal. Cuando aparece **Listo para instalar**, **Aplicar y guardar** usa las transacciones, el historial y Deshacer existentes.

La compatibilidad de Ultra Sol/Ultra Luna se comprueba con las dependencias nativas concretas del parche, la identidad del juego, el mapa de memoria, Bag.cro, objetos, bolsillos, textos e integridad CRO/CRR. Ya no se exige que segmentos completos del ejecutable coincidan con una ROM de referencia. Se mantienen los rechazos de dependencias críticas incompatibles y los controles de concurrencia.

Las mentas cambian la **naturaleza real** del Pokémon; no reproducen la semántica de las mentas de generaciones posteriores. Respaldar el proyecto y la partida sigue siendo necesario. Deshacer los archivos del proyecto no revierte lo que ya se haya guardado dentro de una partida.

## Ajustes mínimos de interfaz

- En el instalador se evita el aviso redundante «Mentas no disponibles» cuando la preparación ha alcanzado **Listo para instalar**.
- La lista extensa de archivos permanece en **Diagnóstico técnico**, cerrado inicialmente. La confirmación de guardado muestra un resumen.
- No se modifica el funcionamiento ni las reglas de compatibilidad validados en `bd07d42`.

## Validación y alcance

El propietario confirma una **prueba jugable satisfactoria de `bd07d42` en su ROM previamente randomizada**: las mentas Gen7 funcionan correctamente dentro del juego. Esta confirmación se refiere a su caso concreto y no se extrapola a todas las revisiones o romhacks. **La ROM específica del tester continúa pendiente de validación.**

Como evidencia anterior se conservan las pruebas con copias reales de Ultra Sol randomizado: preparación, instalación, reapertura, historial, deshacer exacto y preservación de datos ajenos; Ultra Luna instalado se reconoció en una copia aislada. Las pruebas sintéticas, las comprobaciones de archivos y el arranque de la aplicación no sustituyen la prueba jugable del propietario ni validan la ROM pendiente del tester.

La versión se prepara desde la línea validada del PR #57, con cambios posteriores limitados a presentación, pruebas de interfaz, documentación y metadatos de versión. **Los PR #55, #56 y #57 permanecen sin fusionar.** No incluye cambios pendientes de Nintendo DS. La funcionalidad DS ya publicada en la versión anterior se conserva.

El tag apunta al commit exacto utilizado para compilar el ZIP. `BUILD_PROVENANCE.json`, `VALIDATION_REPORT.md` y `SHA256SUMS.txt` acompañan la publicación para identificar la fuente y verificar el paquete. Esta publicación no actualiza `main` mediante un merge.

## Uso

1. Abre una copia respaldada del proyecto extraído y entra en **Mecánicas y CRO → Mentas Gen7**.
2. Pulsa **Comprobar estado y preparar**; si aparece **Listo para instalar**, pulsa **Aplicar y guardar**.
3. Genera la salida desde esa copia y comprueba el juego con una partida respaldada. Ante **Instalación no compatible**, consulta el diagnóstico de la dependencia concreta.

No requiere PowerShell, comparación manual de hashes ni compartir archivos de la ROM para instalar las mentas. Los iconos opcionales conservan su preparación y validación propias.

Se mantienen las licencias, atribuciones y fuentes de terceros incluidas en el paquete. No se distribuyen ROMs, partidas ni archivos extraídos de juegos.
