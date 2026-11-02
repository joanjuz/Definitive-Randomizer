# Escalado de entrenadores según la cronología de la historia (WikiDex) — 2026-11-02

USUM ya escalaba a los entrenadores normales con una cronología de WikiDex: cada entrenador tiene su etapa de la historia y su posición dentro de ella, y su nivel avanza desde el cap del jefe anterior hasta justo por debajo del siguiente (curva 1,6). Ahora lo hacen también los demás juegos:

- **3DS:** XY, ORAS y Sol/Luna.
- **Nintendo DS:** Diamante/Perla, Platino, HeartGold/SoulSilver, Negro/Blanco y Negro 2/Blanco 2.
- **Switch:** Espada.

## Cómo se obtuvo la cronología

La cronología de cada juego está en `pk3DS.Core/Story/<juego>.json` y se genera con `tools/story-chronology` (ver su README):

- **Fuentes de WikiDex:** el orden de los lugares sale de la guía de cada juego, y los entrenadores de cada lugar, de las plantillas `TE/Entrenador` de su página.
- **Emparejamiento:** cada entrenador se identifica en el juego por su equipo exacto (especies y niveles).
- **Jefes:**
  - los líderes, por su combate de gimnasio;
  - el Alto Mando;
  - el combate final: el Campeón; Ghechis en Negro/Blanco, Kukui en Sol/Luna y Lionel en Espada.
- **Cuándo decide el nivel original:** si la guía no sitúa al entrenador, o lo sitúa donde solo se llega más tarde (Surf, Fuerza…).

| Juego | Entrenadores | Situados por la guía | Postgame (sin escalar) | Jefes (cap original) |
|---|---|---|---|---|
| Diamante/Perla | 670 | 342 | 65 | Roco 14 · Gardenia 22 · Brega 30 · Mananti 30 · Fantina 36 · Acerón 39 · Inverna 42 · Lectro 49 · Alto Mando 57 · Cintia 66 |
| Platino | 742 | 349 | 166 | Roco 14 · Gardenia 22 · Fantina 26 · Brega 32 · Mananti 37 · Acerón 41 · Inverna 44 · Lectro 50 · Alto Mando 53 · Cintia 62 |
| HeartGold/SoulSilver | 737 | 374 | 176 | Pegaso 13 · Antón 17 · Blanca 19 · Morti 25 · Aníbal 31 · Fredo 34 · Yasmina 35 · Débora 41 · Alto Mando 42 · Lance 50 |
| Negro/Blanco | 586 | 322 | 181 | Zeo/Millo/Maíz 14 · Aloe 20 · Camus 23 · Camila 27 · Yakón 31 · Gerania 35 · Junco 39 · Lirio/Iris 43 · Alto Mando 50 · Ghechis 54 |
| Negro 2/Blanco 2 | 813 | 373 | 218 | Cheren 13 · Hiedra 18 · Camus 24 · Camila 30 · Yakón 33 · Gerania 39 · Lirio 48 · Ciprián 51 · Alto Mando 58 · Iris 59 |
| XY | 747 | 285 | 27 | Violeta 12 · Lino 25 · Corelia 32 · Amaro 34 · Lem 37 · Valeria 42 · Ástrid 48 · Édel 59 · Alto Mando 65 · Dianta 68 |
| ORAS | 833 | 239 | 72 | Petra 14 · Marcial 16 · Erico 21 · Candela 28 · Norman 30 · Alana 35 · Vito y Leti 45 · Plubio 46 · Alto Mando 52 · Máximo 59 |
| Sol/Luna | 382 | 220 | 50 | Kaudan 15 · Mayla 27 · Denio 39 · Hela 48 · Alto Mando 55 · Kukui 58 |
| Espada | 405 | 180 | 174 | Percy 20 · Cathy 24 · Naboru 27 · Judith/Alistair 36 · Sally 38 · Morris/Mel 42 · Nerio 46 · Roy 48 · Lionel 65 |

- **Postgame:** en HeartGold/SoulSilver, Kanto (orden libre, después de Lance) cuenta como postgame. En Negro/Blanco, Mirto también.
- **Espada/Escudo:** la cronología solo se aplica a Espada. Escudo no recibe los IDs de Espada, como ya pasa con el preset de caps, y no hay extracción de Escudo para generar la suya.

**Validación del método con USUM.** Se generó también la cronología de USUM y se comparó con su tabla manual, con las etapas de la tabla agrupadas por kahuna:

- **Misma etapa:** 202 de 329 entrenadores.
- **Una etapa antes:** 126 entrenadores. Son los que van justo después de un kahuna con un nivel que todavía no supera el suyo, así que su nivel final queda casi igual.
- **Más lejos:** ninguno.

USUM sigue usando su tabla manual.

## Cómo se aplica

La opción visible es «Escalado según la cronología de la historia (WikiDex)», en «Política global de Level Caps» (Espada: «Niveles de entrenadores»). Está activada por defecto y actúa junto a «Escalado progresivo de entrenadores sin cap».

- **Entrenadores normales y reglas «Ace dinámico» (0):** pasan del cap del jefe anterior a «separación» niveles por debajo del siguiente, según su posición en la guía. Se mantienen las diferencias de nivel dentro del equipo, como en USUM.
- **Cap de cada ventana:** el de la regla de Level Cap de su jefe, si la tiene; si no, el nivel original del jefe.
- **Jefes sin regla:** conservan su nivel. Un cap positivo sigue fijando el nivel exacto.
- **Postgame y entrenadores sin situar:** no cambian.
- **Romhacks:** si los niveles de los jefes no coinciden con la cronología, se usa el escalado por nivel de antes.

### Compatibilidad

- **Templates e historiales antiguos:**
  - 3DS: la clave de control `GlobalCaps.Story` y `LevelCaps.StoryScaling` son nulos o no existen, así que se repiten con el escalado por nivel.
  - DS: `NdsTrainerOptions.StoryScaling` es `false`.
  - Espada: `SwordShieldTrainerOptions.StoryScaling` es `false`.
- **Templates nuevos en versiones anteriores:** ignoran la opción (3DS) o la rechazan por propiedad desconocida (Switch).

## Corrección encontrada por el camino (XY/ORAS)

`RSTE` calculaba el cap de una regla «Ace dinámico» con `GetModifiedLevel(ace, rLevel ? multiplicador : 0)`. Con «Nivel» desactivado (lo habitual), el factor era 0: la regla quedaba en cap 1 y el escalado progresivo bajaba a los entrenadores a nivel 1. Ahora el factor es 1 cuando el aumento de nivel está desactivado. Sol/Luna no tenía el problema.

## Validación

- **Suites de CI y `--requested-only`:** incluye `TrainerStoryChecks` (los 9 perfiles y las reglas del motor en un perfil sintético) y pasa.
- **`--story-probe`, en memoria:** XY, ORAS, Sol/Luna y Espada, sin escribir nada. Cada entrenador situado queda por debajo del nivel final de su siguiente jefe y en el orden de la guía, y el postgame no cambia:

  | Juego | Entrenadores situados | Cambian de nivel |
  |---|---|---|
  | XY | 707 | 634 |
  | ORAS | 748 | 649 |
  | Sol/Luna | 323 | 301 |
  | Espada | 220 | 197 |

- **`--nds-ui-probe`:** la misma comprobación en los cinco juegos DS, con copias reales: Platino 491 de 563, Diamante 540 de 592, HeartGold 475 de 548, Negro 313 de 389 y Blanco 2 516 de 582. La prueba anterior del escalado por nivel sigue pasando con la opción desactivada.
- **Sin probar en juego:** la progresión de niveles en una partida, por cada juego.
