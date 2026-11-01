# Nintendo DS: paridad de interfaz con 3DS y auditoría visual — 2026-11-01

Objetivo del propietario: quien ya randomiza en 3DS no debe notar diferencias al usar el modo DS. El backend de cada plataforma es distinto; la interfaz, los nombres y el flujo son los mismos.

## Tablas de Level Caps y Trainer Move Rules

En Entrenadores, el Inspector de DS tiene ahora «Tabla de Level Caps» y «Tabla de Trainer Move Rules», y abren la misma tabla que en 3DS (`TrainerRulesWindow`).

**Grupos.** Cada entrenador se clasifica por su clase de entrenador (`NdsTrainerTools.Kind`). Sirve en volcados en inglés y en español, y en romhacks:
- **Jefes · Líderes de gimnasio:** clase «Leader».
- **Jefes · Alto Mando y Campeón.**
- **Importantes · Rivales:** Silver.
- **Importantes · Rivales y especiales:** «PKMN Trainer»: Cheren, Bianca, N, Red, Lucas, Dawn…
- **Importantes · Otros jefes:** Cyrus y sus comandantes, los ejecutivos y Giovanni, N y Ghetsis, Colress, Ingo y Emmet.

Los filtros «Entrenadores importantes» y «Jefes» funcionan igual que en 3DS. En DS no hay Mega, movimiento Z ni EV de entrenador, así que esas columnas no aparecen.

**Level Caps.** Misma semántica que el randomizador de entrenadores de 3DS (Sol/Luna):
- **Cap positivo:** todo el equipo pasa a ese nivel.
- **Cap 0:** el equipo no cambia, y su Ace marca la etapa.
- **«Máx. 3»:** conserva el Pokémon de mayor nivel y los dos primeros del resto.
- **«Escalado progresivo de entrenadores sin cap»** (pestaña «Niveles de entrenadores», grupo «Política global de Level Caps», mismas claves que 3DS): los entrenadores sin cap se mueven entre los caps que los rodean con `TrainerLevelProgression`. Quedan como mucho «Separación de nivel» niveles por debajo del cap siguiente, y se conservan las diferencias de nivel dentro de cada equipo.

**Trainer Move Rules.** «Usar regla» da al equipo movimientos mejorados, sea cual sea el modo general. «Mov. estado» permite o excluye los movimientos de estado.

Las reglas forman parte de las opciones de la operación (`NdsTrainerOptions.LevelCaps`, `MoveRules`, `ScaleBetweenCaps`, `CapGap`). Se guardan en el historial y en el Global Template, y Batch las reproduce. Los templates anteriores siguen siendo válidos.

## Iniciales

Las opciones usan las claves y los nombres de 3DS:
- «Iniciales básicos con dos evoluciones (línea de 3 etapas)» (`BasicStarter`);
- «Permitir Pokémon legendarios» (`Legends`);
- en Diamante y Platino, «Objeto aleatorio para el inicial».

Desaparece el cuadro numérico «1» de rango 1–1.

Las líneas de 3 etapas salen de la tabla de evoluciones de la propia ROM, ya con los cambios aplicados. Un inicial nunca es una evolución, y una de sus evoluciones evoluciona otra vez. Así se respetan las evoluciones randomizadas y los romhacks. Sin opciones nuevas, la elección es la misma que antes para la misma semilla.

## Movimientos huevo

Se muestran como formulario de dos columnas, igual que el editor Egg Moves de 3DS. Antes eran una tabla de una sola fila con listas de unos 60 px que cortaban los nombres.

## Opciones con texto largo y otros arreglos visuales

- **Casillas de opciones:** pasan a varias líneas en lugar de cortarse, en 3DS y en DS. El contenido sigue siendo el texto, así que las pruebas y la accesibilidad no cambian.
- **Título «Rangos BST progresivo»:** se ajusta en varias líneas.
- **MT/MO y tutores de Gen6:** a 1366×768 al 125 %, el Inspector plegado tapaba las opciones. Ahora la página se desplaza.
- **Tabla de reglas:**
  - la columna «Grupo» se ensancha cuando los nombres de grupo son largos;
  - «[PK][MN] Trainer» se muestra como «PKMN Trainer».

## Auditoría visual

- **Nueva opción `--visual-audit-ds <carpeta> <rom>…`:** abre el espacio DS con volcados reales, que solo se leen (se comprueba su SHA-256), a los mismos cuatro tamaños que `--visual-audit`. Recorre:
  - todas las secciones;
  - sus opciones de randomizado y pestañas;
  - Ajustes del juego, Global Template e Historial;
  - las dos tablas de entrenadores.
- **`--visual-audit`** ahora también abre el panel de opciones de cada editor de registros, con todas sus pestañas.
- **En ambas**, el Inspector plegado de las ventanas estrechas se despliega para llegar a sus botones.

Resultado:

| Auditoría | Vistas | Problemas |
|---|---|---|
| DS (Platino, Diamante, HeartGold, Negro y Blanco 2) | 844 | 0 |
| 3DS | 600 | 0 |

## Validación

- **`--nds-ui-probe` en los cinco juegos**, con siete comprobaciones nuevas:
  - la tabla de reglas trae líderes, Alto Mando y Campeón, e importantes;
  - el cap 50 con «Máx. 3» deja el equipo en tres Pokémon de nivel 50;
  - el escalado progresivo mueve a los entrenadores sin cap sin pasar de cap − separación;
  - la regla de movimientos sin estado deja movimientos mejorados sin movimientos de estado;
  - los iniciales empiezan líneas de 3 etapas;
  - Movimientos huevo es un formulario;
  - el Global Template reproduce los mismos cambios, incluidas las reglas.
- **Suites del CI:** todas pasan, también `--nds-only` (161) y `--nds-ui-only` (15).
- **Además:** `--synthetic-only` (4081) y `--all-ui-only` (408).

**Pendiente:** la prueba en el juego.

## Segunda parte: las opciones de randomizado de 3DS en DS

Cada sección de DS usa ahora las mismas opciones que su editor de 3DS:
- las mismas **claves**, así que caen en las mismas pestañas;
- los mismos **textos**;
- los mismos **valores por defecto**;
- los mismos **grupos**.

El núcleo DS se amplió para hacerlas funcionar. Lo que los juegos de DS no tienen (formas Mega, shinies, SOS, movimientos Z, calidad de Smart Items) no aparece. Las opciones propias de DS van en grupos aparte («Más opciones», «Al estilo de Universal Randomizer»).

| Sección | Opciones de 3DS | Cambios en el núcleo DS |
|---|---|---|
| Entrenadores | **Main**: multiplicar nivel, BST progresivo, legendarios de evento y del juego, generaciones, randomizar por BST, randomizar especies. **Estadísticas y movimientos**: movimientos (conservar, aleatorios, por nivel, Metrónomo), ataques potentes desde un nivel, sin daño fijo, mínimos ofensivos y STAB, habilidades (solo Gen5). **Entrenadores**: 6 Pokémon para importantes, mínimo y máximo por equipo, un tipo por entrenador, clases, evolución final desde un nivel. **Reglas**: IA máxima, IV al máximo, combates dobles, límites de nivel. **Priorizar mejor estadística** y **Movimientos mejorados**, por normales, importantes y jefes, con «Incluir MT». **Objetos equipados**: aleatorios, Smart Items por grupo, excluir poco útiles, no repetir. **Política global de Level Caps.** | `NdsTrainerTools`: tamaño máximo y 6 para importantes (respetando los filtros de especies), habilidad aleatoria en Gen5, ataques potentes, mínimos ofensivos y STAB, sin daño fijo, movimientos mejorados y mejor estadística por grupo, Smart Items por grupo, objetos poco útiles, cláusula de objetos. `Gen5RandomizerOptions`: generaciones, legendarios del juego y de evento por separado. |
| Encuentros | **Especies permitidas**: randomizar, generaciones, legendarios, de evento, BST similar con tolerancia, «BST más cercano», especies distintas por fila, BST progresivo. **Niveles**: multiplicador y ajuste plano. Más el mapeo global de UPR. | Tolerancia BST en porcentaje, BST más cercano, especies distintas por tabla, ajuste plano de niveles. |
| Estáticos | **Especies permitidas**: randomizar, generaciones, legendarios, de evento, BST similar con tolerancia, legendarios por legendarios. | Filtros de estáticos en `NdsDataOptions`. |
| Movimientos huevo | **Opciones originales**: priorizar STAB y porcentaje, incluir MO. Metrónomo como opción aparte. | MO excluidas del sorteo. |
| Level Up | **Randomizar todos los Pokémon**: permitir MO, sesgo STAB y porcentaje, comenzar con 4, distribuir niveles hasta un nivel, excluir daño fijo, Metrónomo. | Niveles distribuidos y 4 al nivel 1, como `LearnsetRandomizer`. |
| Evoluciones | **Ajustes personalizados**: randomizar destinos con tolerancia BST, intercambio a nivel con nivel elegible. Hora del día y nivel máximo en «Más opciones». | Nivel de intercambio y tolerancia configurables. |
| Pokémon | Variar estadísticas con porcentaje, barajar, tipos con probabilidad de un solo tipo, habilidades con Wonder Guard y Shadow Tag/Arena Trap, objetos equipados, fijar ratio de captura. | Modo `Vary`, probabilidad de un solo tipo, Wonder Guard opcional, excluir habilidades de atrapar, captura fija. |
| Movimientos | Tipos de movimientos; poder, precisión y PP en «Más opciones». | — |

**Templates.** «Cargar/Exportar template de entrenadores» y «Cargar/Exportar template Wild» aparecen también en DS. Guardan las opciones y, en entrenadores, las filas de las tablas de Level Caps y Move Rules. Las filas solo se restauran en el mismo juego. Con eso el editor común recuerda las opciones al cambiar de sección, como en 3DS.

**Registro de cambios.** Las reglas de equipo (objetos, IA, IV, movimientos) se registran como «Entrenador N · clase nombre», igual que el cambio de especie. Antes solo aparecían con la clase y el nombre.

**Compatibilidad.** Las opciones nuevas del núcleo tienen valores por defecto que reproducen el comportamiento anterior. Los Global Template y lotes de DS ya guardados dan el mismo resultado con su semilla.

## Arreglos en 3DS encontrados por el camino

En el randomizador de entrenadores de 3DS muchas etiquetas salían en inglés o con el nombre interno del control. Por ejemplo: «Event Legends», «Force Fully Evolved at Level», «Ensure at least # Damaging Moves», «Max IVs», «Use Levelup Only», «ForceHighPower» y «Stats/Moves». Ahora se muestran en español, con los mismos textos que DS.

## Validación de la segunda parte

`--nds-ui-probe` en los cinco juegos (44 comprobaciones en cada uno). Las nuevas comprueban sobre el resultado:
- **Entrenadores:**
  - el filtro de generación y legendarios;
  - el máximo de equipo con «6 Pokémon para importantes»;
  - la cláusula de objetos;
  - los ataques potentes desde un nivel;
  - las habilidades en Gen5.
- **Salvajes:** el filtro de generación y el ajuste plano de nivel.
- **Estáticos:** legendarios por legendarios.
- **Level Up:** 4 movimientos al nivel 1, niveles distribuidos y sin daño fijo.
- **Movimientos huevo:** sin MO del juego.
- **Evoluciones:** el nivel de intercambio.
- **Pokémon:** la variación de estadísticas, la captura fija y sin Wonder Guard.
- **Templates:** la ida y vuelta del template de entrenadores.

## Tercera parte: vistas de 3DS en DS, Smart Items por generación y Espada/Escudo

### Vistas

- **Level Up.** DS abre la misma `LevelUpView` de 3DS:
  - una tabla vertical por especie, con las tarjetas de opciones a la derecha;
  - «Metrónomo · todos», «Randomizar todos» con sus opciones y la exportación CSV.
  - Detrás está la interfaz `ILevelUpSource`, con dos implementaciones: `GameLevelUpService` (3DS) y `NdsLevelUpSource` (DS).
  - Las listas de DS ya pueden crecer o acortarse (`NdsLearnsets`: cada especie es un archivo del NARC). El límite es la lista más larga del juego original, porque el juego carga la lista en un búfer fijo.
  - Cada guardado es una operación `Learnset` del historial. Contiene las listas completas, así que el Global Template la repite igual.
- **MT y tutores.** La nueva vista `NdsMachineTutorView` tiene la disposición de 3DS:
  - pestaña MT: tabla vertical, «Asignación de MT» (Randomizar y Guardar) y «Compatibilidad de MT»;
  - pestaña de tutores, solo en los juegos que tienen tutores (Platino, HeartGold/SoulSilver y Blanco 2/Negro 2).
  - Negro y Blanco muestran la lista de MT sin poder editarla, porque está en el ARM9 DSi, que no se modifica. «TM Sanity» y «Follow Evolutions» sí funcionan en esos juegos, porque solo tocan la compatibilidad.
- **TM/Tutor Sanity y Follow Evolutions.** Son nuevas opciones de `NdsDataOptions` (`MachineSanity`, `MachineFollowEvolutions`, `TutorSanity`, `TutorFollowEvolutions`):
  - Sanity: la especie pasa a ser compatible con las MT y tutores cuyo movimiento aprende por nivel.
  - Follow Evolutions: la evolución hereda la compatibilidad de la preevolución.
  - Solo se añade compatibilidad, nunca se quita. Los templates antiguos no cambian, porque las opciones valen `false` por defecto.
- **Pokémon.** El editor DS muestra la tarjeta de cabecera de 3DS (sprite, nombre, tipos, BST y estado guardado) y el total de estadísticas en el Inspector. Usa la interfaz `IRecordSummary` del editor común. El total se actualiza mientras se editan las estadísticas.

### Smart Items adaptado a cada generación

- **DS: «Calidad Smart Items · jefes, importantes y normales».** Las tres opciones aceptan Normal, Fuerte o Competitivo, como en 3DS.
- **El objeto se elige con el moveset final.** Los objetos disponibles en cada generación son:
  - Restos, Baya Zidra y Baya Ziuela;
  - Cinta Fuerte y Gafas Especiales;
  - objetos Elegidos, solo para atacantes puros sin movimientos de estado;
  - Cinta Experto, Banda Focus y Vidasfera, esta solo con 3 o más tipos de ataque;
  - el potenciador del tipo STAB y Lodo Negro para los de tipo Veneno;
  - Hierba Única para los movimientos de carga, Hierba Blanca para los que bajan las propias estadísticas, y la roca del clima que usa;
  - Mineral Evolutivo, solo en Gen5 y solo para especies que todavía evolucionan.
- **Cómo filtra la calidad.** Normal usa los objetos de 60 puntos o menos, Fuerte usa todos y Competitivo solo los mejores.
- **Templates antiguos.** Repiten el algoritmo anterior: la calidad vale -1 por defecto.
- **Espada/Escudo: corrección de IDs.** Smart Items daba Cinta Experto, Refleluz y Toxiesfera donde debía dar Cinta Fuerte, Gafas Especiales y Cinta Experto. Los IDs correctos son 266, 267 y 268.
- **Espada/Escudo: objetos de Gen8.** Se añaden Mineral Evolutivo (con los datos de evolución), Hierba Única, Hierba Blanca, las rocas de clima y Botas Gruesas (tipos Fuego, Hielo, Volador y Bicho).
- **Espada/Escudo: lo demás.** El resto de opciones de entrenador ya tenía paridad: calidad por entrenador, alcance por regla y Move Rules.

### Validación de la tercera parte

- **Compilación:** 0 advertencias y 0 errores.
- **Suites de CI:** las 23 pasan, incluidas `--swsh-only` (316), `--swsh-ui-only` (112), `--swsh-parity-only` (239), `--nds-only` (161), `--nds-ui-only` (15) y `--synthetic-only` (4081).
- **`--nds-ui-probe`:** 50 comprobaciones por juego en Diamante, Platino, HeartGold, Negro y Blanco 2, con copias reales. Cubren:
  - la vista Level Up, el guardado de una lista más larga, Metrónomo sin escribir nada y Randomizar todos con 4 movimientos al nivel 1;
  - la vista MT/tutores y TM Sanity con Follow Evolutions;
  - la tarjeta de Pokémon;
  - Smart Items Competitivo;
  - la repetición del Global Template.
- **`--visual-audit-ds`** (Platino, Negro y Blanco 2): 484 vistas, 0 problemas y 0 avisos.
- **`--visual-audit`** (UltraSol): 600 vistas y 0 problemas. Los 80 avisos son de la extracción de prueba reducida (faltan archivos) y no tienen que ver con estos cambios.
- **Sin probar en juego.** Falta comprobar en el juego las listas de Level Up más largas, los objetos nuevos de Smart Items (DS y Espada/Escudo) y la compatibilidad añadida.

## Diferencias que siguen entre DS y 3DS

- **«Expandir cantidad» en Randomizar todos (Level Up).** DS conserva el tamaño de cada lista. A mano sí se pueden añadir movimientos, hasta el límite del juego.
- **Movimientos huevo.** «Ampliar lista» no existe: en Diamante y Platino la tabla está dentro de un overlay de tamaño fijo.
- **Habilidades de entrenador.** Solo en Gen5; en Gen4 el byte de habilidad no lo leen todos los juegos.
- **Clases de entrenador.** En DS «Randomizar clases» baraja los nombres de clase, no la clase (sprite y música).
- **Lista de MT de Negro y Blanco.** Solo lectura (ARM9 DSi).
- **Sin equivalente en DS:** opciones de Megas, shinies, SOS y movimientos Z.
