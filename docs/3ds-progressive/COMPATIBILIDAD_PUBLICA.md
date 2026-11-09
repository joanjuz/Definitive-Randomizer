# Matriz pública de compatibilidad

Qué juegos admite Definitive Randomizer (WPF) y **qué nivel de comprobación tiene cada uno**. Estado a 2026-11-09, sobre `main` en `cbf6f95`. Es un resumen para usuarios: el detalle técnico de cada módulo está en el [índice](INDICE.md).

> **Cómo leer esta matriz.** «Implementado» no significa «probado en el juego». Cada columna es una evidencia distinta y ninguna sustituye a las demás. Donde no hay confirmación documentada, la celda lo dice; no se interpreta como que funciona ni como que falla.

## Niveles de comprobación

| Nivel | Significado |
|---|---|
| **Implementado** | La función existe en la aplicación. |
| **Automático** | La cubren las suites automáticas y el CI con datos sintéticos o fixtures. No usa ROMs reales. |
| **Archivos reales** | Se ejecutó con una copia real del juego del propietario (lectura, edición, reconstrucción, reapertura), sin modificar el original. |
| **En juego** | El propietario del proyecto lo comprobó jugando, normalmente en emulador. Solo cubre lo que se indica, no una partida completa. |
| **No comprobado / limitado** | Sin evidencia documentada, o con una restricción conocida. |

## Nintendo 3DS

Requiere una extracción **ya descifrada** (la aplicación no descifra ROMs comerciales). La reconstrucción, el historial con deshacer y las recetas Global/Batch están disponibles en las cuatro familias.

| Juego | Implementado | Automático | Archivos reales | En juego | Notas |
|---|---|---|---|---|---|
| **X / Y** | Editores y randomizadores de Pokémon, movimientos, evoluciones, entrenadores, encuentros, objetos y tiendas; Progressive; Global/Batch | Sí (suites sintéticas de las cuatro familias) | Sin evidencia documentada en esta línea WPF | Sin confirmación documentada para esta línea | Paridad de funciones **no** asumida respecto a otras familias. |
| **Omega Ruby / Alpha Sapphire** | Igual que X/Y, más tutores BP y herramientas Gen6 propias | Sí | Sin evidencia documentada en esta línea WPF | Sin confirmación documentada para esta línea | — |
| **Sol / Luna** | Editores y randomizadores; Global/Batch | Sí | Sin evidencia documentada en esta línea WPF | Sin confirmación documentada para esta línea | El balance de movimientos y las mecánicas Progressive están **excluidos a propósito**. Sin mentas. |
| **Ultra Sol** | Todo lo anterior, más las extensiones Progressive (level caps, Relearner, tutores, Megas, distribución de mentas) | Sí | Sí: copias reales (mentas, distribución, historial, deshacer, reconstrucción) | **Mentas, iconos, distribución, Global Template, Batch y ROM generada** (propietario, emulador) | Resto de funciones de USUM: ver la lista de pendientes en [LIMITACIONES_ESTABLE.md](LIMITACIONES_ESTABLE.md). |
| **Ultra Luna** | Igual que Ultra Sol | Sí | Sí: instalación de mentas reconocida en copia real | **No comprobado** jugando | Mentas con perfil propio; no se extiende la confirmación de Ultra Sol. |

Región y revisión en 3DS: la aplicación identifica el título por la cabecera NCCH y, para los parches nativos (mentas), **por la huella exacta del ejecutable**, no por un número de versión comercial. No se anuncia soporte de «todas las revisiones». Cuando una revisión no está acreditada, la aplicación se detiene y muestra qué dependencia no coincide. *[Confirmar antes de publicar: la lista de regiones/revisiones 3DS con evidencia real; ver sección «Pendiente de confirmar» al final.]*

## Nintendo DS (Gen4 y Gen5)

Seis perfiles de **ROM original** identificados por código de juego, revisión y huellas de sus tablas. Cada juego tiene su propio perfil: no se trasladan offsets entre juegos ni entre generaciones. Un archivo con código admitido pero huella distinta se reconoce como **no verificado** y se rechaza en modo original.

| Juego | Código / rev. | Implementado | Automático | Archivos reales | En juego |
|---|---|---|---|---|---|
| **Diamante** (USA) | `ADAE` / 5 | Entrenadores, encuentros, iniciales, objetos y tiendas, datos, evoluciones, estáticos/intercambios, movimientos huevo y tutores, ajustes, Global y Batch | Sí | Sí (volcado del propietario) | Sí: fases 0–14 (propietario, 31/10/2026) |
| **Platino** (USA) | `CPUE` / 1 | Igual, más el editor de entrenadores compartido con 3DS | Sí | Sí | Sí: fases 0–14 y **edición de un entrenador (Pokémon, cuatro movimientos y objeto) comprobada en combate** |
| **HeartGold** (USA) | `IPKE` / 0 | Igual que Diamante | Sí | Sí | Sí: fases 0–14 |
| **Negro** | `IRBO` / 0 | Igual que Diamante (Gen5) | Sí | Sí | Sí: fases 0–14 |
| **Blanco** (España) | `IRAS` / 0 | Igual que Diamante (Gen5) | Sí | Sí | Sí: fases 0–14 |
| **Blanco 2** (USA/EUR) | `IRDO` / 0 | Igual que Diamante (Gen5) | Sí | Sí | Sí: fases 0–14 |

Qué **no** se acredita en DS:

- **Negro 2, SoulSilver, Perla y otras regiones o revisiones** no tienen perfil y se rechazan.
- **Modo romhack:** es **beta**. Admite ROM modificadas de los seis juegos sin exigir las huellas de las tablas, pero solo parchea el código del juego (overlays, ARM9) si es el original. Se probaron Renegade Platinum y Volt White 2 con resultado satisfactorio; no es soporte general de romhacks.
- **Negro, Blanco y Blanco 2** son DSi-enhanced: su ARM9 no se modifica.
- La edición de entrenadores comprobada en combate se confirmó en **Platino**; no se extiende a otros juegos DS ni a controles no probados.
- **El editor de eventos y mapas de DS no forma parte de esta versión.**

## Nintendo Switch: Espada y Escudo

Solo la versión **1.3.2** de cada juego. Espada y Escudo tienen perfiles, runtimes y validaciones **propios**: lo comprobado en uno no se traslada al otro. En vez de reconstruir una ROM, la aplicación **exporta un mod** en formato Ryujinx.

| Juego | Implementado | Automático | Archivos reales | En juego |
|---|---|---|---|---|
| **Espada 1.3.2** | Diez editores de datos, entrenadores, encuentros, objetos, tiendas, movimientos/Level Up/evoluciones, MT/TR/tutores, Global/Batch de mods, runtime propio (encuentros, level caps, EXP) | Sí (`--swsh-*`) | Sí (extracción del propietario) | Sí, en **Ryujinx** (propietario) |
| **Escudo 1.3.2** | Igual, con su propio perfil y runtime | Sí | Sí | Sí, en **Ryujinx**: las opciones de 0.9.0-preview.1 |

Qué **no** se acredita en Switch:

- **Consola real / hardware ni Atmosphère.** No se declaran validados.
- **DLC.** No se detectan licencias ni se declara soporte.
- **Una partida completa** de cada juego ni todos los eventos o combinaciones de opciones.
- **Combates dobles aleatorios:** protegidos para los primeros combates (los de Paul y los de menor nivel que el primer líder); no hay protección de runtime para un único Pokémon utilizable en dobles posteriores. El valor predeterminado es 0 %.
- Las animaciones del Pokédex están desactivadas temporalmente.

## Funciones transversales

| Función | Alcance | Estado |
|---|---|---|
| **Definitive Game Data v1** | Exporta el estado guardado de Ultra Sol/Ultra Moon (especies, estadísticas, tipos, habilidades, movimientos básicos, evoluciones) para Progressive Tracker | Implementado, publicado en preview y con interoperabilidad comprobada con Tracker. No identifica la ROM que ejecuta el emulador ni cubre DS/Switch. |
| **Historial y Deshacer** | 3DS (puntos de restauración). DS y Switch tienen su propio historial pero no el mismo contrato de restauración | Ver [RESPALDO_Y_RECUPERACION.md](RESPALDO_Y_RECUPERACION.md). |
| **Global Template y Batch** | Cada plataforma tiene el suyo; no se mezclan recetas 3DS, DS y Switch | Implementado. |

## No incluido en esta versión

Editor de eventos y mapas de DS · mentas en Gen6, Sol/Luna, DS o Switch · caramelos raros o repelentes infinitos · perfiles de ROM hacks populares y biblioteca comunitaria (Hub) · Negro 2, SoulSilver y Perla · soporte de DLC, consola real o Atmosphère en Switch.

## Pendiente de confirmar antes de publicar

Estos puntos requieren decisión o dato del propietario; no se rellenaron por suposición:

1. Para X/Y, OR/AS y Sol/Luna, qué evidencia con archivos reales o en juego existe para la **línea WPF**. Hasta confirmarlo, la matriz declara «sin evidencia documentada».
2. Regiones y revisiones concretas de los juegos 3DS con evidencia real.
3. Región exacta de `IRBO` y de los demás volcados DS, si se desea indicarlas por idioma además de por código.
4. Si la ROM propia del tester de mentas (pendiente en el seguimiento de PR #57) se incorpora a la matriz de Ultra Sol.
