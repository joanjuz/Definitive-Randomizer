# Editor de mapas 3DS — Gen6 (X/Y, Rubí Omega/Zafiro Alfa)

Primer incremento del editor de mapas de 3DS. Muestra cada zona con la rejilla de permisos real de sus bloques y sus eventos (NPC, objetos, warps y activadores), seleccionables en el mapa o en la lista. **Es de solo lectura**: ningún control escribe en el proyecto.

## Arquitectura

- `pk3DS.Core/Maps/MapEditorContracts.cs`: contrato de presentación común (`IMapEditorBackend`, `IMapResourceProvider`, `IMapEventProvider`, `IMapMutationValidator`). Describe lo que ve el usuario y lo que permite cada adaptador; **no comparte formatos binarios** entre familias.
- `pk3DS.Core/Maps/Gen6/`: adaptador Gen6. Lee los archivos a través de delegados, sin tocar el proyecto en disco.
- `3DS.Progressive.App/Map3dsCanvas.cs` y `Map3dsEditorView.cs`: lienzo y vista. Solo hablan con el contrato. Ruta de navegación `MapEditor3ds` (Herramientas Pokémon → «Editor de mapas»), disponible en Gen6.

## Formatos verificados

| Archivo | X/Y | OR/AS | Contenido |
| --- | --- | --- | --- |
| Zonas (`encdata`) | a/0/1/2 | a/0/1/3 | paquete `ZO`: cabecera 0x38 (matriz en 0x04, ubicación en los 10 bits bajos de 0x1C), entidades + script, script de zona, encuentros |
| Matriz (`mapMatrix`) | a/0/4/2 | a/0/4/0 | paquete `MM`: anchura y altura en 0x04/0x06 y un índice de bloque u16 por celda (0xFFFF = vacío). Siguen más datos por celda que no se interpretan |
| Bloques (`mapGR`) | a/0/4/1 | a/0/3/9 | paquete `GR`: rejilla 40×40 de permisos (u32 por casilla), modelo BCH, malla de colisión y tres partes de 128 bytes |

Entidades: u32 de longitud (bytes desde ese campo hasta el script), recuentos u8 de objetos, NPC, warps y activadores, u32 de activadores de zona y registros de 0x14, 0x30, 0x18, 0x18 y 0x18 bytes. La longitud declarada coincide con los recuentos en todas las zonas legibles de las copias comprobadas; una zona que no cuadra se omite con su motivo.

Posiciones:

- NPC (0x28/0x2A), objetos (0x08/0x0A) y activadores (0x0C/0x0E): casillas de la zona.
- Warps (0x08/0x0A/0x0C): X, altura e Y en 1/18 de casilla. En OR/AS todos tienen tipo de coordenadas 0 y `X % 18 == 9` (centro de casilla). En X/Y, 197 warps tienen tipo 1 y valores libres: se muestran como posición aproximada.
- En X/Y, las zonas 63–69 (Ciudad Luminalia), 191 y 215 guardan NPC y objetos con valores que no son casillas. Cuando la mayoría de NPC y objetos de una zona cae fuera de su matriz, ninguna posición de esa zona se dibuja; los eventos aparecen en la lista con un aviso.
- La orientación de los NPC (0x0C) toma valores 0–7 y se muestra como valor bruto: su correspondencia con las direcciones no está verificada.

## Pruebas

| Nivel | Opción | Resultado |
| --- | --- | --- |
| Sintética | `--map3ds-gen6-only` | 17 comprobaciones |
| Interfaz fuera de pantalla | `--map3ds-gen6-ui-only` | 10 comprobaciones |
| Copia real, solo lectura | `--map3ds-gen6-probe` | X e Y: 360 zonas, 4626 eventos; OR/AS: 534/536 zonas, ~5000 eventos; todos los bloques GR usados legibles; los GARC de origen no cambian |
| Captura real | `--map3ds-gen6-ui-probe` | Vaniville con 16 eventos dibujados |
| Juego | — | no aplica (solo lectura) |

## Limitaciones

- No se dibujan todavía los modelos BCH ni las texturas; la vista es esquemática (rejilla de permisos).
- Sin edición. Cada campo editable necesitará su propia verificación y prueba en el juego.
- Sol/Luna y Ultra Sol/Ultra Luna usan otros formatos (paquetes `BG` y modelos GFL2) y necesitan su propio adaptador.
