# Limitaciones que declara la versión estable

Lista pública de lo que la primera versión estable WPF **no** hace o no garantiza. Debe leerse junto con la [matriz de compatibilidad](COMPATIBILIDAD_PUBLICA.md) y la [guía de respaldo](RESPALDO_Y_RECUPERACION.md). Una limitación aquí no es un fallo oculto: es lo que el proyecto no ha comprobado o ha decidido dejar fuera.

## Distribución e instalación

- **Windows x64 únicamente.** El paquete es autocontenido.
- **Los ejecutables no tienen firma digital.** Windows o SmartScreen pueden avisar. Comprueba el origen y el SHA-256; una suma de comprobación acredita integridad, no sustituye una firma.
- No se distribuyen ROMs, extracciones, partidas, recetas privadas ni gráficos oficiales. Los iconos de las mentas son una descarga **opcional** desde PokéAPI y no se empaquetan.
- La aplicación no descifra juegos comerciales: la extracción 3DS debe venir ya descifrada.

## Qué significa «estable» aquí

La versión estable cubre el alcance descrito en la matriz de compatibilidad. **No** promete que toda combinación de opciones, ROM, revisión o ROM hack funcione, ni que se haya jugado cada evento de cada juego. Las pruebas automáticas, con archivos reales y dentro del juego son evidencias distintas, y cada una se indica por separado.

## Copias de seguridad y Deshacer

- La aplicación **no sustituye tus copias**: guarda tú el original, la copia de trabajo y tus partidas.
- **Deshacer** existe para proyectos 3DS, con los 8 últimos cambios, y solo si los archivos siguen como los dejó ese cambio. No revierte partidas guardadas, ROM ya generadas ni archivos fuera del proyecto.
- La recuperación ante guardados interrumpidos tiene límites: **no** se promete protección frente a cualquier fallo físico, apagón durante una escritura, sistema de archivos defectuoso o edición externa posterior. Se comprobó en Windows/NTFS.
- DS y Switch no comparten el mismo contrato de restauración que 3DS (ver la guía de respaldo).

## Nintendo 3DS

- **Mentas Gen7:** solo **Ultra Sol y Ultra Luna**, con perfiles exactos. Cambian la naturaleza **real** del Pokémon; no implementan el sistema de estadísticas de generaciones posteriores. Confirmadas jugando en **Ultra Sol**; **Ultra Luna no está comprobada jugando**. No hay mentas en Sol/Luna, Gen6, DS ni Switch.
- Los ejecutables desconocidos o con diferencias no acreditadas **no se parchean**: la aplicación muestra qué dependencia no coincide en lugar de forzar la instalación. No se promete compatibilidad con cualquier ROM previamente modificada.
- Global Template y Batch requieren una copia que ya tenga instaladas las dependencias (por ejemplo, el parche de mentas); no las instalan en silencio.
- Las mecánicas Progressive están más desarrolladas en **Ultra Sol/Ultra Luna**; no todas existen en todos los juegos. Sol/Luna excluye el balance de movimientos a propósito.
- **Incidencias USUM conocidas y sin resolver:**
  - Un **cuelgue al entregar los pétalos** en una partida de USUM. No tiene reproducción ni causa determinada. Si juegas USUM modificado y te ocurre, repórtalo con los datos de la guía de respaldo.
  - Un **diagnóstico localizado del movimiento Z** en el primer combate contra Tilo. No se ha demostrado como fallo general.
- El visor de objetos de mapa (OWSE) conserva el alcance de lectura del pk3DS original; no es un editor de mapas.

## Nintendo DS

- Seis juegos con perfil exacto (ver la matriz). **Negro 2, SoulSilver, Perla** y otras regiones/revisiones no se admiten.
- **Modo romhack en beta.** El código del juego (overlays, ARM9) solo se parchea si es el original. Negro, Blanco y Blanco 2 son DSi-enhanced y su ARM9 no se modifica.
- Las confirmaciones jugables corresponden a lo que se indica en la matriz. La edición de entrenadores comprobada en combate fue en **Platino**.
- **El editor de eventos y mapas de DS no está incluido** en esta versión. No hay edición de scripts, destinos ni geometría de mapas.

## Espada y Escudo

- Solo **1.3.2**. Otras versiones se rechazan.
- Se **exporta un mod** (formato Ryujinx); no se reconstruye una ROM. Si otro mod reemplaza los mismos archivos, desactívalo durante la prueba.
- **No se declaran validados:** consola real / hardware, Atmosphère, DLC ni una partida completa de cada juego.
- **Combates dobles aleatorios:** protegidos para los combates iniciales; **sin protección de runtime** para jugadores con un solo Pokémon utilizable en dobles posteriores. El valor predeterminado es 0 %.
- Las animaciones del Pokédex están desactivadas temporalmente.
- Espada y Escudo son distintos: no traslades configuraciones o conclusiones de uno al otro.

## Definitive Game Data v1

- Solo Ultra Sol/Ultra Luna: especies y formas, estadísticas base, tipos, habilidades, propiedades básicas de movimientos y evoluciones.
- No identifica por sí solo la ROM que ejecuta el emulador, ni incluye parches de combate, gráficos ni lecturas de memoria de DS/Switch.

## Fuera de esta versión

Editor de eventos y mapas DS · mentas Gen6 · caramelos raros y repelentes infinitos · perfiles de ROM hacks y biblioteca comunitaria · paridad completa de aprendizajes en Switch (movimientos huevo) · soporte de consola, DLC y Atmosphère.
