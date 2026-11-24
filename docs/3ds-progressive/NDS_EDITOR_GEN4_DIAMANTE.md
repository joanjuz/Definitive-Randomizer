# Editor DS: perfil Gen4 de Diamante (ADAE/5)

El editor de mapas y eventos de Nintendo DS deja de estar limitado a Platino. Esta entrega añade **Pokémon Diamante USA, revisión 5 (ADAE/5)** con el mismo editor visual. No hay un segundo editor: cada juego tiene un **perfil de eventos Gen4** medido sobre su volcado y el lector existente aplica las rutas, tablas y huellas de ese perfil.

No se añaden Perla, otras revisiones de Diamante, HeartGold/SoulSilver ni Gen5. Si un juego o una revisión no tiene perfil, el editor muestra el aviso habitual y no abre nada.

## Qué está disponible en Diamante

| Función | Diamante ADAE/5 | Platino CPUE/1 |
|---|---|---|
| Mapa con matriz, terreno (colisión), estructuras y modelos con texturas | sí | sí |
| Mover y girar NPC existentes | sí | sí |
| Orientación «mirar fijo» y alcance de visión de entrenadores existentes | sí | sí |
| Mover objetos de fondo y cambiar su dirección de interacción | sí (0–5) | sí (0–6) |
| Mover warps y cambiar su destino a otro warp existente | sí | sí |
| Mover activadores y cambiar su tamaño | sí | sí |
| Añadir, duplicar, pegar o eliminar NPC; añadir enfermera | **no** | sí |
| Checkpoints `.ndsevents.json`, proyecto persistente, exportar ROM, restaurar | sí | sí |

En Diamante no se muestran los botones ni el menú contextual de NPC nuevos, y sus atajos de teclado no se registran. En su lugar aparece una línea que explica que no están acreditados en este juego. El núcleo también los rechaza (`ValidateNewNpc`, `AddNpc` y `WithAppendedScript`), aunque una llamada no venga de la interfaz.

## Diferencias medidas entre Platino y Diamante

| Dato | Platino CPUE/1 | Diamante ADAE/5 |
|---|---|---|
| Cabeceras de mapa (ARM9) | `0xE601C`, 593 × 24 bytes | `0xEEDBC`, 559 × 24 bytes |
| Eventos | `fielddata/eventdata/zone_event.narc`, 534 miembros | `fielddata/eventdata/zone_event_release.narc`, 512 miembros |
| Scripts | `fielddata/script/scr_seq.narc`, 1.124 miembros | `fielddata/script/scr_seq_release.narc`, 1.051 miembros |
| Terreno | `fielddata/land_data/land_data.narc`, 666 bloques | `fielddata/land_data/land_data_release.narc`, 578 bloques |
| Nombres de lugar (banco de texto) | 433 | 382 |
| Scripts de objetos visibles / ocultos (7000+ / 8000+) | 404 / 408 | 370 / 374 |
| Dirección de interacción de objetos de fondo | 0–6 | 0–5 (máximo del juego original) |
| Matrices, nombres internos, áreas y modelos | mismas rutas, contenido propio | mismas rutas, contenido propio |

Cada tabla está fijada por CRC32 en `Gen4EventProfile` y el archivo de eventos por las tres huellas SHA-256 del editor: la de fase 1, la de visión y la de objetos, warps y activadores. Una ROM reconstruida con ediciones aceptadas se vuelve a abrir por esas huellas, igual que en Platino.

**Tabla de cabeceras.** Se buscó en el ARM9 de la copia de Diamante. `0xEEDBC` es el único desplazamiento donde las referencias de las 559 cabeceras están dentro de sus tablas y los 1.118 warps llevan a un warp existente, salvo los 6 dinámicos (ancla 256). Los desplazamientos vecinos también tienen referencias dentro de rango, pero dejan entre 396 y 489 warps sin destino.

**Formato de los registros.** El de Diamante es el de Platino: cuatro bloques con contador u32 y registros de 20, 32, 12 y 16 bytes. Los 512 miembros se leen sin bytes sobrantes. La decompilación pública de Diamante y Perla (pret/pokediamond, `include/map_object.h` y `include/constants/global_fieldmap.h`) tiene los mismos campos en el mismo orden que Platino (pret/pokeplatinum, `include/struct_defs/events.h`), y las mismas direcciones: 0 arriba, 1 abajo, 2 izquierda, 3 derecha.

**Semántica de entrenadores.** Los tipos de entrenador, el alcance y los movimientos «mirar fijo» 14–17 no se encontraron declarados en las fuentes públicas de Diamante que se consultaron. Se aplican las reglas de Platino porque los datos de Diamante son coherentes con ellas:

- el alcance máximo de visión es 6, como en Platino;
- hay 411 entrenadores de tipo 1, 11 de tipo 2 y 21 de los tipos 4–8;
- todos los entrenadores con movimiento 14–17 tienen la dirección 0–3 que corresponde a ese movimiento.

Hasta la prueba jugable, esto queda como una inferencia apoyada en los datos, no como un hecho comprobado en el juego.

## Contratos de archivo

- **Checkpoints.** Mismo formato `DefinitiveRandomizer.NdsEvents` y mismas versiones (1–5). El campo `Game`/`Revision` lleva el juego de la sesión (`ADAE`/`5`). Un checkpoint de un juego se rechaza en otro. Las compilaciones anteriores rechazan los checkpoints de Diamante: solo aceptaban `CPUE`/`1`.
- **Proyecto persistente (`.drproj`).** No cambia el formato ni el lector mínimo. La identidad de la ROM ya enlaza el proyecto a su volcado. Una compilación anterior rechaza un proyecto de Diamante con eventos con el mensaje «el proyecto contiene eventos y esta ROM no es…»; no lo abre sin ellos.
- **Exportación.** Solo cambia el archivo de eventos del juego (`zone_event_release.narc` en Diamante). En Diamante no se modifica el archivo de scripts, porque esta entrega no añade scripts.
- **Detector de proyectos.** Acepta un checkpoint de cualquier juego con perfil si su ROM de origen es de ese mismo juego. Se rechazan las combinaciones cruzadas y los juegos sin perfil, por ejemplo Perla.
- **Global Template y Batch.** No cambian: siguen rechazando la captura de operaciones de eventos.

Las clases conservan el nombre `Platinum*` para no alterar las API existentes. `PlatinumEvents.Parse(...)` sin perfil sigue usando Platino, por lo que los fixtures existentes no cambian. El constructor con perfil es `Parse(Gen4EventProfile, ...)`. Las constantes públicas de Platino siguen existiendo y coinciden con `Gen4EventProfile.Platinum`.

## HeartGold/SoulSilver y Gen5: estado

En la copia de HeartGold (IPKE), los eventos están en `a/0/3/2`: 491 miembros con la misma estructura de cuatro bloques. Sin embargo, la cabecera de mapa coloca la matriz en +4 y los scripts en +6, mientras que Platino y Diamante los colocan en +2 y +4. El lector actual tomaría la matriz y los scripts equivocados, así que HeartGold necesita un adaptador de cabeceras propio antes de tener perfil.

Gen5 (Negro, Blanco, Negro 2 y Blanco 2) usa otros contenedores y registros. Tendrá un lector propio y no comparte nada de este perfil.

## Comprobaciones automatizadas

- `--nds-gen4-profile-only` (23 comprobaciones sintéticas) y `--nds-gen4-profile-ui-only` (5 fuera de pantalla). Ambas están en el CI.
- `--nds-gen4-map-editor-probe <carpeta nueva> <Diamante ADAE/5 privado>`, sobre una copia real. Comprueba las huellas, el censo (559 mapas, 3.160 NPC y 1.118 warps, todos resueltos salvo los 6 dinámicos) y la apertura de terreno, estructuras y gráficos. Después hace estas ediciones:
  - en Hojaverde, gira un NPC, cambia el destino del warp 0, mueve un objeto oculto y amplía un activador;
  - en la Ruta 202, gira un entrenador «mirar fijo».

  Guarda cinco checkpoints, reabre el último, exporta y vuelve a abrir la ROM exportada en el editor. Comprueba que todos los archivos NDS menos el de eventos son idénticos byte a byte. Por último, restaura todo y compara el SHA-256 con la fuente: coincide. Escribe `MANUAL.txt`.
- `--nds-map-render-gallery-probe` con Diamante: 16 mapas renderizados (ciudades, interiores, cuevas y rutas), con modelos y eventos alineados y sin triángulos sin textura en ciudades e interiores.
- Regresión de Platino sobre copia real: sondas de eventos, visión, objetos/warps/activadores, NPC nuevos, scripts y render, todas en verde. La ROM de validación de objetos, warps y activadores regenerada con este código es idéntica byte a byte a la preparada anteriormente.

Ninguna de estas comprobaciones sustituye una prueba en el juego.

## Validación jugable (24-11-2026)

El propietario probó en el juego la ROM generada por la sonda (`Diamante-eventos.nds`, desde Diamante ADAE/5) y aprobó las cinco comprobaciones:

| Comprobación | Resultado en el juego |
|---|---|
| NPC 4 de Hojaverde girado | orientación correcta |
| Warp 0 de Hojaverde con otro destino | lleva a la casa T01R0201 |
| Objeto oculto movido de X 107 a X 108 | se recoge en la nueva casilla |
| Activador ampliado a 9 × 1 | se dispara en toda su anchura |
| Entrenador 0 de la Ruta 202 en «mirar fijo» hacia arriba | orientación correcta |

La aprobación cubre **solo estas cinco operaciones en Diamante ADAE/5**. No se extiende a Perla, otras revisiones de Diamante, HeartGold ni otras funciones que no se ensayaron. Las comprobaciones automatizadas de las secciones anteriores se mantienen aparte: son pruebas sintéticas, de interfaz y sobre copia real, no jugables.

La misma fecha, el propietario aprobó en Platino CPUE/1 las tres comprobaciones del PR #117 (warp modificado, objeto oculto desplazado y activador ampliado), con la ROM `Platino-eventos.nds`.
