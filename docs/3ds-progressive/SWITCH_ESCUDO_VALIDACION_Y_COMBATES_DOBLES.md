# Espada/Escudo 1.3.2: integración y validación — 2026-10-23

## Corrección jugable posterior: primer combate de Paul

El usuario confirmó un cierre al iniciar el primer combate contra Paul. Se verificó que el diagnóstico había convertido las variantes #4/#5/#6 (equipos rivales de dos Pokémon) de modo individual 0 a doble 1, aunque en ese punto el jugador solo dispone del inicial. Esta condición no estaba cubierta por la validación anterior: comprobar el tamaño del equipo rival no comprueba el del jugador.

Con Ryujinx cerrado se respaldó íntegramente `diag` y se restauró exclusivamente el byte 2 de las 246 cabeceras de entrenadores convertidas por el diagnóstico, manteniendo runtime, equipos, niveles, configuración SD y resto de recursos. Se verificó por hashes y por diferencias byte a byte que esos otros datos no cambiaron. El usuario confirmó: **«Sí, entra al combate»**. Esta comparación confirma que la conversión de modalidad causaba el cierre de ese combate; no certifica todos los combates ni todas las herramientas.

- Protección de producción: #4–#9, las variantes iniciales obligatorias de Paul, se conservan individuales aunque se solicite 100 % de dobles, en Espada y Escudo.
- Cobertura: `Swsh_DoublePreservesMandatoryEarlyHopBattle_True_4` hasta `_True_9` y equivalentes `_False_4`–`_False_9`.
- Validación de esta corrección: build Release de App/tests sin errores (88 emisiones previas de advertencias), **229 comprobaciones de paridad aprobadas**, incluidas las 12 variantes introductorias de ambos juegos; `git diff --check` correcto.
- Los dobles fuera de estas variantes continúan pendientes de prueba jugable con al menos dos Pokémon utilizables del jugador. El editor estático no conoce la composición de la partida: aún no existe un guard runtime general para reducir a individual cuando el jugador solo tenga uno. No afirmar que esta protección resuelve todos los escenarios de dobles.
- El mod activo `diag` de esta comparación tiene las modalidades originales restauradas. Copia equivalente: `D:/Progra/Pokemon/Randomizer/ValidacionEscudo20261023/Escudo-sin-dobles-Paul-validado`.
- Respaldo: `D:/Progra/Pokemon/Randomizer/ValidacionEscudo20261023/Respaldo-diag-antes-sin-dobles-100643`. No se modificaron ROM ni guardado.
- **No reutilizar los anteriores `Final-Escudo` ni sus Batch como versión corregida**: conservan el diagnóstico con conversiones de dobles que permitió reproducir el fallo. La lista de resultados siguiente documenta pruebas automáticas previas, no una certificación jugable de esos mods.

## Resultado y alcance

Escudo se integra con el mismo conjunto actual de editores y servicios de Espada. Se comprobó la extracción `D:/Nintendo/Switch/Randomizado/Escudo` sin modificarla. Identidad: `01008DB008C2C000`, Build ID `A16802625E7826BF83B6F9708E475B912A9AB7DF000000000000000000000000`. La regresión real de Espada utilizó `D:/Nintendo/Switch/RomEspadaEX/0100ABF008968000`.

**La validación automática queda completada para los casos descritos. La compatibilidad jugable de Escudo NO se declara cerrada:** no se ejecutó el juego ni se controló el escritorio. No se validaron una partida completa, todos los eventos, todas las combinaciones de opciones, DLC/licencias, hardware real o Atmosphère. El runtime integrado mantiene su restricción a exportación para Ryujinx. Se conserva la retirada de animaciones de Pokédex.

## Implementación

- Runtime autónomo de Escudo integrado en recursos de la aplicación. Se utiliza su módulo y NPDM específicos, no los de Espada. No requiere importar paquetes ni conectarse a Internet durante el uso de la herramienta.
- Recursos públicos de la misma revisión fijada `e69afd5b9d4e5f892237bb7fb30aed10101d9c0a` de [PokeFeurRandomSWSH](https://github.com/JonatanUHC/PokeFeurRandomSWSH/tree/e69afd5b9d4e5f892237bb7fb30aed10101d9c0a/assets/exlaunch/01008DB008C2C000). Licencia y créditos GPL-3.0 conservados. SHA-256 del módulo: `C3969FC0E35339295AE46E88BE915372019A60C12B674B61A21D67AFC08B8B14`. NPDM: `29FAFC4E38C3B1E352F8D4ACBD19F80E8BD944F3FE32380DEF5C0E273A55EB57`.
- Perfil de integridad de Escudo: Build ID reconocido y SHA-256 de `.text` `88F4650C736FB61E1D0C624BFDFF1718C87A286F2686C7C3CBB9378301F8EB28`, obtenido de la extracción proporcionada. Al leer el segmento se comprueba además su hash NSO. Esto permite rechazar código diferente, pero no demuestra por sí mismo la seguridad semántica de todos los hooks.
- La selección de paquete es consciente del título en interfaz, servicio, Global y Batch. Se rechaza un paquete de otro juego. La procedencia exportada registra los hashes del binario realmente utilizado y diferencia validación automática de prueba jugable.
- Caps y preset DivineSword adaptados a Escudo: Alistair #78 y Mel #136 en lugar de Judith #77 y Morris #135. Los restantes 38 registros y niveles se conservan. El lector de jefes y el preset coinciden en ambos juegos. Resultado esperado: **27, 34, 43, 50, 58, 68, 75, 78, 92**. Se conservan las tres variantes de Lionel.
- Accesos al runtime y caps habilitados para Escudo en navegación, inspector, ventana secundaria de randomización y prototipo existente. Cabecera y mensaje de validación nombran el juego actual.
- **Probabilidad de combate doble (%)**, rango 0–100, inicialmente 0, integrada en Entrenadores → Randomizar → IA, objetos y combates y en la ventana secundaria de randomización. Solo convierte individuales con al menos dos Pokémon; conserva los dobles, múltiples y modos desconocidos, así como los equipos de un Pokémon. No inventa miembros ni cambia los bytes restantes de la cabecera.
- La elección usa semilla y entrenador para ser reproducible, respeta el alcance, aparece en vista previa e historial, y se conserva en templates de opciones, Global y Batch. Los templates anteriores que omitan la clave reciben 0. Un 0 conserva el estado actual; no deshace conversiones ya aplicadas. Se mantiene el algoritmo anterior para las demás opciones.

## Matriz comprobada con Escudo real

| Área | Evidencia ejecutada | Límite |
|---|---|---|
| Identidad y Pokémon | Lectura de 785 entradas; randomización personal de 740 elegibles, 11 especies protegidas, 3.614 cambios; repetición, campos permitidos, habilidades y archivo original verificados | Sin inspección jugable de modelos/estadísticas |
| Diez editores de datos | Lectura, previsualización repetible, aplicación en memoria, historial, rechazo de vistas obsoletas, exportación y hashes | No prueba funcional in-game de cada valor |
| Entrenadores | BST progresivo, reglas de movimientos, MT/TR compatibles, Smart Items y categorías; prueba con todos los entrenadores; probabilidad doble y alcance | Combates reales pendientes |
| Evoluciones | 26 rutas de intercambio convertidas a nivel 37, destino y forma conservados; reproducción Global | Evoluciones in-game pendientes |
| Movimientos, Level Up, MT/TR/tutores | Tablas reales, randomización y exportación; compatibilidad y reglas de mejores movimientos | Animaciones y aprendizaje in-game pendientes |
| Estáticos, regalos, iniciales | BST e iniciales de tres etapas; 618 y 40 cambios de previsualización, respectivamente | Eventos y DLC pendientes |
| Tiendas y economía | Tablas y precios leídos/randomizados/exportados; alcance de tiendas especiales cubierto por regresiones | Stock, dinero, Watts y BP en juego pendientes |
| Encuentros y Field Items | 1.729 regiones y 22 grupos de Field Items; generación combinada: 14.237 slots elegibles, 14.744 cambios y 78 objetos protegidos/desconocidos omitidos | Modelos, ubicaciones y rendimiento in-game pendientes |
| Encuentros vacíos | Lectura de los miembros `encount_symbol_t.bin`/`encount_t.bin`; 9.512 campos omitidos materializados, reapertura y selección estables; 1.798 regiones después | No implica más apariciones simultáneas; opción sigue desactivada por defecto |
| Runtime, caps y EXP | Binario específico, configuración, scripts, historial, remoción, hash, invalidación de caps y UI fuera de pantalla | Hooks sin ejecución jugable local |
| Global y Batch | Reproducción de combates dobles, caps y runtime; dos mods aislados por cada juego | No se instalaron las salidas |
| Interfaz | 24 imágenes de vistas reales, tamaños 900/1280/1600 donde el runner los contempla y captura de errores de bindings | 96 dpi de render; no es prueba interactiva de teclado, alto contraste o DPI 125/150/200 % |

Las comprobaciones de SD del probe de expansión escribieron solamente en una **SD ficticia de prueba** dentro de las salidas aisladas. No cambiaron la SD real de Ryujinx ni su configuración compartida con Espada.

## Ejecuciones y resultados

Runner propio: `dotnet 3DS.Progressive.Tests/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll <opción>`; no se sustituyó por `dotnet test`.

| Ejecución | Resultado |
|---|---:|
| `dotnet build pk3DS.slnx -c Release --no-restore` con binlog | 0 errores; 56 emisiones de advertencias previas |
| Build Release de `3DS.Progressive.Tests.csproj`, incluyendo App, con binlog | 0 errores; 88 emisiones de advertencias previas |
| `--swsh-only` | 316 comprobaciones aprobadas |
| `--swsh-parity-only` | 217 aprobadas |
| `--swsh-ui-only` final | 112 aprobadas, fuera de pantalla |
| `--visual-redesign-only` | 95 aprobadas |
| `--all-ui-only` | 398 aprobadas |
| `--synthetic-only` | 4.059 aprobadas, XY/ORAS/SM/USUM sintéticos |
| `--swsh-expansion-probe` con Escudo | 1.457 aprobadas |
| `--pokefeur-probe` con Escudo y `--builtin` | 41 aprobadas |
| `--swsh-version-probe` final con Escudo | 9 aprobadas |
| `--swsh-version-probe` final con Espada | 9 aprobadas |
| `--swsh-parity-probe`, `--swsh-polish-probe`, `--swsh-table-probe`, `--swsh-gameplay-probe`, `--swsh-random-probe`, `--swsh-read-only` con Escudo | Finalizaron correctamente; detalles en logs |
| `--swsh-visual-preview` con Escudo | 24 capturas; sin errores de bindings |
| `git diff --check` | Correcto |

La comparación normalizada por archivo y código de advertencia da **27 advertencias distintas frente a 27 en la línea base, sin nuevas**. El primer build falló por una DLL bloqueada por la aplicación abierta; el usuario la cerró y se recompiló. Los primeros intentos del nuevo probe fallaron al no existir las carpetas padre/Batch de salida: errores de preparación del harness, corregidos y repetidos con éxito; los guards de producción no se relajaron. La revisión visual también detectó una cabecera que aún decía Espada y la revisión de templates detectó una nueva clave inicialmente obligatoria: ambos corregidos y cubiertos por pruebas.

### Regresiones nuevas destacadas

- `Shield_RuntimeUsesPinnedVersionSpecificResources`
- `Shield_RuntimeMetadataTargetsShieldAndModuleDiffersFromSword`
- `Shield_PresetMapsExclusiveGymsWithoutChangingSword`
- `Swsh_DoubleHundredChangesOnlyBattleMode_True` / `_False`
- `Swsh_DoubleSkipsSinglePokemonTeam_True` / `_False`
- `Swsh_DoubleRespectsTrainerScope_True` / `_False`
- `Swsh_DoubleChanceIsMixedDeterministicAndSeedSensitive`
- `Swsh_DoubleGlobalReplayAndHistory_True` / `_False`
- `Swsh_LegacyUiTemplateOmitsDoubleChanceSafely_True` / `_False`
- `Swsh_GameplayWindowExposesDoubleAndRuntimeForBothVersions_True` / `_False`
- `Swsh_RealRejectsOtherVersionRuntime`
- `Swsh_RealNineCapsMatchVersionSpecificPreset`
- `Swsh_RealDoubleCapsRuntimeBatchTwoMods`

## Archivos de esta implementación

- `pk3DS.Core/NX/SwordShieldBattlePolicy.cs`, `SwordShieldGameplay.cs`.
- `3DS.Progressive.App/3DS.Progressive.App.csproj`.
- `3DS.Progressive.App/Assets/Switch/PokeFeur/upstream.json`, `shield/exefs/subsdk9`, `shield/exefs/main.npdm`.
- `3DS.Progressive.App/Services/Switch/DivineSwordCaps.cs`, `PokeFeurPackage.cs`, `SwordShieldOptionsStore.cs`, `SwordShieldProject.Global.cs`, `SwordShieldProject.PokeFeur.cs`, `SwordShieldRecordEditor.Trainers.cs`.
- `3DS.Progressive.App/SwitchWorkspaceView.cs`, `SwordPokeFeurWindow.cs`, `SwordShieldGameplayWindow.cs`, `SwordShieldGameplayWindow.Progressive.cs`, `SwordShieldPrototypeWindow.cs`.
- `3DS.Progressive.Tests/Program.cs`, `SwitchShieldChecks.cs`, `PokeFeurRealChecks.cs`, `SwitchAutomationChecks.cs`, `SwitchTableChecks.cs`.
- Este informe. Se conservaron los cambios locales anteriores y no se hicieron commits ni push.

## Salidas y comprobación manual para cerrar compatibilidad

Logs/binlogs y estados internos de pruebas: `C:/Users/Joanj/AppData/Local/Temp/DR-Shield-validation-20261023`.
Salidas aisladas: `D:/Progra/Pokemon/Randomizer/ValidacionEscudo20261023`.
El mod **diagnóstico**, no una distribución final, `Final-Escudo` incluye runtime de Escudo, encuentros de combate aleatorios, caps DivineSword, EXP y 100 % de conversión a doble en equipos elegibles. No incluye toda la randomización de datos de las otras carpetas de prueba. `Final-Escudo-batch/compatibility_01` y `_02` comprueban Batch independiente. Las carpetas anteriores sin prefijo Final contienen pruebas parciales o de formatos; no deben confundirse con una configuración recomendada para jugar.

Pendiente con el juego actualizado a 1.3.2 y su configuración SD correspondiente, conservando una copia previa:

1. Arranque de Escudo, carga/guardado propio y varios eventos sin reiniciar; rendimiento dentro de casa y en el Área Silvestre.
2. Combate sorpresa visible frente a especie del combate, captura, luchar, selección de movimientos, equipo/cambio de Pokémon y huida, sin bloqueos.
3. Dobles en un entrenador con al menos dos Pokémon y dos Pokémon utilizables del jugador; entrenador de un Pokémon sigue individual; dobles/múltiples originales y combates guionizados/Dynamax no se alteran indebidamente.
4. EXP, caps antes/después de medallas y variantes del campeón; controles y herramientas opcionales por separado. Animaciones de Pokédex continúan retiradas.
5. Tiendas especiales y normales, precios en dinero/Watts/BP, objetos visibles/ocultos, movimientos y aprendizajes, evoluciones, regalos e iniciales, DLC cuando corresponda.
6. Flujo de Global → previsualizar → aplicar → historial → exportar y las dos salidas Batch en juego.
7. Teclado/foco y DPI 100/125/150/200 % y alto contraste de forma interactiva.

Hasta completar estas pruebas con resultados del usuario, el estado correcto es **integrado y validado automáticamente, pendiente de validación jugable de Escudo**.
