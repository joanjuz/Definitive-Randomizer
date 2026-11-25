# Habilidades especiales · Sistema Alfa en otras especies (Ultra Sol / Ultra Luna)

Opción experimental de «Mecánicas y CRO», desactivada por defecto: un Pokémon distinto de Silvally con la habilidad Sistema Alfa toma el tipo de la Memoria que lleva, o Normal sin Memoria. **No asigna la habilidad a nadie**, ni cambia formas, modelos o estadísticas. Se combina con [Multitipo](MULTITIPO_OTRAS_ESPECIES_USUM.md): cada opción se activa por separado.

## Mecanismo

En las dos funciones de tipo de `CoreParam` (`code.bin`), la rama de Silvally exige que la especie sea 773 y la habilidad 225. Por función se cambian dos palabras, en posiciones relativas al punto de Multitipo de cada juego:

| Palabra | Original | Con Sistema Alfa |
|---|---|---|
| sitio + `0x24` | `cmp r5, r1` (Silvally) | `cmp r0, #0xE1` (habilidad Sistema Alfa) |
| sitio + `0x10` | `bne` → tipos de Personal (salida de Arceus sin Multitipo) | `bne` → comprobación de Sistema Alfa |

La redirección de la segunda palabra hace falta para que un Arceus con Sistema Alfa use su Memoria cuando Multitipo está desactivado. Sin Sistema Alfa, esa comprobación falla y el resultado es el original. Orden resultante: Multitipo → Sistema Alfa → Personal.

| Juego | Puntos de Sistema Alfa (archivo) |
|---|---|
| Ultra Sol `00040000001B5000` | `0x3AF2B0`, `0x3AF2C4`, `0x3AF344`, `0x3AF358` |
| Ultra Luna `00040000001B5100` | `0x3AF2B8`, `0x3AF2CC`, `0x3AF34C`, `0x3AF360` |

### Convivencia con Multitipo

Las dos opciones comparten perfiles y huella de las funciones (`MultitypeUnlockPatch.MaskedWindow`). La huella excluye los puntos de ambas, y cada opción solo acepta en los puntos de la otra el original o el valor parcheado. Se actualizaron las huellas de Multitipo, pero lo que escribe no cambia, así que los proyectos que ya tienen Multitipo instalado se siguen reconociendo.

## Evidencia

- **Emulación (Unicorn) de las funciones reales de US y UM** en las 4 configuraciones (ninguna, solo Multitipo, solo Sistema Alfa, ambas), con 28 casos cada una: Arceus, Pikachu y Silvally con Multitipo, Sistema Alfa u otra habilidad, sin objeto, con Tabla o con Memorias. En todos los casos el resultado es el esperado: Multitipo da el tipo de la Tabla, Sistema Alfa el de la Memoria, y lo demás queda igual que el original. Arnés fuera de Git.
- `--rks-only` (CI): 24.
  - Las 4 configuraciones y los dos órdenes de instalación y retirada, con bytes exactos.
  - Retirar una opción conserva la otra; doble instalación, parcial y palabra ajena (que bloquea las dos).
  - Editor con perfiles reales.
- `--rks-real <ExeFS US> <ExeFS UM> <RomFS UM> <salida>` (copias reales): 25.
  - La misma matriz sobre los `code.bin` reales.
  - Las mentas siguen identificando el `code.bin`, Shiny Rate no cambia y Player Level Caps funciona en los dos órdenes.
  - Editores de Multitipo y Sistema Alfa juntos: historial, deshacer por pasos con bytes exactos y Global Template.
  - Genera los mods IPS de prueba.
- Regresiones sin fallos: Multitipo 26 y 13 reales, Disfraz 9, Caramelos Raros 69, mentas 98, Mega Ability Sync 165, capacidades 129, guía 233, manual general 113.

## Sin comprobar (no se promete)

- **Multiataque:** el tipo del movimiento se decide en `Battle.cro`; no se ha analizado si depende de la especie.
- **Cambio de Memoria en mitad del combate, Truco y Desarme:** la inmunidad de la Memoria en Silvally probablemente es por especie; en otras especies la Memoria podría retirarse.
- **Formas:** Silvally sigue cambiando de forma con la Memoria (otra función); las demás especies no cambian de forma.
- Prueba jugable pendiente.
