# Mentas USUM — iconos de fase 1.1

Actualización visual independiente de la [fase 1 funcional](MENTAS_GEN7_USUM_FASE1.md), vinculada al [issue #51](https://github.com/joanjuz/Definitive-Randomizer/issues/51). No cambia naturalezas, consumo, precios, tiendas ni distribución de objetos. No incorpora Ability Patch ni fase 2. Sin cambio de versión ni release.

## Estado de validación

El propietario confirmó la prueba manual de fase 1.1 en **Ultra Sol dentro del emulador**: los iconos de las mentas aparecen correctamente, las mentas funcionan y los gráficos de los objetos anteriores se conservan. Esta confirmación permite cerrar el seguimiento visual de fase 1.1; no acredita una prueba manual en Ultra Luna ni una validación exhaustiva de otros comportamientos.

Se mantienen separados los resultados automatizados US/UM, la reconstrucción y la prueba jugable. No se amplía la confirmación a cancelación, consumo exacto o persistencia de naturaleza/inventario tras guardar y cargar, que no se confirmaron expresamente.

## Fuentes y derechos

Se utilizan los 21 PNG de `sprites/items/<nature>-mint.png` de [PokeAPI/sprites, commit 1b3b7c0a538dd2226ae08d0d2f8955125075bbd4](https://github.com/PokeAPI/sprites/tree/1b3b7c0a538dd2226ae08d0d2f8955125075bbd4/sprites/items). Los nombres ingleses se relacionan con los IDs de fase 1, no con el idioma de la partida. Las fuentes tienen seis variantes gráficas de color compartidas entre las 21 naturalezas; no se inventan diseños nuevos.

La [licencia del repositorio](https://github.com/PokeAPI/sprites/blob/1b3b7c0a538dd2226ae08d0d2f8955125075bbd4/LICENCE.txt) declara CC0 para el repositorio y atribuye el copyright de las imágenes a The Pokémon Company. Esto no acredita una cesión del titular que permita redistribuir los gráficos oficiales como assets de Definitive Randomizer. [Bulbagarden Archives](https://archives.bulbagarden.net/wiki/Archives:Copyrights) también distingue su licencia preferida de los numerosos archivos no licenciados que aloja bajo una alegación de fair use; esa alegación no se trata como permiso general de redistribución. No se utilizan imágenes de Archives.

Por ello, **no se incorporan PNG, texturas convertidas ni archivos del juego al repositorio, al paquete de la aplicación o a los fixtures del CI**. El código contiene solo URLs fijadas por commit, IDs y SHA-256 de entrada y salida. Un botón opcional descarga los 21 PNG desde PokéAPI a la caché local y los convierte para el proyecto del usuario. Esta adquisición local evita que la aplicación redistribuya los assets y no otorga una licencia nueva sobre ellos, ni autorización para redistribuir ROMs o modificaciones que los contengan. Crédito de los gráficos: © The Pokémon Company; alojamiento: PokéAPI/sprites.

El primer uso requiere conexión a `raw.githubusercontent.com`. La caché validada permite repetir la preparación sin conexión: `%LOCALAPPDATA%\DefinitiveRandomizer\mint-icons\<commit>`. Un error de red, hash o conversión no instala nada en el juego. No hay descargas al abrir un proyecto ni en las pruebas sintéticas.

## Formato, referencias y compatibilidad

- Solo los dos perfiles US/UM acreditados de fase 1. Se comprueban por separado la identidad NCCH y el ejecutable completo. Una revisión o parche de código desconocido se rechaza.
- Archivo gráfico: `RomFS/a/0/6/1`. En las fuentes US y UM examinadas tiene 769 registros planos GARC v6 y SHA-256 `f198b27133255fb00d2398cfb71eeccb0bdcbd0635f5dd825b0285cc2a5a07f3`. La igualdad se comprobó en ambos títulos; no se extrapola a otras revisiones. No se exige esa huella global al usuario: se conservan cambios en los 769 registros anteriores si el contenedor mantiene la estructura admitida.
- Tabla de objeto → imagen: 960 palabras de 32 bits en `code.bin`, offset de archivo `0x4BC8BC` en ambos perfiles examinados. Huella de la tabla original: `333070d6c0d6c7f4b3ea56a7abdc181358659043d16ad304bc4e6779b762428e`. Los dos accesos nativos identificados cargan esta tabla y usan el archivo gráfico 61.
- La mayoría de las mentas apuntaban a la imagen compartida 768 (`?`); el objeto 622 apuntaba a la imagen 492. **No se sobrescribe ninguna de esas imágenes**: pueden tener otros usuarios.
- Se agregan exactamente 21 imágenes, índices **769–789**, en el orden del catálogo de mentas de fase 1. Solo cambian las 21 palabras de la tabla correspondientes a esos objetos. Los demás bytes del ejecutable y los 769 registros gráficos anteriores permanecen intactos.
- Las nuevas imágenes conservan el perfil gráfico USUM: BFLIM 32×32, RGB5A1, orientación Rotate90, disposición de píxeles de 3DS y LZ11. El contenido visible se ajusta proporcionalmente a 24×24, centrado, conservando transparencia. La reducción usa promedios de área con alfa premultiplicado y aritmética entera; la cuantización a cinco bits por color y un bit de alfa es una limitación del formato. No se cambia el formato ni se añade un runtime gráfico.

La detección de fase 1 admite el ejecutable actualizado únicamente si la normalización de **esas 21 referencias exactas** devuelve una de sus dos huellas completas originales. No se flexibiliza la aceptación de otros cambios en `code.bin`. La detección visual comprueba además las 21 texturas por SHA-256, sin depender de la caché ni del historial.

Un GARC con entradas adicionales ajenas, referencias parciales, una textura de menta modificada o un contenedor desconocido bloquea la instalación; no se eliminan ni se adoptan esos cambios. Las modificaciones de otros iconos dentro de los 769 registros existentes se preservan sin recodificarlas.

## Actualizar un proyecto de fase 1

1. Abre una copia extraída con la fase 1 instalada y reconocida. Las tiendas y sus precios pueden estar ya editados.
2. En **Mecánicas y CRO → Mentas Gen7 — Ultra Sol / Ultra Luna**, pulsa **Comprobar estado y preparar**.
3. Pulsa **Descargar y preparar iconos (PokéAPI)**. Se descargan, verifican y convierten los 21 gráficos automáticamente. Todavía no se escribe en RomFS ni ExeFS.
4. Revisa la propuesta y pulsa **Instalar iconos preparados**. Se modifican únicamente `ExeFS/code.bin` (o `.code.bin`) y `RomFS/a/0/6/1` en una transacción del historial. No es necesario reinstalar las mentas, recrear tiendas ni insertar imágenes manualmente.
5. Vuelve a comprobar el estado o reabre el proyecto: debe mostrar **21 iconos reconocidos**. Una segunda instalación no agrega otras 21 entradas.
6. Reconstruye por el flujo existente. Los cambios de ExeFS se incluyen en la reconstrucción y su compresión; no se modifica una ROM original ni una partida. No hay cambios CRO/CRR nuevos en esta actualización.

## Historial y deshacer

La acción **Instalar iconos de mentas Gen7** registra los dos archivos juntos y bloquea los cambios si las fuentes o dependencias cambiaron desde la propuesta. Reutiliza las transacciones, respaldos y recuperación existentes. No hay un escritor alternativo.

Deshacer esa acción restaura ambos archivos byte a byte y deja instalada la fase 1 funcional, con sus tiendas y precios. Si después se modificaron esos archivos, se conservan las comprobaciones de seguridad del historial. Deshaz primero la actualización de iconos antes de intentar retirar la fase 1. No se promete recuperación universal ante apagones ni restauración de cambios desconocidos.

## Prueba visual en el emulador

Usa una copia del proyecto y una partida respaldada. Si las tiendas ya contienen mentas, conserva su configuración; el stock visible depende del progreso del juego.

1. Instala los iconos y genera una ROM/modificación nueva. Carga esa salida en el emulador, con la salida de fase 1 anterior retirada para evitar que otro mod la tape.
2. Abre la tienda en la que aparecían las mentas. En los puntos de la interfaz que muestran gráficos, deben aparecer hojas de menta en lugar de `?`. Algunas tiendas no muestran un gráfico en todas sus pantallas.
3. Comprueba en el inventario las 21 mentas disponibles. Compara especialmente Firme, Osada, Modesta y Seria para distinguir colores. Varias naturalezas comparten el mismo gráfico de color por diseño de la fuente.
4. Revisa un objeto ajeno, por ejemplo Carburante, y confirma que conserva su imagen. Comprueba que la tienda sigue ofreciendo los mismos objetos y precios.
5. Cierra y vuelve a abrir el juego para comprobar la carga visual; reabre también Randomizer para comprobar la detección. Anota título, perfil, idioma y pantallas observadas.

La prueba visual de fase 1.1 ya tiene confirmación expresa del propietario en Ultra Sol: iconos correctos, mentas funcionales y gráficos anteriores conservados. El procedimiento anterior se conserva como referencia reproducible; no se declara que cada paso o cada caso adicional haya sido probado. La validación manual de Ultra Luna sigue pendiente; sus pruebas automatizadas no la sustituyen.

## Verificación automatizada

`--usum-mint-icons-only` usa imágenes, código, textos y archivos sintéticos: mapa de 21 mentas, orientación/alfa, conversión, compresión, adquisición/caché con HTTP simulado, perfiles US/UM, rechazo de estados desconocidos, preservación de recursos, transacción con fallo inyectado, historial, reapertura y deshacer.

`--usum-mint-icons-real-copies <salida nueva> <Ultra Sol fase 1> <Ultra Luna fase 1>` usa material privado y la descarga real. Crea proyectos nuevos, instala/reabre/deshace/reinstala, comprueba la conservación de tiendas y otros archivos y guarda evidencia y una lámina de iconos fuera de RomFS. No modifica las fuentes ni distribuye ese material. El CI solo ejecuta la suite sintética.

### Resultados de esta entrega

- Build de App y runner: cero errores y advertencias.
- 88 comprobaciones específicas de iconos; 89 de regresión de fase 1; 6 de interfaz, aprobadas. Las pruebas sintéticas no necesitan acceso a PokéAPI ni datos comerciales.
- Copias privadas completas de Ultra Sol y Ultra Luna: descarga y conversión reales, instalación, detección al reabrir, deshacer exacto de los dos archivos y reinstalación. Las tiendas con sus 21 mentas y los demás archivos se conservan; las fuentes no se modificaron.
- Una reconstrucción Full3DS de Ultra Sol y reextracción: los 750 archivos coinciden byte a byte y ambos parches siguen reconocidos. No se extrapola esta comprobación a una reconstrucción probada de Ultra Luna.
- Se revisó una lámina privada de las 21 texturas decodificadas: seis variantes de color, alfa y orientación coherentes. La revisión de esta lámina se distingue de la prueba manual del propietario en Ultra Sol, ya satisfactoria. No se distribuye esa lámina.

La regresión general del código funcional se aprobó en el CI del PR #52 para `d680db6ecb491ad28db8279f864b720bae88af7c` (ejecución `42178671328`). No se repitió localmente para este cierre documental; los checks del SHA definitivo se registran en el PR.
