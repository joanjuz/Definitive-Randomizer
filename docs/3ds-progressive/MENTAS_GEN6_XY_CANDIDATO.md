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

Las extracciones X e Y acreditan independientemente GARC **v4**, 579 imágenes, y texturas **BCLIM 30×30 RGB5A1, orientación None**, con 2048 bytes de píxeles y footer de 40 bytes. USUM utiliza GARC v6 y BFLIM 32×32 Rotate90: no se trasladan esos formatos. La imagen convertida de USUM se decodifica, se centra en 30×30 y se codifica en el formato XY. Se verifica su hash independiente.

Se añaden imágenes **579–599** y se actualizan únicamente las 21 palabras de la tabla en `.code.bin`. Se preservan exactamente los 579 registros anteriores; la reconstrucción canónica del GARC original coincide byte por byte con el original observado. Se rechazan tablas vectorizadas, rangos inválidos, compresión ajena, referencias parciales, texturas modificadas y cambios no reconocidos del ejecutable. El instalador de iconos requiere las mentas nativas instaladas, prepara dos archivos, relee las dependencias, valida la propuesta y registra su propia transacción y restauración exacta. No modifica `DllBag.cro`, CRR, textos, precio ni el handler de mentas.

La aceptación del primer candidato X corresponde a sus iconos heredados. El candidato posterior con estos iconos requiere únicamente una nueva comprobación visual y un control de uso del propietario; no se ha ejecutado automáticamente en un emulador.

El candidato privado `PruebasMentasXY-X-03/Pokemon-X-XY-MINT-EXPERIMENTAL.3ds` tiene tamaño `1801076736`, SHA-256 `1B83157D351560A9B20C3F000AA1BD6537C2DD66C6685A707562CDD3909D903E`. Su rebuild/reextracción conserva los 656 archivos esperados. La instalación real de iconos modificó únicamente `.code.bin` y `a/0/9/4`; deshacer y reinstalar conservaron exactamente el proyecto previo. El candidato X-02 y su guardado no se modificaron. Se puede abrir manualmente la nueva ROM desde el mismo perfil aislado de Citra, con el guardado de prueba que el propietario ya cargó; no requiere otro `main`. Validación pendiente: iconos de distintos colores, un objeto normal y un uso de menta como control.

## Evidencia técnica obtenida

| Prueba | Resultado |
|---|---|
| Runner `--gen6-mints-only` | 328 comprobaciones sintéticas aprobadas, incluidas conversión, referencias, fuentes, rechazo de mutaciones, propuestas alteradas, fallos transaccionales, idempotencia y reversión de iconos X/Y |
| Python `test_*.py` | 16 aprobadas; incluye restricciones y checksums de preparación de una copia de guardado |
| Regresiones sintéticas | USUM mentas 98, distribución USUM 128, Persistent Items X/Y 122, recordador X/Y 72, Batch Gen6 6 y Global 134 aprobadas |
| Iconos USUM | 88 comprobaciones aprobadas después de añadir el servicio XY |
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
