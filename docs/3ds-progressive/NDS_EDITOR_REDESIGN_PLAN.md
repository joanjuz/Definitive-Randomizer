# Nintendo DS — rediseño del editor de mapas: diagnóstico, referencia DSPRE y plan

Documento de decisión para **Platino USA CPUE/1**. Recoge el diagnóstico de la fase 5A (PR #66), lo que hace DSPRE en las operaciones equivalentes, la propuesta de copia entre mapas, la arquitectura del renderer real y la división del trabajo. Ninguna parte se ha validado dentro del juego salvo lo que el propietario confirmó.

## 1. Diagnóstico del PR #66 con la interfaz real

Se reprodujo el flujo del propietario con **ratón y teclado reales** (SendInput) sobre una copia de la ROM, en una ventana de 1180×760 (`--nds-live-ui-probe`). Las operaciones **sí se completaban**, pero la experiencia las hacía casi inutilizables:

| Hallazgo | Tipo | Efecto |
|---|---|---|
| Con el formulario «NPC nuevo» abierto, el lienzo quedaba en **~70 px** de alto (el formulario vivía bajo el mapa) | defecto de diseño | no se veía dónde quedaba el NPC |
| Tras previsualizar, el NPC nuevo **no aparecía en el mapa** (solo un texto de diferencias) | defecto | parecía que no había pasado nada |
| Duplicar y Pegar obligaban siempre a pasar por el formulario | fricción | más pasos que DSPRE |
| «Añadir» abría el formulario en la casilla contigua a la plantilla, sin elegir casilla en el mapa | defecto de diseño | había que teclear coordenadas |
| El clic derecho **no seleccionaba** el NPC bajo el cursor: el menú actuaba sobre la selección anterior | defecto | se copiaba otro NPC |
| Tras guardar, el lienzo **volvía a encuadrar** el mapa y se perdía la posición | defecto | desorientación tras cada guardado |
| Copiar entre mapas: **rechazado** | por diseño (5A) | contrario a lo pedido |
| Eliminar: solo NPC del último cambio, vía «Restaurar» | por diseño (5A) | no se podía quitar un NPC añadido antes |
| El encabezado seguía diciendo «No añade ni elimina registros» | texto obsoleto | confusión |

Las 75 + 52 pruebas de la 5A comprobaban la lógica, no la usabilidad: llamaban a métodos internos con un lienzo grande y fuera de pantalla. Por eso ahora hay una sonda local con entrada real y las pruebas de interfaz cubren el diseño compacto con el formulario abierto.

## 2. Cómo lo hace DSPRE (referencia; licencia AGPL‑3.0)

Revisado en `DS-Pokemon-Rom-Editor/DSPRE` (`ROMFiles/EventFile.cs`, `ROMFiles/MapFile.cs`, `ROMFiles/AreaData.cs`, `Editors/EventEditor.cs`, `Editors/MapEditor.cs`, `RomInfo.cs`). **DSPRE es AGPL‑3.0 y este proyecto GPL‑3.0**: no se copia código ni recursos; solo se usan hechos de formato, contrastados con pret/pokeplatinum y con la copia privada.

| Aspecto | DSPRE | Definitive Randomizer (tras esta corrección) |
|---|---|---|
| Añadir NPC | registro con valores por defecto (sprite 1, script 0, flag 0) en el centro de la celda, ID libre más bajo | casilla elegida en el mapa, plantilla visible y cambiable, script 0/flag 0, ID libre, validación espacial |
| Duplicar | copia **todos** los campos, incluidos script y flag, en la misma posición | copia apariencia y configuración; script/flag a 0; elige casilla; avisa si se superpone |
| Eliminar | quita el registro y reindexa, sin comprobaciones | solo NPC añadidos por el editor (cualquier posición); originales protegidos con explicación |
| Importar entre mapas | `EventFileImport` copia registros completos de otro archivo de eventos (los scripts quedan con números de otro mapa) | copia **básica** (modo A) con explicación de lo que se reinicia |
| Guardado | reescribe el archivo de eventos en la carpeta desempaquetada | checkpoint inmutable reproducible + exportación verificada |
| Render de mapa | OpenGL: terreno `land_data` (NSBMD) + edificios `build_model` (NSBMD) con texturas NSBTX por área | esquemático + colisión + posiciones de props (PR #67) |
| Sprites de NPC | `mmodel.narc` según la tabla de overworlds del juego | marcadores geométricos |

DSPRE prioriza la libertad del *romhacker*; aquí se mantiene la reversibilidad y se explica cada limitación, sin dar por buena una operación que no se puede demostrar.

## 3. Copiar NPC entre mapas

- **Modo A (implementado): apariencia y configuración básica.** Se crea un NPC **nuevo** en el mapa destino: se conservan gráfico, movimiento, dirección, visión y rangos; se **reinician** script y flag a 0 (pertenecen a otro mapa); el ID es uno libre del destino; la altura bruta se toma del NPC más cercano del destino (no se lee BDHC). El formulario lo explica antes de previsualizar. No se promete diálogo ni comportamiento.
- **Modo B (no implementado): con comportamiento.** Exige interpretar el script del NPC, sus flags, textos y referencias por ID local y remapearlos en el destino. Requiere un desensamblador de scripts acreditado (tabla de comandos de Platino) y reglas de reubicación; hasta entonces no se ofrece.
- **Cortar entre mapas** sigue rechazado: implicaría eliminar un NPC original.

## 4. Renderer real de mapas

### Datos comprobados en la copia privada (solo lectura)

| Pieza | Ubicación | Hecho |
|---|---|---|
| Área del mapa | byte 0 de la cabecera (411 → área **6**) | coincide con pret/pokeplatinum (`MapHeader`) y DSPRE |
| `area_data.narc` | 75 entradas de 8 bytes | área 6 = conjunto de texturas de edificios **0**, texturas del terreno **6** |
| `build_model.narc` | 590 modelos `BMD0` | el ID de modelo del *map prop* es el índice; los modelos 22, 23, 67 y 236 de T01 contienen `MDL0` y `TEX0` |
| `areabm_texset.narc` | 71 `BTX0` | texturas de edificios por conjunto |
| `map_tex_set.narc` | 74 `BTX0` | texturas del terreno por conjunto |
| `area_build.narc` | 71 listas | conjunto 0 lista los modelos cargados en el área (incluye 22, 23, 67, 236) |
| `land_data` | sección de modelo `BMD0` con `MDL0` | geometría del terreno de cada bloque |
| `mmodel.narc` | 470 entradas | sprites de NPC (la tabla gráfico → sprite está en el código del juego; pendiente de localizar) |

### Arquitectura propuesta

1. **Lectores independientes (Core, solo lectura):** `NitroModel` (BMD0/MDL0: nodos, materiales, formas y listas de comandos GX) y `NitroTextures` (BTX0/TEX0: texturas y paletas en los 7 formatos del DS, incluido 4×4 comprimido). Implementación propia a partir de la especificación del hardware (GBATEK) y de pret/pokeplatinum.
2. **Resolución por mapa:** cabecera → área → conjuntos de texturas; celda de la matriz → `land_data` → modelo del terreno; *map props* → `build_model[ID]`.
3. **Rasterizado ortográfico cenital por bloque** con WPF 3D fuera de pantalla (`Viewport3D` + cámara ortográfica, 16 px por tile): una imagen de 512×512 por bloque, en caché. El lienzo actual la dibuja **debajo** de eventos, colisión y estructuras, en el mismo espacio de tiles, así la selección y el arrastre de NPC no cambian.
4. **Capas:** «Gráficos del mapa» (terreno + edificios), colisión, estructuras, eventos; desactivar gráficos devuelve la vista esquemática.
5. **Errores visibles:** modelo o textura ausente o en un formato no soportado → se marca el bloque o el prop y se explica; nunca se inventa geometría.

### Entregas del renderer

1. ID de modelo → archivo y área → texturas, con sonda real (sin dibujar).
2. Lector BMD0/BTX0 con pruebas de ida y vuelta sobre la copia privada (recuento de vértices, texturas decodificadas).
3. Prueba visual de **Pueblo Hojaverde** (bloque 3,27) comparada con una captura del juego o de DSPRE aportada por el propietario.
4. Integración como capa con caché y rendimiento medido.
5. Sprites de NPC cuando se acredite la tabla gráfico → sprite.

## 5. PR #67 (estructuras)

Los conflictos con `main` son de **inserción contigua**: `NdsEventEditorView.cs` (casillas de capas y leyenda junto a los controles estructurales), `Program.cs`, `RunnerCatalog.cs`, `RUNNER_PRUEBAS.md` e `INDICE.md`. No hay conflictos de lógica. Propuesta: una rama de integración nueva desde `main` (después de este PR) que incorpore los commits de #67, resuelva esas inserciones y vuelva a ejecutar `--nds-map-props-*` y las suites de eventos; el PR #67 original se conserva como referencia.

## 6. Arquitectura del editor para lo que viene

| Capa | Contenido | Escritura |
|---|---|---|
| Eventos | NPC, objetos de fondo, warps, activadores | operaciones reproducibles en checkpoint v2 |
| Terreno/colisión | atributos de `land_data` | solo lectura |
| Estructuras | *map props* | solo lectura |
| Modelos/texturas | BMD0/BTX0 | solo lectura (render) |
| Matriz | celdas, cabeceras, alturas | solo lectura |

La selección y el portapapeles ya son tipados; la **selección múltiple y copiar grupos** requieren propuestas con varias operaciones guardadas de forma atómica en un solo checkpoint (extensión directa del formato v2). Objetos, warps y edición de scripts esperan a un análisis de scripts acreditado. Sectores completos: ver [NDS_SECTOR_EDIT_FEASIBILITY.md](NDS_SECTOR_EDIT_FEASIBILITY.md) (PR #67).

## 7. División en PR

| PR | Contenido | Aceptación |
|---|---|---|
| A (este) | corrección de NPC estructurales: colocación directa, NPC pendiente visible, menú contextual en el cursor, copia entre mapas modo A, eliminar NPC añadidos, vista conservada al guardar, sonda con entrada real | flujo del propietario con entrada real; suites `--nds-npc-*`, eventos, lienzo; en juego por el propietario |
| B | integración de #67 con `main` | `--nds-map-props-*` y regresión DS |
| C | lectores de área, BMD0 y BTX0 (sin render) | sonda real: todos los modelos de T01 y sus texturas decodificados |
| D | render cenital por bloque y capa «Gráficos del mapa» | comparación visual de Pueblo Hojaverde por el propietario |
| E | selección múltiple y grupos (propuestas multi‑operación) | guardado atómico y restauración exacta |
| F | sprites de NPC | tabla gráfico → sprite acreditada |
