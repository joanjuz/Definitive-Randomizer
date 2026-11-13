# Limitaciones que declara la versión estable

Lista pública de lo que la primera versión estable WPF **no** hace o no garantiza. Debe leerse junto con la [matriz de compatibilidad](COMPATIBILIDAD_PUBLICA.md) y la [guía de respaldo](RESPALDO_Y_RECUPERACION.md). Una limitación aquí no es un fallo oculto: es lo que el proyecto no ha comprobado o ha decidido dejar fuera.

## Distribución e instalación

- **Windows x64 únicamente.** El paquete es autocontenido.
- **Los ejecutables no tienen firma digital.** Windows o SmartScreen pueden avisar. Comprueba el origen y el SHA-256; una suma de comprobación acredita integridad, no sustituye una firma.
- No se distribuyen ROMs, extracciones, partidas, recetas privadas ni gráficos oficiales. Los iconos de las mentas son una descarga **opcional** desde PokéAPI y no se empaquetan.
- La aplicación no descifra juegos comerciales: la extracción 3DS debe venir ya descifrada.

## Qué significa «estable» aquí

La versión estable cubre el alcance descrito en la matriz de compatibilidad. **No** promete que toda combinación de opciones, ROM, revisión o ROM hack funcione, ni que se haya jugado cada evento de cada juego. Las pruebas automáticas, con archivos reales y dentro del juego son evidencias distintas, y cada una se indica por separado. La aceptación técnica de `main` no encontró un defecto vigente y reproducido de corrupción o pérdida de datos en este alcance; eso no equivale a garantía absoluta. En X/Y, OR/AS y Sol/Luna no hay pruebas jugables documentadas en la línea WPF: su evidencia es con archivos reales.

## Copias de seguridad y Deshacer

- La aplicación **no sustituye tus copias**: guarda tú el original, la copia de trabajo y tus partidas.
- **Deshacer** existe para proyectos 3DS, con los 8 últimos cambios, y solo si los archivos siguen como los dejó ese cambio. No revierte partidas guardadas, ROM ya generadas ni archivos fuera del proyecto.
- La recuperación ante guardados interrumpidos tiene límites: **no** se promete protección frente a cualquier fallo físico, apagón durante una escritura, sistema de archivos defectuoso o edición externa posterior. Se comprobó en Windows/NTFS.
- DS y Switch no comparten el mismo contrato de restauración que 3DS (ver la guía de respaldo).

## Nintendo 3DS

- **Mentas Gen7:** solo **Ultra Sol y Ultra Luna**, con perfiles exactos. Cambian la naturaleza **real** del Pokémon; no implementan el sistema de estadísticas de generaciones posteriores. Confirmadas jugando en **Ultra Sol**; **Ultra Luna no está comprobada jugando**. No hay mentas en Sol/Luna, Gen6, DS ni Switch.
- La instalación de mentas no exige hashes completos de `code.bin`, pero sí comprueba las dependencias nativas concretas del parche y la integridad de Bag, objetos, textos y CRO/CRR. Si una dependencia necesaria no coincide, **no se instala** y se explica cuál; no hay instalación forzada. No se promete compatibilidad con cualquier ROM previamente modificada ni se certifica el estado del motor creado en ejecución. Probar el juego sigue siendo necesario.
- Global Template y Batch requieren una copia que ya tenga instaladas las dependencias (por ejemplo, el parche de mentas); no las instalan en silencio.
- Las mecánicas Progressive están más desarrolladas en **Ultra Sol/Ultra Luna**; no todas existen en todos los juegos. Sol/Luna excluye el balance de movimientos a propósito.
- **Incidencias USUM conocidas y sin resolver:**
  - Un **cuelgue al entregar los pétalos** en una partida de USUM. Su causa sigue sin determinar y no hay una reproducción nueva; no está solucionado. Si juegas USUM modificado y te ocurre, repórtalo con los datos de la guía de respaldo.
  - Un **diagnóstico localizado del movimiento Z** en el primer combate contra Tilo. No se ha demostrado como fallo general.
- El visor de objetos de mapa (OWSE) conserva el alcance de lectura del pk3DS original; no es un editor de mapas.
- **Mega Ability Sync (Ultra Sol/Ultra Luna):** solo actúa si **los dos** juegos del combate tienen instalada una versión compatible del parche; si solo la tiene uno, los dos se comportan como el original y no hay aviso en pantalla. Confirmado por el propietario en un combate real por enlace (caso principal, 13/11/2026). **Sin confirmar en combate:** un solo lado con parche, sala creada por el otro jugador, dobles/múltiples/Battle Royal, Ultra Sol contra Ultra Luna, Rayquaza y la convivencia en combate con Persistent Items. No cubre Regresión Primigenia, Ultraestallido ni otros cambios de forma. Los proyectos con la primera rutina deben actualizarla (Mecánicas y CRO lo indica) y reconstruir las dos ROM.
- **Combates por Internet o enlace con ROM modificadas:** la estabilidad en línea entre juegos randomizados de forma distinta **no** está garantizada; se investiga por separado y no hay parches experimentales en esta versión.
- **Player Level Caps, Persistent Items y Shiny Rate:** parches de código con informe de capacidades de solo lectura; si la firma no coincide (otra revisión o una modificación externa) **no se instalan**. Se comprobó por pruebas automáticas que Player Level Caps y Persistent Items pueden instalarse juntos; no hay prueba jugable documentada de esa combinación.
- **Perfiles compartibles (formato 1):** guardan la receta de Global Template de un juego, sin recursos, templates creados, textos del juego ni rutas locales. Un perfil pertenece a un juego; importarlo solo carga sus secciones y no certifica que el resultado coincida con otra ROM.

## Nintendo DS

- Seis juegos con perfil exacto (ver la matriz). **Negro 2, SoulSilver, Perla** y otras regiones/revisiones no se admiten.
- **Modo romhack en beta.** El código del juego (overlays, ARM9) solo se parchea si es el original. Negro, Blanco y Blanco 2 son DSi-enhanced y su ARM9 no se modifica.
- Las confirmaciones jugables corresponden a lo que se indica en la matriz. La edición de entrenadores (PR #56) se comprobó en combate **solo en Platino** (un Pokémon, cuatro movimientos y objeto); no se extrapola a otros juegos DS, a controles no probados, a formas arbitrarias ni a una partida completa.
- **Proyectos DS persistentes:** el archivo `Juego.nds.drproj` se guarda junto a la ROM. No hay un «Deshacer» general de tablas como el de 3DS. Los diálogos de ubicación alternativa y de recuperación no tienen revisión manual documentada.
- **Editor de eventos y mapas DS: solo Platino USA (CPUE/1).** Mover NPC existentes (campos o ratón), añadir/duplicar/copiar/pegar NPC, copiar una Enfermera Joy con su comportamiento y compartir el script de un NPC de mensaje, con checkpoints y restauración; lienzo de matrices, terreno y estructuras **de solo lectura**. Confirmado en juego por el propietario: la edición manual de X/Y y el intercambio de NPC, y una Enfermera Joy copiada de Ciudad Jubileo a Pueblo Hojaverde. El arrastre con el ratón, la edición estructural general y «compartir script» no tienen prueba jugable propia. **No** hay edición de scripts arbitrarios, destinos (warps), activadores, geometría, estructuras ni entrenadores nuevos, ni soporte de otros juegos, revisiones o romhacks.

## Espada y Escudo

- Solo **1.3.2**. Otras versiones se rechazan.
- Se **exporta un mod** (formato Ryujinx); no se reconstruye una ROM. Si otro mod reemplaza los mismos archivos, desactívalo durante la prueba.
- **No se declaran validados:** consola real / hardware, Atmosphère, DLC ni una partida completa de cada juego.
- **Combates dobles aleatorios:** protegidos para los combates iniciales; **sin protección de runtime** para jugadores con un solo Pokémon utilizable en dobles posteriores. El valor predeterminado es 0 %.
- Las animaciones del Pokédex están desactivadas temporalmente.
- Espada y Escudo son distintos: no traslades configuraciones o conclusiones de uno al otro.

## Definitive Game Data v1

- Solo Ultra Sol/Ultra Luna: especies y formas, estadísticas base, tipos, habilidades, propiedades básicas de movimientos y evoluciones; bloques opcionales con el estado guardado de Player Level Caps (`playerLevelCaps`) y la procedencia histórica del último perfil aplicado (`profileProvenance`).
- La procedencia es una declaración histórica (nombre, autor, huella): **nunca** certifica que la ROM coincida con un perfil. Ediciones posteriores, Deshacer o una receta posterior sin perfil se señalan o anulan la atribución.
- No identifica por sí solo la ROM que ejecuta el emulador, ni incluye parches de combate, gráficos ni lecturas de memoria de DS/Switch. La interoperabilidad con Progressive Tracker se comprobó con pruebas automáticas y en navegador, no con la aplicación de escritorio en una partida real.

## Fuera de esta versión

Editor de eventos DS fuera de Platino CPUE/1 y edición de scripts, warps, geometría o entrenadores nuevos · parches de estabilidad en línea · mentas Gen6 · caramelos raros y repelentes infinitos · perfiles de ROM hacks y biblioteca comunitaria · paridad completa de aprendizajes en Switch (movimientos huevo) · soporte de consola, DLC y Atmosphère.
