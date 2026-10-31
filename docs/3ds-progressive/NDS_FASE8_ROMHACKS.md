# NDS fase 8: perfil de romhacks — 2026-10-31

Modo **romhack** para ROM modificadas de los juegos admitidos (Diamante, Platino, HeartGold, Negro, Blanco ES, Blanco 2). Es un perfil aparte de los originales: el modo original no cambia y sigue rechazando cualquier tabla que no coincida con su CRC32.

## Cómo funciona

- Ventana DS: casilla **«Admitir romhacks (tablas modificadas; beta)»**. Con ella marcada, una ROM que no pasa la verificación estricta se abre como romhack; un volcado original se sigue abriendo como original. La identidad muestra «romhack (tablas modificadas)» y una línea por cada opción no disponible con su motivo; el registro de cambios lo indica también.
- `Gen5Data.OpenRomhack` y `Gen4Data.OpenRomhack`:
  - **no exigen el CRC32 de las tablas**, pero sí su estructura: que cada NARC se lea, nombres y estadísticas de todas las especies, nombres de objetos, entrenadores (cabeceras y equipos coherentes), encuentros y objetos del mapa;
  - permiten exportar.
- **El código del juego solo se parchea si es el original.** Cada opción que toca un overlay o el ARM9 se activa solo si ese binario conserva su CRC32 original; si no, queda en `Unavailable` con el motivo, la ventana la desactiva y el motor la rechaza (`ArgumentException`).
  - Gen5: los iniciales necesitan el overlay de gritos original y además una prueba en seco (los scripts tienen que dar los iniciales en todas sus posiciones). En Negro y Blanco las tiendas necesitan el overlay 21 original; en Blanco 2 basta con que el NARC de tiendas tenga la estructura esperada.
  - Gen4: los iniciales necesitan el overlay de iniciales original, el ARM9 en HeartGold y una prueba en seco. Las tiendas y los objetos ocultos necesitan el ARM9 original; sin él se barajan solo los objetos visibles.
- Los romhacks pueden añadir ramas del rival: Renegade Platinum tiene un combate más. En modo romhack se admiten **al menos** las ramas esperadas, y cada una se comprueba igualmente contra los iniciales originales. En los originales se sigue exigiendo el número exacto.
- Encuentros Gen4 recortados: Sacred Gold acorta 130 archivos a 192 bytes y Renegade uno a 380. Fuera del modo original se admiten archivos más cortos y solo se leen y escriben las tablas que caben enteras; el modo original sigue exigiendo el tamaño exacto.
- ARM9: solo se considera comprimido si, además del campo de los parámetros del módulo, tiene un pie BLZ plausible (como UPR-ZX). Renegade Platinum y Sacred Gold dejaban el campo puesto en un ARM9 que no está comprimido.

## Romhacks probados (copias aisladas; los archivos de origen no cambian)

| ROM | Código | No disponible | Cambios (todas las opciones posibles) |
|---|---|---|---|
| Renegade Platinum ESP | CPUE | tiendas y objetos ocultos (ARM9 modificado) | 9890, con iniciales |
| Sacred Gold ESP | IPKE | iniciales, tiendas y objetos ocultos (ARM9 modificado) | 8429 |
| Volt White 2 ESP | IRDO | iniciales (scripts modificados) | 8766 |
| Blanco ReNivel | IRAS | iniciales y tiendas (overlays modificados) | 7554 |

## Validación

- `--nds-only` (sintético, CI): 85. Romhack Gen5 sin overlays: se abre, lista iniciales y tiendas como no disponibles, rechaza pedirlos y exporta. Romhack HeartGold con un archivo de encuentros recortado: lo lee, lista lo no disponible y exporta sin tocar el ARM9. El modo estricto sigue rechazando ambos y ARM9 sintético con compresión realista.
- `--nds-ui-only`: 9. Con la casilla marcada, el romhack sintético se abre con iniciales y tiendas desactivados, y una ROM que no se puede leer se sigue rechazando.
- `--nds-romhack-probe` (nueva, ROM reales): 24/24 en los cuatro romhacks. Cada uno:
  - es rechazado como original y se abre como romhack;
  - se randomiza con las opciones posibles;
  - no se toca el código modificado;
  - la exportación se relee y conserva el resto de archivos;
  - el origen no cambia.

## Pendiente

- Prueba en juego de un romhack exportado, por ejemplo Renegade Platinum con iniciales o Volt White 2.
- Los romhacks de otros juegos (Perla, SoulSilver, Negro 2, otras regiones) no se admiten: necesitarían el perfil de su juego original.
