# Definitive Randomizer 1.1.0-preview.3 · prueba privada XY-MINT

@DISTRIBUTION_STATUS@

Commit compilado: `@BUILD_COMMIT@`.

@DISTRIBUTION_METADATA@

Esta entrega privada del PR #147 permite probar las 21 mentas de naturaleza y los iconos corregidos en Pokémon X/Y desde **Mecánicas y CRO → Mentas Gen6 · X/Y**. X tiene aceptación jugable del propietario; la comprobación de Y queda a su cargo. No se presenta como una release pública ni como una validación jugable independiente de Y.

## Cómo probar

1. Extraer el ZIP en una carpeta nueva y abrir `DefinitiveRandomizer.exe`. No reemplazar la Preview instalada.
2. Abrir una copia extraída de la ROM de Y, también si ya está randomizada. Entrar en **Mecánicas y CRO → Abrir Mentas Gen6**.
3. Pulsar **Comprobar y preparar**, revisar la propuesta e **Instalar las 21 mentas**. Después pulsar **Instalar iconos corregidos**. Los dibujos se descargan de PokéAPI y se verifican antes de convertirlos.
4. Para obtenerlas fácilmente, usar la opción independiente **Añadir las 21 mentas a tiendas**: conserva el inventario existente y añade las que faltan a las nueve tablas generales, cuando caben en el espacio verificado.
5. Usar **Rebuild** con una ruta nueva. En Lime3DS, abrir esa ROM con una copia del guardado Y. Comprobar nombre/icono, cambio de naturaleza y estadísticas, consumo de una unidad, cancelación y mismo tipo sin consumo; guardar, cerrar y reabrir.

Cada operación se registra en Historial. Deshacer se realiza en orden inverso y devuelve los bytes anteriores del proyecto, incluidos sus datos randomizados; no cambia lo guardado dentro de una partida.

La corrección de «Movimientos mejorados» en Gen6 evita que el archivo opcional vacío anule la casilla general. Conserva las categorías de entrenadores seleccionadas y las reglas de archivos personalizados (Keep, nivel, alcance y probabilidad). El selector STAB no cambia: el caso Gyarados 85 / Concha Filo 75 se comprobó por separado sobre datos aislados de Y y con regresión sintética. La política de Gen7 permanece igual.

## Compatibilidad

La instalación conserva Pokémon, entrenadores, encuentros, movimientos, inventarios y textos ajenos a las mentas. No requiere que esos datos coincidan íntegramente con una ROM vanilla. Sí comprueba el título, el mapa nativo de memoria, las instrucciones/importaciones que usa el handler, los slots que ocupará, los segmentos y relocaciones CRO y la estructura CRR. Los hashes que necesitan reparación se preparan y guardan en la misma transacción; no se fabrican firmas RSA.

Se admiten los perfiles X/Y observados y los cambios de datos identificados del randomizer. Una revisión de ejecutable o modificación de código nativo desconocida puede seguir bloqueada: el mensaje explica el recurso incompatible. No se acredita ejecución en consola ni servicios online.

La corrección del instalador reconoce las extracciones HackingToolkit3DS que contienen simultáneamente `HeaderNCCH0.bin` (partición del juego) y `HeaderNCCH.bin` (contenedor NCSD de 16 KiB). Usa la primera sin renombrar ni modificar las cabeceras. Dos cabeceras de partición candidatas siguen rechazándose como ambiguas. Mentas e iconos comparten esta detección.

Global Template y Batch ofrecen instalaciones separadas de Persistent Items, mentas e iconos, y dos checks independientes para incluir mentas en tiendas o en suelo visible/oculto. Instalar no activa los pools automáticamente. Conservar los CSV y recursos adjuntos de cada receta.

@DISTRIBUTION_WORKFLOW@
