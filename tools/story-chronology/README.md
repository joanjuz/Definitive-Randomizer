# Cronología de la historia (WikiDex)

Genera `pk3DS.Core/Story/<juego>.json`, la cronología que usa «Escalado según la cronología de la historia (WikiDex)» en XY, ORAS, Sol/Luna, Diamante/Perla, Platino, HeartGold/SoulSilver, Negro/Blanco, Negro 2/Blanco 2 y Espada. USUM conserva su tabla auditada a mano en `SMTE`.

## Cómo se construye

1. **Orden de los lugares.**
   - Se leen las guías de WikiDex (`Guía de Pokémon …`) por la API, con sus subpáginas en el orden del índice.
   - Cada sección aporta el lugar de su encabezado. Los enlaces del texto solo cuentan cuando el encabezado no es un lugar, porque también apuntan hacia delante.
2. **Entrenadores de cada lugar.**
   - Se toman las plantillas `TE/Entrenador` (especie y nivel de cada Pokémon) de las páginas de lugar; las páginas de personaje se excluyen.
   - Las ediciones se detectan por coincidencias.
3. **Emparejamiento.** Cada entrenador del juego se empareja por su equipo exacto (especies y niveles), que no depende del idioma.
4. **Hitos.**
   - Son el líder (su combate en la página de su Gimnasio o Estadio; si no la tiene, el de menor ID por debajo del combate final), el Alto Mando y el Campeón, o el combate final: Ghechis en Negro/Blanco, Kukui en Sol/Luna y Lionel en Espada.
   - Las variantes de versión o inicial forman un solo hito.
5. **Ventana de cada entrenador.**
   - Se usa la del lugar donde aparece, acotada por los líderes situados en su gimnasio.
   - Decide su nivel original cuando la guía no lo sitúa, cuando lo sitúa en una zona que solo se alcanza más tarde (Surf, Fuerza…) o cuando es incoherente.
   - Por encima del Alto Mando sin lugar en la historia, o tras el combate final, es postgame y no se escala.

## Uso

```
dotnet 3DS.Progressive.Tests.dll --trainer-dump <extracción 3DS | ROM .nds | extracción de Espada> dumps/<nombre>.json
python -I generate.py <carpeta de salida> Pt DP HGSS BW B2W2 XY ORAS SM SWSH
```

- **Volcados que espera `generate.py`:** en `dumps/` (o `STORY_DUMPS`), con los nombres `D, Pt, HG, B, W2, X, OR, Sol, UltraSol, Sw` y extensión `.json`.
- **Caché:** las páginas de WikiDex se guardan en `cache/` (o `STORY_CACHE`).
- **Sin versionar:** los volcados y la caché no se versionan (`.gitignore`).
- **Fuentes de solo lectura:** el volcado solo lee la extracción o la ROM.

`--story-probe <fuentes…>` aplica el escalado en memoria a juegos reales y comprueba tres cosas: cada entrenador situado queda por debajo del nivel final de su siguiente jefe, sigue el orden de la guía y el postgame no cambia.
