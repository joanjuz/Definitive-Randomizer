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
                 el historial va en bloques «history:000000», «history:000001»…: listas de operaciones con sus cambios
pie (40 B)       «DRDSEND1» + SHA-256 de todo lo anterior
```

- **Versionado.** `FormatVersion` y `MinReaderVersion`: un lector con versión inferior rechaza el archivo (`NewerFormat`) sin tocarlo; los campos desconocidos se conservan (`Extra`) al reescribir, tanto en la raíz del manifiesto como dentro de la identidad de la ROM, de cada operación y del historial de eventos. No se conservan dentro de una operación de eventos, de un cambio ni del índice de tablas (sus formas son fijas): una versión futura que necesite un campo ahí debe subir `MinReaderVersion`. La cabecera y el manifiesto deben declarar el mismo lector mínimo.
- **Integridad.** CRC-32 de la cabecera, SHA-256 del archivo, SHA-256 de cada tabla, coherencia cabecera–manifiesto, límites de tamaño antes de reservar memoria (manifiesto 32 MiB, 4096 tablas, 256 MiB por tabla, 1 GiB total), descompresión acotada a la longitud declarada. Leer nunca lanza una excepción por contenido malo: devuelve `Truncated`, `NotAProject`, `NewerFormat`, `Corrupt` o `Invalid` con un mensaje. `Truncated` se reserva para lo que podría ser el principio de un proyecto (también un archivo vacío); cualquier otro contenido es `NotAProject`, sea cual sea su longitud.
- **Manifiesto completo.** Unas sumas válidas no bastan: `NdsProjectCodec.Validate` exige que el manifiesto esté completo y sea coherente (generación ≥ 1, identificador de proyecto, identidad de ROM con SHA-256 y SHA-1 bien formados, operaciones con receta y cambios, operaciones de eventos con su contenido, listas presentes). La misma regla se aplica al escribir, así que no se produce un archivo que luego no se pueda leer, y un documento leído nunca tiene partes ausentes.
- **Cabecera útil tras un daño.** La generación y el número de operaciones están en la cabecera protegida: si el cuerpo se daña, la recuperación puede decir cuántas operaciones podrían perderse.
- **Historial fuera del manifiesto, en bloques.** Las operaciones (acción, semilla, receta y lista de cambios) se guardan como tablas comprimidas propias, `history:000000`, `history:000001`…, cada una un arreglo JSON de operaciones consecutivas protegido por su SHA-256 y validado igual que el manifiesto; un proyecto sin operaciones no tiene ninguna. Medido con el contenedor real en cinco volcados: una sola randomización registra miles de cambios, y con el historial dentro del manifiesto seis operaciones ocupaban 1,85 MiB sin comprimir y 36 superaban el límite de 32 MiB. Como tablas: 180–219 KiB con seis operaciones y 1,7–2,9 MiB con 36 operaciones y 190 000–300 000 cambios.
- **Bloques del historial.** Dónde termina un bloque depende solo de las operaciones: se recorren desde la primera y un bloque se cierra en cuanto pesa 8192 (una unidad por operación y otra por cada cambio registrado) o tiene 256 operaciones. Añadir operaciones nunca mueve una frontera anterior: solo el último bloque, el abierto, puede crecer. Por eso un guardado comprime únicamente ese bloque y reutiliza los cerrados con sus bytes exactos (una randomización completa suele cerrar su propio bloque; las ediciones manuales se acumulan en el abierto). Al leer, el bloque *n* debe llamarse `history:` más *n* con seis cifras y aparecer en ese orden: un hueco, un desorden, un nombre sin relleno, un bloque vacío o que no sea una lista es `Invalid`; uno dañado, `Corrupt`. Otras fronteras escritas por otro programa se aceptan si están en orden y se reescriben con la regla al guardar. El nombre único `history` de las compilaciones experimentales (nunca distribuidas) ya no se acepta.
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

Código en `3DS.Progressive.App/Services/Nds/Project/` (sin tocar motores, scripts ni el renderer): `NdsProjectModel`, `NdsProjectCodec`, `NdsProjectIdentity`, `NdsProjectStore` (contrato y almacén en memoria) y `NdsProjectTransactor`. Pruebas: `--nds-project-format-only` (134 comprobaciones sintéticas, en el CI) y la sonda `--nds-project-measure` (real, solo lectura).

| Área | Qué se prueba |
|---|---|
| Ida y vuelta | todos los campos, proyecto vacío, bytes deterministas y estables tras recargar, campos futuros conservados, 40 tablas, operaciones convertibles sin alias |
| Truncado | **todos** los prefijos de un proyecto se rechazan como truncados y sin excepción |
| Corrupción | **todos** los bytes volteados se detectan; 1500 daños de bits aleatorios; 300 archivos basura; bytes añadidos; cabecera dañada |
| Manifiesto hostil | 31 manifiestos con sumas válidas y contenido incompleto o contradictorio (nulos en la ROM, las operaciones, los eventos o el índice; versiones discordantes; generación 0 o negativa): todos `Invalid`, ninguno lanza |
| Historial | bloques comprimidos fuera del manifiesto; fronteras que no se mueven al añadir operaciones; serialización incremental idéntica a una nueva en cada paso; bloques cerrados con los mismos bytes en el guardado siguiente; reapertura en el mismo orden y con los mismos bytes; otras fronteras leídas y reescritas; hueco, desorden, bloque vacío, nombre sin relleno o antiguo `history`: `Invalid`; bloque dañado: `Corrupt` al abrir y al verificar; 13 historiales con sumas válidas y contenido incompleto, mal formado, de más o de menos: todos `Invalid`; operaciones contadas en la cabecera pero ausentes; 2001 operaciones con 204 000 cambios en 1 MiB, con el guardado siguiente en decenas de milisegundos |
| Límites | tablas o manifiesto excesivos, longitud absurda, cabecera y manifiesto discordantes, tabla más larga que lo declarado, SHA-256 de tabla erróneo (con el resumen del archivo rehecho, para probar los controles estructurales) |
| Compatibilidad | checkpoints v1, v2 y v3 se importan, se guardan y se exportan idénticos; fuera de v1–v3 se rechazan; misma ROM y SHA-256 que el detector universal |
| Almacén | comparar y sustituir, tres respaldos, fallas inyectadas sin cambiar lo guardado, modificación externa, segunda instancia, reapertura tras liberar |
| Recuperación | copias ofrecidas por antigüedad, pérdida estimada, nada se reemplaza en silencio, archivo dañado conservado, continuar tras recuperar, principal truncado, ausente, sin cabecera fiable, respaldos dañados, formato nuevo intacto |
| Transacción | orden preparar–guardar–publicar; fallo al preparar; fallo y verificación fallida sin publicar; resultado desconocido (guardado o no) con bloqueo y reconciliación; instalación en memoria fallida; 400 operaciones con reaperturas y fallos, donde solo permanecen las confirmadas |
| Identidad | hashes completos, copia renombrada o movida coincide, un byte distinto es otra ROM, otro juego o revisión, ROM no admitida o ilegible, convención de nombres |
| Escala | 6 MiB brutos y 5000 operaciones: 30 ms y 35 ms |

**Auditoría del 10/11/2026 (antes de construir sobre el formato).** Defectos confirmados con una prueba que fallaba y corregidos en este mismo PR: (1) la lectura lanzaba `NullReferenceException` con un historial de eventos sin operaciones y aceptaba manifiestos incompletos con sumas válidas; (2) un principal de formato más reciente se trataba como dañado y una recuperación podía sustituirlo por un respaldo anterior; (3) con la misma generación se podía sobrescribir otro proyecto u otra ROM; (4) una excepción del almacén fuera de contrato se tomaba por «nada cambió», y la falta de memoria al instalar dejaba la sesión por detrás del archivo sin bloquearla; (5) solo se conservaban los campos desconocidos de la raíz del manifiesto; (6) un archivo corto ajeno se clasificaba como truncado; (7) el «0 KiB» del informe de medidas era una división entera; (8) el historial viajaba sin comprimir en el manifiesto y un proyecto con decenas de randomizaciones dejaba de poder guardarse (ahora es una tabla comprimida). La disposición del contenedor (cabecera, manifiesto, tablas, pie) no cambia; el punto 8 mueve las operaciones del manifiesto a una tabla, antes de que exista ningún archivo v1 publicado.

## 7. PR 2: almacén en disco

`NdsProjectFileStore` cumple el contrato de `INdsProjectStore` con archivos reales (`3DS.Progressive.App/Services/Nds/Project/NdsProjectFileStore.cs`). Nada lo usa todavía: se conecta a las operaciones en el PR 3.

**Guardar.** (1) Se lee el principal y se comprueba que sigue siendo el mismo proyecto, de la misma ROM, en la generación esperada y con los mismos bytes que esta instancia vio por última vez. (2) El proyecto nuevo se escribe entero en `Juego.nds.drproj.tmp-…`, en la misma carpeta, con escritura directa y vaciado a disco. (3) Se relee del disco y se compara byte a byte con lo que se quería escribir. (4) La generación que se va a sustituir se copia como `.bak1` y las anteriores bajan (`.bak2`, `.bak3`); si `.bak1` ya es esa generación no se rota, así que un guardado fallido no consume respaldos. (5) El temporal sustituye al principal con **un único renombrado** (`MoveFileEx`, reemplazar y esperar a disco). (6) Se relee el principal. Todo lo anterior al paso 5 no toca el principal: cualquier fallo ahí es definitivo (`WriteFailed`, `VerificationFailed`, `ReadOnly`) y deja el proyecto como estaba. Desde el paso 5 la respuesta depende de qué es el principal después: si sigue siendo, demostrablemente, el anterior, es un fallo definitivo; si no, `OutcomeUnknown`.

**Por qué un renombrado y no `File.Replace`.** `ReplaceFile` puede terminar con el principal renombrado a respaldo y el nuevo sin colocar (el principal desaparece); con un renombrado que reemplaza, el principal es siempre la generación anterior completa o la nueva completa. El respaldo es una copia y no se vacía a disco: si un corte de luz lo deja a medias, sus sumas lo delatan y no se ofrece.

**Bloqueo.** `Juego.nds.drproj.lock` se mantiene abierto mientras una instancia usa el proyecto (otros pueden leer quién lo tiene, nadie más puede abrirlo para escribir) y se borra con su manejador; si el proceso muere, el sistema lo libera. Una segunda instancia recibe `AlreadyInUse` con el identificador del proceso que lo tiene. `OpenReadOnly` abre sin bloqueo y nunca escribe.

**Modificaciones externas.** Además de la generación y la identidad, el almacén recuerda el SHA-256 del principal que leyó o escribió por última vez: si otro programa lo reescribe con la misma generación, el siguiente guardado se rechaza (`StaleGeneration`) y el archivo no se toca. Un principal borrado con respaldos presentes se recupera, no se recrea; un proyecto borrado por completo no se recrea desde una sesión antigua. Si otro programa tiene el archivo abierto un instante (antivirus, sincronizadores), leer y sustituir se reintentan hasta 1 s.

**Recuperación.** `Recover` instala solo una copia que siga siendo uno de los respaldos, primero la hace duradera en un temporal, después aparta el principal dañado como `.damaged-<UTC>` (renombrado; nunca se borra ni se pisa, con sufijo si el nombre ya existe) y coloca la copia. Si el proceso se detiene en medio, falta el principal y los respaldos están intactos: se vuelve a ofrecer la misma recuperación.

**Carpeta sin permiso de escritura.** `NdsProjectLocator.Resolve` decide el lugar: junto a la ROM si la carpeta admite archivos (se comprueba creando uno, que desaparece con su manejador); si no, una ruta alternativa en `%LOCALAPPDATA%\DefinitiveRandomizer\Projects\<SHA-256 de la ROM>\<código>-rev<revisión>.nds.drproj`, que depende solo de la identidad de la ROM y por eso se reencuentra aunque la ROM se mueva o se renombre. Resolver no crea nada: usar la ruta alternativa para un proyecto **nuevo** requiere consentimiento (`NeedsConsent`), un proyecto existente en una carpeta de solo lectura se abre en solo lectura en vez de duplicarse (`ReadOnly`), y un proyecto presente en ambos lugares se informa como ambiguo (`Ambiguous`) para que no se elija uno en silencio. La interfaz de estas decisiones es del PR 4 y del PR 5.

**Cómo se acredita.** `--nds-project-store-only` (82 comprobaciones, en el CI) usa el sistema de archivos real de una carpeta temporal; `--nds-project-store-probe <carpeta nueva>` ejecuta lo mismo en el disco que se indique.

| Área | Qué se prueba, con archivos reales |
|---|---|
| Contrato | los mismos casos que el almacén en memoria: comparar y sustituir, tres respaldos, reapertura, almacén cerrado, bytes del archivo iguales a la serialización |
| Fallos inyectados | un fallo en cada uno de los seis pasos anteriores a la sustitución deja el proyecto anterior, sin temporales y sin gastar respaldos; un temporal con un byte cambiado, truncado, vacío o desaparecido se detecta al releer; un fallo tras la sustitución es `OutcomeUnknown` |
| Fallos reales | el principal abierto por otro programa (no se sustituye; se guarda al soltarlo), abierto en exclusiva (ilegible, no «dañado»), marcado de solo lectura, y un archivo de más de 1 GiB rechazado sin leerlo |
| Cortes de proceso | un proceso hijo se termina sin limpieza en cada uno de los siete pasos de un guardado: el proyecto encontrado después es siempre una generación completa, los respaldos son válidos y el siguiente guardado continúa y limpia el temporal abandonado |
| Concurrencia | de 16 aperturas simultáneas una obtiene el proyecto; 200 cierres y reaperturas seguidos; otro proceso lo retiene hasta que muere; cuatro procesos compiten varios segundos y la generación final es la suma exacta de los guardados que cada uno informó |
| Modificación externa | misma generación con otro contenido, generación más nueva, otro proyecto, otra ROM, principal borrado, proyecto borrado entero, formato más reciente |
| Recuperación | principal truncado a 0, 10, 64, la mitad y todos menos un byte; cuerpo dañado; respaldos dañados; sin principal; contenido ajeno; respaldo cambiado después de ofrecerse; dos recuperaciones en el mismo instante |
| Permisos | con una regla de acceso real que deniega crear archivos: apertura `ReadOnly`, lectura en solo lectura, ruta alternativa con consentimiento, la carpeta de la ROM intacta |
| Transacción | 300 operaciones sobre el almacén real con sesiones reabiertas y fallos en pasos al azar: solo quedan las confirmadas |

Resultados del 10/11/2026 en el equipo del propietario: 82 de 82 en un SSD NVMe (guardado duradero de 167 KiB: mediana 12 ms) y 82 de 82 en el disco mecánico donde están las ROM (mediana 49 ms).

**Lo que no se ha demostrado.** No se ha cortado la corriente: que un archivo vaciado y un renombrado con escritura directa sobrevivan a un corte de luz es la garantía de Windows y NTFS, y depende de que el disco no mienta sobre su caché. Tampoco se ha probado un disco lleno real (solo fallos inyectados en los mismos puntos), unidades de red, FAT/exFAT ni carpetas sincronizadas con la nube. Entre la comprobación del paso 1 y el renombrado del paso 5 queda una ventana de milisegundos en la que un programa externo podría reescribir el principal sin ser detectado; el bloqueo solo coordina instancias de esta aplicación.

## 8. PR 3: transacciones de las operaciones DS

Código: `NdsProject.Persistence.cs`, y cambios acotados en `NdsProject.Apply`/`Reset` y `NdsEventSession`. **La aplicación todavía no asocia ningún proyecto** (lo hará la apertura B2 en el PR 4): una sesión abierta con `NdsProject.Open` sigue siendo solo memoria, igual que antes.

- **Persistencia explícita.** `AttachPersistence(almacén, identidad, documento existente o nulo, …)` es la única forma de hacer persistente una sesión. Sin documento crea el proyecto (su primera generación, vacía, se escribe en ese momento); con documento lo instala tras comprobar que es de esa ROM. Batch, la repetición de Global Template sobre proyectos temporales y las pruebas no llaman a ese método y **no crean archivos junto a la ROM**.
- **`Apply`.** Las tablas resultantes (archivos, overlays, ARM9, overlays por recomprimir) se construyen aparte, el documento se guarda y se verifica, y solo entonces se instalan en la sesión junto con la operación del historial (acción, semilla, receta, cambios). Si el guardado falla, la sesión queda exactamente como estaba y la misma vista previa sigue siendo válida.
- **`Reset`.** Es una generación nueva y vacía; la anterior queda en los respaldos.
- **Eventos.** `NdsEventSession.Save` se divide en `Stage` (decide el resultado sin escribir ni cambiar la sesión) e `Install`. `NdsProject.SaveEvent(propuesta)` guarda en el proyecto el historial de eventos, los bytes de eventos y el archivo de scripts cuando difieren del original, **sin escribir un checkpoint**, e instala después. Las sesiones no persistentes siguen guardando checkpoints `.ndsevents.json` como hasta ahora, y el historial guardado conserva la forma de los checkpoints v1–v3.
- **Conflictos de scripts al confirmar.** Si una operación trae un archivo de scripts y el historial de eventos añadió scripts (o al revés), se comprueba al confirmar que no editan el mismo archivo de scripts, con la misma regla que usa la exportación; antes solo fallaba al exportar. Se aplica también a sesiones no persistentes cuando la operación general llega después de los eventos.
- **Reanudar.** Instalar un documento es todo o nada: se rechaza si alguna tabla u overlay no existe en la ROM, si el modo romhack no coincide, si trae un ARM9 en un juego donde no se modifica, si trae eventos en un juego sin soporte, si los scripts de eventos y de otro módulo no combinan, o si el historial de eventos **no reproduce byte a byte** los datos guardados. En ese caso la sesión no cambia.
- **Resultado desconocido.** `Apply`, `Reset` y `SaveEvent` lanzan `NdsProjectFaultedException` y la sesión no admite más operaciones hasta `ReconcilePersistence()`, que instala lo que haya en disco. Un conflicto (`StaleGeneration`, `DifferentProject`) rechaza la operación sin perder la sesión ni adoptar el otro contenido.
- **Coste.** El guardado es síncrono (12 ms en SSD y 49 ms en el disco mecánico medidos en el PR 2, más la serialización de la operación nueva). Se ejecuta donde se llame a `Apply`; moverlo fuera del hilo de la interfaz, si hiciera falta, es del PR 5.

**Cómo se acredita.** `--nds-project-transactions-only` (83 comprobaciones, en el CI), con ROM sintéticas de los seis perfiles abiertas como romhack (las tablas originales no son redistribuibles), y `--nds-project-transactions-probe <carpeta nueva> <ROM…>` con volcados privados en solo lectura (los archivos del proyecto se escriben en la carpeta de salida, nunca junto a la ROM).

| Área | Qué se prueba |
|---|---|
| Equivalencia | tras **cada** operación (edición manual de entrenador y de especie, randomizar entrenadores y salvajes, equipos y reglas, datos, movimientos por nivel, ajustes del juego donde aplican, segunda edición y segunda randomización) la sesión viva, el proyecto reabierto desde disco y una sesión sin persistencia tienen los mismos bytes: tablas, overlays, ARM9, recompresión e historial |
| Exportación | la ROM exportada por la sesión viva, por una sesión reanudada tras cerrar y por una sesión sin proyecto es idéntica byte a byte |
| Continuidad | el trabajo sigue tras reanudar; una vista previa obsoleta se rechaza sin tocar el proyecto; descartar es una generación vacía y la anterior queda en el respaldo |
| Fallos | escritura fallida y verificación fallida: la operación no se aplica y se puede reintentar; resultado desconocido: bloqueo, y reconciliar instala lo que quedó guardado; cambio externo: rechazo sin perder la sesión ni sobrescribir |
| Rechazos al asociar | otro juego, otro volcado, modo romhack distinto, tabla u overlay inexistentes, eventos sin soporte, sesión con cambios, segundo proyecto |
| Procesos temporales | Batch y las sesiones sin persistencia no crean `.drproj` ni bloqueos; un Global Template aplicado a un proyecto persistente da la misma ROM, una generación por operación, y la plantilla capturada es la misma tras cerrar y reabrir |
| Eventos | guardar sin checkpoint, reapertura idéntica, operaciones generales y de eventos intercaladas, deshacer hasta el original, historial que no reproduce los bytes u operación desconocida rechazados sin instalar nada, regla de combinación de scripts |

Resultados del 10/11/2026 con los volcados del propietario (Platino, Diamante, HeartGold, Negro y Blanco 2; Blanco no está disponible): 51 de 51. En los cinco se aplican los nueve tipos de operación. En Platino, además: una enfermera con su script añadido se guarda en el proyecto sin checkpoint; otra edición del mismo archivo de scripts se rechaza al confirmar, en los dos órdenes, sin guardar nada; la edición de otro archivo combina; la ROM exportada tras reanudar es idéntica; y deshacer devuelve los eventos y los scripts originales. Las ROM de origen no cambian y no aparece nada junto a ellas.

**No probado.** Nada de esto se ha jugado: son comprobaciones de bytes. La jugabilidad de la ROM exportada depende de los motores, que este PR no modifica.

## 9. PR 4: apertura con proyecto (B2)

Desde este PR la aplicación **sí crea y reanuda proyectos**: abrir una ROM DS desde «Abrir proyecto» (o desde el botón de apertura del editor DS, que ahora usa el mismo flujo) devuelve una sesión persistente. Código: `NdsProjectOpener`, `ProjectDetector` (tipo `DsProject`), `ProjectOpenCoordinator` y los puntos de la interfaz que preguntaban por cambios sin guardar.

**Qué ocurre al abrir una ROM DS.** El motor la abre y la verifica como siempre (ninguna comprobación cambia; un romhack sigue necesitando «Admitir romhacks»). Se calcula su identidad (SHA-256 y SHA-1 completos) y se busca su proyecto, **siempre por la identidad de la ROM que el proyecto registra y nunca por el nombre**:

1. `Juego.nds.drproj` junto a la ROM.
2. Otro `.drproj` de la misma carpeta que registre esta ROM y cuya propia ROM ya no esté a su lado (la ROM se renombró y el proyecto no).
3. La ruta alternativa derivada del SHA-256 (sección 7).

Si no hay ninguno se crea junto a la ROM. La ROM de origen no se modifica en ningún caso.

| Situación | Resultado |
|---|---|
| Hay un proyecto y es de esta ROM | Se reanuda: tablas, overlays, ARM9, historial y eventos |
| ROM y proyecto movidos o renombrados juntos | Se reanuda; el siguiente guardado anota la ruta nueva |
| Solo se renombró la ROM | Se encuentra su proyecto por contenido; no se crea un segundo proyecto |
| Una copia de la ROM junto a la original, que conserva su proyecto | La copia recibe su propio proyecto |
| El proyecto es de otro juego, otra revisión u otro volcado | Se rechaza; el proyecto no se toca y no se crea nada |
| Dos proyectos reclaman la misma ROM | Se rechaza sin elegir; el usuario abre el `.drproj` que quiera |
| Proyecto de una versión más reciente | Se rechaza sin tocarlo |
| Principal dañado, con copia válida | Se pregunta, indicando la copia, su fecha y cuántas operaciones se perderían; si se acepta se instala y el dañado se conserva |
| Principal dañado, sin copias | Se rechaza; nunca lo sustituye un proyecto nuevo |
| La carpeta de la ROM no admite archivos y no hay proyecto | Se pide consentimiento para guardarlo en la carpeta de usuario; sin él no se abre nada |
| El proyecto existe en una carpeta que ya no admite escritura | Se abre para lectura; las operaciones se rechazan con un mensaje, no se pierden en silencio |
| La misma ROM ya está abierta | Se rechaza («ya está abierto en esta ventana», o qué proceso lo tiene) y la sesión abierta sigue igual |

**Abrir un `.drproj`.** El detector lo reconoce por su firma y solo lo considera abrible si está entero y encuentra su ROM: en la ruta que usó por última vez, con el nombre del proyecto, o como otro `.nds` del mismo juego, revisión y tamaño en la carpeta del proyecto. El motor compara después el SHA-256 completo, así que una ROM parecida se rechaza sin tocar el proyecto. Si la ROM no aparece, el mensaje indica abrir directamente la ROM.

**Checkpoints antiguos.** Abrir un `.ndsevents.json` abre la ROM con su proyecto e importa el historial del checkpoint (v1, v2 o v3), tras validarlo y reproducirlo con las reglas de siempre. El archivo solo se lee. Importar el mismo historial otra vez no cambia nada; un checkpoint con un historial distinto del que ya tiene el proyecto se rechaza (no se mezclan). El historial importado se puede volver a exportar como el mismo documento.

**Interfaz.**
- El editor de eventos, con proyecto, guarda en él sin pedir un archivo de checkpoint; el texto de ayuda y el estado lo indican. Sin proyecto se comporta como antes.
- Cambiar de ROM o cerrar la aplicación ya no pregunta por «cambios sin exportar» cuando la sesión DS tiene proyecto, porque el trabajo está guardado. Una sesión sin proyecto (la carga directa en memoria que usan las pruebas) sigue preguntando.
- La pantalla de inicio del editor DS indica si la sesión tiene proyecto y su generación.
- Un guardado que falla llega a los editores como `NdsProjectSaveException` (una `InvalidOperationException`, como el resto de rechazos de una operación), de modo que cada vista lo muestra con su mensaje habitual y el cambio no se aplica. «Descartar cambios» también se guarda y, si no puede, no descarta.
- Si la apertura falla en cualquier punto, la sesión anterior se conserva: el proyecto preparado se libera con su bloqueo.

**Cómo se acredita.** `--nds-project-open-only` (43 comprobaciones, en el CI): cada fila de la tabla anterior con ROM sintéticas y archivos reales, una regla de acceso real para las carpetas sin permiso, el detector, la entrada del motor universal y la importación de checkpoints. `--universal-open-only` (102) cubre la ventana real: una ROM DS abre con su proyecto, reemplazarla no pregunta y no pierde nada, **reabrirla reanuda el trabajo**, y una sesión sin proyecto sigue pidiendo confirmación. `--nds-events-ui-only` (16) cubre el editor de eventos en modo proyecto, incluido un guardado fallido. La sonda con Platino real importa un checkpoint v3 (enfermera con script) y reanuda después sin él.

**Pendiente de comprobar a mano.** Nada de lo anterior sustituye abrir la aplicación: conviene ver en una ROM real que aparece `Juego.nds.drproj` junto a ella, que al cerrar y reabrir se continúa, y los textos de los dos diálogos nuevos (ubicación alternativa y recuperación).

## 10. PR 5: estado visible y resistencia

**Estado del proyecto.** `NdsProject.PersistenceState` resume en un valor lo que el usuario necesita saber, y el editor DS lo muestra siempre en la barra lateral («Estado del proyecto DS», con nombre accesible y anuncio no intrusivo). Solo aparece un botón cuando hay algo que decidir.

| Estado | Cuándo | Texto | Acción |
|---|---|---|---|
| Sin proyecto | Sesión solo en memoria | «Sin proyecto: los cambios solo están en memoria hasta exportar.» | — |
| Guardado | Toda operación confirmada está en disco | «Proyecto guardado · generación N» | — |
| Guardando | Mientras el almacén trabaja | «Guardando el proyecto…» | — |
| Error | El último guardado falló y el cambio no se aplicó | «Error al guardar: el último cambio no se aplicó…» | Repetir la operación |
| Solo lectura | El proyecto no se puede escribir | «Proyecto de solo lectura…» | — |
| Conflicto de escritura | El archivo cambió fuera, es otro proyecto o está dañado | «Conflicto de escritura: … no se sobrescribe.» | «Recargar el proyecto guardado» |
| Recuperación pendiente | No se sabe si el último guardado terminó | «Recuperación pendiente…» | «Reconciliar con lo guardado» |

«Recargar» muestra lo que hay en disco, sin sobrescribirlo, y solo si sigue siendo el mismo proyecto de la misma ROM (la sesión solo contenía operaciones ya guardadas antes del cambio externo); pide confirmación. «Reconciliar» instala lo guardado, llegara o no la operación interrumpida. «Guardando» se informa desde dentro del guardado; como el guardado es síncrono, en pantalla solo se llega a ver cuando la operación se ejecuta fuera del hilo de la interfaz (el editor de eventos).

**Resistencia.** `--nds-project-resilience-only` (50 comprobaciones, en el CI) aplica 300 operaciones por perfil en los seis perfiles (ediciones manuales de entrenadores y especies sobre celdas distintas, una randomización cada diez y una randomización de datos cada diez), cierra y reanuda desde disco cada 20 y hace fallar uno de cada nueve guardados en un paso al azar. Una sesión de control sin proyecto aplica solo las operaciones confirmadas.

| Qué se exige | Resultado (10/11/2026, por perfil) |
|---|---|
| Solo quedan las operaciones confirmadas | 207 confirmadas, 22 rechazadas, 4 de resultado desconocido reconciliadas, 16 sesiones (71 de las 300 no son aplicables a la ROM sintética) |
| Cada sesión reanudada es idéntica byte a byte a una que nunca se cerró | 15 reanudaciones por perfil, 0 diferencias |
| La ROM exportada tras la última reapertura es idéntica | sesión viva = reanudada = sin proyecto |
| El estado mostrado sigue cada fallo y cada recuperación | Guardado / Error / Recuperación pendiente en cada paso |
| Los tres respaldos son generaciones completas y consecutivas, sin temporales | sí |
| Tamaño del proyecto | 434–863 KiB con 50 000–83 000 cambios registrados |
| Guardar una operación | mediana 51–84 ms, máximo 146–276 ms (SSD) |

Los seis estados se comprueban además sobre el editor DS real fuera de pantalla: texto, botón visible solo cuando procede, reconciliar, recargar, y que otro proyecto puesto en su lugar no se carga.

`--nds-project-resilience-probe <carpeta nueva> <operaciones> <ROM…>` repite la resistencia con volcados privados en solo lectura. Con los cinco volcados del propietario y 200 operaciones por ROM (10/11/2026): 35 de 35; 135–143 operaciones confirmadas, 16 guardados fallidos y 11 sesiones por ROM; proyectos de 1,4–1,7 MiB con 145 000–171 000 cambios registrados (veinte randomizaciones completas cada uno); guardar una operación: mediana 140–174 ms, máximo 399–453 ms. Las ROM de origen no cambian y no aparece nada junto a ellas.

**Coste de guardar a medida que crece el proyecto (11/11/2026).** La medición previa atribuía el coste creciente a comprimir el historial entero; `--nds-project-save-bench` (cinco volcados, 40 randomizaciones completas alternando especies y datos, cinco ediciones manuales en cada punto, disco mecánico D:) mostró que lo que más pesaba era otra cosa: antes de sustituir el archivo, `Commit` lo volvía a leer **entero, historial incluido** (150 ms con 130 000 cambios, 310 ms con 260 000). Comprimir el historial costaba unos 30 ms con 20 randomizaciones y verificarlo otros 30. Se corrigieron las dos cosas: el historial va en bloques (solo se comprime el abierto; un bloque cuyos bytes ya se demostró que dan su SHA-256 no se vuelve a descomprimir al verificar un guardado), y `Commit` no relee un principal que es, byte a byte, el que el mismo almacén leyó entero o escribió y verificó (lo comprueba con su SHA-256 junto a la generación, el proyecto y la ROM; cualquier otro caso sigue la comprobación completa de siempre). Además, al abrir un proyecto sus bloques quedan reconocidos, así que el primer guardado tras reabrir ya no recalcula el historial. Confirmar una edición manual: con 20 randomizaciones, de 255–295 ms a 51–67 ms; con 40, de 453–529 ms a 71–138 ms; el primer guardado tras reabrir, de 400–558 ms a 57–68 ms (20) y de 788–966 ms a 78–125 ms (40). Lo que sigue creciendo es la escritura del archivo (instantánea completa: 1,1–1,3 MiB con 20 randomizaciones, 2,2–2,5 MiB con 40), a velocidad de disco, y la lectura del historial al abrir el proyecto (150–175 ms con 20, 300–340 ms con 40), una vez por apertura. Tablas completas en [PROYECTOS_DS_PERSISTENTES_MEDIDAS.md](PROYECTOS_DS_PERSISTENTES_MEDIDAS.md).

## 11. Plan de PR




1. **PR 1 (este).** Contrato, formato, identidad, almacén en memoria, transacción y mediciones.
2. **PR 2. Almacén en disco.** `NdsProjectFileStore`: temporal en la misma carpeta, `Flush(true)`, relectura y verificación, `File.Replace` con rotación de respaldos, bloqueo con un archivo abierto sin compartir (el sistema lo libera si el proceso muere), detección de cambios externos por generación e identidad del archivo, carpetas sin permisos de escritura (alternativa explícita en `%LOCALAPPDATA%`, con consentimiento), recuperación y conservación del dañado. Pruebas con fallas inyectadas y con un proceso hijo que se interrumpe, como `RecoveryChecks`.
3. **PR 3. Integración en `NdsProject`.** `Apply`, `Reset` y eventos (`NdsEventSession.Commit` sin checkpoint por operación) a través del transactor; validar la fusión de scripts al confirmar; Batch sin persistencia. Prueba clave: el proyecto reabierto es idéntico, byte a byte, al estado en vivo para cada tipo de operación y cada perfil.
4. **PR 4. Apertura desde B2.** Nuevo tipo `DsProject` en el detector, creación y reanudación automáticas tras verificar la ROM, apertura desde el `.drproj` si la ROM cambió de sitio (volver a localizarla y comprobar la identidad), importación de checkpoints, y cambio de proyecto sin «descartar» porque el trabajo ya está guardado.
5. **PR 5. Interfaz y resistencia.** Indicador (guardando, guardado, error, solo lectura, bloqueado por reconciliación), diálogos de recuperación y de conflicto, simulación de varios días con cierres forzados, sonda con entrada real, documentación y notas de 1.0.0.

## 12. Limitaciones y riesgos abiertos

- El PR 1 no escribe en disco: el almacén real y lo que se ha demostrado y lo que no están en la sección 7.
- No hay mediciones de Blanco (España); se cubre con las de Negro y Blanco 2 (misma estructura).
- Se midieron seis operaciones representativas; un uso con más módulos podría producir un estado mayor. La escala probada deja un margen de más de diez veces.
- La variante «solo miembros NARC que cambian» se descartó por innecesaria. El «0 KiB» que mostraba en ediciones manuales era una división entera (son 50–84 bytes); está explicado y corregido en el informe de medidas.
- Una tabla puede declarar hasta 256 MiB y el conjunto hasta 1 GiB: un archivo pequeño y hostil con sumas válidas puede hacer reservar esa memoria (acotada, no ilimitada). Los proyectos reales ocupan cientos de KiB.
- Los bytes comprimidos dependen de la implementación de Brotli de .NET: la igualdad byte a byte entre documentos iguales se garantiza dentro de una misma compilación; la integridad no depende de ello (el SHA-256 de cada tabla es de los bytes sin comprimir).
