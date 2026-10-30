# NDS fase 3: overlays e iniciales Gen5 (Negro y Blanco ES) — 2026-10-30

Cambio de los tres iniciales en todos los sitios donde el juego los usa. Requirió leer y escribir overlays y escribir textos Gen5.

## Piezas nuevas

- **Overlays (`NdsRom`)**: lectura de la tabla de overlays ARM9 y descompresión BLZ con `BLZCoder.Decompress`, un envoltorio público nuevo sobre el decodificador existente (sin cambiar el algoritmo ni los métodos que usa 3DS). Un overlay modificado **se guarda sin comprimir**: se escribe su archivo y en su entrada de la tabla se actualiza el tamaño y se borra la bandera de compresión. Así no se usa el compresor BLZ que comparte ExeFS de 3DS. Se rechaza un reemplazo con otro tamaño descomprimido.
- **Escritura de texto (`Gen5Text.Replace`)**: copia sin tocar las líneas no modificadas y cifra las cambiadas con su clave original; conserva el campo de cada línea, el relleno a 4 bytes y una segunda sección. Sin cambios devuelve los mismos bytes.
- **Perfil**: textos de historia, gráficos de Pokémon, gráficos de la pantalla de elección y overlay 223, cada uno con su CRC32 (el del overlay, sobre los datos descomprimidos, como UPR-ZX).

## Qué cambia al elegir iniciales

Modos: sin cambios (por defecto en la ventana), aleatorios (tres especies distintas, reproducibles por semilla) y personalizados (posiciones Planta, Fuego y Agua).

1. **Scripts**: la especie en las seis posiciones de cada inicial.
2. **Script del Pokédex (archivo 792)**: el mismo parche que UPR-ZX aplica desde 2014. Se buscó su motivo: la llamada de 6 bytes desplaza `24 00 A7 02` y parte de `E7 00 00 00`; el bloque añadido repite ambas instrucciones, añade dos comandos (`DE 00 00 00`, `F8 01`) y vuelve (`05 00`). Solo se aplica si la firma, incluida la instrucción desplazada, aparece una sola vez. El efecto concreto de los dos comandos no está documentado; la prueba en juego confirmó que la escena del Pokédex funciona con el parche.
3. **Pantalla de elección**: paleta y sprite frontal (descomprimido de LZ11) de cada especie desde los gráficos de Pokémon, igual que UPR-ZX. Se exige cabecera `RLCN`/`RGCN` y el mismo tamaño que el hueco.
4. **Gritos**: la tabla tras el prefijo `08 0A 07 00 08 00 00` del overlay 223 (aparece una sola vez y debe contener los iniciales originales).
5. **Texto de elección**: los nombres ya salen de una variable del juego; solo se sustituye el tipo escrito en cada línea, en el idioma del juego («Grass» → «Electric», «Planta» → «Eléctrico»). Las frases de Cheren no se tocan (no nombran tipos; UPR-ZX las reescribe en inglés).

Cualquier comprobación que falle rechaza los iniciales sin escribir nada.

## Hallazgos

- Los sprites de la pantalla de elección **no son idénticos byte a byte** a los de combate: mismo tamaño (4.656 bytes) y cabecera `RGCN`, datos distintos desde el byte 9. Una primera comprobación que exigía igualdad falló y se sustituyó por la de formato y tamaño. La prueba en juego confirmó que se ven correctamente.
- Al reescribir cada línea de texto consigo misma, ≈50 % de las 103.364 líneas (Negro y Blanco ES) dan bytes idénticos y el resto difiere solo porque estaban comprimidas; al releer, **todas** las líneas conservan su texto.

## Validación

- `--nds-only` (sintético, CI): 36. Tabla de overlays, reemplazo sin comprimir y rechazo de otro tamaño; escritura de texto idéntica sin cambios y sustitución de una línea; rechazo de iniciales repetidos, fuera de 1–649 o sin especies.
- `--nds-gen5-starters-probe` sobre Negro y Blanco ES: 20/20. Formato de sprites, solo cambian scripts, gráficos de elección, texto de historia y overlay 223; scripts con los nuevos iniciales; gritos en overlay sin comprimir de 13.696 bytes; sprites y paletas nuevos; tipo «Electric»/«Eléctrico» en el texto; llamada del script al bloque añadido; resto de archivos idénticos; aleatorios distintos y reproducibles; originales sin cambios.
- `--nds-ui-only`: 7; `--nds-ui-probe` con Blanco ES: 8 (iniciales personalizados parten de los originales y llegan a la vista previa).
- ROM de prueba para el juego: `D:\Nintendo\DS\Juegos\Pruebas\Blanco-ES-iniciales-prueba.nds` (solo iniciales: Pikachu, Charmander y Squirtle).

## Prueba en juego

Confirmada por el propietario el 30/10/2026 en DeSmuME (modo DS) con la ROM de prueba de Blanco ES (Pikachu, Charmander y Squirtle): pantalla de elección con imágenes, gritos y tipo correctos, entrega del inicial, escena del Pokédex y primer combate. Con esto, el parche del script del Pokédex y el uso de los sprites de combate en la pantalla de elección quedan confirmados para Blanco ES; Negro (USA/EUR) tiene la validación automática equivalente. No se declara una partida completa ni compatibilidad en modo DSi o consola.
