# Iteración 57: tutores efectivos ORAS y preservación de la fuente en Batch

Rama `feature/3ds-progressive-ui`. Trabajo local sin commits, push, publicación ni merge de Eris-Ultranova. La integración previa se documenta en `ITERACION_56_INTEGRACION_ESTABILIDAD_LOCAL.md`.

## Tutores ORAS

El editor antiguo de pk3DS modificaba únicamente una lista de movimientos y advertía que el cambio no se reflejaba en juego. Se contrastó con el [motor UPR-ZX](https://github.com/Ajarmar/universal-pokemon-randomizer-zx/blob/master/src/com/dabomstew/pkrandom/romhandlers/Gen6RomHandler.java#L2751) y con lecturas de las referencias OR y AS.

Las dos ROMs contienen 63 posiciones de compatibilidad en cuatro listas, con separadores y terminador; la tabla BP tiene 60 entradas y otro orden. Dark Pulse, Roost y Sleep Talk son las tres posiciones sin entrada BP. Se usa un mapa explícito de índices; no se presupone que las dos tablas tengan el mismo orden ni se recalcula el mapa a partir de IDs que pueden cambiar al randomizar.

- `Gen6TutorLayout` exige firmas únicas, marcadores, terminador, precios válidos y sincronía de las 60 entradas.
- `GameTutor6Service` permite editar movimientos y precios BP; randomiza movimientos únicos con exclusiones de campo/IDs incompatibles y puede conservar, fijar o eliminar el coste. Guarda en una transacción ambas representaciones del movimiento y el precio; preserva los demás bytes y las posiciones sin tienda.
- `GameTutor6CompatibilityService` añade Sanity según los movimientos de nivel y propaga flags por cadenas, ramas y formas de evolución. Opera solo sobre los 60 bits activos de tutores BP en Personal; conserva MT/MO, tutores por tipo, tres posiciones sin tienda y bits reservados. Reconstruye la tabla maestra y valida todas las dependencias antes de aplicar.
- `Gen6TutorView` integra el editor y las dos operaciones de compatibilidad. Se ofrece solo para ORAS.
- Global captura/restaura las opciones del editor y de compatibilidad. La selección de acciones reconcilia los flags del snapshot.
- Batch Gen6 incorpora `move-tutors.randomize`, `move-tutors.sanity` y `move-tutors.follow-evolutions`: 36 acciones admitidas en total. Los movimientos se asignan antes de calcular compatibilidad; esto se comprueba también cuando el orden del snapshot era inverso. La misma regla se aplica a MT/MO.

## Correcciones detectadas por los ensayos reales

Una primera ejecución de Batch OR detectó dos efectos del host antiguo sobre la fuente: renombraba `code.bin` a `.code.bin` durante la apertura y reserializaba Game Text al cerrar la ventana. Se abortó la validación por los controles de hashes/inventario. El código conservaba su SHA-256; se restauró su nombre. Game Text se recuperó desde el roundtrip que coincidía con el hash previo y con una segunda copia independiente. Se respaldó la versión alterada en el directorio privado de validación. Los hashes finales restaurados constan en `source-repair-result.json`.

La apertura del puente WPF ahora exige ExeFS extraído y código descomprimido único; no renombra, descomprime ni desempaqueta la fuente. El cierre del puente no guarda Game Text ni RandSettings. La normalización a `.code.bin`, necesaria para formularios antiguos, ocurre únicamente sobre staging aislado y conserva los bytes. El cierre interactivo normal reutiliza la detección de textos sin cambios.

También se conectaron las rutas de navegación de SMDH, gráficos, restauración selectiva y Shuffler. Los tests ahora activan la navegación usada por sus botones, además de comprobar directamente las vistas.

## Evidencia

- OR y AS: proceso original independiente sobre copias nuevas; 60 tutores/precios reproducidos con los bytes exactos de la propuesta manual con semilla. Los 658 hashes de cada fuente y archivos ajenos de las copias se conservan.
- OR: WPF preflight → backend original → precios BP gratis → Trimmed3DS → extracción. Parche manual exacto; los 658 archivos fuente y el inventario original permanecen intactos tras apertura y cierre corregidos.
- Datos sintéticos: edición conjunta de tablas, randomización, flags reservados, tabla maestra, idempotencia, dependencias, Global, scopes, orden y replay. WPF dirigido: 13 checks de rutas y opciones pasaron.
- La batería completa final pasó 2574 comprobaciones. El resultado y compilación Debug constan en los logs de `ITERACION_57_PRUEBAS`. No son pruebas in-game. Debug conserva 79 advertencias heredadas y cero errores.
- Doce archivos protegidos permanecen idénticos; se preserva Wild 001–003 y las notas personales.

Las copias y contenedores con datos de juegos están en `D:\Progra\DefinitiveRandomizer-validation`, fuera del repositorio/documentación. No se distribuyen binarios de juegos.

## Límites actuales

La integración de tutores supera el editor antiguo que modificaba una sola tabla, pero todavía requiere validación en juego en OR y AS. El ensayo completo OR probó precios; el proceso independiente probó movimientos y precios. No prueba todas las combinaciones de módulos, versiones o semillas.

Permanecen el editor de pantalla de título Gen6, proyecciones Global adicionales, textos localizados/perfiles avanzados pendientes en la matriz, catálogos y templates externos no reconocidos, y el cierre de empaquetado/regresión en todas las variantes. Se mantienen pendientes las comprobaciones in-game de las nuevas funciones. No se declara versión estable completa ni se solicita validación final como si toda la migración estuviera cerrada.
