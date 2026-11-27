# Persistent Items X/Y — investigación protegida

Fecha: 26/11/2026. Base: `24f89911c1a08f0b9f81bb7a641aa3b96279c316`
(main, merge del PR #144). **No hay instalador Gen6 ni candidato jugable.**
Persistent Items continúa disponible únicamente para USUM. No se habilitó un
botón X/Y, ni se modificaron motores, servicios, templates, recordador, mentas,
Level Caps, Preview instalada o versiones. Este documento no acredita la función.

## Estado por fase

| Fase | Resultado |
|---|---|
| A: investigación | Ruta candidata localizada en recursos reales de ambos títulos; lector reproducible de solo lectura y perfiles observados separados. Semántica incompleta. |
| B: parche | Bloqueado: faltan trazas de modos y procedencia del objeto. Ningún payload ni perfil autoriza instalación. |
| C: transacción e historial | Infraestructura existente revisada y regresión sintética específica XY; no instalador ni deshacer de un parche Gen6. |
| D: WPF | Bloqueada hasta acreditar B. Disponibilidad USUM-only preservada y comprobada. |
| E: regresiones/CI | Suite de protección y lector sintético añadidos al CI; resultados al final. |
| F: juego/cierre | Pendiente por separado en X e Y. PR de investigación en borrador. |

## Método y procedencia

Se leyeron extracciones locales autorizadas de X e Y. La cabecera NCCH0
(`NCCH` en `0x100`, Program ID little endian en `0x118`) concuerda con el
Program ID del exheader en `0x200`: X `0004000000055D00`, Y
`0004000000055E00`. El campo de versión **del contenedor NCCH** vale 2;
no equivale a una revisión de juego o actualización instalada.

Las huellas completas, separadas por título, están en
[`observed_profiles.json`](../../tools/research/xy-persistent-items/observed_profiles.json).
Incluyen cabecera, exheader, `.code.bin`, `DllBattle.cro`, `DllField.cro`,
`static.crs`, `.crr/static.crr` y archivo de objetos `a/2/2/0`.
Son identidades observadas, **no prueba de procedencia retail intacta** ni una
lista de revisiones compatibles. En particular hay historial de edición en una
extracción; reconocer su identidad no convierte sus archivos en originales.
El lector rechaza títulos cruzados y no analiza offsets fijos cuando alguna
dependencia difiere del conjunto observado. No contiene modo de forzar instalación.

| Recurso | X | Y |
|---|---|---|
| DllBattle.cro | 1.040.384 bytes; SHA-256 `9C07A3342DFBA4ADFC3569271D65FE9FAD88F1E271D5BA650179377E60D0A5A1` | Mismo tamaño y SHA, comprobados independientemente |
| .code.bin | `0F6D3E1D…142797A` | `62554AC3…6692FE7` |
| DllField.cro | `ED043EB6…BE6489` | `F4D77DD5…080BD3` |
| static.crs | `30D10ADA…BFFDAA` | `598EAA95…FECBD` |
| static.crr | `D9611CA3…C93CEB` | `0637C641…06248` |

No se añadieron dumps, ROMs, guardados, claves ni desensamblados comerciales.
Los JSON de resultados, logs y listados opcionales se conservan fuera de Git.

## Contraste con USUM y contratos existentes

Se revisaron `GamePersistentItemsService`, `Gen7PersistentConsumablesPatcher`,
`PatchInteractionChecks`, `PatchCapabilityChecks` y `PatchCapability`.
USUM localiza dos firmas ARM únicas en `Battle.cro`: cambia la condición de
entrada en la conciliación y una condición de restauración. Su servicio usa
propuesta con propietario, huella de origen, `History.Commit` y relectura.
`NotAccredited` y `Conflict` nunca permiten aplicar. No se amplió esa acreditación
a Gen6. La mejor clasificación de USUM es adaptación por firmas, no revisión
completa fijada por hash; se conserva ese contrato existente sin cambios.

En X/Y el recurso correcto es **DllBattle.cro**. Su código llama a imports
estáticos: el gestor de objetos recibe tres argumentos, incluido `r2=0x11`.
USUM usa otros registros y organización. No se buscaron sus firmas para conceder
compatibilidad XY ni se copiaron offsets, tipos de combate o payloads.

## Ruta ARM encontrada en X e Y

Todos los offsets siguientes son **de archivo**, salvo los marcados VA.
Son observaciones de los dos conjuntos fijados, no direcciones universales.

| Punto | Evidencia estática |
|---|---|
| `0x1469C` | Entrada de rutina candidata: conserva contexto en `r9`; lee `contexto+0x10`; comprueba byte `+0x4B`, después byte `+0`. |
| `0x146B8–0x146C4` | Acepta valores 0/1 en una ruta; otros van a `0x14AB8`. Falta asignar esos valores a modos reales. |
| `0x1945C` | Llamada directa a `0x1469C`, después de otra llamada en `0x19450`, dentro de una ruta de cierre candidata. No demuestra por sí sola todos los finales de combate. |
| `0x14848–0x14860` | Lee byte `[*(r9+0x10)]`, compara con 1 y hace `BNE 0x148DC`. La secuencia exacta aparece una vez. |
| `0x14864–0x14884` | Obtiene participantes por índice desde `*(tipo/contexto+0x4C)` y desde el equipo en `r8`; conserva punteros en `r6/r10`. |
| `0x14888–0x148AC` | Lee campo del primer participante, consulta parámetro `0x11`; `CMP r0,#0`, `NOP`, `BEQ 0x14948`. Otra secuencia exacta única. |
| `0x148B0–0x148CC` | Si el parámetro es distinto de cero y ambos campos difieren, escribe cero en el segundo participante. |
| `0x14948–0x1495C` | Escribe el valor previo conservado en `r6` al segundo participante y vuelve al bucle. Candidato a restauración nativa. |

Las instrucciones de condición observadas son `1A000020` en `0x14854`
y `0A000025` en `0x148AC` (palabras ARM). Una ampliación del primer salto y
un salto incondicional al segundo destino son **hipótesis de experimento**;
no se implementaron. Aún podrían restaurar indebidamente un objeto transferido
o entrar en modos excluidos. No basta con que existan dos saltos parecidos a USUM.

El lector busca las secuencias Gen6 completas, incluidas las llamadas originales;
informa ausencia o duplicación. No trata esos estados como instalaciones propias
o parciales: no existe una instalación Gen6 acreditada que pueda reconocer.

### Imports, estructuras y clasificación de objetos

Se recorrieron módulos/imports y sus lotes de relocaciones, resolviendo `|static|`
contra los segmentos de `static.crs`. Los resultados coinciden en X e Y:

| Slot de archivo CRO | Destino VA del código principal | Observación |
|---|---|---|
| `0x268` | `0x39E508` | Acceso al participante por índice; exige índice menor que 6. |
| `0x620` | `0x4A1C54` | Lee byte `+0x18`; candidato a número de integrantes. |
| `0x6B0` | `0x4A16C8` | Sigue puntero `+0xC`, llama `0x4A0C84`: accede al bloque de datos y lee halfword `+2`. |
| `0x6B8` | `0x39D804` | Sigue puntero `+0xC`, salta `0x39BB98`: escribe halfword `+2` bajo el mismo protocolo de acceso. |
| `0x3B0` | `0x427F5C` | Carga registro del objeto y llama al dispatcher `0x4A22B8` con parámetro conservado. |

La entrada 17 de la tabla del dispatcher apunta a VA `0x4A23B4`:
`LDRB r0,[r0,#0xE]; BX lr`. Esto acredita **qué campo consulta** la ruta.
Su correspondencia con el ID equipado es una inferencia respaldada por los
lectores/escritores y el catálogo; falta observarla en ejecución con un objeto
identificado. El nombre semántico `PRM_ID_SPEND` no se importó como hecho de Gen6.

En el GARC XY `a/2/2/0`, el byte `0x0E` vale 1 para:

| Caso candidato | ID |
|---|---:|
| Baya Zidra | 158 |
| Hierba Blanca | 214 |
| Hierba Única | 271 |
| Banda Focus (Focus Sash) | 275 |
| Tarjeta Roja | 542 |
| Botón Escape | 547 |
| Seguro Debilidad | 639 |

El informe enumera también todos los IDs con ese byte distinto de cero. **No es
una whitelist de restauración**: hay otros objetos (por ejemplo ID 43) y el campo
no demuestra que su ruta de consumo, transferencia o lanzamiento sea idéntica.
No se modifican tablas ni consumo desde la Bolsa.

`DllBattle` importa `|static|` y `DllBackGround`, sin dependencia directa declarada
de `DllField`. El código principal contiene referencias `rom:/DllBattle.cro`.
La relación de ciclo de vida con Field y los finales interrumpidos requiere traza;
no se deduce de un nombre de módulo ni de un único caller.

## Integridad, nombre físico y convivencia

Los cuatro SHA internos de `DllBattle.cro` coinciden en las dos muestras. Su
SHA-256 de los primeros `0x80` bytes aparece en la tabla CRR de cada juego
(133 entradas). Esto acredita pertenencia a la tabla observada; **no** acredita
la firma RSA ni el comportamiento del loader después de una modificación.
Un futuro parche debe simular hashes CRO y política CRR/loader, validar el
inventario y comprometer dependencias atómicamente. No se recalcula una firma
RSA mediante SHA-256. Referencia estructural: [CRO0, 3dbrew](https://www.3dbrew.org/wiki/CRO0).

La tabla de segmentos distingue el código real (`0x180`, longitud `0xCD1FC`)
del tamaño de código del header (`0xDBE80`); no reservar espacio libre utilizando
solo el segundo. Import relocations: header `0xF8/0xFC`, entradas de 12 bytes;
anonymous imports: `0x110/0x114`, entradas de 8; internas: `0x128/0x12C`.
La investigación no escribe ni cambia ninguna de ellas.

Se preserva la corrección de
[iteración 82](ITERACION_82_NOMBRES_ROMFS.md): `FileChange` resuelve la entrada
física antes de reemplazar/restaurar. El nuevo test XY pide deliberadamente
`DLLBattle.cro` y exige que permanezca exactamente `DllBattle.cro`, sin duplicados,
al guardar, fallar, rechazar una propuesta obsoleta y recuperar una transacción.
El lector exige el nombre físico correcto; no lo repara silenciosamente.

Tabla de tipos XY: `0xD12A8`, 324 bytes. Escudo Real: secuencia observada en
`0xBAA80`, cambio existente en `+4`. No intersectan los puntos candidatos
`0x14854/0x148AC`. **Disjunción de rangos no acredita convivencia**: hashes,
CRR, otras dependencias y orden del historial también importan. El perfil de
investigación rechaza cualquier hash diferente; no acepta todavía esos parches.
No se ejecutaron secuencias A/B de instalación Gen6, desinstalación, reapertura,
rebuild o reextracción de un parche inexistente. Deshacer futuro debe usar bytes
de esa operación y respetar LIFO del historial, nunca reemplazar el CRO por vanilla.

## Experimento siguiente y protocolo jugable

Primero obtener trazas **sin parche** en copias aisladas con guardados copiados,
sin servicios en línea. Identificar el `.text` cargado de DllBattle como `T`;
un offset de archivo `F` corresponde a `T+(F-0x180)` en estos perfiles.
No usar los offsets CRO como VA absolutas ni los valores de battle type de USUM.

1. Breakpoints en entradas `0x1469C`, `0x14850`, `0x148A0`, `0x14948` y el
   caller `0x1945C`, convertidos con la base observada. Registrar tipo/contexto,
   byte `+0x4B`, índices, punteros de ambos equipos y retornos del getter/query.
2. Repetir salvaje y entrenador normal en **X y luego Y**, con Baya Zidra ID 158
   y Banda Focus ID 275. Correlacionar participante, objeto inicial, objeto
   tras consumo y objeto final. Registrar las llamadas al escritor (incluido cero).
3. Repetir Hierbas, Seguro Debilidad, Botón Escape y Tarjeta Roja; doble con
   dos consumidores, debilitamiento, retirada, huida, captura y derrota.
4. Controles de Desarme, Truco, Ladrón y Lanzamiento: anotar propietario de origen,
   posesión de cada participante y Bolsa antes/después. Separar consumo propio de
   destrucción, robo y transferencia; conservar toda restauración vanilla que ya exista.
5. Identificar instalaciones y modo local PvP con trazas controladas, sin conectarse
   en línea. Acreditar su exclusión por condiciones propias de Gen6. Si no hay modo
   controlado disponible, quedan excluidos y no se habilita producción por inferencia.

Solo después se puede diseñar B y un candidato aislado con vista previa de
instrucciones/rangos, dependencias completas, relectura y deshacer exacto.
Para ese candidato, registrar por separado X/Y, hashes antes/después y:

| Prueba | Aceptación |
|---|---|
| A/B/C | Consumible se activa y desaparece durante ese combate; al terminar salvaje/entrenador vuelve al mismo portador. |
| D | Equipados no consumidos intactos; conteo de Bolsa idéntico, sin duplicación. |
| E | Modos excluidos conservan comportamiento nativo demostrado. |
| F | Guardar, cerrar emulador, reabrir: objeto equipado correcto. |
| G | Ambos órdenes con tabla de tipos/Escudo Real/otros cambios acreditados; deshacer LIFO conserva lo anterior; rebuild y reextracción conservan nombre/bytes. |

**No hay ROM candidata para estas pruebas todavía.** Las ROM del recordador del
PR #144 no contienen Persistent Items y no sirven como candidato de esta función.
En la primera entrega no se inició una sesión con guardados. La continuación
descrita abajo verificó conexión GDB y arranque aislado, sin traza de batalla.
No hay evidencia jugable nueva X/Y.

## Comandos reproducibles y resultados

Ejecutar desde la raíz del worktree. `X_PROYECTO`/`Y_PROYECTO` son las extracciones
autorizadas; el lector solo lee y escribe su informe por stdout. Guardar informes
fuera del repositorio o bajo `artifacts/`. `--listing` requiere Capstone y emite
desensamblado privado; no adjuntarlo al PR. El lector no instala nada.

```powershell
python tools/research/xy-persistent-items/inspect_persistent_items.py X_PROYECTO
python tools/research/xy-persistent-items/inspect_persistent_items.py Y_PROYECTO
python -m unittest discover -s tools/research/xy-persistent-items -p test_inspection.py -v
dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release -p:UseAppHost=true '-bl:artifacts/persistent-research/build-{}.binlog'
dotnet 3DS.Progressive.Tests/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll --gen6-persistent-items-only
dotnet 3DS.Progressive.Tests/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll --patch-capability-only
```

Resultados locales: build Release del runner (incluye App/Core/LegacyBatch),
0 errores y 1 advertencia CS0675 en `NdsMapRenderChecks.Synthetic.cs:54`, archivo
preexistente sin cambios. SDK 10.0.301; binlog privado generado. Suite
`--gen6-persistent-items-only`: **16 PASS**; `--patch-capability-only`: **129 PASS**,
incluidas las interacciones USUM; lector Python: **17 tests OK**. Exit 0 en las
tres ejecuciones finales. La primera invocación Python con redirección desde
PowerShell 5 devolvió estado de shell 1 aunque unittest informó OK por stderr;
se repitió sin redirección con `exit $LASTEXITCODE`, obteniendo exit 0.
Lecturas reales X/Y: identidades completas coincidentes, firmas candidatas únicas,
cuatro hashes CRO válidos y pertenencia a CRR en ambos. Se repitieron las huellas
después de la investigación, sin cambios de las fuentes. CI remoto pendiente.
Las comprobaciones sintéticas prueban lectores y barreras, no ejecución del motor.
No se reclaman estados original compatible/instalado/parcial, instalación
idempotente, reversión de Persistent Items o compatibilidad de actualizaciones.

## Continuación: captura dinámica preparada para intervención del propietario

Se conserva el mismo PR #145 y la misma rama. Por indicación del propietario,
**las pruebas jugables en emulador las realiza él**. No se automatizan combates,
inputs ni acciones sobre sus instancias habituales. La sesión inicial de
diagnóstico se cerró antes de preparar la captura manual.

### Herramientas encontradas y resultados reales limitados

Existen Citra, Azahar y Lime3DS. Se utilizó una copia portátil de Citra Nightly
2104, commit `0ff3440`, con ROM y guardado copiados. Se verificó que cargaba
su configuración y almacenamiento aislados y que no encontraba actualización
instalada para X en esa copia. El Program ID cargado fue `0004000000055D00`.
No se importaron mods, cheats ni claves de las instalaciones habituales.

El servidor GDB anunció `PacketSize=2000;qXfer:features:read+;qXfer:threads:read+`.
`qfThreadInfo` devolvió `m1`; `Hg1` seleccionó el hilo y `pf` devolvió
`00001000` (VA `0x00100000`, little endian). Se leyó memoria del ejecutable y
el log registró cargas de módulos de título. **No se llegó a cargar/trazar
DllBattle ni a completar un combate.** La salida SDL apareció negra; no se
atribuye una causa definitiva. Una petición de interrupción terminó en timeout.
Por tanto, no se declara que la captura completa esté validada en el emulador.
La copia original del guardado mantuvo su SHA-256 después de cerrar la sesión.

Se contrastó el protocolo con la [fuente preservada del commit exacto de Citra](https://raw.githubusercontent.com/itsTyrion/lemon/0ff3440232d6bc9226e37c41c05c5aead03f37fd/src/core/gdbstub/gdbstub.cpp).
**Tanto Z0 como Z1 insertan una trampa de cuatro bytes en RAM.** No son
breakpoints de hardware sin escritura. Repetir Z en una dirección puede perder
el original guardado, y cerrar el socket no restaura automáticamente los puntos.
El cliente nuevo usa propiedad exclusiva, rechaza duplicados, verifica la trampa
y comprueba la restauración exacta mediante z antes de continuar. Si falla,
bloquea resume/step y pide cerrar únicamente la copia aislada antes de guardar.
No expone paquetes de escritura de memoria/registros ni fabrica firmas CRO/CRR.

### Preparador y trazador experimentales

- `prepare_trace.py` valida el proyecto observado, título NCSD/NCCH y guardado
  del mismo título. Crea una carpeta nueva, copia ROM/runtime/guardado con su
  `00000001.metadata`, conserva huellas y configura únicamente esa copia. No
  lanza el emulador. Rechaza destino existente, enlaces y destino dentro de una
  fuente; verifica que las fuentes no cambiaron. Coincidencia de título no
  demuestra que toda la ROM corresponda a la extracción: se conserva esa limitación.
- `gdb_trace.py --launch` inicia solo Citra Qt de la sesión preparada, conecta a
  localhost, selecciona el hilo y lee registros individuales. Comprueba muestras
  nativas del ejecutable; no afirma haber acreditado su hash completo en RAM.
  Al ver la carga de DllBattle en el log, lee la cabecera cargada, deriva `.text`
  y verifica cada instrucción original antes de insertar puntos temporales.
- Se capturan los siete puntos pedidos, además de `0x14888`, lectura actual,
  llamadas al escritor y retornos. `0x19460` termina automáticamente **una sola
  batalla**, limpia los puntos antes de descargar el CRO y deja el emulador parado.
  Para otra batalla se inicia otra sesión del emulador y un nombre de caso nuevo.
- Los JSONL guardan registros, byte de modo sin asignarle nombres, metadatos,
  candidatos a equipos, valor consultado e intención de escritura. Los bloques
  de Pokémon permanecen **opacos**: no se inventan especie, identidad ni procedencia.
  Bolsa, identidad visible y evento de consumo se anotan manualmente. Ningún
  JSONL constituye por sí solo aprobación jugable ni autoriza instalar.

### Primera prueba manual: validar captura del original X

La carpeta local preparada es `artifacts/pi145-x` dentro de este worktree. Es
una **ROM original copiada, sin Persistent Items**; no se espera restauración
nueva de objetos. No es el candidato del recordador ni una build de Preview.

Desde la raíz del worktree:

```powershell
python tools/research/xy-persistent-items/gdb_trace.py --session artifacts/pi145-x --case X-control-salvaje-01 --launch
```

1. Esperar el arranque de la copia Citra Qt y el aviso del terminal de que se
   puede jugar manualmente. Si aparece un error, no forzar ninguna validación.
2. Anotar Pokémon utilizado, objeto equipado y cantidades relevantes de Bolsa.
3. Entrar en un combate salvaje normal y terminarlo o huir. Esta primera prueba
   valida la instrumentación; todavía no exige disponer de un consumible.
4. El terminal debe mostrar `Original DllBattle verified; RAM breakpoints active`
   y, al terminar, `Owned RAM instructions restored`. Puede haber pausas al leer
   estructuras. Cerrar después **solo la instancia aislada**.
5. Informar qué ocurrió en pantalla, objeto/Bag antes y después y si aparecieron
   esos avisos. No hace falta subir ROMs ni guardados: el log queda local en
   `artifacts/pi145-x/evidence/X-control-salvaje-01.jsonl`.

Si hay pantalla negra, timeout, cabecera/instrucción diferente o mensaje
`CLEANUP UNVERIFIED`, cerrar esa copia antes de guardar o continuar e informar
el texto. No usarla como evidencia de funcionamiento. No arrancar todavía la
matriz completa ni realizar conexiones de juego en línea.

Después de revisar esta primera traza se habilita **la siguiente captura**, no
el parche: Baya Zidra consumida en salvaje, luego entrenador, Banda Focus,
Hierbas/Seguro/Tarjeta/Botón y procedencia con Desarme/Truco/Ladrón/Lanzamiento.
Cada caso necesita estado inicial y final visible, conteo de Bolsa y su propio
JSONL. Y requiere preparación y evidencia separadas; no se extrapola X.

### Validación técnica de esta continuación

Los clientes y el preparador se prueban con sockets, RAM y archivos sintéticos;
no se lanzan emuladores desde las pruebas. El CI ahora descubre `test_*.py`,
incluidos `test_gdb_trace.py` y `test_prepare_trace.py`.

```powershell
python -m unittest discover -s tools/research/xy-persistent-items -p 'test_*.py' -v
```

Resultado local: **53 tests Python OK**, con validación del framing, checksum,
escape/RLE, comandos permitidos, hilo/registros, propiedad/restauración de
breakpoints, bloqueo tras fallo, mapping dinámico, snapshots opacos, límites de
copias y aislamiento de configuración. La preparación real X finalizó sin
lanzar el emulador y con verificación de copias/fuentes. No hay cambios en C#,
WPF, Core, USUM, recordador o Preview. CI nuevo pendiente al registrar esta sección.

## Primera carga real de DllBattle y corrección de sincronización GDB

El propietario comunicó un control en Lime3DS: Gardevoir con Gardevoirita,
funcionamiento normal y sin cambio visible en Bolsa. No aparecieron los avisos
del cliente GDB; el objeto no se consumió. Se conserva como observación manual
limitada, no como traza ni validación de restauración.

Después autorizó continuar en Citra. Citra Qt mostró correctamente el título de
X y comprobó muestras de las dependencias nativas. Los pulsos de teclado
automatizados no hicieron avanzar el juego, aunque las asignaciones se veían
correctas en su configuración; el propietario introdujo manualmente el control.
No se modificó su instancia Lime3DS ni se guardó sobre su partida original.

En el caso privado `X-agent-control-03`, el log real del cargador registró:

- `DllBattle` cargado en `0x006A5000`, `fixed_end=0x00787000`.
- Tamaño de entrada CRO `0xFE000`, `fix_level=1`, parámetro `crr_address=0`.

Son observaciones de **esta sesión**, no una base constante ni prueba de la
política completa CRR/RSA. La cabecera cargada no llegó a leerse: el cliente
falló con `Incomplete memory response` antes de insertar ningún breakpoint.
El propietario observó el juego congelado; el depurador había detenido la CPU
y el cliente terminó con error sin reanudarla. Se cerró únicamente la copia
aislada. No fue una ejecución de un parche Gen6.

Se reprodujo el fallo desde el arranque, sin entrar en combate ni insertar
trampas. La captura TCP real mostró, tras el byte de interrupción `03`, un
paquete de parada `T05…` seguido de un paquete vacío `$#00`. La siguiente
lectura recibía ese paquete vacío; su respuesta auténtica quedaba pendiente
para la orden posterior. Este resultado acredita la desincronización del
cliente con el comportamiento observado de este binario, sin atribuir una
causa de compilación a la fuente preservada.

`Rsp.interrupt` ahora introduce una consulta soportada de solo lectura como
barrera, consume como máximo ese único paquete vacío y exige la respuesta
de capacidades antes de permitir otra orden. **No reenvía órdenes Z/z** ni
acepta datos de memoria incompletos. Dos ciclos reales pausa/lectura desde el
arranque verificaron 384 bytes y una lectura posterior de 16 bytes idénticos
a los datos previos, con cero breakpoints insertados. Esto prueba la corrección
de transporte en ese escenario; la secuencia completa de batalla sigue pendiente.

Pruebas sintéticas actualizadas: **55 tests Python OK**, incluyendo
`test_citra_empty_interrupt_trailer_is_drained_before_memory_without_resending`
y `test_interrupt_fence_rejects_unknown_reply_and_more_than_one_empty_trailer`.
El CI anterior para `6100f783` terminó [SUCCESS](https://github.com/joanjuz/Definitive-Randomizer/actions/runs/45931151974).
La nueva revisión necesita su CI y la repetición de captura de combate.

## Trazas originales completadas: salvaje y Liga en X

No se instaló ningún parche funcional. Solo se insertaron breakpoints temporales
en RAM, retirados con relectura de las instrucciones originales. Los JSONL,
direcciones privadas y datos de Pokémon permanecen fuera de Git.

### Control salvaje `X-agent-control-04`

La cabecera cargada dio `.text = 0x006A5180`; las direcciones se calcularon con
`T + (F - 0x180)`, sin fijar esta base para futuras sesiones. Se verificaron
14 instrucciones originales. La secuencia observada fue `1945C → 1469C →
14850 → 14854 → 19460` (offsets de archivo hexadecimales). Modo `0`, byte
`+0x4B = 0`, lado `0`. En `14854`, `r0 = 0`, `CPSR = 0x80000010`: el BNE
original se tomó y omitió el bucle de conciliación. No se alcanzó `148AC`.
Esto acredita el modo de este control salvaje, no todos los combates salvajes.
SHA-256 del JSONL privado:
`E5F84047159654052B37D9A51D0ADC42F98BD2F9F7D79236A7C275960573C701`.

Tras retirar los breakpoints, esa versión del cliente dejaba la CPU pausada.
El propietario observó la detención al finalizar. Ahora `resume_clean()` solo
devuelve el control si no quedan trampas propias ni fallos de verificación,
y espera el ACK de `continue` antes de desconectar. Un sondeo real sin combate
comprobó limpieza y ACK; la captura siguiente verificó además la continuación
visible del juego. Ante limpieza incompleta sigue prohibida la reanudación.

### Baya Zidra en Liga `X-sitrus-trainer-06`

El propietario confirmó que era un combate de la Liga, que Gardevoir llevaba
Baya Zidra, que esta se activó y que terminó el combate. Después confirmó que
Citra respondía y Gardevoir figuraba sin objeto. No se reinició la partida
ni se editaron sus banderas de entrenadores. Es evidencia de ese combate de
historia; aún falta un entrenador corriente fuera de la Liga.

La captura original muestra:

| Punto (archivo) | Valor y ruta observados |
| --- | --- |
| `14854` | Modo `1`, byte `+0x4B = 0`, `r0 = 1`, CPSR `0x60000010`: BNE no tomado. |
| `148A0` | Objeto de referencia de Gardevoir `158` (Baya Zidra). |
| `148AC` | Parámetro 17 devuelve `1`, CPSR `0x20000010`: BEQ no tomado. |
| `148B8` | Consulta del objeto actual devuelve `0`. |
| `148CC` | Setter nativo recibe objeto `0` para el CoreParam de Gardevoir. |
| `19460` | Ambas vistas del equipo contienen Gardevoir con objeto `0`, checksum válido. |

Los otros cuatro miembros recorrieron `14948 → 14950 → 14954`, con parámetro
17 igual a `0`; el setter recibió respectivamente los objetos `664`, `234`,
`268` y `270`. Sus objetos finales permanecieron iguales. Las dos vistas son
estructuras mutables observadas, no copias inmutables ni prueba independiente
del guardado: ambas empezaron con objeto `158` y acabaron con `0` en el slot
de Gardevoir. No deben utilizarse como diario de procedencia durante la batalla.

Se verificaron todos los breakpoints retirados y el ACK de continuación.
SHA-256 del JSONL privado:
`E1D40C5587149F7919E449CDC95624CF7999D49760F65376B957A81B924A7FE4`.
No se obtuvo un inventario anterior al combate porque este comenzó antes de
la preparación solicitada. **El delta de Bolsa no está acreditado.** Tampoco
se trazó el instante de activación: lo confirmó el propietario.

### Lectura comprobada de los Pokémon y siguiente experimento

`pokemon_snapshot.py` lee el puntero de datos en `Accessor + 8` y su estado de
cifrado en `+0x0C`; no confunde el Accessor con un PK6. Obtiene las posiciones
de bloques de la dependencia nativa fijada por hash, coteja su tabla en RAM,
descifra únicamente una copia y exige checksum válido antes de informar especie
u objeto. La etiqueta de identidad es un resumen local de EC/PID/propietario,
útil para correlacionar estas capturas; no garantiza unicidad entre clones.
Los errores conservan los datos opacos y se notifican, sin inventar campos.

Resultado técnico: **68 tests Python OK**, incluidos los 32 selectores de bloque,
datos truncados/corruptos, límites, inmutabilidad y reanudación condicionada a
limpieza. El CI del commit previo `9706a86e` terminó
[SUCCESS](https://github.com/joanjuz/Definitive-Randomizer/actions/runs/45933263898).
Los cambios de esta sección necesitan su propio CI. Sin cambios C#/WPF/USUM.

La evidencia demuestra qué hacen ambas condiciones en estos dos controles,
pero no autoriza invertirlas: falta distinguir consumo propio de transferencias,
Desarme y Lanzamiento, además de los modos excluidos y la política completa CRR.
El siguiente experimento debe registrar la mutación del objeto durante el
combate y su caller, además de la conciliación, con inventario inicial/final.
X y Y siguen bloqueados para producción; Y aún no tiene evidencia dinámica.

### Instrumentación opcional del cambio de objeto

`--item-events` añade tres breakpoints cuya instrucción original se verifica
antes de insertar ninguna trampa: `6B140` (entrada), `6B1F0` (después del
`STRH r6,[r4,#0x12]`) y `6B248` (después de los stores a `+0x14` y `+0x12`).
La entrada registra `r0..r3`, LR y el bloque apuntado por `r1`; los puntos
posteriores leen ese bloque desde `r4`. Se conservan los campos `+0x12`,
`+0x14`, `+0x19`, `+0x167`, `+0x16A` y los bytes opacos. Los nombres reflejan
offsets, **no** certifican todavía qué mecanismo de consumo o transferencia
representa cada campo. LR convertido a offset se etiqueta como candidato.

La inspección estática del recurso fijado encuentra llamadas directas a
`6B140` en `1A470`, `1F4EC`, `20E68`, `20E7C` y `6C1EC`. Sus retornos son
respectivamente `1A474`, `1F4F0`, `20E6C`, `20E80`, `6C1F0`. No se asignan
todavía estos callers a movimientos concretos sin una traza del caso.

Ejemplo reproducible, con la instancia aislada cerrada y el guardado preparado:

```powershell
python tools/research/xy-persistent-items/gdb_trace.py --session artifacts/pi145-x --case X-sitrus-provenance-07 --launch --item-events
```

El propietario preparó otra Baya Zidra en Gardevoir y comunicó **993** en Bolsa.
Se conservó una copia privada del guardado y su metadata antes de la siguiente
captura. Es una observación inicial, no un resultado de ese experimento.

La suite ampliada terminó con **73 tests Python OK**. Las cinco pruebas nuevas
cubren campos/endian/argumentos de entrada, selección de `r4` en los puntos
posteriores, errores de memoria sin campos inventados, rechazo de offsets
desconocidos y verificación de las 17 instrucciones antes de instrumentar.
La nueva captura quedó conectada y en espera de la ejecución manual. Aún no
hay un resultado del consumo instrumentado que permita clasificar su caller.

## Activación original instrumentada de Baya Zidra en X

El caso `X-sitrus-provenance-07` completó la captura. El propietario confirmó
al finalizar: **993 Bayas Zidra en Bolsa**, igual que antes del combate, y
Gardevoir sin la baya que tenía equipada. Es un conteo manual anterior/posterior;
no se presenta como una lectura automática de todo el inventario ni como prueba
de ausencia de duplicación para los demás mecanismos.

Se verificaron las 17 instrucciones originales con `.text = 0x006A5180` en esta
sesión. Hubo una entrada a la rutina candidata y dos observaciones posteriores:

| Punto (archivo) | Observación |
| --- | --- |
| `6B140` | `r2=0`, `r3=1`, LR convertido a offset `6C1F0`: llamada desde `6C1EC`. Participante nativo `+0x19=0`; `+0x12=158`, `+0x14=0`. |
| `6B1F0` | Mismo puntero de parámetro: `+0x12=0`, `+0x14=0`, `+0x16A=0x80`. El store del objeto actual ya ocurrió. |
| `6B248` | Mismo puntero: `+0x12=0`, `+0x14=158`, `+0x16A=0x80`. Se observan los stores de la ruta con `r3` distinto de cero. |

En `6B248`, el byte `+0x167` todavía vale `0`: el breakpoint está **antes**
del bloque que lo modifica. No se deduce su valor final de esa muestra. El
campo `+0x14` conserva el ID de la baya; esto por sí solo no acredita un marcador
exclusivo de consumos restaurables. Falta compararlo con Lanzamiento y otras
formas de pérdida/transferencia.

Después, `14854` observó modo `1`; `148AC` recibió objeto original `158` y
parámetro 17 igual a `1`; `148CC` pasó `0` al setter nativo. Al volver por
`19460`, los datos checksum-válidos del mismo Gardevoir contienen objeto `0`.
Los otros cuatro objetos siguen siendo `664`, `234`, `268`, `270`. No hubo
errores de estructuras/descifrado en esta captura. Las 17 trampas propias se
retiraron y verificaron antes del ACK de continuación.

SHA-256 del JSONL privado:
`085E8A03ECE5772665B755143EEB08C4B88806E8868EDB97FE5A2B0442426FD1`.
La copia anterior del guardado y su metadata también permanecen privadas.
No se modificaron ROM, CRO ni CRR para este experimento. El resultado identifica
un caller real de activación de Baya Zidra; no demuestra aún que todos los
consumibles ni todos los modos pasen por él. El siguiente control solicitado
es Lanzamiento con una baya equipada para comparar caller, campos y conciliación.

## Control de procedencia: Lanzamiento en X

El caso `X-fling-sitrus-08` terminó sin errores de lectura/descifrado. El
propietario confirmó **992 Bayas Zidra en Bolsa antes y después**, una baya
equipada inicialmente a Gardevoir, Lanzamiento ejecutado correctamente con
efecto de la baya sobre el rival y Gardevoir sin objeto después. Las cantidades
y el efecto visible son observaciones manuales; la traza acredita por separado
la mutación y la conciliación. No se interpreta el efecto sobre el rival como
un consumo normal del objeto propio.

| Observación | Activación propia (`07`) | Lanzamiento (`08`) |
| --- | --- | --- |
| Caller de `6B140` | `6C1EC` (LR `6C1F0`) | `1A470` (LR `1A474`) |
| Argumentos `r2`, `r3` | `0`, `1` | `0`, `1` |
| Entrada: campos `+0x12`, `+0x14` | `158`, `0` | `158`, `0` |
| Tras `6B244`: campos `+0x12`, `+0x14` | `0`, `158` | `0`, `158` |
| Modo final observado | `1` | `1` |
| Parámetro 17 para objeto inicial `158` | `1` | `1` |
| Setter en `148CC` | `0` | `0` |

En la entrada de Lanzamiento, el byte `+0x167` ya valía `0x80`; en el control
anterior valía `0`. Esta diferencia aislada **no acredita** un discriminador:
no se ha demostrado qué modifica ese bit ni su estabilidad entre escenarios.
El byte `+0x16A` pasó de `0` a `0x80` en ambos casos.

Al retorno `19460`, ambas vistas checksum-válidas contienen el mismo Gardevoir
con objeto `0`; los otros cuatro objetos se mantienen. Se verificó la retirada
de las 17 trampas y el ACK de continuación. SHA-256 del JSONL privado:
`EBFC5313A0FDBF1231884346DC1699ED8674DEE7A6B25431E1F32FB4E9A08C17`.
Guardado previo, metadata, JSONL y observaciones quedan fuera de Git.

**Consecuencia para el diseño:** ni el ID conservado en `+0x14` ni `r3=1`
identifican exclusivamente un consumible restaurable. La condición final
observa la misma combinación de objeto inicial/parámetro 17/objeto actual en
estos dos casos. Invertirla o forzar la restauración usando solo esos valores
también alcanzaría Lanzamiento, contrario al contrato de procedencia. El
caller distingue estas dos ejecuciones concretas; no se declara todavía que
sea suficiente para todos los objetos o scripts. Hace falta una política de
procedencia que registre el consumo admitido y descarte pérdidas/transferencias
posteriores, validada con Truco, Ladrón y Desarme. Se ha solicitado preparar
Truco como siguiente control. No se habilita ningún parche de producción.
