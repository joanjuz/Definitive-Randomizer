# XY-MINT: primer candidato experimental de X

Continuación del [PR #147](https://github.com/joanjuz/Definitive-Randomizer/pull/147), asociado al [issue #146](https://github.com/joanjuz/Definitive-Randomizer/issues/146). La investigación anterior se conserva en [MENTAS_GEN6_XY.md](MENTAS_GEN6_XY.md).

## Estado y alcance

Existe un handler ARM, un instalador experimental transaccional y una imagen reconstruida de Pokémon X. **El propietario cargó el `main` de prueba y dio por validada la integración de X, señalando únicamente los iconos heredados.** Esta aceptación no se extrapola a Y ni se convierte en una afirmación de que cada caso del protocolo fue reportado individualmente. Producción, WPF, Global y Batch siguen bloqueados hasta completar su integración. Y tiene perfil independiente y replay del handler con su ejecutable, pero no un candidato aceptado en juego. Por indicación del propietario, las validaciones en emulador quedan a su cargo; no se ejecutarán nuevas pruebas jugables autónomas.

El candidato X contiene las 21 mentas y una operación adicional y explícita de tiendas de prueba: añade las 21 una sola vez a nueve stocks generales. Instalar el handler no randomiza ni distribuye objetos por sí mismo. La conexión general de tiendas, suelo, Pickup, Global y Batch sigue pendiente.

## Flujo nativo

Los offsets siguientes corresponden al archivo `DllBag.cro`, no a una dirección cargada constante:

| Punto | Tratamiento experimental |
|---|---|
| `0x428`, original `E51FF004` | Branch al filtro propio. Los objetos ajenos vuelven al destino original `0x43EA8C` del ejecutable. |
| `0x2B5C`, original `E5942058` | Branch antes del paso previo `0x976C`. Los objetos ajenos reproducen `LDR r2,[r4,#0x58]` y continúan en `0x2B60`. |
| `0x976C` | No se ejecuta para mentas: contiene acciones previas compartidas que no deben afectar al Pokémon antes de confirmar la operación. |
| `0x97FC/0x9800` | Descuento original omitido únicamente por la ruta propia, que ya llamó una vez al borrado nativo. |
| `0x9814` | Reentrada al finalizador original tras preparar la pila y el retorno que espera. |

El payload tiene `0xB00` bytes, generados reproduciblemente desde `native/3ds/xy-nature-mints/mints.s`. Las ramas se reubican mediante las herramientas CRO del proyecto. Los fallbacks conservan los registros y las banderas CPSR. No se sustituye indiscriminadamente el handler compartido de aplicación `0x450`.

La operación copia Core, Accessor, los 232 bytes cifrados y los 28 bytes de equipo a un bloque propio de pila de `0x130` bytes. Requiere Accessor cerrado, punteros válidos y alineados, Pokémon no huevo/bad egg, naturaleza distinta y contexto de mensaje disponible. Aplica el cambio y los cinco cálculos de estadísticas **sobre la copia**, usando las rutinas originales ya acreditadas. Llama al borrado original de Bolsa `0x434944` con cantidad uno y comprueba su retorno. Solo después de un borrado correcto copia el resultado al Pokémon original. No conserva punteros a la pila ni introduce asignaciones heap.

El replay verifica que no se escriba el Pokémon original antes del descuento; un fallo de elegibilidad, contexto o cantidad no escribe el Pokémon ni genera mensaje de éxito. El finalizador específico evita el segundo descuento. Esto no sustituye la verificación de selección, cancelación y mensajes en la UI real.

## Mensajes e IDs

El banco real de mensajes de Bolsa es **140 (`0x8C`)**, con 179 filas originales. En `DllBag.cro:0x12B54`, el thunk `0xAD0` resuelve a `0x348DA8`; los argumentos son `r1=0x8C`, `r2=1`. El constructor crea MsgData en contexto `+0x28` mediante `0x11E308`. La función `0x3480A4` selecciona un buffer de texto, no un banco. El formateador `0x348834` utiliza MsgData y el formatter en contexto `+0x30`.

El instalador añade la fila **179** al banco 140 de cada uno de los ocho idiomas, conserva sus filas anteriores y modifica únicamente las filas de objetos asignadas en bancos 95–99. No se sustituye el banco 1 ni se copian índices de USUM.

Asignación **experimental**, separada del mapa oficial de producción:

| ID | Naturaleza interna | Menta |
|---:|---:|---|
| 113 | 1 | Huraña |
| 114 | 3 | Firme |
| 115 | 4 | Pícara |
| 120 | 2 | Audaz |
| 121 | 5 | Osada |
| 122 | 8 | Agitada |
| 123 | 9 | Floja |
| 124 | 7 | Plácida |
| 125 | 15 | Modesta |
| 126 | 16 | Afable |
| 127 | 19 | Alocada |
| 128 | 17 | Mansa |
| 129 | 20 | Serena |
| 130 | 21 | Amable |
| 131 | 23 | Cauta |
| 132 | 22 | Grosera |
| 133 | 10 | Miedosa |
| 426 | 11 | Activa |
| 427 | 13 | Alegre |
| 622 | 14 | Ingenua |
| 623 | 12 | Seria |

El ID 623 está dentro de los 718 registros originales. Las referencias literales 623 de `0x1252FC/0x2C6754` corresponden a bucles Mersenne Twister de 624 palabras, no prueban uso como objeto. Las búsquedas anteriores de referencias tipadas en tiendas, suelo, Pickup y scripts no encontraron colisiones; no constituyen una demostración universal sobre todos los eventos del juego.

La tabla de iconos empieza en archivo **`0x43DB74`**, VA `0x53DB74`, e indexa por ID. El GARC `a/0/9/4` tiene 579 imágenes. El primer candidato conservaba los iconos de los placeholders y los iconos heredados 492/493 de 622/623; el instalador posterior de iconos añade las imágenes de mentas descritas abajo.

## Integridad, instalación y reversión

`Gen6NatureMintPatch` distingue original, instalado, parcial, modificado e incompatible; verifica firmas, perfil, título y digest del resultado. La marca propia no basta para aceptar un archivo modificado. X e Y tienen dependencias independientes en `observed_profiles.json`.

`Gen6NatureMintService` está deshabilitado por defecto. El CLI experimental debe habilitarlo explícitamente en una copia identificada. Genera `EditorProposal`, valida de nuevo antes de aplicar y utiliza las transacciones e historial existentes. La vista previa obsoleta o alterada se rechaza. La instalación repetida es idempotente. Deshacer restaura los bytes anteriores de esa operación, sin sustituir globalmente archivos por versiones vanilla.

Se mantiene el nombre físico **`DllBag.cro`**, se usa el asignador CRO compatible con segmentos/relocaciones y se regeneran sus hashes. En CRR se reemplaza únicamente la entrada de hash correspondiente al CRO. **La firma RSA existente no se regenera ni se presenta como firma válida del contenido modificado.** El comportamiento del cargador con el candidato y el uso en hardware no están acreditados por el rebuild ni por Unicorn.

El perfil reconoce el `.code.bin` original y, solo en X, la huella exacta de la expansión de tiendas del candidato aceptado (`A66CD57050FBDD851B7C87CC022F0C9D76EF62B234D71AE9491DDA5DC4879DF6`). Normaliza únicamente las 21 referencias gráficas propias antes de comparar esas huellas. No acepta otras ediciones del ejecutable. La convivencia general con otros parches sigue pendiente; no se declara acreditada con Persistent Items, tabla de tipos o Escudo Real.

## Iconos específicos de X/Y

`Gen6MintIconService` reutiliza las fuentes PokéAPI fijadas por commit y SHA-256 de USUM, sin cambiar sus servicios. La asignación se hace por el orden de naturalezas acreditado, usando los IDs de X/Y; no se copian los IDs de USUM.

Las extracciones X e Y acreditan independientemente GARC **v4**, 579 imágenes, y texturas **BCLIM 30×30 RGB5A1, orientación None**, con área de datos de 2048 bytes y footer de 40 bytes. **Los datos de iconos llevan una paleta propia; no son 2048 bytes de píxeles directos.** USUM utiliza GARC v6 y BFLIM 32×32 Rotate90: no se trasladan esos formatos. La imagen convertida de USUM se decodifica y se centra en 30×30 antes de crear la paleta XY.

El propietario informó que el candidato X-03 mostraba los iconos vacíos. La causa se reprodujo ejecutando el código original: `0x338B04` copia los datos mediante `0x300EF8` y luego expande la paleta. En `0x338B44/48` lee tamaño de entrada y cantidad; en `0x338B50/64`, `cantidad <= 16` selecciona índices de cuatro bits, con el nibble alto primero (`0x338B90..0x338BB4`). Para más de 16 usa índices de ocho bits (`0x338C24..0x338C78`). Los colores son halfwords RGB5A1 copiados sin reinterpretación a la salida. El candidato X-03 tenía ceros iniciales de píxeles directos; esa rutina los interpretaba como paleta vacía y producía alfa transparente en sus 21 imágenes. El decodificador gráfico propio había aceptado los píxeles directos: aquella prueba no demostraba aceptación por el juego.

La conversión v2 escribe `ushort 2`, cantidad de colores, paleta RGB5A1 e índices Morton; reserva el índice cero para transparencia (`0xFFFE`) y conserva el área de 2048 bytes. Cuando se necesitan más de 69 colores opacos se aplica reducción determinista mediante median cut ponderado por población. La paleta resultante tiene como máximo 70 entradas con transparencia, mantiene el alfa y usa nibble alto primero también con exactamente 16 colores. Los hashes de las 21 texturas se actualizan para esta conversión acreditada; las texturas v1 fallidas no se aceptan como instaladas.

Se añaden imágenes **579–599** y se actualizan únicamente las 21 palabras de la tabla en `.code.bin`. Se preservan exactamente los 579 registros anteriores; la reconstrucción canónica del GARC original coincide byte por byte con el original observado. Se rechazan tablas vectorizadas, rangos inválidos, compresión ajena, referencias parciales, texturas modificadas y cambios no reconocidos del ejecutable. El instalador de iconos requiere las mentas nativas instaladas, prepara dos archivos, relee las dependencias, valida la propuesta y registra su propia transacción y restauración exacta. No modifica `DllBag.cro`, CRR, textos, precio ni el handler de mentas.

El propietario validó primero el funcionamiento de X con iconos heredados. Después de corregir el formato de paleta confirmó que X-04 «funcionó al 100%»: queda registrada también su aceptación de los iconos en juego. No se ha ejecutado automáticamente en un emulador ni se extrapola esta confirmación a Y.

El candidato anterior `PruebasMentasXY-X-03/Pokemon-X-XY-MINT-EXPERIMENTAL.3ds` tiene tamaño `1801076736`, SHA-256 `1B83157D351560A9B20C3F000AA1BD6537C2DD66C6685A707562CDD3909D903E`. Su rebuild/reextracción conserva los 656 archivos esperados. La instalación real de iconos modificó únicamente `.code.bin` y `a/0/9/4`; deshacer y reinstalar conservaron exactamente el proyecto previo. Esto acreditaba la transacción, pero no el formato gráfico que requería el juego.

**X-03 queda descartado para validar los iconos:** el propietario observó imágenes vacías y el replay reprodujo el problema. El candidato corregido y aceptado por el propietario es X-04, con la paleta v2 descrita arriba. Sus 21 imágenes también se visualizaron fuera del juego después de expandirlas, y los 2048 bytes de cada una coinciden con la ejecución nativa de X e Y. La evidencia jugable procede de la confirmación del propietario; la validación independiente de Y sigue pendiente.

La ROM X-04 tiene tamaño `1801072640`, SHA-256 `6F841869CCED1CCDF104E812FC0D054400E230DF0ED9BC076259C073F71A7002`. Rebuild y reextracción conservan los 656 archivos. Comparados los proyectos X-03 y X-04, cambia únicamente `RomFS/a/0/9/4`: ejecutable, handler, objetos y textos se mantienen idénticos. Las partidas y candidatos anteriores no se modificaron. El propietario realizó la comprobación visual del candidato X-04 con su partida de prueba y la dio por aprobada. No se afirma que haya reportado individualmente cada caso del protocolo general.

## Evidencia técnica obtenida

| Prueba | Resultado |
|---|---|
| Runner `--gen6-mints-only` | 341 comprobaciones sintéticas aprobadas, incluidas paleta/alpha/índices y límites 16/17/70, reducción determinista, rechazo de píxeles directos, transacciones y reversión de iconos X/Y |
| Python `test_*.py` | 20 aprobadas; incluye formato de paleta, nibble alto, límites e índices inválidos, LZ11 y las comprobaciones anteriores de guardado aislado |
| Regresiones sintéticas | USUM mentas 98, distribución USUM 128, Persistent Items X/Y 122, recordador X/Y 72, Batch Gen6 6 y Global 134 aprobadas |
| Iconos USUM | 88 comprobaciones aprobadas después de añadir el servicio XY |
| Expansión ARM de paletas v2 | 49 casos aprobados por título, sin stubs: límites 1/4/15/16/17/70, control negativo de píxeles directos, los 21 iconos vacíos de X-03 y los 21 iconos corregidos de X-04 |
| Replay del handler X | 232 casos aprobados, también sobre el CRO exacto producido por el instalador |
| Replay del handler Y | 232 casos aprobados con su ejecutable independiente |
| Instalación real aislada X | 11 archivos, lectura posterior, deshacer exacto, reinstalación y reapertura del proyecto comprobados |
| Reconstrucción/reextracción X | 656 archivos RomFS/ExeFS idénticos al proyecto candidato; nombre físico conservado |

Los replays ejecutan cifrado, getter/setter de naturaleza, cinco estadísticas y búsqueda/descuento/compactación nativos. La consulta de recursos de objetos y la presentación gráfica usan stubs de contrato: **no son una ejecución completa del juego**. Cubren 21 IDs, cantidades 1/2/999, misma naturaleza, huevo, falta de objeto/cantidad/contexto/manager, Accessor abierto, debilitado y base de carga alternativa. Verifican checksum, atributos ajenos, ABI/pila, mensaje y un único descuento.

La imagen privada de X se llama `Pokemon-X-XY-MINT-EXPERIMENTAL.3ds`, tamaño `1801064448` bytes, SHA-256:

```text
BF7E6533EE68E3D47646BADA476ADB9FE91585EDCDCE8834C779CDA78FC2E9D0
```

No se incluyen ROMs, guardados ni buffers comerciales en Git. La primera apertura autónoma no conservó el archivo preparado. Después se generó un archivo separado para importar manualmente; el propietario confirmó que lo cargó y que la integración quedó correcta. Esa confirmación es la evidencia jugable de X. No se realizaron nuevas pruebas autónomas del emulador.

## Protocolo manual del propietario

Usar exclusivamente el candidato y una **copia** de guardado en un perfil aislado de emulador. No abrir una partida habitual para probar. En esta fase no se requieren combates.

1. Comprobar que el candidato arranca, permite continuar la copia y abre Bolsa sin bloqueo. Obtener una menta en una tienda general si la copia no contiene suministros; el precio experimental es 20000.
2. Anotar naturaleza, cinco estadísticas y cantidad de una menta de naturaleza distinta. Usarla en un Pokémon válido: mensaje localizado, naturaleza real cambiada, estadísticas coherentes y exactamente una unidad menos. El resto del equipo y los objetos deben permanecer iguales.
3. Repetir la misma naturaleza: sin efecto ni consumo. Cancelar la selección de otro uso: sin cambio ni consumo. Intentar un huevo: rechazo sin cambio ni consumo.
4. Guardar, cerrar y reabrir manualmente: confirmar naturaleza, estadísticas y cantidad. Si hay bloqueo, detenerse y anotar el punto; no seguir sobre esa copia.
5. Tras el control inicial, comprobar las 21 mentas, incluida Seria, y un objeto normal de medicina como control de regresión. La aceptación independiente de Y se hará con un candidato Y posterior.

## Comandos técnicos reproducibles

Desde el worktree, con .NET 10, Python, Keystone 0.9.2 y las dependencias ya documentadas:

```powershell
python native/3ds/xy-nature-mints/generate.py --check
python -m unittest discover -s tools/research/xy-nature-mints -p 'test_*.py' -v
dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release
dotnet 3DS.Progressive.Tests/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll --gen6-mints-only
```

El CLI `tools/research/xy-nature-mints/playable` ofrece `plan`, `install-experiment`, `verify-undo`, `prepare-test-shops` y `rebuild-reextract`. Exige raíz aislada con marcador y salidas nuevas; los replays requieren binarios privados compatibles. No ejecutar comandos de emulador como parte de estas comprobaciones técnicas.

El replay privado `replay_icon_palette.py PROYECTO_ORIGINAL --archive GARC_ICONOS_CANDIDATO --failed-archive GARC_X03 --output INFORME_NUEVO` ejecuta la rutina original y su memcpy, sin stubs ni cambios en el juego. Comprueba los límites de paleta, reproduce el fallo con las imágenes X-03 y contrasta los 2048 bytes expandidos de cada imagen nueva con una referencia independiente. Requiere las dependencias originales verificadas por título; no es una prueba jugable.
