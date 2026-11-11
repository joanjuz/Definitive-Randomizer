# Definitive Game Data v1

Contrato de metadatos JSON de PokeJuz, independiente de clases C#/Python. Este documento es la referencia técnica del productor y del consumidor, con [JSON Schema](definitive-game-data-v1.schema.json) y [ejemplo sintético](examples/definitive-game-data-v1.synthetic.json).

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

No incluye binarios, imágenes, textos de historia, rutas locales, historial privado, partidas, tokens ni configuración SD. Los nombres y metadatos del juego se usan localmente; el formato no otorga permiso para redistribuir contenido de terceros. El ejemplo publicado es enteramente sintético.

## Validación y compatibilidad

El JSON Schema describe forma/rangos; el consumidor también debe comprobar claves duplicadas y relaciones entre registros. Los IDs de especie/forma, movimiento y catálogos no se repiten. Cada referencia de tipo/habilidad debe existir. Cada origen de evolución debe corresponder a una especie exportada. Límites v1: JSON hasta 5 MB, 4096 registros de especie/evolución, 729 movimientos, 234 habilidades, 18 tipos y ocho rutas por origen; nombres no vacíos hasta 200 caracteres. Booleanos no son IDs enteros. Rechazar JSON malformado, valores no finitos y versiones mayores antes de persistir.

Los campos opcionales desconocidos se ignoran, sin reinterpretarlos como instrucciones. Añadir campos opcionales compatibles puede conservar v1; cambiar semántica o campos obligatorios requiere otro esquema y un consumidor preparado. Perfiles/Hub podrán referenciar el fingerprint y productor, sin convertir este documento en un parche ni introducir dependencias binarias.

Precedencia para la misma partida y juego: **DGD → CSV pk3DS → referencia original**. Retirar DGD restaura CSV/referencia. Un cambio de partida/juego no permite que se mezclen catálogos; un archivo inválido conserva la importación anterior. La previsualización no persiste nada. Las decisiones de revisión desconocida se aceptan explícitamente al aplicar.

## Fuera de esta entrega

No incluye XY/ORAS/Sol/Luna/DS/Switch, nuevos perfiles de memoria, efectos avanzados/flags de movimientos, tabla de tipos modificada en runtime, learnsets, entrenadores, encounters, shiny, objetos de partida ni reproducción de parches. Exportar habilidad/nombre o estadística no instala mecánicas. Se distinguen pruebas sintéticas, el recorrido entre lenguajes y la comprobación jugable con datos reales; ninguna se presenta como otra.

## Validación de esta implementación

Randomizer: compilación Release del proyecto de pruebas y referencias, no incremental, sin errores ni advertencias; 37 comprobaciones dirigidas DGD y 4546 de la regresión general, con datos sintéticos y UI WPF. Incluyen guardado real del editor, deshacer, lectura desde disco con caché anterior, formas, cambios de movimientos/evoluciones, destinos, fuentes intactas y disponibilidad solo USUM.

Interoperabilidad: un JSON generado por el exportador C# sobre GARC sintéticos se importó en el servicio Python de Tracker: PS base 123, Ataque 141, tipo Fuego, movimiento de potencia 137 y prioridad -2, evolución a especie 3/nivel 27. Se comprobó persistencia al reabrir, aislamiento de otra partida y retirada con vuelta al CSV. El ejemplo reducido publicado también es sintético. No se usaron ROMs reales, partidas ni memoria del emulador para estas pruebas.
