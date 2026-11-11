# Proyectos DS persistentes con autoguardado

**Requisito para Definitive Randomizer 1.0.0.** Un proyecto asociado a una ROM `.nds` que se crea al abrirla, se guarda solo después de cada operación confirmada y se recupera al reabrirla, de modo que se pueda trabajar varios días sin perder nada y exportar la ROM jugable solo cuando se pida.

Este documento reúne la auditoría técnica, la arquitectura aprobada, las mediciones que justifican el formato y el plan por PR. **El PR 1 (este) solo define y acredita el formato; nada del autoguardado está conectado todavía a las operaciones ni a los botones.**

## 1. Decisiones aprobadas

| Tema | Decisión |
|---|---|
| Archivo | `Juego.nds.drproj`, junto a la ROM; respaldos `.drproj.bak1` a `.bak3`; bloqueo `.drproj.lock`; archivo dañado conservado como `.drproj.damaged-<UTC>` |
| Creación y reanudación | Automáticas al abrir y verificar la ROM (B2) |
| ROM original | Siempre intacta; no se duplica |
| Autoguardado | Después de cada operación confirmada; las vistas previas nunca se guardan |
| Contenido | Cambios generales DS, eventos, scripts, configuraciones e historial |
| Fuente de verdad | Las tablas binarias modificadas. Las recetas, semillas y listas de cambios son metadatos y alimentan las funciones existentes (historial, Global Template) |
| Eventos | Compatibles con los checkpoints v1, v2 y v3 (se importan y se exportan idénticos) |
| Exportar ROM | Solo por acción explícita |
| Deshacer | Sin deshacer general de tablas en esta fase |

## 2. Auditoría del estado actual

**`NdsProject`.** Todo el estado vive en memoria: las tablas modificadas (`_files` por ruta de ROM, `_overlays`, `_recompress`, `_arm9`) y una lista de `NdsOperation(Action, Seed, Changes, Recipe)`. Todas las ediciones y randomizaciones entran por `Apply(NdsPreview)`; una `NdsPreview` pertenece a una revisión y no toca el estado hasta aplicarse. Solo existe `Reset()` para descartar. `Export` escribe la ROM nueva y su registro, y la ROM de origen permanece abierta con `FileShare.Read`.

**`NdsEventSession`.** Historial propio de operaciones de Platino (mover, añadir con script opcional, eliminar un NPC añadido). Cada guardado publica un checkpoint inmutable v1, v2 o v3, se relee y se compara; `Replay` es determinista y está validado. Tiene deshacer (quitar la última operación). Está excluido de Global Template y Batch (`CaptureGlobal` lo rechaza).

**Qué se reconstruye por receta y qué no**

| Operación | ¿Reconstruible solo con la receta? | Decisión |
|---|---|---|
| Edición manual (`Edit`, `TrainerEdit1`, MT y tutores) | Sí, función pura de enteros por clave | Se guarda el resultado igualmente |
| Level Up (`Learnset`) | Sí, la receta lleva las listas completas | Igual |
| Eventos (mover, añadir, eliminar) | Sí en v1–v3, con los bytes de script incluidos | Operaciones y, además, los bytes de eventos y scripts |
| Randomizaciones (`Species`, `Trainers`, `Data`) y `Tweaks` | Solo en teoría: usan semilla y alcance, y la elección depende de la versión del código y del estado previo | **Se guardan las tablas resultantes** |
| ARM9, overlays y recompresión (Gen4) | No, son bytes derivados | Se guardan como tablas |
| `Reset` | No es aditiva | Registrada como nueva generación; el respaldo conserva la anterior |

No se promete reconstruir randomizaciones solo desde sus parámetros: un cambio de algoritmo entre versiones alteraría trabajo de días.

**Riesgos detectados**
- La fusión de scripts con el randomizador (`MergeScriptArchive`) solo falla al exportar: hay que validarla al confirmar cada operación (PR 3).
- `BuildBatch` abre proyectos desde la ROM: deberá abrirlos sin persistencia para no crear archivos ni bloqueos junto a la ROM (PR 3).
- El modo romhack debe guardarse en el proyecto y reaplicarse al reabrir (`NdsProjectRom.IsRomhack`).
- Referencias reutilizables de 3DS: el bloqueo con `ExclusiveFile` y el reemplazo atómico; no se reutiliza `RecoveryJournal` (pensado para varios archivos).

## 3. Decisión A: instantánea completa, sin diario

Se midió en cinco perfiles reales ([PROYECTOS_DS_PERSISTENTES_MEDIDAS.md](PROYECTOS_DS_PERSISTENTES_MEDIDAS.md)).

| Medida | Resultado (Platino, Diamante, HeartGold, Negro, Blanco 2) |
|---|---|
| Tamaño bruto de las tablas que cambia una operación | 25–260 KiB; «Ajustes del juego» es la mayor (200–260 KiB) |
| Tamaño comprimido (Brotli rápido) de una operación | 10–177 KiB |
| Estado acumulado tras las seis operaciones | 320–411 KiB brutos, **181–233 KiB comprimidos** |
| Instantánea completa duradera (comprimir, escribir, `Flush`, relectura con SHA-256, reemplazo) | **43–54 ms** |
| Registro incremental duradero (añadir y `Flush`) | **39–46 ms** |
| Leer, descomprimir y verificar el estado completo | 2 ms |

**Conclusión.** En el disco mecánico de las ROM el coste lo domina el vaciado a disco (~40 ms), no el tamaño: escribir 230 KiB cuesta lo mismo que añadir 15 KiB. En un SSD NVMe ambos bajan a 5–9 ms y 1–3 ms (revisión del 10/11/2026 en el informe de medidas): la diferencia absoluta es de milisegundos. Un diario no ahorra tiempo apreciable y exige tolerar colas truncadas, compactar y recuperar en dos fases. Se adopta la alternativa más simple: **un único archivo, instantánea completa con reemplazo atómico y tres generaciones anteriores como respaldo**. Si el estado creciera (eventos y textos), la prueba de escala del PR 1 (6 MiB brutos, 5000 operaciones: 30 ms para serializar y 35 ms para leer y verificar) y la sonda de PR 2 lo vigilan; solo si una medición real lo contradijera se reabriría esta decisión.

## 4. Formato (versión 1)

Un archivo autoverificable (`NdsProjectCodec`):

```text
cabecera (64 B)  «DRDSPRJ1», versión del contenedor, lector mínimo, generación, nº de operaciones,
                 longitud del manifiesto, nº de tablas, hora UTC, longitud total, CRC-32
manifiesto       JSON UTF-8 (NdsProjectMeta): identidad de la ROM, eventos, ajustes, índice de tablas
tablas           [u32 longitud][bytes] en el orden del índice (Brotli o crudas; el SHA-256 de los bytes crudos está en el índice);
                 una de ellas, «history», es la lista de operaciones con sus cambios
pie (40 B)       «DRDSEND1» + SHA-256 de todo lo anterior
```

- **Versionado.** `FormatVersion` y `MinReaderVersion`: un lector con versión inferior rechaza el archivo (`NewerFormat`) sin tocarlo; los campos desconocidos se conservan (`Extra`) al reescribir, tanto en la raíz del manifiesto como dentro de la identidad de la ROM, de cada operación y del historial de eventos. No se conservan dentro de una operación de eventos, de un cambio ni del índice de tablas (sus formas son fijas): una versión futura que necesite un campo ahí debe subir `MinReaderVersion`. La cabecera y el manifiesto deben declarar el mismo lector mínimo.
- **Integridad.** CRC-32 de la cabecera, SHA-256 del archivo, SHA-256 de cada tabla, coherencia cabecera–manifiesto, límites de tamaño antes de reservar memoria (manifiesto 32 MiB, 4096 tablas, 256 MiB por tabla, 1 GiB total), descompresión acotada a la longitud declarada. Leer nunca lanza una excepción por contenido malo: devuelve `Truncated`, `NotAProject`, `NewerFormat`, `Corrupt` o `Invalid` con un mensaje. `Truncated` se reserva para lo que podría ser el principio de un proyecto (también un archivo vacío); cualquier otro contenido es `NotAProject`, sea cual sea su longitud.
- **Manifiesto completo.** Unas sumas válidas no bastan: `NdsProjectCodec.Validate` exige que el manifiesto esté completo y sea coherente (generación ≥ 1, identificador de proyecto, identidad de ROM con SHA-256 y SHA-1 bien formados, operaciones con receta y cambios, operaciones de eventos con su contenido, listas presentes). La misma regla se aplica al escribir, así que no se produce un archivo que luego no se pueda leer, y un documento leído nunca tiene partes ausentes.
- **Cabecera útil tras un daño.** La generación y el número de operaciones están en la cabecera protegida: si el cuerpo se daña, la recuperación puede decir cuántas operaciones podrían perderse.
- **Historial fuera del manifiesto.** Las operaciones (acción, semilla, receta y lista de cambios) se guardan como una tabla comprimida más, `history` (un arreglo JSON), protegida por su SHA-256 y validada igual que el manifiesto; un proyecto sin operaciones no la tiene. Medido con el contenedor real en cinco volcados: una sola randomización registra miles de cambios, y con el historial dentro del manifiesto seis operaciones ocupaban 1,85 MiB sin comprimir y 36 superaban el límite de 32 MiB (el proyecto habría dejado de guardarse). Como tabla: 180–219 KiB con seis operaciones y 1,7–2,9 MiB con 36 operaciones y 190 000–300 000 cambios. El JSON de cada operación confirmada se calcula una sola vez, así que guardar cuesta la operación nueva y no todo el historial (las operaciones confirmadas no se modifican).
- **Determinista.** Las tablas se ordenan por nombre; documentos iguales producen bytes iguales.
- **Eventos.** `NdsProjectEvents` guarda las operaciones y la auditoría con la forma de los checkpoints; `FromCheckpoint` importa v1, v2 y v3 y `ToCheckpoint` los exporta idénticos (la versión se deduce con la misma regla de `NdsEventSession`). Los bytes de eventos y de scripts se guardan además como tablas para poder verificar la repetición.

## 5. Contrato de persistencia

**Almacén (`INdsProjectStore`).** Síncrono y **duradero al volver**: no hay cola ni guardado en segundo plano, así que no queda nada pendiente que perder al cerrar y no hace falta una barrera de finalización.
- `Load()`: nunca modifica; devuelve `Empty`, `Loaded`, `Damaged` (con un plan de recuperación) o `NewerFormat`.
- `Commit(siguiente, generaciónEsperada)`: compara y sustituye sobre la generación. Si el archivo cambió fuera de la sesión, `StaleGeneration`; el resultado se relee y se verifica antes de tocar el anterior; cualquier error distinto de `OutcomeUnknown` garantiza que nada duradero cambió.
- `Recover(candidata)`: conserva el archivo dañado con otro nombre (nunca lo borra) e instala la copia válida elegida; se niega si el principal es válido.
- La generación no es la única condición: el archivo guardado debe seguir siendo **el mismo proyecto de la misma ROM** (`DifferentProject` si alguien puso otro en su lugar, aunque tenga la misma generación).
- Un principal escrito por una versión más reciente no está «dañado»: `Commit` y `Recover` lo rechazan con `NewerFormat` y no se sustituye por una copia anterior. `Recover` solo instala una copia que siga siendo uno de los respaldos del proyecto.
- `Commit` solo lanza `NdsProjectStoreException`; si un almacén deja escapar otra excepción ha roto el contrato y quien llama debe tratar el resultado como desconocido.
- Bloqueo: dos instancias sobre el mismo proyecto → `AlreadyInUse`.
- `InMemoryNdsProjectStore` especifica este comportamiento (incluidas fallas inyectadas: escritura fallida, escritura corrupta, resultado desconocido) y servirá a las pruebas de las capas siguientes.

**Transacción (`NdsProjectTransactor`, decisión B).** Orden fijo: *preparar* el estado resultante sin mutar la sesión → hacerlo *duradero y verificado* → solo entonces *publicarlo* en memoria. Si preparar o guardar falla con un error definitivo, no queda nada aplicado ni informado como guardado. Dos casos excepcionales **bloquean** nuevas operaciones hasta `Reconcile()`: el almacén informa de un resultado desconocido, o el guardado terminó pero la instalación en memoria falló; reconciliar vuelve a leer lo guardado y lo instala (aunque la operación interrumpida no llegara a guardarse), y se niega si lo guardado es otro proyecto. También es «resultado desconocido» cualquier excepción del almacén fuera de su contrato, y la falta de memoria al instalar bloquea igual que cualquier otro fallo de instalación (la operación ya es duradera). Una operación no puede cambiar el proyecto ni la ROM. Un conflicto (`StaleGeneration`, `DifferentProject`) no bloquea ni adopta nada: la sesión conserva su estado y no puede guardar hasta que el usuario decida (PR 5).

**Recuperación (decisión C).** Si el principal está dañado, truncado o falta, nunca se reemplaza en silencio: el plan lista las copias válidas (más reciente primero), cuántas operaciones podrían perderse (o «desconocido» si la cabecera tampoco es fiable) y el archivo dañado se conserva.

**Identidad y rutas (decisión D).** `NdsProjectRom` guarda SHA-256 y SHA-1 completos, código, revisión, juego, longitud, marca de romhack y la última ruta conocida. `NdsProjectAssociation.Evaluate` decide `Match`, `DifferentGame` o `DifferentRom`; un nombre o una carpeta nunca son evidencia, así que una ROM movida o renombrada se localiza de nuevo y se comprueba por contenido, y una ROM distinta no se asocia jamás. `NdsProjectRomIdentity.Compute` reutiliza `NdsRom` y los perfiles admitidos, y coincide con el detector universal (B2).

**Rendimiento y concurrencia (decisión E).** Cada operación cuesta del orden de 50 ms de escritura. Se ejecutará fuera del hilo de WPF con el bloqueo de interacción existente (`UiProgress`); como la escritura es síncrona y duradera, cerrar la ventana solo debe esperar a la operación en curso.

## 6. PR 1: qué incluye y cómo se acredita

Código en `3DS.Progressive.App/Services/Nds/Project/` (sin tocar motores, scripts ni el renderer): `NdsProjectModel`, `NdsProjectCodec`, `NdsProjectIdentity`, `NdsProjectStore` (contrato y almacén en memoria) y `NdsProjectTransactor`. Pruebas: `--nds-project-format-only` (133 comprobaciones sintéticas, en el CI) y la sonda `--nds-project-measure` (real, solo lectura).

| Área | Qué se prueba |
|---|---|
| Ida y vuelta | todos los campos, proyecto vacío, bytes deterministas y estables tras recargar, campos futuros conservados, 40 tablas, operaciones convertibles sin alias |
| Truncado | **todos** los prefijos de un proyecto se rechazan como truncados y sin excepción |
| Corrupción | **todos** los bytes volteados se detectan; 1500 daños de bits aleatorios; 300 archivos basura; bytes añadidos; cabecera dañada |
| Manifiesto hostil | 31 manifiestos con sumas válidas y contenido incompleto o contradictorio (nulos en la ROM, las operaciones, los eventos o el índice; versiones discordantes; generación 0 o negativa): todos `Invalid`, ninguno lanza |
| Historial | tabla comprimida y no parte del manifiesto; 13 historiales con sumas válidas y contenido incompleto, mal formado, de más o de menos: todos `Invalid`; operaciones contadas en la cabecera pero ausentes; 2001 operaciones con 204 000 cambios en 1 MiB, con el guardado siguiente en decenas de milisegundos |
| Límites | tablas o manifiesto excesivos, longitud absurda, cabecera y manifiesto discordantes, tabla más larga que lo declarado, SHA-256 de tabla erróneo (con el resumen del archivo rehecho, para probar los controles estructurales) |
| Compatibilidad | checkpoints v1, v2 y v3 se importan, se guardan y se exportan idénticos; fuera de v1–v3 se rechazan; misma ROM y SHA-256 que el detector universal |
| Almacén | comparar y sustituir, tres respaldos, fallas inyectadas sin cambiar lo guardado, modificación externa, segunda instancia, reapertura tras liberar |
| Recuperación | copias ofrecidas por antigüedad, pérdida estimada, nada se reemplaza en silencio, archivo dañado conservado, continuar tras recuperar, principal truncado, ausente, sin cabecera fiable, respaldos dañados, formato nuevo intacto |
| Transacción | orden preparar–guardar–publicar; fallo al preparar; fallo y verificación fallida sin publicar; resultado desconocido (guardado o no) con bloqueo y reconciliación; instalación en memoria fallida; 400 operaciones con reaperturas y fallos, donde solo permanecen las confirmadas |
| Identidad | hashes completos, copia renombrada o movida coincide, un byte distinto es otra ROM, otro juego o revisión, ROM no admitida o ilegible, convención de nombres |
| Escala | 6 MiB brutos y 5000 operaciones: 30 ms y 35 ms |

**Auditoría del 10/11/2026 (antes de construir sobre el formato).** Defectos confirmados con una prueba que fallaba y corregidos en este mismo PR: (1) la lectura lanzaba `NullReferenceException` con un historial de eventos sin operaciones y aceptaba manifiestos incompletos con sumas válidas; (2) un principal de formato más reciente se trataba como dañado y una recuperación podía sustituirlo por un respaldo anterior; (3) con la misma generación se podía sobrescribir otro proyecto u otra ROM; (4) una excepción del almacén fuera de contrato se tomaba por «nada cambió», y la falta de memoria al instalar dejaba la sesión por detrás del archivo sin bloquearla; (5) solo se conservaban los campos desconocidos de la raíz del manifiesto; (6) un archivo corto ajeno se clasificaba como truncado; (7) el «0 KiB» del informe de medidas era una división entera; (8) el historial viajaba sin comprimir en el manifiesto y un proyecto con decenas de randomizaciones dejaba de poder guardarse (ahora es una tabla comprimida). La disposición del contenedor (cabecera, manifiesto, tablas, pie) no cambia; el punto 8 mueve las operaciones del manifiesto a una tabla, antes de que exista ningún archivo v1 publicado.

## 7. Plan de PR

1. **PR 1 (este).** Contrato, formato, identidad, almacén en memoria, transacción y mediciones.
2. **PR 2. Almacén en disco.** `NdsProjectFileStore`: temporal en la misma carpeta, `Flush(true)`, relectura y verificación, `File.Replace` con rotación de respaldos, bloqueo con un archivo abierto sin compartir (el sistema lo libera si el proceso muere), detección de cambios externos por generación e identidad del archivo, carpetas sin permisos de escritura (alternativa explícita en `%LOCALAPPDATA%`, con consentimiento), recuperación y conservación del dañado. Pruebas con fallas inyectadas y con un proceso hijo que se interrumpe, como `RecoveryChecks`.
3. **PR 3. Integración en `NdsProject`.** `Apply`, `Reset` y eventos (`NdsEventSession.Commit` sin checkpoint por operación) a través del transactor; validar la fusión de scripts al confirmar; Batch sin persistencia. Prueba clave: el proyecto reabierto es idéntico, byte a byte, al estado en vivo para cada tipo de operación y cada perfil.
4. **PR 4. Apertura desde B2.** Nuevo tipo `DsProject` en el detector, creación y reanudación automáticas tras verificar la ROM, apertura desde el `.drproj` si la ROM cambió de sitio (volver a localizarla y comprobar la identidad), importación de checkpoints, y cambio de proyecto sin «descartar» porque el trabajo ya está guardado.
5. **PR 5. Interfaz y resistencia.** Indicador (guardando, guardado, error, solo lectura, bloqueado por reconciliación), diálogos de recuperación y de conflicto, simulación de varios días con cierres forzados, sonda con entrada real, documentación y notas de 1.0.0.

## 8. Limitaciones y riesgos abiertos

- El PR 1 no escribe en disco: la durabilidad real (vaciado, reemplazo atómico, bloqueo del sistema) se acredita en el PR 2.
- No hay mediciones de Blanco (España); se cubre con las de Negro y Blanco 2 (misma estructura).
- Se midieron seis operaciones representativas; un uso con más módulos podría producir un estado mayor. La escala probada deja un margen de más de diez veces.
- La variante «solo miembros NARC que cambian» se descartó por innecesaria. El «0 KiB» que mostraba en ediciones manuales era una división entera (son 50–84 bytes); está explicado y corregido en el informe de medidas.
- Una tabla puede declarar hasta 256 MiB y el conjunto hasta 1 GiB: un archivo pequeño y hostil con sumas válidas puede hacer reservar esa memoria (acotada, no ilimitada). Los proyectos reales ocupan cientos de KiB.
- Los bytes comprimidos dependen de la implementación de Brotli de .NET: la igualdad byte a byte entre documentos iguales se garantiza dentro de una misma compilación; la integridad no depende de ello (el SHA-256 de cada tabla es de los bytes sin comprimir).
