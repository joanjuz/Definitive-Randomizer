# Compatibilidad del instalador de mentas USUM

La compatibilidad depende de las funciones y estructuras que utiliza el parche. No se exige que el hash de code.bin ni de sus segmentos completos coincida con una referencia. Los cambios en otros lugares del ejecutable no bloquean las mentas. No hay instalación forzada ni se requiere comparar hashes manualmente.

## Preparación temporal y guardado

Se comprueban el Title ID NCCH de Ultra Sol/Ultra Luna, el tamaño descomprimido, el mapa del exheader y las dependencias nativas del perfil seleccionado por título. Se mantienen las comprobaciones de Bag.cro completo y sus cuatro hooks originales, los 21 objetos reservados, bolsillos, textos de diez idiomas y CRO/CRR.

La propuesta se aplica mediante `FileTransaction` a copias aisladas de todos los archivos afectados y dependencias leídas. Se relee el resultado, se exige el reconocimiento completo del runtime y se contrastan los archivos modificados y los ajenos. La copia se elimina en éxito/error. Preparar no guarda en el proyecto ni crea historial.

Solo después se muestra **Listo para instalar** y se habilita **Aplicar y guardar**. El guardado usa `ProjectHistory`, transacciones, bloqueos, concurrencia y Deshacer existentes. El exheader también es una dependencia de distribución. Los otros estados son **Instalado correctamente** e **Instalación no compatible**, con explicación y diagnóstico técnico desplegable.

## Dependencias nativas fundamentadas

El manifiesto `UsumMintNativeDependencies.cs` contiene 62 rangos y 9.185 bytes por perfil: instrucciones alcanzables, sus literales y tablas concretas. Un cambio en un rango necesario se rechaza aunque la instalación temporal pueda escribirse correctamente.

Las raíces proceden de los seis literales de `native/3ds/nature-mints/mints.s` y del ajuste UM de `UsumNatureMintPatch.Payload`. Son direcciones virtuales; se resta `100000` para obtener el offset de code.bin:

| Entrada | US | UM | Contrato utilizado |
|---|---|---|---|
| getter | `4AE0E0` | `4AE0E8` | Naturaleza desde el objeto Pokémon y su bloque cifrado |
| begin_access | `32258C` | `32258C` | Apertura, descifrado y checksum PK7 |
| block_a | `4AD588` | `4AD590` | Selección del bloque A; se escribe su byte `14` |
| end_access | `322514` | `322514` | Cierre, checksum y recifrado |
| derive_stat | `4AEA1C` | `4AEA24` | Lectura de IV con selectores 1–5 |
| store_stat | `324D84` | `324D84` | Reescritura de esos IV y recálculo nativo de estadísticas |

Se siguen ramas y llamadas directas, incluidos tail calls y tablas de saltos ARM acotadas por comparación. Se incluyen las rutas de especie/forma, campos PK7, fórmulas y auxiliares de memoria/sincronización alcanzados. Se protegen instrucciones completas de esas rutas, no ventanas arbitrarias alrededor de prólogos. Cada intervalo tiene un hash separado US/UM.

También se incluyen:

- Permutación PK7 en `5E6994`, 128 bytes: índice `(EC >> 13) & 31`, paso de cuatro bytes; cubre las rutas de bloques A/B/C usadas por getters y setters.
- Modificadores de naturaleza en `5E6A14`, **125 bytes**: 25 naturalezas × cinco estadísticas. No es la matriz de efectividad de tipos.
- Pools PC-relativos y tablas de saltos de las funciones que los leen.
- Descriptor en `5BA204`, ocho bytes: el gestor carga el destino de `5BA1CC+38` y su ajuste de `+3C`. Ajuste cero selecciona el callback directo `4B3694` US / `4B369C` UM, cuyo cuerpo se incluye.
- Interfaz del mensaje nativo en `6486A0`, 16 bytes, y adaptadores del gestor usados por la consulta de datos personales.

El límite del análisis de archivos es el ABI del motor: objetos, listas de receptores, vtables y sincronización creados dinámicamente no pueden certificarse mediante una instantánea de archivos. Se fijan los adaptadores y el descriptor estático anteriores; no se deducen destinos dinámicos desconocidos de una instalación temporal satisfactoria ni se anuncia soporte de otros runtimes/ABIs. Esto no demuestra que un romhack arbitrario o su estado de ejecución sean correctos ni sustituye probar el juego.

El generador de desarrollo `native_dependencies.py` reproduce los rangos desde las dos referencias privadas con SHA-256 exacto. Requiere Capstone 5 y solo emite offsets, etiquetas y hashes; no incorpora instrucciones ni archivos comerciales. No es necesario para usar la aplicación ni compilar el manifiesto generado.

Una incompatibilidad identifica perfil, dependencia, intervalo de code.bin y dirección virtual. Ejemplo: getter US, `code.bin 3AE0E0–3AE0F3`, dirección `4AE0E0`. Ya no se presenta una región de casi cinco megabytes como única causa.

MT, shiny, efectividad de tipos, obediencia, mapas de iconos y demás datos ajenos no son requisitos de instalación funcional. Los iconos opcionales mantienen sus comprobaciones propias de mapa completo y archivo gráfico; no se adopta un mapa parcial como instalación de iconos válida.

## Regresión real del PR #57

`9b9349b` aún comparaba segmentos completos después de preparar la copia temporal. Se reprodujo su rechazo usando una copia completa del Ultra Sol randomizado indicado por el propietario, con Bag original y sin mentas. Hay 621 bytes distintos frente a la referencia US, incluidos cambios en offsets `225AD4`, `2843F4`, `29A044`, `341654`, `3417D8`, tablas y espacio/código añadido. Esas diferencias no intersectan el manifiesto nativo y se conservan.

Con la corrección, el mismo proyecto real supera preparación, instalación, reapertura e historial; Deshacer restaura exactamente los archivos de juego y el ejecutable randomizado. Se contrastan archivos ajenos y fuente original antes/después. El runner privado `--usum-mints-project-regression` usa una salida nueva separada y no escribe en el origen. También contempla reconocimiento de un proyecto real ya instalado, como el Ultra Luna indicado por el propietario.

Las pruebas focalizadas de compatibilidad rechazan cambios en las seis entradas, checksum/cifrado, fórmulas, tablas de bloques/naturaleza y descriptor/interfaz. Aceptan mutaciones ajenas antes rechazadas por segmento. Se conservan memoria, hooks, rollback, concurrencia, relectura y deshacer. No se ejecutan suites generales locales para esta corrección.

El propietario confirmó que probó personalmente **`bd07d42` dentro del juego con su ROM de Ultra Sol previamente randomizada y las mentas funcionaron correctamente**. Esta confirmación jugable corresponde a Ultra Sol y a ese caso concreto. **Ultra Luna no tiene una prueba jugable acreditada por esta confirmación y la ROM específica del tester continúa pendiente**. El reconocimiento de archivos de una copia ya instalada de Ultra Luna no equivale a ejecución en juego.

El fallo CI anterior de `c6b670e` era distinto: `Lifecycle()` sobrescribía el exheader válido del fixture con ceros. Su corrección se conserva; `ValidateMemoryLayout` no se relaja.

## Validación focalizada de la corrección

Compilación Release no incremental con apphost y binlog: 0 errores y 0 advertencias. Suites: instalación 98, distribución 128, interfaz 28, compatibilidad nativa 66 e iconos 88 comprobaciones aprobadas. El probe completo de Ultra Sol randomizado supera 12 comprobaciones; el de Ultra Luna ya instalado, tres. Se revisan además registros de objetos ajenos, nibbles y miembros de bolsillos, bancos y filas de texto fuera de las mentas. La fuente US conserva code.bin SHA-256 `b5388f7500d91be01499a99ca007c98212068608ed7c83c43952e1d5148e9e09`.

## Prueba privada

1. Abre una copia respaldada del proyecto con el ejecutable privado nuevo y entra en **Mecánicas y CRO → Mentas Gen7**.
2. Pulsa **Comprobar estado y preparar**. Si aparece **Listo para instalar**, pulsa **Aplicar y guardar**.
3. Genera la salida desde esa copia y prueba las mentas con una partida respaldada. Ante **Instalación no compatible**, comparte solo el diagnóstico textual de la dependencia concreta.

No es necesario ejecutar PowerShell, comparar SHA-256 ni enviar ROMs, partidas o binarios. Esta consolidación no modifica el PR #55 ni las funciones del PR #56 y no crea tags, previews o releases.

## Consolidación para integrar en main — 09/11/2026

Base del PR #57: `bd07d42377bd709eb71b167e5741badf9bdd0f1d`. Preview publicada: `6389158048f154bd535afb12b5c79816a009f1aa`, con los commits adicionales `aec6ddf` y `6389158`. Se comparó su diferencia final y se incorporó selectivamente, sin cherry-pick de los commits completos:

| Archivos de la preview | Tratamiento en #57 |
|---|---|
| `UsumMintPoolControl.cs`, `UsumNatureMintView.cs` | Se incorporan sin diferencias respecto a `6389158`: omitir el informe redundante únicamente en el instalador, también en accesibilidad, y resumir la confirmación de guardado. Otros editores conservan su informe. |
| `UsumNatureMintChecks.cs` | Se incorpora sin diferencias respecto a `6389158`: tres comprobaciones de interfaz que acreditan los dos comportamientos anteriores y mantienen la lista extensa dentro de Diagnóstico técnico, cerrado inicialmente. |
| `MENTAS_GEN7_USUM_COMPATIBILIDAD.md` | Se incorpora la confirmación del propietario precisando Ultra Sol; se actualizan integración y límites. |
| `3DS.Progressive.App.csproj`, `MainWindow.xaml`, `Publish-Preview.ps1` | Se excluyen versión, pie y valor predeterminado propios de preview.2; permanecen como en main. |
| `RELEASE_0.15.0_PREVIEW_2.md` y su entrada en `INDICE.md` | No se importan las notas ni la entrada de publicación. La release histórica conserva su fuente/tag; este PR no la vuelve a empaquetar ni publicar. |

Se integró `main` en `cbf6f9502185fb4be69e5f54aa1cd41def20d1c1`, que ya contiene el PR #56. No hubo conflictos textuales. Git combinó automáticamente `Program.cs`, `RunnerCatalog.cs`, `RUNNER_PRUEBAS.md` e `INDICE.md`; se revisó que conserven tanto las suites/probes de entrenadores DS como las de mentas. El código y las pruebas específicos de entrenadores DS permanecen idénticos a ese main. No se modifica la rama de eventos #55 ni se incluye su implementación.

Los servicios de ROM, dependencias nativas, preparaciones temporales, transacciones y reglas de compatibilidad de mentas permanecen idénticos a `bd07d42`; los cambios posteriores de funcionamiento son únicamente los de presentación ya publicados en `6389158`. No se reabre la investigación de compatibilidad ni se acredita una prueba jugable nueva del commit de integración.

Validación local de la integración (runner propio, Release):

| Comprobación | Resultado |
|---|---|
| Compilación no incremental de Tests/App/Core/LegacyBatch, apphost y binlog | 0 errores, 0 advertencias |
| `--usum-mints-only` | 98 aprobadas |
| `--usum-mints-ui-only` | 31 aprobadas |
| `--usum-mints-distribution-only` | 128 aprobadas |
| `--usum-mint-icons-only` | 88 aprobadas |
| `--nds-trainers-only` | 171 aprobadas |
| `--nds-ui-only` | 27 aprobadas, fuera de pantalla |

Las seis suites finalizaron con código 0. Las tres comprobaciones importadas se mantienen en la suite de interfaz: `MintUiInstallerDoesNotShowDistributionUnavailableMessage`, `MintUiOtherEditorsKeepDistributionStatus` y `MintUiReadyKeepsFileListOnlyInTechnicalDiagnostic`. Acreditan la ausencia del aviso redundante en el instalador, la conservación del informe en los demás editores y la ubicación exclusiva de la lista de archivos en el diagnóstico.

No se repitieron suites generales, auditorías visuales extensas, probes de ROM reales ni pruebas dentro del juego para esta integración: se conservan sus evidencias previas con su alcance original. La regresión DS focalizada comprueba serialización, recetas y presentación sobre la base #56 sin modificarla. Los 13 archivos de trabajo previo del árbol principal se contrastaron por SHA-256 y permanecieron intactos; no forman parte de este commit. El CI final de #57 se registra en la descripción del PR tras el push, sin atribuir al commit de integración el CI histórico de `bd07d42` ni la prueba jugable de ese commit.
