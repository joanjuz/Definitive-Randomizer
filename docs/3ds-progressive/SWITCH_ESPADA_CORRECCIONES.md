# Espada: corrección de objetos visibles y encuentros sorpresa

22 de octubre de 2026 UTC · rama `feature/switch-swsh-prototype`. Sin commits ni push.

## Decisiones del usuario

Consolidar Espada primero. Escudo se analizará cuando el usuario proporcione su extracción. Entrenadores randomizados: confirmados por el usuario, sin detalles que permitan afirmar cobertura exhaustiva. El comportamiento solicitado para encuentros visibles es ver una especie fuera y combatir contra otra aleatoria al entrar; no debe presentarse como implementado por randomizar la tabla de spawns.

## Corrección implementada

Auditoría en lectura del mod anterior: **169 Poké Balls rojas**, solo **40 cambiadas**. La mayor causa era el filtro demasiado limitado a Poké Balls, medicinas y bayas: dejaba intactos Caramelos Raros, objetos equipables, piedras y tesoros. El lector también recorría ocho entradas del vector Flags como si todas fueran variantes de objeto; la referencia de pkNX modifica solo Flags[0]. Las otras siete posiciones sumaban 1183 falsos candidatos desconocidos.

Ahora:

- Pool explícito de categorías SWSH verificadas: objetos comunes/equipables, combate, Poké Balls, medicinas, bayas, ingredientes y tesoros; incluye Caramelo Raro y chapas. Se cruza con el mapa de hashes del juego.
- Objetos clave, MT/DT y cristales Dynamax excluidos. Las Poké Balls amarillas de MT permanecen intactas.
- Cada reemplazo elegible difiere del objeto de entrada. Se exigen dos candidatos seguros como mínimo.
- En visibles se modifica solamente el primer hash; se conservan cantidades y el resto de flags. En ocultos se mantiene cada variante con su peso, cantidad y respawn.
- Algoritmo registrado como `swsh-gameplay-fnv-splitmix-v2` por el cambio del pool y la elección sin repetición del original. Las salidas de entrenadores y encuentros permanecen idénticas con la semilla de diagnóstico.
- La UI explica el alcance nuevo y aclara que los encuentros visibles todavía comparten especie con la batalla.

## Resultados reales

Extracción de Espada 1.3.2 suministrada por el usuario, sin modificarla. Semilla 20261021, cinco módulos marcados y BST similar:

- **169/169 Poké Balls rojas cambiadas** en la salida final, frente a 40/169 antes. No es una suposición sobre azar.
- **4547 cambios de objetos** entre visibles y ocultos. Las 78 entradas conservadas corresponden a modelos de MT amarillas.
- 14229 ranuras elegibles, 14662 cambios de campos en total (1396 entrenadores, 8719 encuentros, 4547 objetos).
- Los **436 recursos de entrenadores** exportados y `data_table.gfpak` son idénticos byte a byte a los del mod anterior validado. No se cambió su algoritmo.
- Verificación independiente con `lz4.block` de los **254 miembros de placement.gfpak**: decodifican correctamente, conservan tamaños internos, flags opacos y MT amarillas. La auditoría coteja los primeros hashes de las 169 Poké Balls.
- El runner real comprueba cambios limitados a campos conocidos, repetición determinista, snapshots del origen y exportación reabierta.

Mod nuevo, copia verificada SHA-256 de 440 archivos:
`C:/Users/Joanj/AppData/Roaming/Ryujinx/mods/contents/0100abf008968000/DR-Prueba-Items-Corregidos-20261022`

No se sobrescribieron mods anteriores ni se alteró su configuración. Para probarlo, cerrar el juego y dejar únicamente este mod activo, desactivando el diagnóstico anterior y Divine Sword. Probar objetos aún no recogidos: el mod no restablece las marcas de recogida del guardado. Este mod **no** introduce todavía encuentros sorpresa.

## Compilaciones y pruebas

Compilación final: `dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release --no-incremental --nologo -v:q -bl:<temporal>/build-{}.binlog`: **0 errores, 82 advertencias**, ninguna en los archivos cambiados de Switch. Binlogs y salidas en el temporal `DR-SWSH-followup-20261022`.

Runner propio:

- `--swsh-only`: **189 aprobadas**.
- `--swsh-ui-only`: **26 aprobadas**, componentes fuera de pantalla.
- `--synthetic-only`: **3951 aprobadas** en ejecución final secuencial.
- `--swsh-gameplay-probe`: mod final generado/reabierto y extracción original conservada.
- `git diff --check`: aprobado.

Una ejecución intermedia de la suite completa falló en «SM Batch original executable is included in Debug output», coincidiendo con una recompilación que reponía los archivos del backend. Se repitió después de terminar la compilación y pasó completa; no se modificó el backend para ocultar el fallo. La asociación con la recompilación es una explicación del entorno de ejecución, no una corrección de lógica demostrada. La suite final se ejecutó sin compilaciones concurrentes.

| Requirement | Evidence |
|---|---|
| Cubrir objetos antes omitidos | `Swsh_Game_ItemsIncludeRareCandyHeldItemsAndStones` |
| Evitar conservar el original | `Swsh_Game_ItemsAlwaysDifferFromOriginal` |
| Conservar flags del evento | `Swsh_Game_VisibleItemOnlyPatchesFirstHashPreservingEventFlags` |
| Conservar cantidades y probabilidades | `Swsh_Game_ItemsPreserveQuantitiesWeightsRespawnAndEvents` |
| No alterar entrenadores validados | comparación byte a byte de 436 recursos entre mods |

Archivos modificados en esta corrección: `pk3DS.Core/NX/SwordShieldGameplay.cs`, `3DS.Progressive.App/SwordShieldGameplayWindow.cs`, `3DS.Progressive.Tests/SwordShieldGameplayChecks.cs`, `docs/3ds-progressive/HOJA_RUTA_SWITCH.md`, `SWITCH_SWSH_PRIORIDADES.md` y este informe. Se preservó el resto del trabajo pendiente de la rama.

## Investigación del combate sorpresa: pendiente de implementación

La tabla `encount_symbol_k.bin` determina el Pokémon que se genera fuera; cambiarla no desacopla su identidad de combate. La referencia primaria [PokeFeurRandomSWSH](https://github.com/JonatanUHC/PokeFeurRandomSWSH) describe modos independientes y un módulo ExLaunch para separar ambos comportamientos. Su generador distingue `visible_mode`, `battle_mode` y pools/semilla en el bloque `[randomizer]`. No se importó ni se ejecutó su generador ni su módulo precompilado.

Se cotejaron en lectura las 12 firmas de hooks del apartado randomizer de su [perfil Espada 1.3.2](https://github.com/JonatanUHC/PokeFeurRandomSWSH/blob/main/contract/hook-baseline-epee-1.3.2.json) contra el segmento .text de `exefs/main` de la extracción del usuario: **12 coincidencias**. Incluyen lectura de especie salvaje, setters/getters de especie/forma generada, generación de spawns y copia al objeto de batalla. La coincidencia acredita las instrucciones de entrada, no que la lógica de un módulo sea correcta ni que baste con cambiar una instrucción.

La distribución pública examinada contiene los binarios de ExLaunch y el perfil de firmas, pero no los archivos `research/swsh-mods-exl/source/program/patches/randomizer.hpp` que cita ese perfil. Hace falta una implementación nativa revisable/reproducible o localizar la fuente exacta antes de integrar esa lógica con garantías. No se ha añadido un módulo binario no auditado al mod del usuario.

Criterios pendientes para la implementación propia:

1. Sortear una vez por encuentro, al iniciar combate; excluir la especie visible si existe alternativa. Mantener el sorteo durante todas las consultas del mismo combate.
2. Propagar especie y forma coherentes a estadísticas, habilidad, movimientos, nombre, Pokédex y captura; capturar debe conservar la especie combatida.
3. Separar combates salvajes de entrenadores; preservar estáticos/regalos y otras rutas salvo opción explícita.
4. Mantener niveles/probabilidades, validar pool base presente y las firmas completas antes de emitir el módulo; no reutilizar offsets en Escudo.
5. Exportación ExeFS nueva junto al RomFS y metadatos, sin reemplazar la extracción ni cambiar NPDM a ciegas.
6. Probar en Espada: entrar varias veces al mismo modelo, captura, huida, derrota, encuentros ocultos, entrenadores y eventos. Solo entonces declarar el modo funcional.

Pendiente manual de esta entrega: recoger varias Poké Balls rojas y objetos ocultos con el mod final; teclado/DPI reales. El agente no operó una partida. Escudo queda expresamente fuera de la validación actual.
