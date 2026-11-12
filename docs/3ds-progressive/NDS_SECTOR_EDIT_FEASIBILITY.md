# Nintendo DS — edición de sectores: informe de factibilidad (fase 5C)

**Solo investigación. No hay código ni escritura de sectores.** Un «sector» es una región rectangular del mapa de **Platino USA CPUE/1** (en bloques de 32×32 tiles o en tiles). Se separan dos operaciones que no tienen el mismo riesgo.

## Datos relevantes (copia privada CPUE/1, solo lectura)

| Pieza | Dato |
|---|---|
| `map_matrix.narc` | 289 matrices, 1604 celdas (432 vacías, `0xFFFF`); cada celda referencia un miembro de `land_data` y, opcionalmente, una cabecera y una altitud |
| `land_data.narc` | 666 miembros, 16 125 840 bytes; cada uno = cabecera de 4 tamaños + 0x800 B de atributos de terreno + *map props* (≤ 32) + modelo NSBMD del terreno + BDHC |
| `zone_event.narc` | 534 miembros; coordenadas de eventos en tiles de la matriz; ≤ 51 NPC por archivo; scripts por ID local |
| Reconstrucción | `Narc.Write` + `NdsRom.Save` admiten archivos de otro tamaño (en su sitio o reubicados en espacio libre; la ROM tiene ≈ 24 MB libres tras el fin del contenido) |
| Datos de **área** | determinan qué modelos de edificios y qué texturas carga cada mapa; **no se leen** en el proyecto |

## A. Copiar eventos contenidos en una región — viable por fases

La infraestructura de la fase 5A ya añade registros de NPC con checkpoint v2, ID libre, validación espacial y reproducción determinista.

- **NPC dentro de la región: viable** como un lote de operaciones `AddNpc` (mismas reglas: tope de 51, IDs libres, script 0, flag 0, gráficos y movimientos acreditados, destino evaluado con `NdsSpatialQuery`). Requiere: una **propuesta de varias operaciones guardada de forma atómica** en un único checkpoint (hoy cada guardado publica una operación), el rectángulo de selección y un fantasma de grupo que conserve las distancias relativas. Un NPC con script se copia **sin** script (pierde su comportamiento): útil para figurantes, no para NPC de la historia.
- **Objetos de fondo (20 B), warps (12 B) y activadores (16 B): no recomendado todavía.** Los warps llevan mapa y ancla de destino, los activadores y los objetos referencian scripts por ID. Sin un analizador de scripts y sin reglas de numeración de warps, copiar o duplicar crearía referencias duplicadas o colgantes.
- **Pegar entre archivos de eventos distintos: no demostrable** (ver 5A): IDs, scripts y flags son locales a cada mapa.

## B. Copiar terreno, modelos, estructuras y colisiones — no viable todavía

Hay dos formas de «copiar un bloque» y ninguna es segura con los datos actuales:

1. **Apuntar una celda de la matriz a otro `land_data` existente** (edición mínima: solo cambia un u16 en `map_matrix.narc`). Serializable y reversible, pero: el bloque copiado queda **compartido** (editar uno cambia los dos); su terreno NSBMD, texturas y *map props* dependen de los **datos de área** del mapa destino — un prop cuyo modelo no esté en el área cargada se pone a 0 por el motor, y las texturas del terreno pueden faltar; las alturas BDHC deben encajar con los bloques vecinos; y los eventos de la región **no** se copian. Sin parsear los datos de área no se puede comprobar la compatibilidad.
2. **Clonar un miembro de `land_data`** (añadir un miembro de ~ 24 KB de media a un NARC de 16 MB, ajustar el contador, reubicar el archivo completo en espacio libre y apuntar la celda al nuevo miembro). Técnicamente alcanzable con `Narc.Write`/`NdsRom.Save`, pero exige además lo anterior, un presupuesto de memoria de carga por mapa que no se conoce y editar el `land_data` copiado de forma coherente (colisión, props, BDHC) sin poder comprobarlo visualmente.

Además faltan: el lector de **datos de área** y de la correspondencia ID de modelo → archivo, el lector de NSBMD/NSBTX para al menos comprobar cajas y texturas, BDHC, y una forma de **comprobar el resultado en el juego** (solo el emulador del propietario).

## Propuesta de implementación

| Etapa | Contenido | Condición para empezar |
|---|---|---|
| 1 | Selección rectangular de solo lectura con informe del contenido (eventos por tipo, props, tiles bloqueados/desconocidos) | ninguna; reutiliza lector, terreno y props |
| 2 | Copiar/pegar **NPC** de la región con checkpoint v2 (lote atómico de adiciones, fantasma de grupo) | propuesta multi-operación en `NdsEventSession`; confirmación en juego de 5A |
| 3 | Lector de datos de área y de la relación modelo → archivo; comprobador de compatibilidad de un bloque con un área | investigación de `area_data` y NSBMD con sonda de solo lectura |
| 4 | *Vincular* una celda a un `land_data` compatible (solo matriz), con bloque marcado como compartido y restauración | etapa 3 + validación visual y en juego |
| 5 | Clonar `land_data` y edición de terreno/colisión | etapas 3–4, BDHC y presupuesto de memoria investigados |

No se debe habilitar copia ni eliminación de terrenos hasta completar las etapas 3 y 4.

## Riesgos

Compartir sin querer un bloque entre cabeceras; modelos o texturas ausentes en el área destino (cierre del juego o gráficos corruptos); alturas BDHC discontinuas; superar la carga por mapa (≤ 32 props); desincronizar colisión y geometría; invalidar el perfil acreditado de `land_data`/matrices (CRC32 pinneados) para ROM exportadas; y la imposibilidad de revertir de forma exacta si se mezclan operaciones de matriz y de eventos sin un formato de checkpoint propio.
