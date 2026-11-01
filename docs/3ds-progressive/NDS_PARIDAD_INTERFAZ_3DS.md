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

## Diferencias que siguen entre DS y 3DS

Son opciones cuyo backend es distinto y que habría que alinear una por una:
- **Entrenadores:** 3DS usa las opciones del randomizador Progressive (filtros por generación, Event Legends, tolerancia BST, movimientos mejorados, mejor estadística y Smart Items por categoría). DS usa las de UPR-ZX (modo de especies, un tipo por entrenador, tamaño del equipo, IV, IA, dobles). DS tampoco tiene «Cargar/Exportar template de entrenadores».
- **Encuentros:** 3DS agrupa «Especies permitidas» por generación; DS tiene modo de especies y mapeo global.
- **Movimientos huevo:** 3DS ofrece «Ampliar lista», «Movimientos por especie» y «Porcentaje STAB»; DS ofrece «Aleatorios / Solo Metrónomo» y «Del mismo tipo (%)».
- **Encuentros estáticos:** 3DS tiene muchas opciones (generaciones, legendarios, niveles, objetos, habilidades…); DS tiene un único modo.
