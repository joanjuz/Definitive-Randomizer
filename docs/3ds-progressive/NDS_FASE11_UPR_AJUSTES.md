# NDS fase 11: más herramientas de UPR-ZX y ajustes del juego — 2026-10-31

Segunda tanda de herramientas al estilo de Universal Pokémon Randomizer ZX en el espacio de trabajo DS (Gen4 y Gen5) y los ajustes de código de Gen4.

## Entrenadores y salvajes

- **Entrenadores**:
  - un tipo por entrenador: el equipo nuevo se elige entre especies de ese tipo, respetando el modo de especie;
  - forzar la evolución final desde un nivel (rama aleatoria si hay varias);
  - niveles en % (de −50 a +100, siempre entre 1 y 100).

  Un cuarto modo, «Sin cambios», aplica solo niveles y evoluciones.
- **Salvajes**:
  - mapeo global: la misma especie original se cambia por la misma especie nueva en todo el juego, no solo en su zona;
  - niveles en %.
- Los niveles y las evoluciones forzadas se aplican como ediciones de celdas sobre las tablas ya randomizadas, igual en las dos generaciones.

## Estáticos, regalos e intercambios

- **Estáticos y regalos**: las listas de UPR-ZX (`gen4_offsets.ini`/`gen5_offsets.ini`), copiadas en `NdsStaticsData.cs` con su crédito. Hay 31 en Negro/Blanco, 41 en Blanco 2, 17 en Diamante, 20 en Platino y 38 en HeartGold.
  - Cada estático escribe su especie en todas sus posiciones de script y el nivel en la primera, como UPR-ZX.
  - Un estático cuyas posiciones no tienen todas la misma especie se descarta al leer. Así una lista que no corresponda al juego (Blanco y Blanco 2 usan las de su pareja) no puede escribir donde no debe. En los cinco volcados todos coinciden.
  - Randomización: aleatoria, BST similar o legendarios por legendarios.
- **Intercambios**: especie entregada, especie pedida y objeto (NARC de intercambios, con su CRC32). Se randomizan la entregada y la pedida. Los apodos y el texto de los intercambios no cambian.
- **Evoluciones**, dos opciones más:
  - quitar las evoluciones por hora del día: amistad de día o de noche → amistad; objeto de día o de noche → usar objeto;
  - nivel máximo de evolución.

## Ajustes del juego (Gen4)

Página «Ajustes del juego» en SISTEMAS. Se aplica como una operación más y se exporta con el resto.
- **Texto instantáneo**: IPS de UPR-ZX sobre el ARM9 (17 bytes).
- **Pokédex Nacional desde el inicio**: IPS sobre su script.
- **Correr sin las Deportivas**: se anula el salto que comprueba la medalla; se localiza por un prefijo único.
- **Mundo Distorsión más rápido**: IPS sobre su script, solo en Platino.

Los parches IPS están en `pk3DS.Core/NDS/Patches` (GPLv3, marcados como binarios en `.gitattributes`). Algunos esperan bytes vacíos añadidos al final, como hace UPR-ZX: 4 en el Pokédex de HeartGold y 12 en el Mundo Distorsión.

Solo se aplican sobre código original: el ARM9 que verifica el perfil (no se ofrecen si un romhack lo cambió) y scripts que siguen siendo los originales, por eso un ajuste de script no se aplica dos veces.

En **Negro, Blanco y Blanco 2** la página explica por qué no hay ajustes: el texto rápido modifica el ARM9, protegido por las firmas DSi, y el Pokédex Nacional comparte script con el parche de los iniciales.

## Validación

- `--nds-only` (sintético, CI): 142. Incluye:
  - intercambios;
  - entrenadores temáticos con niveles subidos;
  - evolución forzada que solo cambia especies;
  - mapeo global con niveles bajados;
  - nivel máximo de evolución;
  - el aplicador IPS (registros normales y repetidos, rechazo fuera del archivo y de otros formatos).
- `--nds-data-probe` sobre los cinco juegos, apilando todas las randomizaciones, ediciones y ajustes y exportando una vez:
  - estáticos e intercambios leídos;
  - los legendarios se cambian por legendarios;
  - ni evoluciones por hora ni niveles de evolución por encima de 40;
  - en Gen4, los ajustes cambian 17 bytes del ARM9 y sus scripts, y el ajuste de script no se repite;
  - la ROM exportada conserva el ARM9 ajustado y se relee igual en las nueve secciones.
- Regresión completa (ver el PR).

## Prueba en juego

**Confirmada por el propietario el 2026-10-31**: las ROM de prueba de la fase 14, que acumulan las fases 9 a 14, funcionan correctamente en el juego.
