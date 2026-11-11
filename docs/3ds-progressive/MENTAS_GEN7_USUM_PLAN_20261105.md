# Mentas Gen7 para USUM: investigación y plan de implementación

Fecha: 5/11/2026, America/Guatemala. Estado: **plan técnico; no instalado ni implementado**. Alcance: Ultra Sol y Ultra Luna, sin port a Sol/Luna, Gen6, DS o Switch. Este documento resuelve el siguiente bloque técnico.

## Base integrada y cierre de DGD

El propietario confirmó que la integración DGD v1 funciona en su prueba manual. Se integraron los PR en este orden, comprobando inmediatamente antes de cada merge el SHA de cabecera, ausencia de conflictos y estado `clean` de GitHub:

| Orden | Repositorio / PR | Cabecera revisada | Checks antes de integrar | SHA final de rama principal local |
|---|---|---|---|---|
| 1 | [DefinitivePkm #2](https://github.com/joanjuz/DefinitivePkm/pull/2), `main` | `d83bd23b9fd7ce9a19ec268beaf00b2d8f2659f1` | Sin checks configurados; no había requisitos bloqueantes | `2e4a3cb7217a8c57012aa06a915fea4bfa127e44` |
| 2 | [Definitive-Randomizer #47](https://github.com/joanjuz/Definitive-Randomizer/pull/47), `main` | `604425b6185d7b78b2c38160c6e4b427119c591a` | `build-wpf`: success, [run aprobado](https://github.com/joanjuz/Definitive-Randomizer/actions/runs/42145715878) | `f802b4a555f423d27f55e700653ca50f35fe01e8` |
| 3 | [NuzlockeTracker #20](https://github.com/joanjuz/NuzlockeTracker/pull/20), `master` | `95167d09f73568e410164b5ed060662fbfe51b3d` | Ruff/mypy, Python 3.13/3.14, E2E y bundle: success; [pruebas](https://github.com/joanjuz/NuzlockeTracker/actions/runs/42146113779), [Windows](https://github.com/joanjuz/NuzlockeTracker/actions/runs/42146113615) | `69b4aa17753a4b23ffc3e1e604b421a281077f52` |

Las tres copias se actualizaron por fast-forward. No se repitieron suites: las cabeceras seguían siendo las ya aprobadas. No se creó ninguna release ni se ejecutó el despliegue web; Pages mantiene disparo manual. La validación manual de DGD no se extrapola a las mentas.

## Resultado de la investigación

La referencia es **adhfult/pk3DS-but-mostly-for-USUM**, commit fijado `274d836e88f840c9c326a569198ebff8031969d6` (24/10/2026). Se revisaron los dos paquetes de naturaleza, su instalador, modelo de paquete, tabla de bolsillos y licencia. No se compiló ni ejecutó el fork ni se probó su payload en una ROM.

Fuentes primarias fijadas:

- [README y créditos](https://github.com/adhfult/pk3DS-but-mostly-for-USUM/blob/274d836e88f840c9c326a569198ebff8031969d6/README.md).
- [AbilityPatchAndNatureMints.json](https://github.com/adhfult/pk3DS-but-mostly-for-USUM/blob/274d836e88f840c9c326a569198ebff8031969d6/patch-packages/AbilityPatchAndNatureMints.json).
- [NatureShifter.json](https://github.com/adhfult/pk3DS-but-mostly-for-USUM/blob/274d836e88f840c9c326a569198ebff8031969d6/patch-packages/NatureShifter.json).
- [PatchPackageInstaller.cs](https://github.com/adhfult/pk3DS-but-mostly-for-USUM/blob/274d836e88f840c9c326a569198ebff8031969d6/pk3DS.Core/Modding/Research/PatchPackageInstaller.cs), [PatchPackage.cs](https://github.com/adhfult/pk3DS-but-mostly-for-USUM/blob/274d836e88f840c9c326a569198ebff8031969d6/pk3DS.Core/Modding/Research/PatchPackage.cs) e [ItemPocketTable.cs](https://github.com/adhfult/pk3DS-but-mostly-for-USUM/blob/274d836e88f840c9c326a569198ebff8031969d6/pk3DS.Core/Modding/Research/ItemPocketTable.cs).
- [LICENSE.md del fork, GPL versión 3](https://github.com/adhfult/pk3DS-but-mostly-for-USUM/blob/274d836e88f840c9c326a569198ebff8031969d6/LICENSE.md).

### Qué hace realmente

`AbilityPatchAndNatureMints` define **21 mentas consumibles y un Ability Patch**. Cada menta selecciona un ID de naturaleza; el bloque escribe la naturaleza que lee `GetSeikaku` y refresca las estadísticas. **Cambia la naturaleza real**, no implementa una naturaleza estadística separada conservando la personalidad original. Esto se deduce del paquete y su rutina; no es una confirmación jugable local.

Los 21 destinos nativos, en el orden del paquete, son:

`1, 3, 4, 2, 5, 8, 9, 7, 15, 16, 19, 17, 20, 21, 23, 22, 10, 11, 13, 14, 12`.

Hay veinte combinaciones no neutrales y una neutral (ID 12). No se debe calcular el destino como «posición + 1». `NatureShifter` es otra función: objeto clave reutilizable que recorre las 25 naturalezas. No satisface por sí solo el requisito de mentas normales consumibles.

**Alcance recomendado para el primer port:** 21 mentas que cambian la naturaleza real, con esa semántica indicada en interfaz y descripción. Dejar Ability Patch y Nature Shifter fuera. Si el producto exige conservar la naturaleza original y cambiar solo el efecto estadístico, hay que elegirlo antes de implementar: requiere otro diseño de dato persistente y hooks; el código estudiado no lo resuelve. No reservar un byte de PK7 ni EventWork por suponerlo libre. Esta diferencia es una decisión de alcance, no una tarea de auditoría general.

### Licencia y procedencia

El fork publica GPLv3 en `LICENSE.md`; este repositorio también contiene GPLv3. La reutilización propuesta es como código GPLv3, conservando licencia, avisos de autoría y procedencia, y documentando qué se modifica. El README acredita a Kaphotics por pk3DS y a ABZB por investigación ARM/CRO; eso no permite atribuir automáticamente cada línea del paquete a una persona concreta.

Al portar, añadir una ficha en `docs/third-party/` con URL, commit, archivos adaptados y licencia, y conservar los avisos pertinentes. El bloque está publicado como hexadecimal en JSON; **no se encontró un archivo ensamblador correspondiente por nombre/extensión en el árbol revisado**. La entrega debe incluir una representación fuente legible/reproducible y su adaptación, sin depender únicamente de copiar el bloque opaco. No copiar sprites, CRO, code.bin, ROMs ni gráficos del fork. No se incorporó código de terceros durante esta investigación.

## Compatibilidad técnica concreta

| Hallazgo del fork | Consecuencia para Definitive Randomizer |
|---|---|
| Clona datos del objeto 645, pero usa IDs diferentes | Añadir registros/nombres no basta: el camino especial de Cápsula Habilidad reconoce el 645. No enganchar la ruta antigua de cambio de habilidad |
| Usa el flujo normal de bolsa y cuatro hooks en `Bag.cro` | Hace falta un parche específico de bolsa, no una receta de efectos de objetos en batalla |
| Bloque compartido `bag-item-effects`, 1552 bytes (`0x610`), `SourceBase=0x18010` | Ese origen no es un hueco libre de nuestra ROM. Asignar código con el expansor existente y reubicar llamadas/punteros; portar solo la funcionalidad necesaria |
| Tabla de despacho en el bloque, offset `0x48C`, elementos de 4 bytes, capacidad 96 y terminador | Usar un registro tipado/versionado con 21 IDs y destinos, límite y terminador comprobados. Mantener los destinos no secuenciales |
| Referencia a funciones de `.code`, con sondeo de un desplazamiento +8 en Ultra Luna | Crear perfiles de llamadas/firmas separados US y UM. No certificar versiones desconocidas usando solo ese sondeo |
| Bolsillo en GARC `a/1/0/3`, primer miembro, nibbles: ID par alto / impar bajo | El campo `Item.PocketField` del editor actual no sustituye esta tabla. Actualizar ambos cuando proceda, conservando el nibble vecino y otros miembros |
| El instalador advierte que la bolsa empaqueta el ID en 10 bits | No superar 1023. El MVP debe además permanecer dentro de los registros y límites actuales del editor, sin ampliar el formato de partida ni el techo de objetos |
| Nombres/descripciones y líneas de mensajes del banco 1 | Localizar usando los bancos reales; asignar mensajes sin sobrescribir diálogos existentes. Los índices 159–164 son referencias del fork, no posiciones automáticamente libres |

Los cuatro sitios de referencia son **offsets de archivo de su perfil**, todavía no comprobados contra nuestros binarios:

| Sitio `Bag.cro` | Bytes originales que espera el paquete | Función indicada |
|---|---|---|
| `0xC318` | `1ED1FFEB` | Comprobación al usar |
| `0xDDC8` | `72CAFFEB` | Disponibilidad / etiqueta de uso |
| `0xC358` | `3F0100EA` | Despacho sin manejador nativo |
| `0x80DC` | `DB0000EA` | Mensaje / retorno |

El paquete propone IDs `113,114,115,123,124,125,126,127,128,129,130,131,132,133,426,427,622,837,838,839,840`; **son candidatos, no una reserva aprobada**. El 867 del Ability Patch y el 877 de Nature Shifter no forman parte del port propuesto. Un nombre vacío o `AllowUnnamed` no demuestra ausencia de uso en tablas, scripts o mods previos.

### Infraestructura existente que sí se reutiliza

- `CroPatchSession.TryAllocateRelocatableCode`, `TryWriteCode` y `TryBuildImage`: asignación/expansión y construcción controlada. No asumir que resuelven automáticamente todas las llamadas externas de este payload.
- `CroIntegrity.Rehash` / `RebuildCrr` en `Services/GameCroIntegrityService.cs`: hashes finales y actualización del CRR conservando los demás CRO.
- `ProjectHistory`, `FileChange` y las transacciones existentes: una propuesta y un guardado conjunto, dependencias verificadas, recuperación y deshacer.
- `GameItemService`, `GameMartService`, `GameFieldItemsService`, `GamePickupService`, catálogos y editores actuales: acceso a objetos y fuentes de obtención, una vez actualizados sus catálogos.

`GameCroRecipeService` aplica recetas al `Battle.cro` actual. El JSON del fork contiene `OtherCros`, `ItemData`, `DataTables` y textos que ese contrato no interpreta. **No se importará directamente ni se ampliará el motor para admitir todos los paquetes del fork.** Se propone un preparador tipado de mentas en Core y un servicio de instalación USUM en App.

## Integración de objetos, distribución y randomización

| Sistema | Trabajo implementable | Límite / protección |
|---|---|---|
| Objetos | Definir 21 identidades estables `mint:<natureId>` y su asignación a IDs del perfil; clonar campos necesarios, configurar uso/consumo y bolsillo Medicina, nombres/descripciones localizados | No tocar IDs ocupados, objetos clave, MO/MT, cristales o funciones de historia. No reutilizar bits de otro mod |
| Tiendas normales y BP | Hacerlas seleccionables manualmente y añadir opción explícita de incluir mentas; precios configurables con las unidades correctas | `GameMartPresets.ItemPool` es estático: hay que incorporar la selección activa. Mantener tiendas por progreso y política de compras únicas; no convertir automáticamente todas las mentas en compra única |
| Field Items visibles/ocultos | Añadir las mentas instaladas al pool permitido y a la edición manual EI/EB; respetar exclusiones y alcance | `FieldItemTemplate` ya maneja listas; no sobrescribir recogibles de historia ni escribir miembros ajenos del GARC |
| Recogida | Extensión opt-in del pool de `GamePickupService` | El pool heredado `GetRandomItemList` es estático. La instalación sola no debe alterar sus semillas anteriores |
| Recompensas | Primera entrega: objetos recogibles, tiendas y objeto equipado de un Pokémon regalo mediante `GameStaticEncounterService`; describir cada vía con su nombre real | Un regalo Pokémon con objeto no equivale a recompensa directa de NPC. El campo `Prize` de entrenador solo está expuesto en Gen6: no inventar una recompensa Gen7 desde él |
| NPC / entrega de objeto directa | Segundo subbloque si se prioriza: un script de entrega USUM identificado, validado y reversible; no editor general de eventos | No sustituir una recompensa de historia ni afirmar soporte universal de scripts. No es condición para probar el primer objeto utilizable |
| Pools de entrenador / Pokémon salvaje | Conservar las mentas fuera de Smart Items y objetos de combate por defecto; permitir solo elecciones manuales o una opción específica coherente | No son objetos equipados competitivos con efecto pasivo. Un cambio de naturaleza no es una acción que deba elegir la IA rival |
| Global Template y Batch | Acción propuesta `usum.nature-mints.install`, con versión, perfil, IDs/destinos y hash del recurso; instalar antes de randomizar tiendas/campo/Recogida; usar staging por salida | Claves nuevas con valores por defecto que preserven recetas anteriores. No confundir nueva receta con JSON de paquetes del fork. Versiones antiguas deben rechazar acciones desconocidas |
| Tracker / DGD | La naturaleza y estadísticas del Pokémon siguen viniendo de RAM; comprobar que la naturaleza real modificada se refleja al releer | DGD v1 no exporta un catálogo de objetos ni semántica de mentas. No declarar integrados sus nombres/iconos en Tracker sin una extensión de contrato separada |

Propuesta de opción: `IncludeInstalledMints=false` por defecto en cada randomizador implicado. La instalación habilita edición manual y un catálogo de capacidades, pero no altera silenciosamente todos los sorteos antiguos. Guardar el mapa de IDs en la receta; no volver a sortearlo según el idioma, orden de controles o disponibilidad de otro proyecto.

## Plan por entregas, archivos y dependencias

**Primer PR funcional recomendado:** instalador de 21 mentas consumibles con naturaleza real, disponible en Objetos/tienda manual, para el primer perfil USUM verificado. Debe permitir obtener y usar una menta, guardar/cargar y deshacer la instalación. La preparación de perfiles forma parte de ese PR; no se entrega solamente un informe de offsets.

| Entrega | Trabajo y archivos previstos | Aceptación para continuar |
|---|---|---|
| M1 · núcleo e instalación | Nuevos `pk3DS.Core/Modding/Research/UsumNatureMintPatch.cs`, fuente ARM reproducible y `UsumItemPocketTable.cs`; nuevos `Services/UsumNatureMintService.cs` y catálogo/registro tipado. Reutilizar expansor, integridad y transacciones. Referencias en `GameItemService.cs`, vista acotada de mentas y `SystemsView.cs` | Perfil/firmas y 21 IDs verificados en copias; propuesta incluye Bag.cro, datos/textos/bolsillos y CRR. Instala, se obtiene desde una tienda manual existente y funciona en juego; sin fuente modificada |
| M2 · distribución normal | `GameMartPresets.cs`, `GameMartService.cs`, `GameFieldItemsService.cs` y `.Template.cs`, `FieldItemTemplate.cs`, `GamePickupService.cs`, `GameStaticEncounterService.cs`; catálogo activo compartido en servicios | Venta/compra, EI/EB, Recogida y regalo equipado permiten mentas instaladas con opciones explícitas y semilla reproducible. Sin instalación no aparecen; catálogos se refrescan al instalar/deshacer |
| M3 · Global/Batch | `GlobalTemplateDocument.cs`, `GlobalTemplateApplication.cs`, `NativeRecipeFactory.cs`, `NativeRecipeCompletion.cs` y bridges concretos de tiendas/campo/Batch que necesiten el registro de IDs | Reproduce el mismo mapa/efectos y configuraciones en dos salidas aisladas. Usar el mismo núcleo de preparación para WPF/Batch, sin duplicar el parche en dos motores |
| M4 · segundo título y cierre | Segundo perfil US/UM independiente, pruebas de combinación con caps/tutores/compras únicas y rebuild final; informe de aceptación en el documento técnico | Ambos juegos acreditados por separado; un build desconocido se rechaza. Regresión completa una vez al cerrar el bloque. No release automática |

M1–M4 son secuenciales; no iniciar cuatro módulos en paralelo. Si M1 valida primero Ultra Sol, no anunciar Ultra Luna hasta M4 o adelantar su perfil dentro de M1 cuando existan fuentes comprobables. La complejidad del bloque es **alta**: el punto crítico es el uso de objetos desde bolsa y las llamadas a `.code`, no el formulario WPF.

Dependencias delimitadas para comenzar M1:

1. Confirmar semántica propuesta (naturaleza real) y excluir Ability Patch/Nature Shifter.
2. Fuentes limpias aisladas de `Bag.cro`, `.code`, `static.crr`, GARC de objetos, Game Text y bolsillos del perfil inicial; identidad/revisión y firmas. No acceder a ROM/guardado principal para aplicar pruebas.
3. Comprobar los 21 candidatos frente a los registros y referencias relevantes de **ese perfil**, y fijar el mapa seguro o devolver colisiones concretas. No ampliar automáticamente hasta 1024 ni escanear todos los juegos.
4. Reconstruir/documentar el manejador mínimo y sus referencias; contrastar las cuatro firmas y las funciones nativas de acceso al Pokémon/recalcular estadísticas. No portar el sondeo +8 como única validación.

Si no se consigue una reserva segura de 21 IDs dentro del perfil actual, detener esa instalación con un diagnóstico de colisiones. La expansión de objetos/partida es otro alcance, no una solución silenciosa.

## Criterios concretos de aceptación

### Datos y parche

- Cada una de las 21 identidades apunta a su naturaleza nativa correcta. La neutral selecciona el destino documentado; repetir la misma naturaleza no consume el objeto.
- «Se puede usar» y «Usar» llaman al mismo criterio. Un uso válido cambia únicamente la naturaleza prevista y las estadísticas derivadas necesarias; no cambia habilidad, IV/EV, especie/forma, movimientos, objeto equipado, estado o PS actuales, ni cura/revive.
- Cancelar, seleccionar huevo/objetivo inválido, no tener efecto o fallar el uso no consume la menta. Éxito consume exactamente una; bolsa y selección vuelven a su estado nativo sin bloquear mensajes.
- Guardar/reiniciar conserva la naturaleza nueva y el inventario correcto. El Pokémon conserva estructura, cifrado/checksum y funcionamiento de cajas/equipo. Verificar también PS a cero y Pokémon con daño previo.
- Reinstalar el mismo mapa es no-op; una instalación distinta, código desconocido, hook ocupado, tabla/IDs incompatibles o fuente cambiada se rechaza antes de escribir. No adoptar automáticamente un payload de otro mod.
- El guardado es conjunto: Bag.cro, GARC de objetos, bolsillos, textos y CRR; los demás bytes/miembros/CRO se conservan. Deshacer restaura exactamente archivos, catálogo y disponibilidad. No prometer que deshacer archivos revierte la naturaleza o los objetos que el jugador ya guardó dentro de una partida.

### Distribución y automatización

- Las 21 mentas aparecen con nombre/uso/botón correctos en Medicina, editor de objetos, tiendas y selección de objetos compatibles. El nibble vecino del bolsillo no cambia.
- Compra y uso reales de una menta ofensiva, una defensiva y la neutral; cobertura automatizada del mapa completo y de los casos sin efecto/cancelación. Comparar precio dinero/BP cuando se habilite cada vía.
- Con instalación activa y opción de mentas, los sorteos de tiendas/campo/Recogida pueden seleccionarlas y respetan límites y objetos de historia. Desactivando la opción, una semilla y receta previas conservan su comportamiento.
- Manual → Global → Batch conserva mapa, opciones, orden y recursos/hashes. El build y las dependencias se comprueban en cada copia; ninguna salida modifica fuentes ni instala mods.
- Rebuild y reextracción verifican recursos esperados y hashes. Prueba jugable focalizada por título confirma bolsa, uso, mensajes y guardado/carga; no se exige recorrer una partida completa para cada cambio de interfaz.

## Pruebas y fin de investigación

En implementación, crear casos sintéticos específicos para tabla de bolsillos (par/impar), reserva/colisiones, mapa de naturalezas, rechazo de firmas, relocaciones, idempotencia, transacción, replay y catálogos. Añadir un harness ARM con dependencias nativas controladas para efecto/consumo/cancelación; no considerar que eso sustituye el guardado/carga real. Usar las skills de pruebas al escribir o ejecutar esos casos y registrar flags nuevos si se añaden al runner.

Durante M1–M3, compilar el proyecto afectado y ejecutar solo suites relacionadas; una pasada completa y los checks exigidos de CI al cerrar el bloque, sin repeticiones con commits idénticos. No reabrir auditorías visuales, rendimiento ni arquitectura del proyecto.

Esta investigación termina con los puntos de entrada, contratos, riesgos y aceptación anteriores. **No queda una búsqueda general abierta de «más referencias»: el siguiente trabajo es M1.** Los datos aún no comprobados son las firmas/llamadas e IDs concretos de nuestras fuentes US/UM y la validación jugable del port, no la existencia de una implementación de referencia.

En esta tarea solo se verificaron Git/CI y fuentes; no se ejecutaron builds ni suites nuevas, ni se cambiaron C#, XAML, binarios, ROMs o guardados. Se aplicaron las reglas locales y `pk3ds-progressive-safety` para delimitar la investigación; no se afirma una implementación ni una compatibilidad jugable de mentas.
