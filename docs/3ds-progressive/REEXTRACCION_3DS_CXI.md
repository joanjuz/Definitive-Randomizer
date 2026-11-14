# Volver a editar una ROM 3DS ya reconstruida (.3ds y .cxi)

Este documento describe el ciclo **ROM original → randomizar → reconstruir → extraer → editar otra vez → reconstruir** y los límites de cada contenedor.

## Ciclo admitido

1. Extrae la ROM reconstruida con **Extraer ROM** del programa, en una carpeta nueva.
2. Abre esa carpeta como proyecto. Se detecta como una extracción descifrada de 3DS.
3. Edita o vuelve a randomizar. Puedes aplicar un Global Template de nuevo.
4. Reconstruye en un archivo nuevo. Se admiten **.3DS completo** y **Trimmed .3DS**, además de RomFS y ExeFS sueltos.

El ciclo se puede repetir. Las ediciones anteriores se conservan porque la extracción recupera exactamente los archivos que se empaquetaron. Se comprobó byte a byte con una ROM real de Ultra Luna ya randomizada, con dos ciclos completos (detalles al final).

## Por qué hay que usar el extractor del programa

pk3DS y este programa reconstruyen el `.3ds` con el **cifrado de clave fija** de NCCH, que el emulador y la consola aceptan. El extractor del programa reconoce ese cifrado, lo descifra en una copia temporal y no modifica el archivo de origen.

HackingToolkit3DS usa `3dstool`. Se probaron las versiones 1.0.15 y la incluida en los paquetes de pk3DS, y **ninguna descifra la clave fija**:

- `DecryptedExHeader.bin`, `DecryptedExeFS.bin` y `DecryptedRomFS.bin` quedan cifrados, aunque su nombre diga lo contrario.
- La extracción de ExeFS y RomFS falla con `ERROR: the file type is mismatch`.
- Si se extrae encima de una carpeta antigua, sus `ExtractedRomFS` y `ExtractedExeFS` siguen siendo los anteriores. Solo cambian los `Decrypted*.bin`. El proyecto mezcla entonces archivos de dos ROM distintas.

Ahora, la reconstrucción `.3ds` lo detecta antes de empaquetar RomFS. Con un ExHeader cifrado, de otro juego o duplicado con contenido distinto, se detiene, explica la causa y no modifica nada. Antes fallaba al final con `KeyNotFoundException`, sin indicar el componente.

## .3ds frente a .cxi

| | `.3ds` (NCSD) | `.cxi` (NCCH) |
|---|---|---|
| Entrada del extractor | Partición 0 del contenedor | El propio NCCH |
| Cifrado admitido | Ninguno o clave fija de pk3DS en aplicaciones normales | Igual |
| Cifrado comercial | Rechazado: se necesita una copia descifrada | Igual |
| Archivos en la raíz | `ncchheader.bin`, `exheader.bin`, `extraction-manifest.json` y, si existen, `plain.bin` y `logo.bcma.lz` | Igual |
| Carpetas | `RomFS`, `ExeFS` | Igual |
| `code.bin` | Se descomprime (BLZ) y se verifica su tamaño | Igual |
| Particiones de manual y actualización | No se extraen ni se vuelven a incluir | No existen |

Un CXI sin región plain ni logo produce exactamente **tres archivos en la raíz** más `RomFS` y `ExeFS`. Esa es una extracción completa: no faltan componentes que el CXI no tenía.

La partición 0 de una ROM reconstruida, extraída como CXI, produce los mismos archivos que el `.3ds`. Se comprobaron byte a byte RomFS, ExeFS, cabeceras, región plain y logo.

### Reconstruir un .3ds desde un CXI

Es posible, con los mismos límites que desde un `.3ds`. El empaquetador genera la cabecera NCCH a partir del ExHeader, con el Title ID, el código de producto y la región plain de la familia del juego, y usa el logo incluido. Se conserva la identidad (Title ID y Program ID). La salida contiene solo la partición del juego, también cuando la entrada era un `.3ds` comercial: el manual electrónico y los datos de actualización del sistema no forman parte del proyecto.

### Code.bin comprimido sin verificar

Si el ExHeader declara `code.bin` comprimido y su pie BLZ no se puede verificar, la extracción **no se publica**. Antes podía quedar un `code.bin` todavía comprimido, que la reconstrucción volvía a comprimir.

## Mentas Gen7 en un ejecutable modificado

El instalador compara 62 rangos de rutinas nativas por perfil. El mensaje ahora indica:

- la primera dependencia distinta;
- cuántas de las 62 difieren. Si fallan todas, el `code.bin` no es un ejecutable USUM descomprimido de ese juego; si fallan pocas, hay una modificación dirigida;
- que no es un estado de mentas ya instaladas, porque las mentas funcionales no modifican esas rutinas;
- que solo se bloquean las mentas, que el resto del proyecto puede editarse y reconstruirse y que no se escribió nada.

Las comprobaciones no se relajan. No se actualizan hashes ni se acepta un ejecutable con esas rutinas cambiadas.

Las ROM de Ultra Luna disponibles para la prueba superan los 62 rangos. Eso incluye el volcado limpio, todas las reconstrucciones randomizadas con Batch, Global Template, Mega Ability Sync e iconos de mentas, y su reextracción. Las mentas ya instaladas se reconocen después de reconstruir y volver a extraer.

## Comprobaciones

- `--rerandomize-cycle-probe <carpeta nueva> <imagen .3ds/.cxi reconstruida> [<Global Template>]` es una prueba privada con copias. Ejecuta dos ciclos: extraer, abrir, editar un entrenador, reconstruir (Trimmed), volver a extraer y comparar byte a byte. En el segundo ciclo puede aplicar de nuevo un Global Template, edita otro entrenador y randomiza un tercero. Después comprueba que todos los cambios se acumulan, que la identidad NCCH y el estado de las mentas se mantienen y que la imagen de origen no cambia.
- `--rebuild-cache-only` y `--synthetic-only` incluyen las comprobaciones sintéticas del ExHeader: propio, de toolkit descifrado, cifrado, Title ID cero, otra familia, varios distintos, copias idénticas, nombre antiguo, truncado y ausente.
- Las comprobaciones de extracción incluyen el rechazo de un `code.bin` declarado comprimido sin pie BLZ válido.

Ninguna de estas pruebas ejecuta el juego. La jugabilidad de las ROM resultantes sigue requiriendo una prueba en emulador o consola.
