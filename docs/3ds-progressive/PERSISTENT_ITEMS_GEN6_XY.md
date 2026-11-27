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
Hay emuladores en el entorno, pero no se inició una sesión con guardados ni se
obtuvieron trazas de batalla en esta entrega. No hay evidencia jugable nueva X/Y.

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
