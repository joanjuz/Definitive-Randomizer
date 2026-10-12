# 3DS Progressive — preview funcional v0.8.0

Aplicación WPF/.NET 10 independiente, en español y Dark Graphite, dentro del repositorio pk3DS Progressive. Trabaja en `feature/3ds-progressive-ui`, conserva WinForms y reutiliza Core sin modificar esos proyectos.

## Editores reales

- **Pokémon:** lista/búsqueda de especies y formas, sprites personalizados y caché, estadísticas, tipos, habilidades, objetos, EV, EXP/crianza, compatibilidad MT/tutores y parámetros de formas y Z de Gen 7.
- **Movimientos:** lista/búsqueda, campos básicos y avanzados, multigolpe, efectos, estados, objetivos, críticos, turnos, curación, tres slots de stats, 32 flags y parámetros/efectos Z.
- **Entrenadores:** clase, modo, IA, dinero, objetos y equipos de hasta seis Pokémon. Especies/formas, niveles, habilidad, género, objetos y movimientos; IV, EV, naturaleza y shiny en Gen 7. Randomización mediante los flujos originales SMTE/RSTE: generaciones/legendarios, especies/formas y equipos, clases/IA, BST progresivo, niveles, límites por historia y entrenador, Mega/Z, movimientos, Better Movesets, Strong Stat, Smart Items, Item Clause y templates JSON. Las ventanas originales nunca se muestran; las opciones y propuestas permanecen en WPF.
- **Encuentros:** tablas originales de Gen 6, incluyendo hordas y espejo EN de ORAS; día/noche, SOS y clima de Gen 7. Edición de especies/formas, niveles y tasas; randomización con BST, escala y alcance.
- **Evoluciones:** ocho rutas por entrada, método, argumento, destino y forma/nivel de Gen 7. Randomización de destinos, conversión de intercambios y aplicación de templates CSV de evoluciones, incluido/custom, conservando las rutas originales como el normalizador Progressive.
- Los editores permanecen en la misma ventana, con confirmación de cambios y diálogos oscuros. Los controles deshabilitados usan azul opaco #26374A, próximo al logo «3».

El usuario ya validó en Ultra Sol abrir proyecto, guardar stats, compatibilidad MT, movimientos, efectos Z y el template de estadísticas con las previews previas. Los editores y acciones nuevos requieren prueba manual.

## Acciones en el inspector

**Randomizar Pokémon** y **Modificar Pokémon** abren un panel temporal en el centro. Movimientos ofrece **Randomizar movimientos**, **Modo Metrónomo** y **Balance y templates**. El catálogo y el inspector siguen visibles; **Volver al editor** restaura el formulario. Los botones de generar/aplicar permanecen fijos mientras se recorren las opciones. Se recuerdan las opciones de cada acción durante la sesión.

Cada vista previa aleatoria genera una semilla nueva automáticamente. No existe un número editable. Las pruebas de servicios todavía permiten reproducir una propuesta con la semilla registrada. Los valores iniciales de Pokémon coinciden con las capturas del usuario: habilidades, exclusión de Shadow Tag/Arena Trap, objetos, MT y tutores activados; estadísticas, barajado, tipos, Wonder Guard, captura y grupos huevo desactivados. Los slots de stats empiezan desmarcados. MO se ofrece solo en Gen 6. La exclusión de habilidades permanece como opción del randomizador.

Los selectores muestran nombres. Los nuevos randomizadores también usan el inspector y sustituyen temporalmente el centro. Sus parámetros y alcance se recuerdan al cambiar de sección durante la sesión.

**Templates:** se incluyen `moves_gen6.csv`, `moves_gen7.csv` y `pokemon_stats_gen7.csv`. Movimientos permite cargar un CSV propio o usar Progressive. Se conservan los defaults de balance de Gen 6 cuando su CSV incluido está vacío, como en el editor original. Se portaron campos básicos, multigolpe, turnos, estados, stats, flags, parámetros de efecto y efectos Z. Las mecánicas de combate usan los algoritmos originales sobre una copia temporal, incluidos Escudo Real, Hidrochorro/Chapoteo Lodo, Deseo, Meditación/Pantalla de Humo, Conjuro, Neblina, Cerrojo Feérico, Pesadilla y Giro Vil v2. Los instaladores verifican sus firmas sobre copias temporales. Se instala una sola vez, como en MoveEditor7; el guardado verifica los bytes de la propuesta. Un CRO incompatible rechaza la propuesta completa. Desmarcar mecánicas permite aplicar solo datos de movimientos y desactiva las descripciones.

Las descripciones se escriben en el banco español (idioma 6), conservando los demás archivos internos. **Modificar Pokémon** de Gen 7 añade el template de estadísticas de Progressive y permite elegir un CSV propio.

**Corrección v0.8:** Giro Vil V2 mueve la tabla completa de movimientos y el headroom de relocaciones puede aumentar el tamaño por separado del segmento de código. Los registros se resuelven por la relocación #755, y los descriptores por el segmento de lectura, no por el tamaño total. Se retiró la segunda instalación añadida por Studio: el original aplica los patchers una sola vez. No se cambia el payload V2 ni su arreglo multigolpe. Regresiones conjuntas cubren Deseo, Meditación, Cerrojo Feérico, Conjuro, Pesadilla y Giro Vil. Encuentros reutiliza el lector LazyGARC original, descomprime para editar y conserva la compresión al guardar, sin recomprimir otros miembros.

Alcance **Todo / Lista filtrada / Selección**, vista previa antes/después, **Copiar tabla guardada** y **Copiar cambios**. Cambiar parámetros, selección o el editor invalida la propuesta.

## Tiendas y BP (USUM) · Hito 19

- Menú **Edición → Tiendas y BP · USUM**; 28 tiendas normales y 7 tiendas BP.
- Editar ID de objeto y precio BP, añadir o eliminar ranuras en tiendas normales, poner a 0 BP las megapiedras de Battle Tree.
- Resolución de `Shop.cro` mediante `Gen7ShopCroLayout` y reconstrucción segura mediante `Gen7ExpandedMartTable`: soporta CROs previamente expandidos, sin offsets fijos.
- Vista previa con diferencias, relectura del CRO, protección ante cambios externos y guardado conjunto con historial. El editor NO modifica `code.bin`, el GARC de objetos ni el precio de venta de los objetos.
- Segunda fase: Randomizar tiendas normales y BP con el pool original de Gen7; en tiendas normales se protegen MT y opcionalmente los objetos X, y se puede limitar a tiendas especiales. Si los objetos X no se protegen, sus ranuras (incluido Monte Lanakila) se sustituyen por objetos distintos que no sean X. Las opciones están en la misma vista WPF.
- Se añaden Caramelos Raros a las ocho tiendas de progreso sin sustituir ranuras preexistentes, fijando su precio a 10.
- Preset de vitaminas: seis vitaminas reales (no plumas), migración de plumas antiguas, Caramelo Raro, Escama Corazón desde 3 pruebas y Piedra Activadora desde 5 pruebas. Valida la subida EV específica de cada vitamina, activa `AddEVAbove100` y establece su precio a 200, sin parchear `code.bin`.
- Prohibir Cápsula Habilidad usa exclusivamente ID de objeto 645 en USUM (independiente del idioma) para sustituirla en tiendas normales y BP y excluirla de posteriores randomizaciones en la sesión. El botón conserva el precio BP anterior.
- Todas las modificaciones de `Shop.cro` y, cuando corresponde, del GARC `item` se preparan en memoria y se guardan conjuntamente con `ProjectHistory.Commit` tras confirmar la vista previa; se vuelve a validar el archivo original mediante fingerprint SHA-256. Los bytes de la tabla de tutores BP se verifican idénticos.
- Se corrigió la eliminación de ranuras existentes: además del botón de selección, cada fila tiene la acción **Quitar**. No se permite eliminar el último objeto; las tiendas BP mantienen su tamaño fijo.
- **No implementado todavía**: aplicar automáticamente estos presets al Global Template desde los cambios WPF; el Batch ROM Builder sigue ejecutando las acciones del template con el motor WinForms original. Requiere validación manual con ROM US/UM.
- Requiere prueba manual sobre una extracción real de Ultra Sun y Ultra Moon antes de declarar paridad completa.

## Tutores USUM · sincronización Shop.cro / code.bin

- En **MT y tutores → Tutores · movimientos y BP**: los 67 movimientos y sus precios BP son editables directamente, con randomización de movimientos únicos o cambio manual por ranura.
- El motor de sincronización WPF `UsumTutorCodePatch` reutiliza la firma estructural ARM validada de `USUMTutorCodeSyncPatcher`; no depende de offsets de Ultra Sun/Ultra Moon. Rechaza firmas ausentes, comprimidas o ambiguas sin guardar.
- **Vista previa tutores** prepara las diferencias en `Shop.cro` y en la tabla nativa de `code.bin`; **Aplicar tutores y verificar** guarda ambas en una transacción con historial, tras confirmar y verificar hashes originales y tabla CRO.
- Si solamente cambian precios, no se reescribe `code.bin`. Se conserva la edición de precios en SM, sin randomización de los 67 movimientos.
- El editor de MT permanece disponible e independiente: no se cambian sus 100 posiciones al editar tutores. No permite guardar MT si hay tutores pendientes ni guardar tutores si hay MT pendientes.
- Los presets **Tutor Sanity** y **Follow Evolutions** ya se integraron en WPF con casillas independientes y ejecución individual o conjunta. Sanity activa compatibilidad para los movimientos que cada Pokémon ya aprende por nivel; Follow Evolutions propaga compatibilidad por evolución (cadenas, ramas y formas) hasta punto fijo. Ambas operaciones solo agregan flags, nunca los eliminan.
- La vista previa construye en memoria un GARC Personal nuevo desde los archivos activos `personal`, `levelup`, `evolution` y los 67 movimientos actuales de `Shop.cro`. Comprueba que solo cambien bytes de compatibilidad (0x38..0x45), reconstruye/verifica la tabla maestra y registra especies afectadas.
- El guardado verifica SHA-256 de todos los archivos de origen y solo sustituye `personal` mediante una transacción con historial. `Shop.cro`, `code.bin`, `levelup` y `evolution` permanecen intactos, y se refresca la caché Pokémon tras el commit.
- No se permite iniciar Sanity/Follow mientras haya MT, movimientos o precios de tutores pendientes de guardar; desmarcar/cambiar opciones invalida la vista previa anterior.
- Pendiente validación manual sobre ambas ROMs para declarar paridad completa, además de las pruebas sintéticas CI.
- Pendiente: validar la compatibilidad efectiva de movimientos enseñables in-game, y ambas ROMs Ultra Sol/Ultra Luna. Compilar no reemplaza esa prueba.

## Backup e historial

**Generar backup**, junto a **Abrir proyecto**, permite elegir una carpeta fuera del proyecto. Guarda un ZIP de todos los archivos presentes, un manifiesto SHA-256 y el historial. Relee y verifica el ZIP y las fuentes antes de finalizar; un fallo o cancelación elimina el archivo parcial. Representa los archivos guardados al pulsar el botón; las ediciones pendientes en el formulario todavía no están guardadas. No se generan backups automáticos.

**Historial** versión 2 distingue edición individual y acción masiva. Una edición conserva valores anteriores/nuevos y su clave/slot. Una operación masiva conserva acción, opciones, alcance, reglas/template y hashes de auditoría; no conserva resultados por Pokémon ni una semilla fija. La política de randomización es una semilla nueva en cada futura ROM. Los historiales v1 se normalizan al leer/exportar y se actualizan al próximo guardado confirmado. Persiste en `3ds-progressive-history.json`, junto a RomFS/ExeFS, y viaja con el proyecto/backup.

El historial versionado es una base para ampliar el Global Template. Global Template y Batch ROM Builder están incorporados mediante importación JSON v1/coverage8 y ejecución aislada del motor Gen7 original; aún falta sincronizar automáticamente todas las opciones de los editores WPF con el template global.

## Guardado y pruebas

El guardado explícito prepara y relee todos los archivos, detecta cambios externos y verifica los bytes finales. Para balance se guardan juntos movimientos, textos, combate e historial; un fallo revierte los archivos ya reemplazados desde memoria. No es una transacción atómica entre archivos frente a un corte del proceso o del sistema. Personal incluye reconstrucción de tabla maestra; Movimientos respeta GARC directo X/Y y contenedor WD ORAS/Gen 7.

`3DS.Progressive.Tests` verifica servicios con fixtures sintéticos para XY/ORAS/SM/USUM y añade pruebas de inicialización WPF en Windows. Las comprobaciones cubren preservación binaria, alcance, semillas, dependencias, historial portable, backup, reaplicación del CRO ampliado y navegación/estilos de los cinco editores. CI compila, ejecuta esas comprobaciones y publica **3DS-Progressive-Preview-win-x64** autocontenido (14 días de retención).

```powershell
dotnet run --project .\3DS.Progressive.Tests\3DS.Progressive.Tests.csproj -c Release
dotnet run --project .\3DS.Progressive.App\3DS.Progressive.App.csproj -c Release
```

## Sprites

Prioridad de PNG: `%LOCALAPPDATA%\3DS Progressive\sprites\custom\entry-001.png`, `species-001.png`, `001.png`/`1.png`, caché, y descarga opcional desde PokeAPI/sprites. La forma alternativa usa la especie base si no hay sprite de entrada. Sin imagen se muestra el fallback. No se incorporan sprites descargados al repositorio.

## Alcance pendiente

La migración no está completa. Ya se integraron Static Encounters, Egg Moves, Level Up masivo, Player Caps, herramientas CRO, Type Chart Gen7, Global Template y Batch ROM Builder. Continúan pendientes las opciones completas de Progressive Wild, Story Text y módulos Gen6 especiales. En tiendas USUM se incorporaron la edición normal/BP, ranuras ampliables, randomización, caramelos raros y vitaminas con flags para EV 252. Quedan pendientes la sincronización bidireccional de sus opciones con Global Template y validación runtime de ambos juegos. Esta es una **preview**: no sustituye la versión estable WinForms ni se ha fusionado con ella.

Extraer el ZIP **completo** y ejecutar `3DS.Progressive.exe`; conservar la carpeta `custom_balance_templates` junto al ejecutable.

Detalles y comprobaciones manuales: [Hito 08](../docs/3ds-progressive/HITO_08_MOTORES_ORIGINALES.md). Matriz: [Paridad de acciones](../docs/3ds-progressive/PARIDAD_ACCIONES.csv).
