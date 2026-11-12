# Definitive Game Data v1

Contrato de metadatos JSON de PokeJuz, independiente de clases C#/Python. Este documento es la referencia técnica del productor y del consumidor, con [JSON Schema](definitive-game-data-v1.schema.json), [ejemplo sintético](examples/definitive-game-data-v1.synthetic.json) y [ejemplo sintético con `playerLevelCaps`](examples/definitive-game-data-v1.player-level-caps.synthetic.json).

## Exportar y utilizar

En un proyecto **Ultra Sol o Ultra Luna**, guardar primero las ediciones. En **Herramientas ROM → Exportar datos para Progressive Tracker**, elegir un JSON nuevo fuera del proyecto. También se encuentra desde Ctrl+K. El exportador rechaza otros juegos, fuentes incompletas, un guardado pendiente, destinos dentro del proyecto y salidas existentes. El archivo contiene datos, no una receta ni una copia de ROM.

En Progressive Tracker, abrir la partida correcta y **Conexión → Importar datos de Definitive Randomizer**. Revisar edición, revisión, contadores y diferencias antes de confirmar. Cada partida conserva su importación; reemplazar o retirar requiere una acción explícita. La edición tiene que coincidir con la partida. El fingerprint no acredita la ROM que ejecuta el emulador.

El productor retiene abiertos sin compartir escritura los GARC Personal, movimientos, evoluciones y Game Text durante toda la lectura. Comprueba el índice/tabla maestra, reutiliza formatos Core y toma nombres del Game Text guardado. No usa valores de controles, cachés de tablas de la sesión, ni replay/historial. Deshacer y guardar afectan la siguiente exportación porque cambia el archivo leído. Solo se escribe el JSON nuevo mediante temporal y rename sin sobrescritura.

## Identidad y revisión

- `format = PokeJuz.DefinitiveGameData`, `schemaVersion = 1`; versión entera, sin dependencia binaria.
- Productor `id = DefinitiveRandomizer`, `version` informativa de la aplicación.
- `game.id = ultra-sun | ultra-moon`, `generation = 7`; Title ID respectivamente `00040000001B5000` y `00040000001B5100`.
- La edición se deriva del perfil de encuentros USUM del proyecto. Se comprueba contra `ncchheader.bin`/`exheader.bin` cuando existen; si difieren, se rechaza. No se toma del nombre de la carpeta.
- `revisionStatus = unknown`, `revision = null` en el exportador actual. Una extracción no demuestra qué actualización está instalada. Ni la versión de formato NCCH ni ExHeader remaster se presentan como versión del juego. Un productor futuro que pueda acreditarla usará `known`/`1.0`; el consumidor v1 solo admite esa revisión conocida. Desconocida requiere aceptación explícita con advertencia para la partida USUM 1.0; no certifica compatibilidad de otra revisión.
- Región no se infiere del idioma de textos; no se anuncia una región si el proyecto no aporta evidencia fiable.

## Datos y semántica

`coverage = exported-records`: **cada registro presente es completo**. Una clave ausente de las tablas conserva referencia/CSV del consumidor. No significa que una especie ausente tenga estadísticas cero. Una entrada de evolución con `routes: []` significa explícitamente «sin evoluciones» y reemplaza rutas originales de esa especie/forma. El productor actual recorre las 807 especies base, sus formas declaradas válidas, 729 movimientos (0–728), 18 tipos y 234 habilidades. No exporta registros Personal huérfanos sin identidad de especie/forma.

| Tabla/campo | Identidad y significado | Integración en Tracker v1 |
|---|---|---|
| `species` | Clave `(speciesId, formId)`; `personalIndex` solo procedencia, no identificador portable. `name`, dos IDs de tipo, tres habilidades (normal1/normal2/oculta), `baseStats` | Nombre, tipos, estadísticas base, lista de habilidades y evolución por especie/forma en fichas/equipo/cajas y análisis. Los valores vivos/IV/EV/PS del Pokémon siguen viniendo de RAM |
| `baseStats` | `hp, attack, defense, specialAttack, specialDefense, speed`, bytes 0–255 guardados | Referencia modificada, nunca PS actuales ni un cálculo de naturaleza/IV/EV |
| `types` | ID Gen7 y nombre localizado; no redefine la tabla de efectividades | La semántica se obtiene del ID, no del texto traducido |
| `abilities` | ID 0–233 y nombre; sin implementación/efecto del motor | Nombres por ID para la habilidad leída en RAM y las opciones de especie. No cambia el ID del Pokémon ni acredita que un hook esté instalado |
| `moves` | ID 0–728, nombre, `typeId, category, power, accuracy, pp, priority` | Propiedades informativas de los movimientos y análisis que las utilicen |
| `evolutions` | Clave especie/forma de origen; lista de hasta ocho rutas Gen7, ordenadas como el archivo | Reemplaza rutas de la clave, incluida eliminación. Método desconocido se muestra numéricamente sin inventar comportamiento |

Tipos Gen7: `0 Normal, 1 Lucha, 2 Volador, 3 Veneno, 4 Tierra, 5 Roca, 6 Bicho, 7 Fantasma, 8 Acero, 9 Fuego, 10 Agua, 11 Planta, 12 Eléctrico, 13 Psíquico, 14 Hielo, 15 Dragón, 16 Siniestro, 17 Hada`.

Categoría de movimiento: `0 Estado, 1 Físico, 2 Especial`. Prioridad firmada -8..8. Precisión se conserva como byte: no transformar 0/valores especiales en una probabilidad ficticia.

Evolución: `method` es el ID nativo Gen7 de pk3DS (1..42; el slot inactivo 0 se omite), `argument` es parámetro dependiente del método, `targetSpeciesId`, `targetFormId` (-1 conserva la forma), `level`. La forma de origen es independiente de la del destino. La lista vacía es válida. No se traduce el ID de método a un índice PokéAPI.

## Fingerprint y privacidad

`source.kind = saved-project`; `fingerprintAlgorithm = SHA-256`; `scope = personal,moves,evolutions,gametext`. El fingerprint es SHA-256 UTF-8 de Title ID + LF + las entradas `clave:SHA256` ordenadas ordinalmente y unidas por LF. Las cuatro claves de recursos son `personal`, `move`, `evolution`, `gametext`; se añaden los nombres abstractos `ncchheader.bin`/`exheader.bin` si están disponibles y verificados. Hexadecimal mayúsculo sin rutas. Su alcance son estos recursos guardados, **no una ROM reconstruida completa**, el guardado del jugador ni el proceso del emulador.

No incluye binarios, imágenes, textos de historia, rutas locales, historial privado, partidas, tokens ni configuración SD. Los nombres y metadatos del juego se usan localmente; el formato no otorga permiso para redistribuir contenido de terceros. Los ejemplos publicados son enteramente sintéticos.

## Extensión opcional `playerLevelCaps`

Campo raíz **opcional** que describe el parche Player Level Caps de Ultra Sol/Ultra Luna tal como está en los archivos guardados del proyecto. No cambia `schemaVersion = 1`, los campos obligatorios, `source.scope` ni el fingerprint raíz: si el campo falta, el documento significa exactamente lo mismo que antes y un consumidor que no lo conoce lo ignora. **El exportador actual todavía no lo genera**; esta sección fija el contrato que tendrá que cumplir.

Es una **restricción mecánica** de la ROM exportada, no una lista de gimnasios ni de etapas del Tracker. La rutina recorre los puntos en orden y se detiene en el **primero cuya condición no se cumple**: el límite de ese punto es el vigente (`evaluation = ordered-first-incomplete`). Cada límite se aplica, por tanto, *hasta* completar su punto. Con todos cumplidos rige el techo `ceiling = 100`. Bloquea la ganancia de EXP (hook de Battle.cro) y el Caramelo Raro (hook de code.bin); no impide capturar, recibir ni intercambiar Pokémon de nivel superior. El documento **no indica en qué punto está el jugador**: eso depende de su partida y DGD no la lee.

### Versión del bloque

`version` es la versión del bloque, independiente de `schemaVersion`. Solo está definida la 1. Un consumidor que encuentre otra versión ignora el bloque (sin propuesta) y conserva el resto del DGD; el esquema solo valida estrictamente el bloque cuando `version = 1`.

### Estado

`hooks` informa cada copia por separado: `stock` (código original), `applied` (rutina y tabla de este formato), `applied-legacy` (rutina de un formato anterior), `unrecognized` (ni original ni de este parche, o archivo no utilizable, por ejemplo code.bin comprimido) o `missing` (archivo ausente). `status` resume ambas con esta precedencia:

| `status` | Condición | Campos |
|---|---|---|
| `unsupported` | Alguna copia es `unrecognized` o `missing` | `hooks`; `source` opcional (solo si se leyeron los dos archivos); `detail` opcional |
| `legacy-format` | Alguna copia es `applied-legacy` | `hooks`, `source`; `detail` opcional |
| `inconsistent` | Una copia `applied` y la otra `stock`, o ambas `applied` con tablas distintas | `hooks`, `source`; `detail` opcional |
| `installed` | Ambas `applied` y tablas idénticas byte a byte | Todos: `evaluation`, `enforcement = ["exp-gain", "rare-candy"]`, `ceiling`, `source` con `tableSha256`, `checkpoints` (1–128) |
| `not-detected` | Ambas `stock` | `hooks`, `source` |

Solo `installed` lleva `evaluation`, `enforcement`, `ceiling`, `checkpoints` y `source.tableSha256`; el esquema los **prohíbe** en los demás estados, de modo que un estado no instalado nunca contiene una lista utilizable. `not-detected` significa que no se encontró **este** parche, no que la ROM carezca de cualquier sistema de límites: un romhack puede traer el suyo y DGD no lo ve. `legacy-format` conserva el diagnóstico pero no exporta puntos: los formatos anteriores no se reinterpretan. `inconsistent` no ofrece lista importable. `detail` es texto técnico del productor (hasta 500 caracteres), sin rutas locales ni datos binarios.

### Origen

`source` es independiente del `source` raíz: `fingerprintAlgorithm = SHA-256`, `scope = battle.cro,code.bin`, `fingerprint` = SHA-256 UTF-8 de Title ID + LF + `battle.cro:SHA256` + LF + `code.bin:SHA256` (los hashes de los archivos guardados completos), hexadecimal mayúsculo sin rutas. `tableSha256` (solo `installed`) es el SHA-256 de la tabla de runtime: 6 bytes por punto (`u8` tipo 0/1, `u8` límite, `u16` LE argumento 0, `u16` LE argumento 1) seguidos del terminador `FF 64 00 00 00 00`. El consumidor puede recalcularlo a partir de `checkpoints`. Como el fingerprint raíz, no acredita la ROM que ejecuta el emulador ni una revisión concreta del juego.

### Puntos

Cada elemento de `checkpoints` tiene `order` (1..n en el orden del array), `cap` (1–100) y `condition`:

- `{"kind": "event-flag-set", "byte": 0–619, "mask": 1–255}`: se cumple si algún bit de `mask` está activo en ese byte de Event Flags.
- `{"kind": "event-work-at-least", "index": 0–999, "minimum": 1–65535}`: se cumple si el Event Work `index` vale al menos `minimum`.

`label` (1–60 caracteres, sin caracteres de control ni espacios en los extremos) y `labelSource = randomizer-catalog` van siempre juntos y solo aparecen cuando la condición coincide exactamente con una del catálogo investigado de Randomizer (`LevelCapTable.KnownFlags`). Son nombres de ese catálogo, no texto del juego ni traducciones, y no se deducen de la etiqueta que mostrara la vista: se obtienen de la condición leída. Una condición fuera del catálogo va **sin** etiqueta; el consumidor la muestra por su condición y no inventa un nombre.

### Reglas del productor

Lee el estado instalado en los archivos guardados (Battle.cro y code.bin descomprimido) y compara las dos copias. Nunca exporta como aplicado el contenido de la tabla de la vista, una vista previa, un template sin aplicar ni un cambio deshecho: deshacer y guardar se reflejan porque cambia el archivo leído. Un fallo al leer el parche se informa en `status` y no impide exportar el resto del DGD. No añade revisión, región ni ROM ejecutada que no estén acreditadas. El bloque serializado no supera 64 KiB, dentro del límite de 5 MB del documento.

### Reglas del consumidor

Además del esquema, para `installed` comprobar que `order` va de 1 a n en el orden del array, que los límites nunca bajan, que cada condición aparece una sola vez y que `tableSha256` coincide con los puntos; si algo falla, el bloque se ignora (sin propuesta) y el resto del DGD sigue siendo válido. Solo `installed` puede generar una propuesta. El bloque nunca se aplica automáticamente: cualquier uso como límites propios requiere previsualización y confirmación explícita, y no sustituye límites existentes sin esa confirmación. Un consumidor con un máximo de etapas menor que 128 (Progressive Tracker admite 64) rechaza la propuesta completa; **nunca la trunca**. Ninguna propuesta elige un punto vigente.

## Validación y compatibilidad

El JSON Schema describe forma/rangos; el consumidor también debe comprobar claves duplicadas y relaciones entre registros. Los IDs de especie/forma, movimiento y catálogos no se repiten. Cada referencia de tipo/habilidad debe existir. Cada origen de evolución debe corresponder a una especie exportada. Límites v1: JSON hasta 5 MB, 4096 registros de especie/evolución, 729 movimientos, 234 habilidades, 18 tipos y ocho rutas por origen; nombres no vacíos hasta 200 caracteres. Booleanos no son IDs enteros. Rechazar JSON malformado, valores no finitos y versiones mayores antes de persistir.

Los campos opcionales desconocidos se ignoran, sin reinterpretarlos como instrucciones. Añadir campos opcionales compatibles puede conservar v1; cambiar semántica o campos obligatorios requiere otro esquema y un consumidor preparado. Perfiles/Hub podrán referenciar el fingerprint y productor, sin convertir este documento en un parche ni introducir dependencias binarias.

Precedencia para la misma partida y juego: **DGD → CSV pk3DS → referencia original**. Retirar DGD restaura CSV/referencia. Un cambio de partida/juego no permite que se mezclen catálogos; un archivo inválido conserva la importación anterior. La previsualización no persiste nada. Las decisiones de revisión desconocida se aceptan explícitamente al aplicar.

## Fuera de esta entrega

No incluye XY/ORAS/Sol/Luna/DS/Switch, nuevos perfiles de memoria, efectos avanzados/flags de movimientos, tabla de tipos modificada en runtime, learnsets, entrenadores, encounters, shiny, objetos de partida ni reproducción de parches. `playerLevelCaps` describe el estado de un parche, pero no lo instala ni permite reproducirlo, y todavía no lo genera el exportador. Exportar habilidad/nombre o estadística no instala mecánicas. Se distinguen pruebas sintéticas, el recorrido entre lenguajes y la comprobación jugable con datos reales; ninguna se presenta como otra.

## Validación de esta implementación

Randomizer: compilación Release del proyecto de pruebas y referencias, no incremental, sin errores ni advertencias; 37 comprobaciones dirigidas DGD y 4546 de la regresión general, con datos sintéticos y UI WPF. Incluyen guardado real del editor, deshacer, lectura desde disco con caché anterior, formas, cambios de movimientos/evoluciones, destinos, fuentes intactas y disponibilidad solo USUM.

Interoperabilidad: un JSON generado por el exportador C# sobre GARC sintéticos se importó en el servicio Python de Tracker: PS base 123, Ataque 141, tipo Fuego, movimiento de potencia 137 y prioridad -2, evolución a especie 3/nivel 27. Se comprobó persistencia al reabrir, aislamiento de otra partida y retirada con vuelta al CSV. El ejemplo reducido publicado también es sintético. No se usaron ROMs reales, partidas ni memoria del emulador para estas pruebas.

Contrato `playerLevelCaps`: `tools/contracts/check_definitive_game_data.py` (dependencias fijadas en `tools/contracts/requirements.txt`) valida los dos ejemplos con el esquema (Draft 2020-12), los estados válidos e inválidos, las reglas del consumidor que el esquema no expresa, el límite de 64 etapas sin truncar, el tamaño máximo y la ausencia de rutas. Con `--tracker <carpeta>` importa los ejemplos con el validador y el servicio DGD de esa copia de Progressive Tracker. El CI lo ejecuta contra Tracker 1.4.0 fijado por commit y comprueba que acepta el ejemplo ampliado, conserva el bloque sin cambios, mantiene vista previa, estado, consultas, fingerprint raíz y retirada idénticos a los del ejemplo original. En C#, `--definitive-game-data-only` comprueba que el exportador actual no emite el bloque ni cambia `source.scope`, que las etiquetas del ejemplo salen exactamente de `LevelCapTable.KnownFlags`, que su tabla pasa la validación del runtime y que `tableSha256` coincide con `LevelCapTable.ToBytes()`.
