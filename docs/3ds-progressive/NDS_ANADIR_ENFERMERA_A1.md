# Nintendo DS — A1: «Añadir enfermera» en el editor de mapas (Platino CPUE/1)

Entrega A1 de la [hoja de ruta del editor](HOJA_RUTA_EDITOR_Y_1_0.md). Permite colocar una **Enfermera Joy funcional** en cualquier casilla de cualquier mapa con tabla de scripts, sin tener que ir antes a un Centro Pokémon para copiarla. Se apoya en el PR #69 ([NDS_NPC_SCRIPTS_PHASE6.md](NDS_NPC_SCRIPTS_PHASE6.md)): el mismo script reconocido, el mismo enlace `AppendScript`, el checkpoint v3, la exportación y la restauración. **Sin prueba jugable de esta entrega** hasta que el propietario la confirme en el emulador.

## Uso

1. Abre una copia de la ROM CPUE/1 y entra en Nintendo DS → Eventos, en modo Mapa. Elige el mapa.
2. Pulsa **Añadir enfermera** en la barra de NPC, usa **Ctrl+E** con el mapa enfocado o haz clic derecho en una casilla y elige **Añadir enfermera aquí**.
3. Mientras mueves el ratón, el mapa muestra la enfermera fantasma y el veredicto de la casilla. Haz clic en la casilla; **Esc** cancela.
4. El formulario «NPC nuevo» de siempre se abre con:
   - **Comportamiento:** «Con comportamiento (enfermera)», preseleccionado.
   - **Plantilla:** «Enfermera Joy · plantilla de la ROM (…)».
   - **Las dependencias:** el script común 2002, la variable 0x8007 reasignada al ID nuevo y el script añadido al final del archivo de scripts del mapa.
   - **Una nota:** fuera de un Centro Pokémon no hay animación de la máquina.
5. **Previsualizar propuesta** → el NPC aparece como pendiente en el mapa → **Guardar cambios…** (checkpoint nuevo) → **Exportar ROM…**.
6. **Restaurar último cambio…** quita la enfermera y su script de forma exacta.

El portapapeles no cambia: «Añadir enfermera» no sustituye lo que hayas copiado.

## Plantilla acreditada

No se introduce ningún recurso nuevo. La plantilla es una **enfermera real de la propia ROM**: un NPC normal cuyo script local coincide **exactamente** con el de la enfermera del Centro Pokémon, se refiere a su propio ID y tiene un gráfico acreditado. Se elige de forma determinista (mapa con el ID más bajo y, dentro de él, el índice más bajo). En CPUE/1 las 18 enfermeras cumplen el patrón y la plantilla es la del Centro Pokémon de Jubileo (`C01PC0101`, NPC 3, gráfico 26). Si una ROM no tiene ninguna, el botón se desactiva y lo explica; no se inventa nada.

## Validaciones (las del PR #69)

- **ID:** el siguiente libre del archivo de eventos, editable y validado.
- **Casilla:** colisión de terreno, otros eventos, límites de la matriz y avisos de alturas sin evaluar.
- **Script:** el mapa destino necesita una tabla de entradas legible. En los mapas cuyo archivo de scripts es un script de nivel, el comportamiento se rechaza con el motivo y el resto queda intacto.
- **Reproducción del checkpoint:** comprueba de nuevo el patrón, el archivo, el número y el ID.
- **Compatibilidad:** los checkpoints v1 y v2 siguen leyéndose sin cambios. Una enfermera se guarda en v3.

## Pruebas

| Suite | Resultado |
|---|---|
| `--nds-npc-script-only` | 43 (5 nuevas: plantilla real, ROM sin enfermera exacta, enfermera que apunta a otro ID, mapa sin tabla de scripts, elección determinista) |
| `--nds-npc-script-ui-only` | 21 (9 nuevas: botón y atajo, colocación sin tocar el portapapeles, formulario con comportamiento, plantilla nombrada, dependencias, NPC pendiente, guardado v3, restauración exacta, cancelación) |
| `--nds-npc-script-probe` (copia real) | añade «Ruta 201»: plantilla entre las 18 enfermeras, enfermera nueva con su script, el resto de archivos NDS idénticos y restauración exacta; SHA-256 de origen intacto |
| `--nds-live-ui-probe` (entrada real, 1180×760) | Ctrl+E inicia la colocación y Esc la cancela; clic en el botón, clic en la casilla, previsualización con la enfermera pendiente, guardado y ROM exportada para el emulador |
| Regresión DS y visual | estructurales 91/45 (recuento del menú contextual actualizado a 11), arrastre 29/58, eventos 42/12, terreno 50/17, lienzo 32/33, layout 74, `--nds-only` 374, `--nds-ui-only` 27, `--visual-redesign-only` 187 |

Hallazgo para A3: con una ventana de 1024×640 el lienzo mide unos 180 px de alto. La sonda en vivo heredada (paso «Duplicar» del PR #68) no llega a la casilla prevista. No se corrige aquí.

## Prueba en el emulador (propietario)

1. En **R201** (Ruta 201), añade una enfermera en una casilla libre, guarda y exporta.
2. En el juego, con el equipo herido, habla con ella: debe preguntar y **curar** (PS y PP), probablemente sin animación de la máquina.
3. Comprueba que el resto de NPC de la ruta y la enfermera de Jubileo siguen bien.
4. Restaura, exporta y verifica que la enfermera desaparece.
