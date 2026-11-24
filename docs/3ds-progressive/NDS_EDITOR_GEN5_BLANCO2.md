# Editor DS Gen5: Pokémon Blanco 2 (IRDO/0)

El editor de mapas y eventos de Nintendo DS se abre con **Pokémon Blanco 2 USA/EUR, revisión 0 (IRDO/0)**. Usa el lector Gen5 de Negro y Blanco con un perfil propio, `Gen5MapProfile.White2`. Blanco 2 tiene los mismos formatos de cabecera, matriz, bloque y evento, pero sus tablas son otras: rutas, tamaños, CRC y huella propios.

Negro 2 no se habilita: no hay volcado identificado.

## Verificación en la copia real

Volcado del propietario: SHA-1 `b5d7490b…`, título `POKEMON W2`, DSi-enhanced, textos en inglés.

| Recurso | Blanco 2 IRDO/0 | Negro/Blanco |
|---|---|---|
| Cabeceras (`a/0/1/2`, 48 bytes) | **615** (`0xC6B5EB86`) | 427 |
| Eventos | **`a/1/2/6`**, 616 archivos (`0xFE1792B9`) | `a/1/2/5`, 428 |
| Scripts | **`a/0/5/6`**, 1.289 (`0xEA1E0890`) | `a/0/5/7`, 899 |
| Matrices (`a/0/0/9`) | 416, lado máximo 29, 2 con cabeceras (id máximo 475) | 255 |
| Bloques (`a/0/0/8`) | 1.065: WB 961, GC 60, NG 36, RD 8 | 649 |
| Texturas del terreno (`a/0/1/4`) | 409 | 282 |
| Nombres de lugar | texto **109** | 89 |

**Censo:**
- 615 mapas, 3.146 NPC (410 Poké Ball) y 1.089 warps;
- 1.084 warps resuelven a un warp existente con el campo +22; los 5 restantes tienen destino 65535 y se marcan como especiales;
- los 1.025 warps de casilla están en el centro de su casilla;
- 191 registros de raíl;
- 607 de los 627 NPC «mirar fijo» tienen la dirección de su movimiento.

**Colisión.** El mismo patrón que en Negro: el bit 0 del byte 6 está en el 75 % de todas las casillas y en el 4 % de las casillas con un NPC de casilla. Se conocen 1.020 de los 1.065 bloques.

**Diferencias de eventos frente a Negro y Blanco.**
- Hay objetos de fondo de tipo 4 sin script, muy frecuentes en ciudades y rutas. Se muestran como objetos y se pueden mover igual que los demás.
- Aparecen más tipos de entrenador (2, 4, 5, 6, 7, 10, 13, 20). Su visión no se edita, como en todo Gen5.
- El formato de los registros es el mismo.

## El límite de 593 mapas

`PlatinumMapLayout.ParseMatrix` rechazaba cualquier identificador de mapa mayor o igual que 593: el número de cabeceras de Platino, fijado como constante en la fase 1. No es un límite del formato ni de memoria. El identificador solo se compara con las cabeceras guardadas en la matriz (para la máscara de propiedad); no dimensiona nada. Todos los juegos de Gen4 tienen menos cabeceras.

En Blanco 2, 22 mapas tienen un identificador de 593 o más (hasta el 614). Por ejemplo, el mapa 600 es un interior de Pueblo Floresta.

**Corrección:** una sobrecarga `ParseMatrix(…, headerCount)` comprueba el identificador contra el número de cabeceras del juego, entre 1 y 65.535. Las sobrecargas que usa Gen4 conservan el límite de Platino, así que su comportamiento no cambia. El lector de Gen5 pasa el número real de cabeceras de su tabla. Las cabeceras guardadas en las matrices siguen siendo menores de 0xFFFF.

## Qué está disponible

Lo mismo que en Negro y Blanco:
- mover y girar NPC de casilla y orientar entrenadores;
- mover objetos de fondo, cambiar warps a destinos existentes, mover y redimensionar activadores;
- checkpoints (`IRDO`/`0`), proyecto, exportación y restauración exacta.

Siguen deshabilitados, con su motivo:
- la visión y la dirección de interacción;
- los registros de raíl y los warps especiales;
- las Poké Ball;
- NPC nuevos, scripts y enfermeras.

## Comprobaciones

- `--nds-gen5-white2-only`: 16 comprobaciones sintéticas, sobre un fixture de 610 cabeceras. Cubren el perfil, el mapa 605 y su matriz, que las sobrecargas de Gen4 mantienen el límite, los límites de la sobrecarga nueva, una edición con checkpoint, reapertura, exportación y restauración en un mapa por encima de 593, que los checkpoints no se cruzan y el detector (incluido el rechazo de Negro 2).
- `--nds-gen5-white2-ui-only`: 3 comprobaciones fuera de pantalla (título, los 610 mapas en la lista, el mapa 605 en el lienzo).
- Las dos están en el CI.
- `--nds-gen5-white2-probe` sobre la copia real:
  - censo y apertura de las 615 matrices; terreno y gráficos;
  - cuatro ediciones jugables en Ciudad Engobe y la Ruta 20;
  - un activador y un NPC del mapa 593, editados y restaurados;
  - se rechaza mover Poké Ball y crear NPC;
  - guardar, reabrir, exportar y reabrir, con el ARM9 y el resto de archivos idénticos;
  - restauración con el mismo SHA-256.
- Galería: 7 mapas (Ciudad Engobe y dos casas, Ruta 20, Pueblo Floresta, el mapa 600 y el 614), con 0 triángulos sin textura.

**Validación jugable pendiente.**

## Limitaciones

Las mismas que en Negro: estructuras y mobiliario sin dibujar (los interiores muestran solo el suelo y las paredes del modelo de terreno), estaciones sin aplicar, colisión inferida, raíles, visión y dirección de interacción sin interpretar.
