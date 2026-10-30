# Espada/Escudo — randomización de tipos y habilidades

21 de octubre de 2026 · rama feature/switch-swsh-prototype · soporte experimental.

## Uso implementado

Desde Inicio, abrir Espada/Escudo · prototipo Switch, cargar la extracción preparada y desplegar Randomizar Pokémon. Elegir tipos, habilidades o ambos, una semilla entera de 0 a 4294967295 y el alcance (todas las entradas presentes o solo la seleccionada). Previsualizar no modifica el borrador ni la extracción. Añadir al mod, fijado en el pie de la ventana junto a la exportación, confirma los valores; Descartar cambios restablece la sesión. Si no se marca ninguna opción, no se randomiza nada y los cambios anteriores siguen siendo exportables.

Opciones: incluir formas alternas, conservar si cada entrada tiene uno o dos tipos, habilidades normales distintas y permitir Superguarda. La habilidad oculta se elige también, pero puede coincidir con una normal. Sin conservar el número de tipos, la selección entre monotipo y doble tipo tiene probabilidad 50/50. Las formas incluidas se randomizan independientemente. Todavía no hay herencia por evolución.

El editor manual incluye ahora los tres slots de habilidad como IDs Gen8. Sus campos son valores ushort, no bytes; se validan los límites 1–267 y se conservan los demás bytes de la entrada. La randomización utiliza solamente IDs observados en entradas presentes y resueltas de la tabla original, no una lista de habilidades de otra generación ni el pool modificado por una previsualización anterior.

Se preservan las especies que en la tabla original tienen habilidades ligadas a cambios de forma. La protección se extiende a todas sus formas, incluyendo tipos y habilidades. Los IDs protegidos también quedan fuera del pool. Es una política conservadora, no una certificación de que todos los casos especiales de combate estén cubiertos. La edición manual sigue siendo una operación explícita del usuario.

Protección actual por IDs: 59, 121, 122, 161, 176, 197, 208, 209, 210, 211, 225, 241, 248, 258, 266 y 267. Superguarda (25) se excluye salvo habilitación explícita. Si no hay suficientes candidatos para cumplir habilidades normales distintas, la previsualización se rechaza antes de aplicar nada.

## Reproducción e integridad

El algoritmo se identifica como swsh-personal-splitmix64-v1. Se usa un generador propio definido por aritmética de 64 bits y reducción sin sesgo, con streams separados por entrada y por tipo de operación. Activar habilidades no altera el sorteo de tipos con la misma semilla, alcance y entrada. No se modifica System.Random global ni los motores de 3DS.

Para reproducir un resultado se necesitan la misma tabla/borrador de entrada, tabla base del pool, semilla, opciones y versión del algoritmo. El manifiesto registra opciones, cambios por campo, operaciones, semilla, algoritmo, SHA-256 del borrador de entrada y resultado, hashes del proyecto y especies protegidas. La comparación neta cuenta cambios de valor; un ushort puede implicar dos bytes distintos. No se promete importar todavía el manifiesto como Global Template.

Cada propuesta pertenece a una sesión y hash concreto del borrador. Cambiar el borrador, origen, semilla, alcance u opciones invalida la propuesta antes de aplicarla. La aplicación realiza una confirmación única del conjunto de cambios. Un resultado sin diferencias netas no genera un mod nuevo. Los originales, entradas ausentes, formas no resueltas, campos no seleccionados y datos opacos permanecen sin cambios.

Exportación:

- Atmosphère · SD: atmosphere/contents/<TitleID>/romfs y manifiesto externo.
- Ryujinx · carpeta de mod: romfs directamente dentro de la carpeta nueva del mod, junto al manifiesto y LEEME. Copiar esa carpeta a la carpeta de mods del título y activarla en el emulador.

No sobrescribe destinos existentes ni escribe en la extracción. Las salidas nuevas se validan y publican desde staging.

## Archivos modificados en este hito

- pk3DS.Core/NX/SwordShieldPersonalTable.cs: doce campos, offsets y validación de habilidades ushort.
- pk3DS.Core/NX/SwordShieldPersonalRandomizer.cs: opciones, pool, protección y algoritmo puro con semilla.
- 3DS.Progressive.App/Services/Switch/SwordShieldProject.cs: propuesta en bloque, historial, reproducción y dos perfiles de exportación.
- 3DS.Progressive.App/SwordShieldPrototypeWindow.cs y .Randomization.cs: opciones, semilla, alcance, propuesta, aplicación y selección del formato de mod.
- 3DS.Progressive.Tests/SwordShieldChecks.cs: fixtures de habilidades válidas y extensión del enum de campos.
- 3DS.Progressive.Tests/SwordShieldRandomizationChecks.cs y Program.cs: pruebas de núcleo, servicio, interfaz y exportación con datos reales. Las suites swsh-only/swsh-ui-only ya están incluidas en el workflow pendiente de la rama.

## Validación de este hito

Compilación Release final con --no-incremental: 0 errores, 78 advertencias en archivos anteriores; no quedan advertencias en los archivos nuevos de Switch.

- --swsh-only: 143 aprobadas. Incluye Swsh_Rand_V1GoldenSeed123, Swsh_Rand_SameSeedReproducesWithoutSourceWrites, Swsh_Rand_OnlyEnabledFieldBytesChange, Swsh_Rand_ProtectsMechanicSpeciesAndAllForms, Swsh_Rand_ProjectJournalStoresSeedOptionsAndHashes, Swsh_Rand_ExportCarriesReproductionMetadata y Swsh_Rand_RyujinxLayoutContainsDirectRomfs.
- --swsh-ui-only: 20 aprobadas fuera de pantalla. Incluye Swsh_Rand_UI_BulkPreviewWaitsForApply, Swsh_Rand_UI_ApplyRecordsSeedAndEnablesExport, Swsh_Rand_UI_NoOptionsDoesNotBlockExistingModExport y Swsh_Rand_UI_ExportLayoutSelectorIsAccessible. No es prueba interactiva ni de DPI real.
- Regresiones específicas: interfaz visual95, guía233, compras únicas6 e iconos17 aprobadas.
- --synthetic-only: 3905 regresiones aprobadas, incluyendo las 143 del backend Switch; el resto ejercita los módulos y familias existentes.

Con la extracción de Espada facilitada por el usuario y la semilla 20261021 se generó un mod Ryujinx aislado: 740 entradas elegibles, 11 especies protegidas, 230 habilidades candidatas, 3620 valores modificados y 3730 bytes distintos. Se verificaron repetición determinista, offsets permitidos, slots normales distintos, pool, número de tipos y origen idéntico. SHA-256 del recurso final: F6F862DC6D748BDEA85BED4B9148B5B67D7F72BD561012C50C0E1B2471A60EED.

Salida de integración: C:/Users/Joanj/AppData/Local/Temp/DR-SWSH-randomization-20261021/Espada-20261021-Ryujinx. Se preparó primero en esa carpeta de integración y después se copió una carpeta nueva a la ubicación de Ryujinx solicitada previamente por el usuario: C:/Users/Joanj/AppData/Roaming/Ryujinx/mods/contents/0100abf008968000/DR-Prueba-Tipos-Habilidades-20261021. Se verificó de nuevo el SHA-256 instalado. Los mods anteriores no se sustituyeron ni se cambiaron sus estados de activación.

## Pendiente de prueba dentro del juego

La carga del mod que cambiaba el tipo de los iniciales ya fue confirmada por el usuario. Eso no certifica esta randomización nueva, las habilidades ni estabilidad durante combates/eventos. Escudo sigue sin prueba con extracción real.

Para validar habilidades, usar capturas nuevas y comprobar ficha y efecto en combate. Los Pokémon existentes guardan su propio ID de habilidad en la partida, así que modificar personal_total.bin no prueba que ese Pokémon de la partida vaya a actualizarse. No se modifican los guardados. La referencia del formato es G8PKM.Ability de PKHeX.

Para probar el nuevo mod, detener el juego, dejar activo únicamente un mod que reemplace personal_total.bin y reiniciar. Desactivar tanto Divine Sword como el diagnóstico de iniciales si se activa este randomizado. No se han modificado sus estados de activación. Comprobar equipo, movimientos, combate, cambio de forma y varios eventos. La reversión se hace desactivando el mod y reiniciando.

## Fuentes técnicas

- [pkNX PersonalInfo8SWSH](https://github.com/kwsch/pkNX/blob/master/pkNX.Structures/Personal/Info/PersonalInfo8SWSH.cs): estructura y offsets de datos personales.
- [pkNX PersonalRandomizer](https://github.com/kwsch/pkNX/blob/master/pkNX.Randomization/Randomizers/Personal/PersonalRandomizer.cs): referencia del alcance de herramientas existentes; no se importó su estado global ni su implementación.
- [PKHeX Ability](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Game/Enums/Ability.cs): correspondencia de IDs.
- [PKHeX G8PKM](https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/PKM/Shared/G8PKM.cs): almacenamiento de habilidad en el Pokémon de la partida.

Skills aplicadas: pk3ds-progressive-safety, wpf, dotnet-test/code-testing, dotnet-test/run-tests y dotnet-msbuild/binlog-generation. No existe un agente especializado test-engineer invocable en esta sesión; las pruebas se desarrollaron con el runner propio y registros de trabajo fuera del repositorio.

## Cierre de validación de esta iteración

Después de completar el núcleo y el servicio se ejecutó la suite sintética completa con 3905 comprobaciones aprobadas. El ajuste final de UI fijó «Añadir al mod» en el pie y desplazó el panel a la vista previa después de prepararla; se recompiló Release sin incremento y se repitieron las 20 comprobaciones de la ventana. Las 95 regresiones visuales pasaron también con la acción fijada. El último cambio fue esperar la operación del Dispatcher para no introducir advertencias CS4014; no modificó algoritmos ni formatos.

No se ejecutó interacción real de escritorio, escalado real de DPI, prueba jugable de esta randomización ni datos reales de Escudo. No se modificaron los motores 3DS, las extracciones, guardados o mods anteriores. Se añadió únicamente la carpeta nueva del mod de prueba autorizada por la instrucción previa de ubicación. No se hicieron commits ni push de este hito; el último commit de iconos sigue separado.

## Confirmación jugable del usuario — 21 de octubre de 2026

El usuario confirmó «Perfecto si funcionó» después de probar DR-Prueba-Tipos-Habilidades-20261021, la randomización de tipos y habilidades con semilla 20261021 en Espada mediante Ryujinx. Se registra como prueba jugable aprobada por el usuario para ese mod y entorno. Esta confirmación actualiza el estado pendiente de prueba jugable de los apartados anteriores; no procede de una sesión interactiva operada por el agente.

No se especificaron las especies, habilidades, efectos de combate ni duración exacta del recorrido probado. Por tanto, no acredita una cobertura exhaustiva de cada habilidad, forma o evento. Escudo, DPI/teclado real y los módulos futuros conservan sus verificaciones pendientes. No se modificaron código, ROMs, guardados o mods al registrar esta confirmación.

## Ampliación posterior: prioridades y estadísticas

Se añadió «Mezclar estadísticas (conservar BST)»: permuta los seis valores de cada entrada elegible mediante un flujo de semilla independiente (dominio `0x53544154`). Si PS es 1 se conserva ese valor. Las protecciones de especies con mecánicas de forma también se mantienen. No genera nuevos BST ni herencia por evolución. El algoritmo anterior para tipos/habilidades conserva sus resultados cuando la opción nueva está desmarcada. La lista muestra nombres del banco de español (inglés si no existe) y BST.

Entrenadores, encuentros y Field Items tienen su propio panel, comparten el borrador y la exportación con datos personales. Ver [SWITCH_SWSH_PRIORIDADES.md](SWITCH_SWSH_PRIORIDADES.md). Una operación es reproducible con la misma semilla, opciones **y borrador de entrada**; aplicar otra operación sobre un borrador ya cambiado puede producir otro resultado. El manifiesto registra hashes de entrada/salida y el historial, pero todavía no existe replay de Global Template para Switch.
