# XY-MINT: acceso nativo y contrato protegido

Issue: [#146](https://github.com/joanjuz/Definitive-Randomizer/issues/146).
Base: `f8f958432958df4a18b4ef482a7d5e1fe3f91b7c`, main con los PR #144 y #145 fusionados.

## Estado de la entrega

**Las 21 mentas todavía no están implementadas para uso en juego. No hay un candidato `.3ds` de mentas ni un instalador habilitado.**

Esta entrega acredita mecanismos nativos de X e Y y añade el contrato de distribución independiente, su bloqueo de ejecución en Global/Batch y pruebas. No presenta los replays como prueba jugable. No cambia los servicios USUM, el recordador, Persistent Items ni la Preview instalada.

| Componente | Resultado |
|---|---|
| Identidad de X e Y | Cabeceras reales y perfiles SHA-256 independientes, verificados en extracciones nuevas |
| Naturaleza real | Getter nativo, bloque A, cifrado y checksum ejecutados en Unicorn |
| Estadísticas | Cinco calculadores/setters originales ejecutados; 800 casos por título |
| 21 naturalezas | Catálogo de 20 no neutras y Seria, sin asignación oficial de IDs |
| IDs de objetos | 20 nombres «???» candidatos y evaluación adicional de #623; asignación pendiente |
| Distribución | Política independiente, predeterminada OFF, con pruebas de filtros, pesos y dependencias; consumidores todavía sin conectar |
| Global y Batch | `Gen6MintItems.Include=true` se rechaza antes de ejecutar o escribir |
| Instalador, historial de instalación y WPF | Pendientes del handler nativo; no se añade una pantalla con instalación ficticia |
| Candidatos, emulador y aceptación manual | Pendientes; no se ha pedido otra batería de combates |

## Recursos y perfiles

El recurso de Bolsa observado en X/Y es **`DllBag.cro`**, no `Bag.cro`. La tabla `a/2/2/0` tiene **718 registros de 36 bytes**, frente a los 960 de USUM. Los textos de objetos observados están en bancos 95–99; no se trasladan los bancos 39–42 de USUM. X/Y tiene ocho archivos de idioma en esta extracción, no los diez usados por el servicio USUM.

`tools/research/xy-nature-mints/observed_profiles.json` contiene por separado las huellas de cabeceras, ejecutable, `DllBag.cro`, `DllField.cro`, `static.crs`, CRR, tabla de objetos, zonas, scripts y ocho archivos de textos. Son perfiles **de investigación de estas copias**, no una declaración de compatibilidad con otras revisiones, actualizaciones o modificaciones.

| Recurso | Pokémon X | Pokémon Y |
|---|---|---|
| Title ID | `0004000000055D00` | `0004000000055E00` |
| `.code.bin` | `0F6D3E1D66CBCD55D46FA453DB4B6AA489E03C3537C6B24026F2A53AF142797A` | `62554AC3D494F6A333D92B156AD239017114ECF95367A97DB57E251456692FE7` |
| `DllBag.cro` | `572FDD6305CF086B204938BD4558A3E806B1D8875FF2D9D424104F0A852854A2` | Misma huella, comprobada de nuevo |
| `static.crs` | `30D10ADA3F829AE5AE2080C15D27DDF809B3D852CBAF5928C44E609764BFFDAA` | `598EAA950FEC39DD4D7915257299B4BCECE7E143F3542A31EB5772E7F89FECBD` |

El analizador rechaza cabeceras cruzadas, dependencias desconocidas, nombres físicos distintos y enlaces. Relee las huellas antes de emitir el informe; no escribe en los recursos. Las referencias se resuelven mediante las relocaciones de `static.crs` de cada título, no suponiendo equivalencia por compartir el CRO.

`DllBag.cro` conserva sus cuatro hashes válidos, tamaño 102400 y segmentos observados: código `(0x180,80152)`, RO `(81920,3204)`, datos `(101592,20)` y BSS de 60 bytes. Su hash original de cabecera aparece exactamente una vez en la tabla CRR de cada título. **Esto no acredita un CRO modificado ni autoriza fabricar firmas CRR.** No se ha ampliado ni alterado ningún segmento, relocación o CRR.

## Hallazgos ARM

Los offsets de esta tabla son **de archivo de `DllBag.cro`**. Para ejecución en vivo debe obtenerse el inicio real de `.text`; no son VAs constantes.

| Offset | Observación |
|---|---|
| `0x428`, literal `0x42C` | Importación de comprobación de uso, resuelta a VA `0x43EA8C` |
| `0x2B24`, `0x7E74`, `0xCBF4`, `0xEF2C` | Callers observados de esa importación; sus contextos todavía requieren cierre de ABI/modo |
| `0x2B00..0x30F8` | Región acotada de acción de medicina; `0x2B6C` compara el tipo antes del dispatch |
| `0x450`, literal `0x454` | Aplicación nativa de objeto, VA `0x43DD28` |
| `0x9074` | Construcción de mensajes de medicina, con switch de tipos |
| `0x97E0..0x9848` | Conciliación de uso: `MOV r2,#1` en `0x97FC`, `BL 0x500` en `0x9800` |
| `0x500`, literal `0x504` | Importación VA `0x434944`: valida ID/cantidad, resta la cantidad y compacta cuando llega a cero |

El retorno del borrado de Bolsa no se comprueba inmediatamente en esa ruta original. No basta con saltar a la conciliación después de escribir la naturaleza: hay que cerrar selección, cancelación, efectos previos, fallo de borrado y mensaje. Tampoco se modifica indiscriminadamente la importación compartida: primero debe demostrarse qué callers pertenecen a uso fuera de combate.

Las direcciones siguientes son **VAs de `.code.bin`**, con base observada `0x100000` verificada en cada exheader:

| VA | Evidencia |
|---|---|
| `0x4A0D40` | Getter de naturaleza del Core; carga Accessor desde Core `+0x0C` |
| `0x152C3C` | Getter del Accessor; selector de bloque A y lectura de byte `+0x14` |
| `0x168660` / `0x1685F0` | Apertura y cierre: descifrado, validación/actualización de checksum y cifrado |
| `0x16B358` | Selección del bloque; posición obtenida de la tabla real del ejecutable |
| `0x4A1658` | Consulta nativa de huevo/bad egg candidata para el filtro de uso; integración pendiente |
| `0x4A16F0` | Getter de estadísticas que puede devolver caché; **no** es un setter ni una sustitución acreditada del refresh |
| `0x12B808..0x12B8E0` | Refresh más amplio: nivel/PS y cinco calculadores de estadísticas; no se llama entero sin estudiar su parámetro |
| `0x14FB00`, `0x14FBD4`, `0x14FE78`, `0x14FA2C`, `0x14FDA4` | Calculan/escriben Ataque, Defensa, Velocidad, At. Esp. y Def. Esp. respectivamente |

La inicialización de la tabla personal del replay se deriva de los lectores nativos en `0x167E18`, `0x167E2C`, `0x174C0C` y `0x175C78`. Se prepara un manager propio en RAM de Unicorn con estadísticas base sintéticas. No se reutiliza memoria del proceso del usuario ni se supone su propiedad en el juego.

**Bolsillos:** el getter rápido `0x39FAF4` usa una caché de nibbles a través del global observado `0x5D1728`. La rutina `0x107788` la construye en memoria: el bucle `0x107808..0x107844` lee registros de objetos y empaqueta sus bits de bolsillo. El selector 11 del lector de parámetros (`0x4A2378`) deriva el mismo valor de los bits 7–10 del `ushort +8`. No se ha localizado ni se presupone un archivo de bolsillos separado equivalente a `a/1/0/3` de USUM; no se modifica ese archivo por analogía. La observación estática del constructor/getter está incluida en los informes de ambos títulos, pero aún no hay prueba de una menta seleccionada en la Bolsa.

### Replay nativo reproducible

`replay_accessor.py` ejecuta las rutinas originales de cada `.code.bin`, sin sustituir llamadas nativas por stubs. Un wrapper de prueba ARM existe exclusivamente en RAM de Unicorn; **no es un payload instalable**. No se modifica el archivo ejecutable.

En cada título se ejecutan 32 filas de permutación × 25 naturalezas = **800 casos**. Datos de entrada sintéticos: especie 282, objeto 158, nivel 50, IV/EV cero y bases 100. Se comprueban:

- Getter antes y después, preservación de registros callee-saved y SP.
- Ciclo de cifrado/descifrado y checksum nativos.
- Cambio exclusivamente de naturaleza/checksum en los 232 bytes del Pokémon.
- Resultado de las cinco estadísticas contra una fórmula independiente: 105 neutra, 115 aumentada, 94 reducida.
- Conservación de PS actuales/máximos, nivel, estado y otros bytes del bloque de equipo.

La primera hipótesis del fixture situaba estadísticas dos bytes antes. El replay la rechazó; los setters originales y la ejecución fijan los cinco `ushort` en `+0x0C..+0x14` del bloque de equipo. No se corrigió el ejecutable para acomodar el fixture.

Estos resultados no cubren aún selección desde la Bolsa, huevos, misma naturaleza, cancelación, consumo, guardado en partida, iconos o mensajes. No demuestran que el juego llame al futuro hook en el contexto correcto.

## Asignación de objetos y referencias

Los 20 IDs no nulos cuyo nombre inglés observado es «???» son:
`113,114,115,120,121,122,123,124,125,126,127,128,129,130,131,132,133,426,427,622`.
El ID 0 es el centinela de ausencia y no se reutiliza. Tampoco se reutilizan automáticamente las piedras de megaevolución inaccesibles en XY.

El ID **623** (entrada heredada Gram 1) es un **candidato adicional**, no una asignación aprobada. En cada extracción:

- Se decodificaron 779 envelopes de script: 59 comunes y 720 scripts dentro de 360 paquetes ZO. Otro miembro de 20160 bytes no es un paquete ZO y queda explícitamente fuera de esa decodificación.
- No aparecen palabras decodificadas iguales a 623 ni 622. Esto es ausencia de una constante numérica; no prueba ausencia de referencias calculadas o de otros recursos.
- Los lectores existentes reconocen 26 inventarios de tiendas, 557 ubicaciones visibles/ocultas y 29 entradas Pickup. Ninguna referencia tipada apunta a los 21 candidatos de esa lista ampliada.
- Las dos referencias principales alineadas de `.code` al literal 623 (`0x1252FC`, `0x2C6754`) alimentan bucles de inicialización del generador aleatorio, con desplazamiento 30, multiplicación y tabla de 624 palabras. No se cuentan como operaciones sobre objetos.

La [tabla de objetos XY de PKHeX](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Items/ItemStorage6XY.cs) sirve de contraste externo de objetos clave; no se usa como autorización de escritura ni sustituye la inspección de scripts. No se importa código de esa tabla.

Falta cerrar referencias nativas/guionizadas del slot adicional, icono y efectos sobre inventario, y validar cada entrada del mapa final en ambos títulos. `OfficialItemMap` permanece `null`.

## Distribución, Global y Batch

`Gen6NatureMintCatalog` fija las 21 naturalezas (20 modificadoras y Seria). `Gen6MintItemMap` valida cantidad, exclusividad, límites 1–717 y naturalezas exactas, sin convertir un mapa sintético en evidencia de instalación.

`Gen6MintItemPolicy` es independiente de `Gen7MintItems`. OFF copia el pool conservando orden y multiplicidades, sin consultar instalación, filtrar ni consumir RNG. ON exige una acreditación inyectada, agrega únicamente IDs ausentes, respeta Allowed/Banned y verifica dependencias antes/después. No está conectado a los editores mientras el instalador no esté acreditado; sus pruebas usan IDs sintéticos que no se instalan.

El nodo separado es `ProgressiveApp.Gen6MintItems = { "Include": false }`. Su ausencia equivale a false. Se conservan campos desconocidos al capturarlo y serializarlo. El contrato compartido Core rechaza tipos inválidos, campos duplicados y opt-in para otra familia. Un true de XY válido **también se rechaza** con el bloqueo nativo; no instala nada automáticamente.

El bloqueo actúa en Global, preflight de Batch, puente Gen6 del staging y validador Legacy anterior a la ejecución. Se comprueba que no crea salidas ni cambia archivos/historial. No altera la rama USUM del validador. No se conecta aún la restauración de una preferencia desde historial de instalación inexistente ni la acción garantizada de añadir 21 mentas a tiendas.

## Pruebas técnicas

Resultados locales (27/11/2026):

| Prueba | Resultado |
|---|---|
| Build Release Tests, incluye App WPF/Core/Legacy | 0 errores; warning previo CS0675 en `NdsMapRenderChecks.Synthetic.cs:54` |
| `--gen6-mints-only` | 91 checks sintéticos aprobados |
| `--usum-mints-distribution-only` | 128 checks aprobados |
| `--usum-mints-only` | 98 checks aprobados |
| `--gen6-persistent-items-only` | 122 checks aprobados |
| `--map3ds-reminder-only` | 72 checks aprobados |
| Python sin recursos comerciales ni Unicorn | 12 tests aprobados |
| Perfiles/CRO/importaciones originales | X e Y verificados independientemente |
| Replay original Accessor + estadísticas | X 800 / Y 800 aprobados; entradas sintéticas, no jugable |
| CLI aislado de extracción/lectura | Build 0 errores y 0 warnings; corpus y referencias ejecutados en ambas copias |

Las mutaciones controladas en memoria de los helpers Python detectaron la omisión de la condición NV/BLX y de la validación de alineación/rango de direcciones. No se ejecutó una revisión histórica sin esos cambios. CI incorpora la suite nueva del runner y los tests Python; el resultado remoto debe consultarse en el PR, sin sustituirlo por el resultado local.

## Reproducción

Desde el worktree, con .NET 10 y Python. Ningún comando de replay produce una ROM:

```powershell
dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release -p:UseAppHost=true '-bl:artifacts/xy-mint/tests-{}.binlog'
dotnet 3DS.Progressive.Tests/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll --gen6-mints-only
python -m unittest discover -s tools/research/xy-nature-mints -p 'test_*.py' -v
python -m pip install -r tools/research/xy-nature-mints/requirements-replay.txt
python tools/research/xy-nature-mints/inspect_mints.py 'D:/Progra/Pokemon/Randomizer/PruebasMentasXY-X-01/Proyecto' --output 'D:/Progra/Pokemon/Randomizer/PruebasMentasXY-X-01/evidence/native-new.json'
python tools/research/xy-nature-mints/replay_accessor.py 'D:/Progra/Pokemon/Randomizer/PruebasMentasXY-X-01/Proyecto' --output 'D:/Progra/Pokemon/Randomizer/PruebasMentasXY-X-01/evidence/replay-new.json'
```

Repetir los dos últimos comandos con la carpeta Y. Se rehúsa sobrescribir los informes. Las huellas de origen deben coincidir con el perfil; no se renormalizan para admitir otros binarios.

El CLI `tools/research/xy-nature-mints/playable/XYMint.Experiment.csproj` ofrece `extract`, `inspect`, `scripts` y `references`. Requiere carpetas nuevas `PruebasMentasXY-X-*` o `PruebasMentasXY-Y-*`, cabeceras coincidentes, marcador de aislamiento y salidas nuevas dentro de `evidence`. `inspect` exporta textos comerciales **solo para investigación privada**: esos JSON no deben adjuntarse al PR. El directorio se llama `playable` por el patrón de herramientas del repositorio; no implica que genere un candidato jugable.

## Siguiente experimento y aceptación posterior

El próximo paso concreto es cerrar el flujo de los cuatro callers de `0x428` y la acción `0x2B00`, incluido su paso previo `0x976C`, para ubicar un dispatch de uso fuera de combate. Debe acreditarse el orden entre validación de huevo/misma naturaleza, mutación, mensaje y retirada de exactamente una unidad, con rutas de cancelación/fallo sin efectos. En paralelo debe cerrarse el slot adicional #623. No se emite un payload que omita esas comprobaciones.

Después podrá construirse un candidato aislado X con `CroPatchSession`, actualización de hashes, política CRR/cargador verificada, `EditorProposal`, transacción e historial; delta-undo y relectura deben comprobarse antes de reconstruir. Y necesita preparación y aceptación separadas. La expansión y los hooks deben conservar nombres físicos, relocaciones, CROs ajenos y las modificaciones de #144/#145.

Cuando exista ese candidato, la aceptación manual será: 21 cambios de naturaleza reales/estadísticas, Seria, huevo y misma naturaleza sin consumo, una sola unidad por éxito, atributos ajenos/objeto equipado intactos, guardar/cerrar/reabrir; distribución opcional en las cuatro fuentes con misma semilla/OFF idéntico, añadir 21 a tiendas sin perder inventario, capacidad insuficiente sin escritura; y repetir con recordador/Persistent Items y demás parches. Actualmente esos casos están **pendientes y sin resultado jugable**. No se solicita probar un archivo inexistente.
