# NDS fase 9: Nintendo DS integrado en la interfaz principal — 2026-10-31

Nintendo DS deja de tener una ventana propia y funciona como Espada/Escudo: Inicio → «Nintendo DS · Gen4/Gen5 (beta)» abre un **espacio de trabajo** dentro de la ventana principal, con barra lateral, la **misma vista de edición compartida** que 3DS y Switch (`RecordEditorView`) y la cabecera común: «Abrir proyecto» abre una ROM y «Exportar ROM…» la exporta. La ventana anterior (`NdsRandomizerWindow` y `NdsGen5Session`) se retira.

## Cómo funciona

- **Proyecto DS** (`Services/Nds/NdsProject.cs`): abre una ROM original, o un romhack con «Admitir romhacks» en Inicio. Las ediciones manuales y las randomizaciones se **acumulan** en memoria como tablas cambiadas, cada una con su vista previa. «Exportar ROM…» escribe una ROM nueva y un `.cambios.txt` con cada operación en orden y su semilla. La ROM original no se modifica; «Descartar cambios pendientes» vuelve al estado abierto.
- **Secciones** (`Services/Nds/NdsRecordEditor.cs`, contrato `IProjectEditor` de la vista compartida):
  - Entrenadores, con la presentación de equipos de 3DS/Switch;
  - Encuentros, con la presentación de encuentros;
  - Iniciales, Objetos del mapa y Tiendas.

  Cada sección permite editar a mano entrada por entrada (especies, niveles, objetos y movimientos con sus nombres) y randomizar con el alcance de la vista: todas las entradas, las filtradas o la actual. Lo que no se puede editar no aparece: el número de Pokémon de un equipo es fijo, igual que el contenido de las tiendas fijas (sí se pueden editar a mano). Si una sección no está disponible en un romhack, se muestra el motivo en lugar del editor. En DS se ocultan las tablas de Level Caps y de Trainer Move Rules del inspector de Entrenadores, que todavía no existen para DS.
- **Núcleo** (`pk3DS.Core/NDS`):
  - `NdsCells` lee y escribe cada sección como celdas (entrada, grupo, campo, valor y rango), como el modelo de Espada/Escudo.
  - `Gen5Data.WithDraft` y `Gen4Data.WithDraft` leen las tablas ya cambiadas antes que las originales, así que cada operación parte de las anteriores.
  - `Scope` limita una randomización a las entradas elegidas.
- **Parches de código idempotentes**, para poder cambiar los iniciales varias veces:
  - el salto del Pokédex de Gen5 se detecta frente al script original y no se repite;
  - el parche de la pantalla de elección de Diamante/Platino se rehace siempre desde el overlay original;
  - los patrones de los scripts del rival de Gen4 tratan la especie como comodín y se comprueba aparte que es el inicial actual.

## Validación

- `--nds-only` (sintético, CI): 91, con 6 nuevas de celdas Gen4: lectura, escritura releída con un borrador y rechazo de valores fuera de rango. `--nds-ui-only`: 15, que incluyen:
  - barra lateral y nombres accesibles;
  - secciones que exigen una ROM;
  - romhack con una sección no disponible;
  - campos de entrenador con las claves y nombres de la vista compartida;
  - edición manual acumulada, randomización limitada a una entrada, una propuesta que no se aplica dos veces;
  - exportación con todos los cambios.
- `--nds-ui-probe` con los cinco juegos originales: 10/10 cada uno. Cada sección abre en la vista compartida; se acumulan seis operaciones (entrenador editado a mano, encuentros, iniciales dos veces, objetos y tiendas) y una sola exportación.
  - Al releer, los iniciales son los de la segunda edición.
  - El entrenador editado conserva su cambio tras las randomizaciones posteriores.
  - En Gen5, el script del Pokédex solo lleva el parche una vez.
  - El original no cambia.
- Sin cambios en el resto: `--nds-gen4-probe` 60, romhacks 24, Gen5 16/14/20/20/21.

## Siguiente

Fase B, datos nuevos editables en el mismo espacio de trabajo: Pokémon (estadísticas, tipos, habilidades, ratio de captura), Movimientos, Level Up, Evoluciones, MT/MO, Estáticos e Intercambios. Después, las herramientas de randomización de UPR-ZX (fase C).
