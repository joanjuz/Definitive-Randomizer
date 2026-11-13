# Balance Eris-Ultranova como paquete opcional

El balance de movimientos y estadísticas usado durante el desarrollo de Eris-Ultranova era el valor por defecto de varias herramientas. Es una personalización de ese romhack (por ejemplo, Cerrojo Feérico convierte a todos los Pokémon activos en tipo Hada puro), no una regla general. Desde este cambio, **ninguna herramienta lo aplica si no lo eliges.**

Progressive BST, los randomizadores, Global Template, Batch, el editor de movimientos, Template Studio, la importación y exportación de CSV/JSON y los parches de combate no cambian. Los parches siguen disponibles: cualquier CSV puede pedirlos en la columna `BattlePatch`.

## Qué cambió

| Punto | Antes | Ahora |
| --- | --- | --- |
| `custom_balance_templates/moves_gen7.csv` | 57 filas de Eris-Ultranova, 11 con mecánicas de combate | Solo cabecera y comentarios |
| `custom_balance_templates/pokemon_stats_gen7.csv` | Fila de Slaking | Solo cabecera y ejemplo comentado |
| Datos internos de Gen6 (`BuiltInMoveBalance`) | Se usaban si el CSV de Gen6 estaba vacío | Solo para reproducir recetas anteriores; también como `moves_gen6.csv` del paquete opcional |
| Movimientos → Balance y templates | Empezaba con «Balance Progressive incluido» y el botón «Usar Progressive» | Empieza sin template; hay que cargar un CSV. Sin CSV no hay vista previa |
| Casilla de mecánicas de combate (Batch) | Marcada por defecto (excepto Sol/Luna) | Desmarcada siempre; el texto de ayuda explica qué hace |
| Pokémon → Modificar Pokémon | «Aplicar estadísticas desde un template Progressive» con el archivo incluido | «Aplicar estadísticas desde un CSV que elijas»; sin CSV no hay vista previa |
| Template Studio · Movimientos y Estadísticas | «Importar Progressive» cargaba el balance de Eris | No aparece; «Importar…» abre cualquier CSV |
| Template Studio · aplicar un template con `BattlePatch` | Se instalaban sin preguntar | Se enumeran las mecánicas y se pide confirmación |
| Recetas de Global Template nuevas | Sin CSV se registraba el balance incluido | Sin CSV no se registra ningún balance |
| Motor heredado (LegacyBatch) | Al abrir sus editores añadía filas de Cerrojo Feérico, Pesadilla, Deseo, Meditación, Neblina y Conjuro al CSV compartido | Ya no escribe filas; los ejemplos que crea están comentados |

Se mantienen las importaciones de tiendas, objetos de campo y evoluciones: son catálogos y rutas de evolución del motor Progressive, no balance de combate.

## El paquete opcional

`plantillas-opcionales/eris-ultranova/` contiene `moves_gen7.csv` (idéntico byte a byte al archivo anterior), `moves_gen6.csv` (las mismas filas que antes estaban en el código), `pokemon_stats_gen7.csv` (idéntico al anterior) y `LEEME.md`, que documenta cada mecánica. Se distribuye en el ZIP, pero ningún código lo lee como valor por defecto.

## Compatibilidad con recetas anteriores

- Una receta guardada antes de este cambio con «Balance Progressive incluido» registró el balance sin ruta de archivo. Al cargarla, Batch muestra «Balance de una receta anterior: Eris-Ultranova (paquete opcional)» y la receta se reproduce igual: Gen7 usa el `moves_gen7.csv` del paquete (mismo SHA-256 que el registrado) y Gen6 conserva su comportamiento anterior.
- Los assets de una receta se buscan primero en sus carpetas habituales. El paquete opcional solo se usa si un archivo tiene **exactamente** el SHA-256 registrado en la receta; nunca sustituye un archivo distinto ni suple uno que falte.
- El motor heredado conserva el significado de la acción antigua «moves.balance» de recetas de WinForms: si el CSV compartido no tiene filas, lee el del paquete opcional.

## Pruebas

`--optional-templates-only` (en CI). Datos sintéticos y vistas fuera de pantalla; ninguna prueba en juego. `--template-studio-only` y las pruebas de templates siguen cubriendo el balance de recetas anteriores.
