# Editor de mapas 3DS — Gen7 (Ultra Sol/Ultra Luna), primer incremento

Ultra Sol/Ultra Luna se abren en el mismo editor de mapas que Gen6 (`Map3dsEditorView`), con un adaptador propio de solo lectura. Cada entrada de la lista es un **mundo** y se dibuja con sus modelos y texturas reales vistos desde arriba (la capa «Modelos/texturas» se activa por defecto porque Gen7 no tiene rejilla de permisos). **Los eventos de Gen7 todavía no se muestran**: su formato no está identificado.

## Formatos verificados (Ultra Luna)

| Recurso | Ubicación | Contenido |
| --- | --- | --- |
| Datos de campo por mundo | `encdata` a/0/8/3 (Ultra Luna; a/0/8/2 en Ultra Sol) | 11 archivos por mundo (336 mundos). Slot 1: paquete `BG` cuya parte 0 son las texturas del mundo (GFL2) y la 1 los shaders; slot 2: matriz `TR` |
| Matriz `TR` | slot 2 | u32 ancho, u32 alto, u32 tamaño de bloque (4200), dos u32 y registros de 12 bytes; el primer u16 es el identificador del bloque (0xFFFF = vacío) |
| Bloques | a/0/8/6 | paquete `BG` por identificador; las partes que empiezan por 0x15122117 son modelos GFL2 (objetos y estructuras en la parte 0, hierba de encuentros en la 1). Los vértices están en coordenadas del mundo: el bloque (c, f) está centrado en (c·4200, f·4200) |

- `pk3DS.Core/Maps/Ctr/Gfl2File.cs`: lector propio de solo lectura de modelos y texturas GFL2 (formato documentado por SPICA, gdkchan, dominio público). Las secciones llevan su longitud y lo desconocido se salta por longitud. Las cadenas con longitud prefijada pueden incluir el terminador. La transformación del modelo **no** se aplica: los vértices ya coinciden con la caja de la cabecera.
- `pk3DS.Core/Maps/Gen7/Gen7MapBackend.cs`: adaptador. Reutiliza el rasterizador cenital de Gen6 (`Gen6TopDownRenderer`), ahora con un rectángulo del mundo arbitrario y varios modelos.
- El rasterizador mezcla ahora los texels con alfa parcial (máscaras de capa) y solo descarta los casi transparentes (alfa < 16).

## Pruebas

| Nivel | Opción | Resultado |
| --- | --- | --- |
| Sintética | `--map3ds-gen7-only` | 10 comprobaciones (matriz `TR`, texturas GFL2, formatos desconocidos, adaptador) |
| Copia real, solo lectura | `--map3ds-gen7-render-probe` | Ultra Luna: 336 mundos; 1078 modelos y 8798 texturas leídos sin errores; mundos 0, 30 y 135 dibujados; los GARC de origen no cambian |
| Juego | — | no aplica (solo lectura) |

## Limitaciones

- **Falta el terreno de las ciudades y rutas principales**: en los mundos comprobados (0, 135) los bloques de a/0/8/6 contienen edificios, árboles, vallas y hierba, pero no las mallas del suelo; en el mundo 30 sí aparecen. El origen del terreno está pendiente de localizar.
- Sin eventos (NPC, warps, activadores) hasta identificar el paquete `ED` (17 tipos de registro, una entrada por zona del mundo).
- Sol/Luna no se ha comprobado y no se habilita.
