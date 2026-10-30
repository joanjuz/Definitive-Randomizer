# NDS fase 2a: randomizer Gen5 de entrenadores y encuentros — 2026-10-30

Primer randomizer de DS, para Pokémon Negro (USA/EUR) y Blanco (España). Motor y exportación; la interfaz llega en la fase 2b. Código en `pk3DS.Core/NDS/Gen5Randomizer.cs`.

## Opciones

- Modos de especie, por separado para entrenadores y encuentros: **mantener**, **aleatorio**, **BST similar** (±10 %, ampliado hasta tener al menos cinco candidatas) y **BST progresivo por nivel**.
- Rangos progresivos por defecto: nv. 1–10 → BST 180–320, 11–20 → 220–380, 21–30 → 280–450, 31–40 → 340–520, 41–50 → 400–580, 51–100 → 480–680. Son los mismos valores que en Espada/Escudo como decisión de diseño, en un tipo propio de DS (no se comparten datos entre plataformas). Se rechazan rangos inválidos y niveles sin rango.
- Especies 1–649. Cada elección depende solo de la semilla y del hueco: la misma semilla da el mismo resultado.

## Qué cambia y qué se conserva

- **Entrenadores:** cambia la especie y la forma pasa a 0 (la forma original pertenece a la especie anterior). Se conservan nivel, objeto, dificultad, habilidad, género, clase, nombre, número de Pokémon y tipo de combate. Si el entrenador tenía movimientos personalizados, se quitan (bandera y datos) y el juego usa los movimientos por nivel de la nueva especie, igual que UPR-ZX.
- **Encuentros:** en cada área (archivo, estación y tipo), todos los huecos con la misma especie original reciben la misma especie nueva, así se mantiene el reparto de probabilidades. Se conservan tasas, niveles y huecos vacíos.
- **Exportación:** `Gen5Data.Export` escribe una ROM nueva con el escritor de la fase 0; el original no se modifica. Solo se exporta desde una ROM cuyas tablas coinciden con el perfil (CRC32). Las ROM exportadas pueden reabrirse en modo de solo verificación (`requireOriginalTables:false`), nunca para volver a randomizar.
- **Iniciales:** fuera de esta fase. Cambiar la especie en los scripts es sencillo, pero la pantalla de elección seguiría mostrando los gráficos y gritos originales, que están en overlays y archivos gráficos.

## Validación

- `--nds-only` (sintético, en el CI): 28 comprobaciones, 9 nuevas sobre una ROM sintética con las rutas de Negro: sin cambios no genera archivos, determinismo por semilla, eliminación de movimientos conservando objetos, niveles y formas, entrenador sin movimientos intacto, coherencia por área en encuentros con tasas y niveles, rango progresivo, rechazo de rangos inválidos y de exportar sin tablas verificadas.
- `--nds-gen5-random-probe <salida> <rom...>` sobre los originales de Negro y Blanco ES (BST progresivo en entrenadores, BST similar en encuentros): 14/14.
  - Entrenadores: 1.303 de 1.304 Pokémon cambian; todos dentro del rango de su nivel; mismos niveles, objetos, tipo de combate, nombres y tamaños de equipo.
  - Encuentros: 4.001 huecos cambian; mismas tasas, niveles y huecos vacíos.
  - Nombres e iniciales idénticos; solo difieren los dos NARC randomizados; misma semilla, mismo resultado; originales sin cambios.

## Pendiente

- Prueba en juego (emulador) de una ROM exportada.
- Fase 2b: sección DS en la aplicación para abrir la ROM, elegir opciones, previsualizar cambios y exportar.
- Después: overlays (BLZ), iniciales, objetos y tiendas, movimientos, Blanco 2/Negro 2 y Gen4.
