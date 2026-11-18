# Editor DS · Lectores NSBMD/NSBTX y render cenital de mapas (Platino CPUE/1)

Primer incremento de la prioridad «renderizado real de mapas». Añade lectores de solo lectura de los formatos gráficos de la DS y un rasterizador cenital por software, los valida con los mapas de Platino CPUE/1 y los conecta al editor como capa opcional **Modelos/texturas**, desactivada por defecto. La vista esquemática sigue siendo la predeterminada.

## Cadena de recursos (verificada con datos reales)

Fuente del formato: `pret/pokeplatinum` (`docs/maps/file_format_specifications.md`, `overlay005/area_data.c`), contrastada con el volcado del propietario. No se incluye código del proyecto citado.

- Cabecera de mapa `+0` → área (`fielddata/areadata/area_data.narc`): índice de props, de texturas de terreno y de luz.
- `area_build_model/area_build.narc` → lista de modelos de `build_model.narc` que carga el área; `areabm_texset.narc` → sus texturas (NSBTX).
- `area_map_tex/map_tex_set.narc` → texturas del terreno (NSBTX).
- `land_data.narc` → atributos, props, **modelo de terreno (NSBMD)** y BDHC de cada bloque de la matriz.

Los cinco archivos nuevos se fijan por CRC32, igual que `land_data`. Un archivo distinto se rechaza.

## Componentes

| Componente | Qué hace |
| --- | --- |
| `NitroFile` | Contenedor Nitro (`BTX0`, `BMD0`): cabecera, marca de orden de bytes y bloques. Comprueba los límites de cada desplazamiento |
| `NitroTextureSet` | Bloque TEX0: diccionarios de texturas y paletas, y decodificación a ARGB32 de los siete formatos (A3I5, 4/16/256 colores, 4×4 comprimido con sus cuatro modos, A5I3, directo), con transparencia del color 0 |
| `NitroModels` | Bloque MDL0: nodos (traslación, rotación y escala), materiales con su textura y paleta por nombre, código de dibujo SBC (nodos, pila de matrices, materiales, formas) y listas de comandos GX (vértices de 16 y 10 bits, XY/XZ/YZ, diferencia, coordenadas de textura, colores, triángulos, cuadriláteros y tiras) |
| `NitroTopDownRenderer` | Rasterizador ortográfico desde arriba: la superficie más alta queda visible, muestreo de textura con repetición o espejo, texeles transparentes y color de vértice. Sin iluminación, niebla ni animación |
| `PlatinumMapAssets` | Resuelve cabecera → área → texturas y modelos, extrae el NSBMD de cada bloque y compone la escena de un bloque (terreno y props con posición, rotación y escala). Un prop cuyo modelo no carga el área no se dibuja y se informa |

Nada se guarda en el repositorio, en el proyecto ni en la configuración: los modelos y las texturas se leen de la ROM del usuario en cada uso.

## En el editor

En **Nintendo DS → Eventos**, la casilla **Modelos/texturas** de la barra de capas dibuja, debajo de los marcadores, cada bloque visible de la matriz con su terreno y sus edificios (16 px por casilla, 512×512 por bloque). Los bloques se renderizan en segundo plano la primera vez y se guardan en memoria (48 como máximo); el lienzo se redibuja cuando cada bloque está listo. Al activarla, el aviso del mapa explica qué no representa la vista gráfica. Si los archivos de la ROM no son los acreditados, la casilla queda deshabilitada y su ayuda dice por qué. Seleccionar, mover, girar y el resto de herramientas funcionan igual con la capa activa.

## Resultados con la copia real CPUE/1

`--nds-map-render-probe <carpeta nueva> <ROM>` (solo lectura; la ROM no cambia):

- 145 archivos de texturas: 5486 texturas decodificadas sin error (A3I5 259, 4 colores 1484, 16 colores 4306, 256 colores 48, A5I3 96).
- 666 de 666 modelos de terreno (599 389 triángulos, unos 0,5 s) y 590 de 590 modelos de edificios (50 935 triángulos).
- En el editor (`--nds-map-render-ui-probe`, fuera de pantalla): Hojaverde y sus bloques vecinos (10 bloques) se renderizan en unos 1,2 s. Los warps quedan sobre las puertas de las casas.
- Pueblo Hojaverde (cabecera 411, área 6, bloque `land_data` 0): los 19 materiales encuentran su textura y su paleta. El render cenital del bloque con sus 8 props (8 px por casilla, 256×256) tarda unos 35 ms y muestra las cuatro casas, el camino, el lago, el cinturón de árboles, las flores y las vallas en sus posiciones. Imágenes privadas en la carpeta de salida; no se distribuyen.

## Consolidación en mapas complejos (incremento posterior)

Galería sobre la copia real (`--nds-map-render-gallery-probe`): ciudades, interiores, terrazas, cuevas y rutas. Para cada mapa se mide la alineación como el desplazamiento de ±2 casillas que mejor hace coincidir los píxeles de los edificios con las colisiones de `land_data`. En las ciudades, más del 80 % de los píxeles de edificios coincide con casillas bloqueadas y solo el 1–8 % queda sobre casillas libres. Donde el óptimo no es (0,0), la diferencia es de 0 a 5 puntos (escalones, aleros y toldos que sobresalen). Los warps de las puertas coinciden con las puertas dibujadas. Ningún mapa tiene triángulos sin textura ni props omitidos.

| Mapa | Bloques | Triángulos | Tiempo | Coincidencia en (0,0) |
| --- | --- | --- | --- | --- |
| Pueblo Hojaverde (T01) | 1 | 1758 | 97 ms | 93 % |
| Ciudad Jubileo (C01) | 4 | 5690 | 229 ms | 95 % |
| Ciudad Pirita (C03) | 3 | 4988 | 143 ms | 83 % |
| Ciudad Corazón (C05) | 4 | 6036 | 125 ms | 92 % |
| Ciudad Rocavelo (C07) | 4 | 7012 | 88 ms | 92 % |
| Ciudad Puntaneva (C09) | 2 | 2782 | 54 ms | 90 % |
| Interiores (casa, Centro Pokémon, gimnasio, etc.) | 1 | 386–916 | 2–6 ms | — |
| Cueva y monte (D05R0101, D16) | 1 | 2098–2289 | 16–18 ms | — / 89 % |
| Rutas 209 y 210 | 3 | 3966–5095 | 58–65 ms | 76–80 % |

Defectos demostrados y corregidos:

- **Matriz completa en bucle.** Con «Ver matriz completa» en el exterior, la caché de 48 imágenes de 512 px expulsaba bloques visibles y los volvía a renderizar sin fin: 526 renders en 40 s, siempre ocupada. Ahora cada bloque se renderiza al tamaño que ocupa en pantalla (potencia de dos de 32 a 512 px), la caché se limita por memoria (96 MB) y un único trabajador atiende primero lo más reciente. Mientras llega una imagen más nítida se muestra la disponible. Resultado: los 256 bloques distintos del exterior en unos 3,5 s y unos 7 MB.
- **Celdas sin dueño sin texturas.** En el exterior, 299 celdas tienen la cabecera 0 («Mystery Zone»). Dibujadas con el área de esa cabecera, 64 064 de sus 103 596 triángulos quedaban sin textura (cuadros planos). El juego las dibuja con el área del mapa en el que está el jugador, así que ahora usan la del propietario más cercano. En los empates se elige el primero cuyo área contiene todas las texturas del bloque. Ahora quedan 0 triángulos sin textura.
- **Ruido a escala pequeña.** Los bloques lejanos se renderizan a 2× o 4× y se promedian ponderando la opacidad, en lugar de tomar un solo texel por píxel.

## Limitaciones conocidas

- Vista cenital ortográfica, no la cámara inclinada del juego. No hay iluminación, niebla, marcas de borde, animaciones de textura ni de props.
- Las rotaciones en forma de pivote de los nodos y el signo de la rotación de los props son una interpretación. Los mapas comprobados no usan rotación de props.
- La mezcla de matrices (`NODEMIX`, para modelos con piel) usa la primera matriz; la geometría estática de los mapas no la necesita.
- Solo Platino CPUE/1.
- Las celdas de una matriz se dibujan con el área de su propia cabecera; las celdas sin cabecera almacenada usan la del mapa abierto.

## Pruebas

| Nivel | Opción | Resultado |
| --- | --- | --- |
| Sintética (archivos Nitro mínimos construidos en la prueba) | `--nds-map-render-only` (en el CI) | 44 PASS |
| WPF fuera de pantalla | `--nds-map-render-ui-only` (en el CI) | 12 PASS |
| Copia real CPUE/1 | `--nds-map-render-probe`, `--nds-map-render-ui-probe` (Hojaverde, matriz completa, celdas sin dueño) y `--nds-map-render-gallery-probe` | PASS |
