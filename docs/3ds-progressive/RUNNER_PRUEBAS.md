# Runner de pruebas de 3DS.Progressive.Tests

`3DS.Progressive.Tests` es un **ejecutable con runner propio**. No usa MSTest ni xUnit, así que `dotnet test` no ejecuta sus comprobaciones. La fuente de verdad es `3DS.Progressive.Tests/Program.cs`: este catálogo se generó a partir de ese archivo el 29/10/2026 y puede quedar desfasado. Si una opción no aparece aquí, buscarla en `Program.cs`.

## Uso

`--nds-trainers-only` comprueba lectura y edición manual de entrenadores, conversión de formatos, conservación de datos, rebuild/reapertura y replay de recetas históricas y nuevas (fixtures Gen4/Gen5). Se incluye en `--nds-only`.

`--nds-trainers-probe <salida nueva> <rom>…` ejecuta el mismo flujo con copias privadas de los perfiles soportados. Requiere un entrenador con movimientos/objetos explícitos y otro sin ellos; solo lee las fuentes y conserva las salidas aisladas. No prueba jugabilidad.

`--usum-mint-icons-only` ejecuta las pruebas sintéticas de conversión, descarga/caché con HTTP simulado y ciclo de instalación/historial/deshacer de iconos en US/UM. No descarga gráficos comerciales. Se ejecuta en CI.

`--usum-mint-icons-real-copies <salida nueva> <Ultra Sol fase 1> <Ultra Luna fase 1>` descarga las fuentes fijadas de PokéAPI y crea copias privadas nuevas para instalar, reabrir, deshacer y reinstalar, conservando tiendas y otros archivos. Nunca edita los orígenes. `--usum-mints-rebuild` también comprueba los iconos tras la reextracción si estaban reconocidos en el proyecto de entrada. Estas comprobaciones no acreditan la imagen dentro del emulador.

`--mart-selector-only` verifica con controles WPF reales fuera de pantalla y Shop.cro sintético la búsqueda de las 21 mentas, selección con Enter y un clic, actualización inmediata, salida de celda, cambio de tienda, vista previa, guardado transaccional y reapertura de tiendas normales y BP. También se incluye en `--rebuild-marts-ui-only` (CI).

`--mart-selector-usum-copy <Shop.cro USUM privado>` ejecuta ese recorrido sobre una copia desechable del archivo indicado, con el resto de datos sintéticos. Lee el origen sin modificarlo; no distribuye datos comerciales ni valida el efecto de las mentas dentro del juego.

`--usum-mints-rebuild <proyecto con mentas> <imagen 3DS nueva> <carpeta nueva de reextracción>` hace una única reconstrucción Full3DS, verifica todos los archivos reextraídos byte a byte y comprueba que el parche siga reconocido. Solo se usa con copias privadas; no valida una partida en el emulador.

`--usum-mints-only` ejecuta el mapa de 21 mentas, bolsillos, rechazo de títulos/huellas/hooks/colisiones, textos sin pérdidas, preparación, transacción con fallo inyectado, historial, reapertura, idempotencia y deshacer con fixtures sintéticos. `--usum-mints-ui-only` verifica fuera de pantalla la advertencia, instalación bloqueada sin propuesta y navegación al editor de tiendas existente. `--usum-mints-arm-fixtures <carpeta>` genera CRO sintéticos para `native/3ds/nature-mints/verify_arm.py`; no contienen datos de juegos. `--usum-mints-real-copies <salida nueva> <Ultra Sol extraído> <Ultra Luna extraído>` copia exclusivamente RomFS/ExeFS y cabeceras necesarias, instala y deshace en las copias, guarda las 21 mentas en una tienda, comprueba precios y fuentes intactas. Requiere material privado; no se ejecuta en CI ni modifica las fuentes. Los resultados jugables se registran aparte según [fase 1](MENTAS_GEN7_USUM_FASE1.md).

`--usum-mints-compatibility-probe <code.bin US reconocido> <code.bin UM reconocido> <Bag.cro original reconocido>` lee dependencias privadas sin modificar las fuentes. Comprueba originales, combinación de tipos/obediencia/MT/shiny, rechazo preciso de cambios en las seis entradas nativas, auxiliares, descriptores y tablas de naturaleza/bloques PK7; acepta cambios ajenos en código, tablas y datos. Verifica instalación/reapertura/deshacer y archivos ajenos. Usa objetos y textos sintéticos: no equivale a una ROM completa ni a una prueba jugable.

`--rerandomize-cycle-probe <carpeta nueva de salida> <imagen .3ds/.cxi reconstruida> [<Global Template>]` extrae la imagen, la abre, edita un entrenador, reconstruye un Trimmed .3DS y lo vuelve a extraer dos veces. En el segundo ciclo puede volver a aplicar un Global Template, edita otro entrenador y randomiza un tercero. Exige que las extracciones coincidan byte a byte con el proyecto editado, que los cambios se acumulen y que se mantengan la identidad NCCH y el estado de las mentas. La imagen de origen no cambia y todo se escribe en la salida nueva. Si falla, guarda la etapa y la excepción completa en `cycle-failure.txt`. Usa copias privadas, no ejecuta el juego. Documentación: [REEXTRACCION_3DS_CXI.md](REEXTRACCION_3DS_CXI.md).

`--usum-mints-project-regression <proyecto USUM privado> <copia nueva de salida>` copia los archivos reales de RomFS/ExeFS y las cabeceras a una salida nueva y separada. En un proyecto sin mentas exige preparación, instalación, reapertura, historial y deshacer exacto de los archivos, preservando lo ajeno; en uno ya instalado comprueba reconocimiento. Contrasta las fuentes antes/después y no escribe en ellas. No ejecuta el juego ni reconstruye una ROM.

`--definitive-game-data-profile-fixtures <carpeta nueva o vacía>` produce documentos del exportador real para ausencia, aplicación, modificaciones y deshacer de procedencia histórica de perfiles. `--definitive-game-data-only` también verifica la procedencia sin escrituras, secciones realmente confirmadas, ediciones posteriores, deshacer, historial antiguo/corrupto y coexistencia con caps.

`--definitive-game-data-only` verifica el exportador USUM con GARC y textos sintéticos: datos guardados, deshacer, lectura actual pese a cachés antiguas, formas, evoluciones vacías, identidad, destinos, privacidad y acceso WPF. También comprueba el bloque opcional `playerLevelCaps` que emite el exportador (estados installed, not-detected, unsupported, inconsistent y legacy-format con Battle.cro y code.bin sintéticos; vista previa, deshacer, Batch y Global Template sin aplicar) y que el ejemplo sintético coincide con el catálogo y la tabla de runtime de Player Level Caps (el esquema y la compatibilidad con Tracker se comprueban con `tools/contracts/check_definitive_game_data.py`). No usa ROMs reales. `--definitive-game-data-fixture <json>` exporta un JSON sintético nuevo para comprobar interoperabilidad con Tracker. `--definitive-game-data-caps-fixtures <carpeta>` exporta un documento sintético por estado de `playerLevelCaps` para `check_definitive_game_data.py --producer`. La suite de datos se incluye también en la regresión general; no se distribuyen datos extraídos.

`--mega-diagnostic-only` verifica el diagnóstico de solo lectura de las habilidades de las megaevoluciones (USUM) con una tabla Personal sintética: formas detectadas, dependencia de la ranura, huella, texto del informe, ausencia de escritura, rechazos y disponibilidad en Herramientas ROM; y la comparación entre dos jugadores a partir de dos exportaciones de datos (diferencias, simetría, archivos intactos y rechazos que indican cuál de los dos archivos falla).

`--mega-ability-sync-only` verifica Mega Ability Sync (USUM) con un modelo de dos juegos con tablas distintas y con módulos sintéticos: reglas del dato añadido al equipo, los dos papeles invertidos, un solo lado con parche, protocolos distintos, instalación, relectura, retirada, conflictos, convivencia con Player Level Caps y Persistent Items, el editor con historial y la sustitución de la primera rutina publicada (165 comprobaciones). `--mega-ability-sync-real <RomFS> <ExeFS>` repite la instalación y la retirada sobre copias en memoria de una extracción real, que solo se lee; no forma parte del CI. Ninguna de las dos ejecuta un combate.

`--stable-polish-only` verifica, fuera de pantalla, las correcciones de interfaz de la estable 1.0.0 (ver [PULIDO_ESTABLE_1.0.0.md](PULIDO_ESTABLE_1.0.0.md)): en MT y tutores USUM, con un `Shop.cro` sintético de 67 tutores, que las acciones del proyecto y las opciones de receta están en grupos distintos, que las casillas de receta no cambian filas ni archivos, que «0 BP» solo cambia precios y «Randomizar movimientos» solo movimientos, y que la vista previa y «Aplicar» guardan precios con historial; la preferencia de modo con una carpeta de estado aislada (`PROGRESSIVE_STATE_DIR`), reinicio simulado, cambio desde Inicio, vista guiada y preferencia inválida; Inicio con una sola apertura universal; contraste WCAG AA de Inicio, de la ventana y del diálogo «Abrir proyecto»; que Deshacer recarga el proyecto, permanece en Historial y conserva el mensaje; la tarjeta de Mega Ability Sync (solo USUM, estado sin escribir) y la ruta recibida al iniciar (una sola ruta existente). Con `PROGRESSIVE_UI_CAPTURE_DIR` guarda capturas. 44 comprobaciones; en CI.

`--optional-templates-only` verifica que el balance Eris-Ultranova es un paquete opcional (ver [PLANTILLAS_OPCIONALES_ERIS.md](PLANTILLAS_OPCIONALES_ERIS.md)): los templates incluidos de movimientos y estadísticas no tienen filas activas; el paquete conserva el balance anterior (Gen7 con Cerrojo Feérico, Gen6 igual a las filas que antes estaban en el código, Slaking); un asset de una receta antigua solo se resuelve en el paquete con el mismo SHA-256; Template Studio no ofrece balance incluido y enumera las mecánicas de combate antes de aplicar; randomizar movimientos, Metrónomo y randomizar Pokémon no leen ningún CSV ni modifican `Battle.cro` en XY, ORAS, SM y USUM; un CSV elegido sin la casilla de mecánicas solo cambia datos. Fuera de pantalla: Batch empieza sin template, sin botón de balance incluido y con las mecánicas desmarcadas; sin CSV no hay vista previa ni se registra un balance implícito en la receta; una receta anterior conserva su balance. Datos sintéticos; ninguna prueba en juego.

`--patch-capability-only` verifica el informe de capacidades de Player Level Caps (solo lectura): estado de cada punto de enganche, nivel (verificada, compatible por adaptación, conflicto, no acreditada), simulación de la instalación, acuerdo con el instalador, transacción fallida, deshacer y la vista WPF fuera de pantalla.

`--rare-candy-only` verifica «Caramelos Raros personalizados» (Rubí Omega) con archivos sintéticos y un perfil calculado a partir de ellos: ubicación de la rutina generada, llamadas solo a funciones con huella, rechazos por título, ExHeader, tamaño, funciones nativas, bucle de movimientos, `DllBag.cro` modificado o con hashes desfasados y CRR sin entrada; instalación sin tocar las entradas, rangos escritos, hashes CRO, CRR ordenada, doble aplicación, instalación parcial, conflictos, parche de referencia externo, restauración exacta y el editor (historial, reapertura, vista previa caducada, captura para Global Template). `--rare-candy-real <extracción OR> <salida nueva> [<referencia descomprimida>]` repite instalación y retirada en memoria sobre una copia real que solo se lee. `--rare-candy-project <copia del proyecto> <imagen nueva> <reextracción nueva>` comprueba la convivencia con los editores Gen6 que escriben `code.bin`, deshacer, reproducción desde Global Template, rebuild completo y reextracción. Las dos últimas no forman parte del CI y ninguna ejecuta el juego.

`--profiles-only` verifica los perfiles compartibles de los juegos 3DS (formato 1; XY, ORAS, SM y USUM): exportación y exclusiones, ausencia de rutas locales, lectura estricta, compatibilidad y conflictos con el proyecto, selección de secciones, aplicación por la reproducción existente y por el ejecutor real de Global Template sobre un proyecto sintético, historial, deshacer y los diálogos WPF fuera de pantalla.

`--player-caps-recipe-only` verifica, con binarios sintéticos de Ultra Sol y Ultra Luna, las recetas de Global Template que llevan una instalación manual de Player Level Caps: formato grabado, lectura, guardado y recarga de la receta, validación sin escritura, reproducción en otro proyecto (solo cambian Battle.cro y code.bin), reapertura, deshacer, reemplazo de una tabla instalada y rechazo sin escritura de recetas no válidas o de proyectos incompatibles.

`--undo-template-regression-only` verifica UndoOf con historial en memoria, archivos sintéticos temporales y lectura WPF fuera de pantalla. Comprueba exportación manual (ManualEdits y ManualOperation), cadenas de restauraciones, selección de snapshots, separación de hijos GlobalApplication entre recetas con acciones del mismo tipo y opciones recuperadas al reabrir. Informa todas las expectativas antes de fallar conjuntamente si quedan regresiones. No usa ROMs. Queda fuera del recorrido predeterminado; el CI la ejecuta explícitamente y rechaza un código de salida distinto de cero.

```powershell
# Compilar (el CI mantiene apphost: Global/Batch comprueba el ejecutable de LegacyBatch)
dotnet build .\3DS.Progressive.Tests\3DS.Progressive.Tests.csproj -c Release -p:UseAppHost=true
# Ejecutar una suite
dotnet .\3DS.Progressive.Tests\bin\Release\net10.0-windows\3DS.Progressive.Tests.dll --synthetic-only
```

- **Sin opciones**: ejecuta la regresión sintética completa (XY, ORAS, SM y USUM) y, en Windows, también las comprobaciones WPF fuera de pantalla.
- `--synthetic-only`: la misma regresión sintética, sin las comprobaciones WPF.
- Cada comprobación imprime `PASS: …`. El primer fallo lanza una excepción y **detiene la suite**, así que un fallo puede ocultar otros posteriores.
- `--help`, `--list` o `-h`: lista las opciones con sus argumentos y si las ejecuta el CI. El listado sale de la tabla fija de `3DS.Progressive.Tests/RunnerCatalog.cs`, que hay que actualizar junto a `Program.cs` y este documento al añadir, renombrar o quitar una opción.
- La mayoría de las opciones exigen exactamente los argumentos indicados. Una opción desconocida o con otro número de argumentos imprime `Opción no reconocida o número de argumentos incorrecto: …`, sugiere `--help` y termina con código 2 sin ejecutar ninguna comprobación.
- Las comprobaciones "UI" renderizan fuera de pantalla: no equivalen a una prueba interactiva ni a una prueba dentro del juego.

## Suites sintéticas y de componentes (sin argumentos)

| Opción | Argumentos | En CI |
|---|---|---|
| `--batch-gen6-only` | — |  |
| `--batch80-only` | — |  |
| `--catch-batch-only` | — |  |
| `--editor-modules-only` | — |  |
| `--gen7-adjust-only` | — |  |
| `--general-manual-only` | — |  |
| `--global-apply-only` | — | sí |
| `--global-options-only` | — | sí |
| `--guidance-only` | — | sí |
| `--history-inspection-only` | — | sí |
| `--transaction-races-only` | — | sí |
| `--undo-toctou-only` | — | sí |
| `--undo-template-regression-only` | — | sí |
| `--item-icons-only` | — |  |
| `--legacy-parity-only` | — | sí |
| `--manual-edits-only` | — |  |
| `--master-batch-only` | — |  |
| `--native-gen6-only` | — |  |
| `--native-wild7-only` | — |  |
| `--nds-only` | — | sí |
| `--nds-map-layout-only` | — | matrices sintéticas de solo lectura; ejecución focalizada independiente |
| `--project-detector-only` | — | detector universal de proyectos: seis perfiles DS, checkpoints v1–v3, 3DS, Switch, rechazos, nombres engañosos y no modificación de las entradas; sintética, en el CI |
| `--universal-open-only` | — | apertura universal (B2): coordinador con anfitrión falso, recuperación 3DS sobre copias desechables, carreras y cierre del diálogo y la ventana principal real con los tres motores; sintética y fuera de pantalla |
| `--nds-project-format-only` | — | formato `.drproj` de proyectos DS persistentes: ida y vuelta, truncado y corrupción exhaustivos, límites, almacén en memoria, recuperación, transacción, identidad de la ROM y escala; sintética |
| `--nds-project-resilience-only` | — | resistencia de proyectos DS persistentes: 300 operaciones por perfil en los seis perfiles, con cierres y reanudaciones cada 20 y guardados que fallan en pasos al azar; estado recuperado y ROM exportada idénticos a los de una sesión sin proyecto; los seis estados del proyecto en el editor DS fuera de pantalla |
| `--nds-project-open-only` | — | apertura de ROM DS con su proyecto persistente: crear y reanudar, ROM movida o renombrada, copia de la ROM, proyecto de otra ROM, dos proyectos, versión más reciente, proyecto dañado con y sin copias, carpeta sin permiso de escritura, detección de `.drproj`, motor universal e importación de checkpoints; ROM sintéticas y sistema de archivos real |
| `--nds-project-transactions-only` | — | transacciones de proyectos DS persistentes: tras cada operación, la sesión viva, el proyecto reabierto y una sesión sin persistencia tienen los mismos bytes y exportan la misma ROM, en los seis perfiles; fallos del almacén, rechazos al asociar, Batch y Global Template sin archivos de proyecto, eventos; ROM sintéticas y sistema de archivos real |
| `--nds-project-store-only` | — | almacén en disco de proyectos DS: contrato, fallos inyectados en cada paso, fallos reales (archivo retenido, solo lectura, sin permiso de escritura), procesos hijo terminados a mitad de un guardado, concurrencia entre procesos, modificación externa y recuperación; sistema de archivos real en una carpeta temporal |
| `--nds-npc-script-only` | — | lector/escritor mínimo de scripts, patrón de la enfermera, scripts compartidos, checkpoint v3; sintética |
| `--nds-npc-structure-only` | — | operaciones estructurales de NPC (registro, NARC, checkpoint v2, política); sintética |
| `--nds-npc-drag-only` | — | planificación y matemática del arrastre de NPC (sin WPF); sintética |
| `--nds-map-terrain-only` | — | lector de terreno `land_data`, consulta espacial y superposición; sintética |
| `--nds-map-props-only` | — | lector de map props (posición e ID de modelo) y modelo de la capa; sintética; sí |
| `--nds-map-canvas-only` | — | modelo espacial puro del lienzo DS de solo lectura; sin WPF |
| `--nds-events-only` | — | incluida también en `--nds-only` |
| `--nds-map-render-ui-only` | — | capa Modelos/texturas del editor con recursos sintéticos: desactivada por defecto, render en segundo plano, vuelta exacta a la vista esquemática y aviso; sí |
| `--map3ds-gen7-only` | — | matriz de mundo Gen7 (`TR`), texturas GFL2 y adaptador USUM de solo lectura con datos sintéticos; sí |
| `--map3ds-render-only` | — | lector H3D (BCH): decodificación PICA y ETC1, transformación UV, mallas y tiras, versiones admitidas, y rasterizador cenital Gen6 con modelos sintéticos; sí |
| `--map3ds-render-ui-only` | — | capa «Modelos/texturas» del editor 3DS con un adaptador de prueba: desactivada por defecto, render en segundo plano con caché, eventos encima y vuelta a la vista esquemática; sí |
| `--map3ds-gen6-ui-only` | — | editor de mapas 3DS fuera de pantalla con el adaptador sintético: lista de zonas, marcadores, selección en mapa y lista, capas, eventos sin dibujar y ausencia de controles de escritura; sí |
| `--map3ds-trainer-facing-only` | — | orientación y visión de entrenadores 3DS: valores Gen6 0–3 y alcance (0x0C/0x0E), rotación de entrenadores USUM (cuaternión), escritura byte a byte, rechazos (rutas, formas no centradas, valores sin verificar) y propiedades; sintética; sí |
| `--map3ds-nurse-only` | — | «Añadir enfermera» Gen6: plantilla tomada de la enfermera del propio juego (script 2100, modelo único y quieta), inserción de un registro tras el último NPC conservando los demás registros, el script de entidades y las demás partes, rechazos (plantilla, dirección, casilla, recuento) validación de la casilla (ocupada, sin bloque, máximo de NPC, zonas sin casillas) y lista de modelos del área (entrada copiada del juego, añadida solo si falta, lista ilegible o llena); sintética; sí |
| `--map3ds-nurse-ui-only` | — | botón y panel «Añadir enfermera» del editor 3DS: casilla por clic, vista previa sin escritura, dirección, guardado a través del proyecto y selección de la enfermera nueva; sí |
| `--map3ds-catch-zones-only` | — | zonas de captura X/Y y OR/AS: valores por familia (hierba X/Y `0x20002004`, OR/AS `0x20004004`; tabla 0x188/0x104; 4/5 partes), copia compartida EN de OR/AS, mapa de zonas de la matriz (10×10 casillas), dueño de cada casilla, usos de cada bloque, pintar solo suelo `0x20` → `0x20002004` byte a byte, añadir la tabla de encuentros (parte 3 del ZO) a una zona sin tabla, rechazos (otra zona, muros, hierba, eventos, bloques repetidos, OR/AS) y relectura; sintética; sí |
| `--map3ds-catch-zones-ui-only` | — | panel «Zona de captura» del editor 3DS: casillas por clic o rectángulo, origen de la tabla, vista previa obligatoria, guardado a través del proyecto; sí |
| `--map3ds-gen7-catch-zones-only` | — | zonas de captura de Ultra Sol/Ultra Luna: regiones de encuentro (ED parte 6: registro de 28 B con tabla y máscara de atributos de suelo, caja o cilindro), paquetes ED/EA reconstruidos byte a byte (también EA vacío), añadir región y tabla, casillas de hierba 0x1F, fuentes de tablas de hierba del juego, plan de la caja, rechazos (otro suelo, sin fuente, áreas de varias zonas) y relectura; sintética; sí |
| `--map3ds-gen7-visible-grass-only` | — | hierba visible de Ultra Sol/Ultra Luna: paquete de bloque (8 partes, datos en 0x80, partes alineadas a 0x80) reconstruido byte a byte, secciones del modelo GFL2, reconocimiento del modelo `Map07_encgrass01`, rechazos (otra estructura, datos tras las secciones, bloque con hierba, plantilla de varias manchas); sintética; sí |
| `--map3ds-gen7-grass-editor-only` | — | hierba visible en el editor USUM: modelo GFL2 sintético leído por el lector del proyecto, plantillas con nombre legible y texturas del área, plan centrado en una casilla, bloque escrito, rechazos (sin texturas, bloque con hierba, fuera del bloque, sin suelo, varios niveles), una copia desplazada no es otra plantilla, casillas con hierba visible e imagen de vista previa; sintética; sí |
| `--map3ds-gen7-grass-editor-ui-only` | — | panel «Añadir hierba visible»: botón solo en USUM con proyecto, plantillas, clic y vista previa (modelo real, superficie, bloque y suelo admitido), sitio no válido, guardado, deshacer, área sin texturas y compatibilidad con el panel de zona de captura; sí |
| `--map3ds-gen7-nurse-only` | — | enfermera de Ultra Sol/Ultra Luna: plantilla de las enfermeras de los Centros (modelo 66, script 5030, CP del modelo), regla del inicio de datos del paquete «AC», plan, registro movido y girado con la curación de Lylia (script común 30571), NPC y partes intactos, modelo añadido o ya cargado, rechazos (ocupada, NPC delante, warp, sin suelo, varias zonas, área llena), enfermeras distintas no se copian, segunda enfermera, cruce de un paso con una valla; sintética; sí |
| `--map3ds-gen7-nurse-ui-only` | — | panel «Añadir enfermera» en USUM: botón con proyecto, vista previa con el modelo del área, casilla ocupada y guardado; sí |
| `--map3ds-gen7-catch-zones-ui-only` | — | panel «Zona de captura» en USUM: tablas de hierba del juego como origen, vista previa, guardado y suelo no válido; sí |
| `--map3ds-trainer-facing-ui-only` | — | panel «Orientación y visión» del editor 3DS: dirección y alcance en Gen6, ángulo libre en USUM, flecha y casillas de visión, vista previa sin escritura y guardado a través del proyecto; sí |
| `--map3ds-gen6-only` | — | lectores de mapas Gen6 (paquetes ZO/MM/GR, entidades, matriz y rejilla de permisos) con archivos sintéticos; adaptador de solo lectura; sí |
| `--nds-event-editing-only` | — | edición de objetos de fondo, warps y activadores: escritura byte a byte, destinos, campos protegidos, checkpoint v5 y proyecto con lector 3; sintética; sí |
| `--nds-event-editing-ui-only` | — | panel contextual de objetos, warps y activadores, arrastre en el mapa y guardado; sí |
| `--nds-gen4-profile-only` | — | perfiles de mapas y eventos Gen4 (Platino CPUE/1, Diamante ADAE/5): rutas, huellas, campos escribibles, rechazo de NPC nuevos y de scripts añadidos en Diamante, checkpoints con su juego, exportación y detector; sintética; sí |
| `--nds-gen4-profile-ui-only` | — | editor de eventos con una sesión de Diamante: título, aviso sin controles de NPC nuevos, direcciones de interacción propias y vista previa sin escritura; sí |
| `--nds-gen4-hgss-only` | — | perfil de HeartGold IPKE/0: cabeceras Johto, `land_data` con sección de sonido, modelos interiores, Poké Ball del suelo como objetos de suelo, visión hasta 7, interacción 0–4, sin NPC nuevos, checkpoints, exportación, restauración y detector; Platino y Diamante sin cambios; sintética; sí |
| `--nds-gen4-hgss-ui-only` | — | editor de eventos con una sesión de HeartGold: título, sin controles de NPC nuevos, Poké Ball con los objetos y sin orientación, visión hasta 7 y vista previa sin escritura; sí |
| `--nds-gen5-black-only` | — | lector Gen5 de Pokémon Negro IRBO/0: cabeceras de 48 bytes, matrices u32, archivos de eventos 20/36/20/22 con cola conservada, warps en dieciseisavos, raíles y warps especiales bloqueados, Poké Ball por script, visión y dirección de interacción no escritas, colisión de permisos, modelo de terreno, checkpoints, exportación, restauración y detector; sintética; sí |
| `--nds-gen5-black-ui-only` | — | editor de eventos con una sesión de Negro: título, sin controles de NPC nuevos, giro de entrenador sin visión, NPC de raíl con motivo, dirección de interacción sin interpretar; sí |
| `--nds-gen5-white-only` | — | perfil de Blanco IRAS/0: identidad, valores de Negro salvo identidad y scripts, mismas lecturas, escrituras y bloqueos, rechazo de tablas no acreditadas, checkpoints no cruzados con Negro, exportación, restauración y detector; sintética; sí |
| `--nds-gen5-white-ui-only` | — | editor de eventos con una sesión de Blanco: título e introducción, sin controles de NPC nuevos, giro previsto sin escritura; sí |
| `--nds-gen5-white2-only` | — | perfil de Blanco 2 IRDO/0 y límite de identificadores de mapa por número de cabeceras (fixture de 610 cabeceras): mapa 605, sobrecargas de Gen4 intactas, edición, checkpoint, exportación y restauración más allá de 593, detector con rechazo de Negro 2; sintética; sí |
| `--nds-gen5-white2-ui-only` | — | editor de eventos con una sesión de Blanco 2: título, 610 mapas en la lista, el mapa 605 en el lienzo; sí |
| `--nds-gen5-buildings-only` | — | edificios Gen5 (solo lectura): perfiles por juego, regla área → paquete de Negro/Blanco y de Blanco 2, estaciones, tabla de edificios (id big-endian), paquetes AB (mitades, duplicados idénticos y ambiguos, rechazos), colocación (giro y Z invertida), avisos de lo que no se dibuja, terreno solo sin archivos y pasada translúcida del renderizador (desactivada fuera de Gen5); sintética; sí |
| `--nds-gen5-buildings-ui-only` | — | selector de estación de la vista gráfica: visible solo con edificios Gen5, cambia la vista previa sin escribir la ROM ni la sesión; sí |
| `--nds-map-render-only` | — | lectores NSBTX/NSBMD, rasterizador cenital y resolvedor de recursos de mapa con archivos Nitro sintéticos; sí |
| `--nds-trainer-vision-only` | — | orientación y visión de entrenadores existentes: contratos del motor, escritura byte a byte, checkpoints v1–v4, proyecto (lector 2) y geometría de la visión; sintética; sí |
| `--performance-only` | — |  |
| `--pickup-only` | — |  |
| `--pokefeur-only` | — |  |
| `--rebuild-cache-only` | — | incluye la validación sintética del ExHeader de la reconstrucción .3DS |
| `--rebuild-stability-only` | — | sí |
| `--remote-sprites-only` | — |  |
| `--requested-only` | — |  |
| `--shiny-batch-only` | — |  |
| `--single-purchase-only` | — |  |
| `--sm-marts-only` | — |  |
| `--starter-text-only` | — |  |
| `--statics7-batch-only` | — |  |
| `--swsh-expansion-only` | — |  |
| `--swsh-only` | — | sí |
| `--swsh-parity-only` | — | sí |
| `--synthetic-only` | — | sí |
| `--template-feedback-only` | — |  |
| `--template-latest-only` | — |  |
| `--template-studio-only` | — |  |
| `--text-classification-only` | — |  |
| `--text-commands-only` | — |  |
| `--text-search-only` | — |  |
| `--title-movies-only` | — |  |
| `--titles6-only` | — |  |
| `--tm6-compatibility-only` | — |  |
| `--tm7-text-only` | — |  |
| `--trades6-only` | — |  |
| `--tutors6-only` | — |  |

`--transaction-races-only` verifica H2/H3 en Windows/NTFS con binarios sintéticos temporales y oplocks: Commit, rollback y Recover ante escritores externos, procesos hijos interrumpidos o matados a mitad, calibraciones sin escritor, journals anteriores y compatibilidad de metadatos. Imprime bytes, SHA-256, secuencia y estado residual de todos los casos antes de exigir las invariantes conjuntamente. La versión corregida con guardados individuales registra **90 correctas y 0 fallidas**; la versión investigada registraba 18 y 8. El modo interno `--interrupt-transaction-races <raíz> [modo]` exige una raíz sintética propia bajo TEMP. Reproducción: [REPRODUCCION_H2_H3_20261104.md](REPRODUCCION_H2_H3_20261104.md). Corrección, resultados y límites: [CORRECCION_H2_H3_20261104.md](CORRECCION_H2_H3_20261104.md). Guardados sin journal explícito y recuperación: [H2_H3_GUARDADOS_INDIVIDUALES_20261104.md](H2_H3_GUARDADOS_INDIVIDUALES_20261104.md). Los modos internos admitidos son `commit`, `recover`, `commit-vacated`, `standalone-vacated`, `standalone-recover`, `garc`, `garc-reader` y `standalone-writer`; un modo desconocido termina con código 2 antes de acceder a la raíz.

`--undo-toctou-only` es una regresión aislada para Windows y un sistema de archivos local con soporte de oplocks. Usa dos archivos temporales sintéticos y pausa la apertura de una copia de restauración mediante un oplock; modifica el destino después de validar su `AfterSha256` y antes de capturarlo para la transacción. No usa ROMs ni esperas temporizadas. Los límites de espera solo detectan bloqueos del mecanismo. El resultado esperado es rechazo, conservación de ambos archivos (incluida la edición externa) e historial idéntico byte a byte. La prueba informa los cuatro resultados antes de exigirlos conjuntamente. Queda fuera del recorrido predeterminado; el CI la ejecuta explícitamente con esta opción.

`RestorePoints.Undo` compara con `AfterSha256` los mismos bytes capturados que entrega a `FileChange`, antes de llamar a `ProjectHistory.Commit`. Una discrepancia durante esa preparación rechaza el conjunto sin escribir archivos ni historial. Esto cierra la ventana entre el preflight `WhyNot` y la captura. Después, `FileTransaction.Commit` retiene todos los destinos, comprueba el fingerprint por ese mismo handle y los reemplaza sin soltarlos, con relectura y rollback; las garantías y sus límites están en [CORRECCION_H2_H3_20261104.md](CORRECCION_H2_H3_20261104.md). La regresión de esta opción verifica solo la ventana de preparación de Undo.

`--mart-layout-ui-only [carpeta de capturas] [extracción USUM]` abre Tiendas en la ventana principal real, fuera de pantalla, para Sol/Luna (Shop.cro sintético), X/Y (tabla ampliada) y ORAS (tabla fija), y para Ultra Sol/Ultra Luna si se pasa una extracción (solo se lee su `Shop.cro`). Con los tamaños lógicos de 1920×1080 al 100, 125 y 150 %, 1366×768 al 100 y 125 %, 1024×640 y la ventana mínima (900×480), comprueba la disposición en tres columnas (lista de tiendas, inventario y acciones) o la compacta, que ningún control salga por la derecha, que la tabla conserve alto y no necesite desplazamiento horizontal, que «Generar vista previa» y «Aplicar y verificar» queden fijos y visibles, y que elegir una tienda normal o BP en la lista muestre su tabla. Con carpeta de capturas guarda una imagen por juego y tamaño, renderizada a la escala correspondiente. No sustituye la comprobación interactiva de foco, contraste y DPI.

`--rebuild-marts-ui-only` incluye la regresión de HistoryView: habilitar «Deshacer este cambio» tras la comprobación automática/manual, deshabilitarlo durante bloqueos anidados o ante cambios externos y retirar suscripciones al descargar la vista. Usa un HwndSource WPF fuera de pantalla y archivos sintéticos. Casos y resultados: [HISTORY_UNDO_BUTTON_20261105.md](HISTORY_UNDO_BUTTON_20261105.md).

## Suites de interfaz WPF fuera de pantalla (sin argumentos)

| Opción | Argumentos | En CI |
|---|---|---|
| `--all-ui-only` | — |  |
| `--global-apply-ui-only` | — |  |
| `--interaction-lock-only` | — | sí |
| `--rebuild-marts-ui-only` | — | sí |
| `--mart-layout-ui-only` | 0 a 2 (carpeta de capturas, extracción USUM opcional) | sí |
| `--nds-ui-only` | — | sí |
| `--nds-events-ui-only` | — | sí |
| `--nds-trainer-vision-ui-only` | — | sí |
| `--nds-map-canvas-ui-only` | — | sí |
| `--nds-map-terrain-ui-only` | — | sí |
| `--nds-map-props-ui-only` | — | sí |
| `--nds-npc-drag-ui-only` | — | sí |
| `--nds-npc-structure-ui-only` | — | sí |
| `--nds-npc-script-ui-only` | — | sí |
| `--requested-ui-only` | — | sí |
| `--swsh-ui-only` | — | sí |
| `--template-feedback-ui-only` | — | sí |
| `--template-latest-ui-only` | — | sí |
| `--template-studio-ui-only` | — | sí |
| `--text-classification-ui-only` | — | sí |
| `--text-commands-ui-only` | — | sí |
| `--tm-default-ui-only` | — |  |
| `--utility-ui-only` | — |  |
| `--visual-redesign-only` | — | sí |
| `--visual-audit` | 1 o 2 (carpeta de capturas, extracción USUM opcional) | sí |
| `--visual-audit-ds` | 2 o más (carpeta de capturas y ROM de DS) |  |

`--global-apply-ui-only` es un alias de `--global-apply-only`.

`--visual-audit <carpeta> [extracción USUM]` abre la ventana principal real fuera de pantalla, con el despachador activo, para X/Y, ORAS, Sol/Luna y Ultra Sol/Ultra Luna. Usa los tamaños lógicos de pantallas habituales: 1280×800 al 100 %, 1920×1080 al 125 % y al 150 %, y 1366×768 al 125 %.

Recorre:
- todas las secciones y sus pestañas;
- las herramientas que se abren desde los hubs («Abrir …»);
- las tablas que se abren desde una sección;
- cada ventana o diálogo que aparece: se captura y se cierra.

Pone en edición una celda de texto y otra de lista de las tablas. Guarda una captura de cada estado, `issues.txt` y `warnings.txt`.

Falla si encuentra:
- controles solapados o botones pegados;
- texto que no cabe o cortado por abajo;
- controles recortados por su contenedor o que se salen por la derecha;
- texto con contraste menor que el de WCAG AA;
- fondos claros del sistema;
- editores de celda con el aspecto del sistema;
- errores al abrir una vista.

El texto acortado con «…» es un aviso y no hace fallar la auditoría. Tiendas y BP de USUM necesita el `Shop.cro` de una extracción real (segundo argumento, que solo se lee); sin él esa sección se omite y queda un aviso.

También abre el panel de opciones de randomizado de cada editor de registros, con todas sus pestañas, y despliega el Inspector cuando una ventana estrecha lo pliega.

`--visual-audit-ds <carpeta> <rom> [rom…]` hace lo mismo con el espacio de Nintendo DS y volcados reales, que solo se leen (se comprueba su SHA-256). Recorre cada sección, sus opciones, Ajustes del juego, Global Template, Historial y las tablas de Level Caps y Trainer Move Rules.

## Comprobaciones con datos reales, copias y probes (requieren rutas)

Necesitan extracciones, ROMs o carpetas de salida. Los argumentos concretos (entrada, salida, semilla…) se definen en la línea correspondiente de `Program.cs` y en la clase de comprobación que invoca. Usar siempre **copias aisladas y rutas de salida nuevas**; nunca la extracción original, la SD ni mods instalados.

| Opción | Argumentos | En CI |
|---|---|---|
| `--arm-audit-export` | exactamente 2 |  |
| `--audit-catch-templates` | exactamente 2 |  |
| `--check-catch-batch-real-copies` | 2 o más |  |
| `--check-catch-real-copies` | 2 o más |  |
| `--check-full-batch-real` | 2 o más |  |
| `--check-marts6-full-batch-real` | 2 o más |  |
| `--check-marts7-full-batch-real` | 2 o más |  |
| `--check-master-full-batch-real` | 2 o más |  |
| `--check-real-rebuild-roundtrip` | 2 o más |  |
| `--check-shiny-independent-copies` | 2 o más |  |
| `--check-statics7-full-batch-real` | 2 o más |  |
| `--check-title-movie-roundtrip` | exactamente 2 |  |
| `--check-trades6-package-copy` | exactamente 3 |  |
| `--check-tutors6-full-batch-real` | 2 o más |  |
| `--check-tutors6-independent-copies` | 2 o más |  |
| `--check-tutors7-full-batch-real` | 2 o más |  |
| `--description-preview-fonts` | opcionales |  |
| `--diagnose-text-catalogue` | exactamente 1 |  |
| `--export-local-english-for-text-research` | 2 o más |  |
| `--extract-independent-image` | exactamente 2 |  |
| `--gen7-adjust-real` | exactamente 2 |  |
| `--interrupt-save-check` | exactamente 1 |  |
| `--nds-gen5-probe` | 1 o más |  |
| `--nds-data-probe` | 2 o más |  |
| `--nds-gen4-probe` | 2 o más |  |
| `--nds-map-layout-probe` | `<informe JSON nuevo> <ROM privada Platino CPUE/1>` | solo lectura; no copia ni exporta ROM |
| `--nds-map-terrain-probe` | `<informe JSON nuevo> <ROM privada Platino CPUE/1>` | solo lectura; no copia ni exporta ROM |
| `--nds-map-props-probe` | `<informe JSON nuevo> <ROM privada Platino CPUE/1>` | solo lectura; no copia ni exporta ROM |
| `--nds-map-props-ui-probe` | `<carpeta nueva> <ROM privada Platino CPUE/1>` | editor de eventos real fuera de pantalla sobre una copia; capturas; sin escrituras |
| `--nds-npc-structure-probe` | `<carpeta nueva> <ROM privada Platino CPUE/1>` | copia la fuente a la carpeta nueva; no modifica la original |
| `--rerandomize-cycle-probe` | `<carpeta nueva de salida> <imagen .3ds/.cxi reconstruida> [<Global Template>]` | dos ciclos de extracción, edición y reconstrucción sobre copias; la imagen de origen no cambia; documentación: [REEXTRACCION_3DS_CXI.md](REEXTRACCION_3DS_CXI.md) |
| `--project-detector-probe` | `<carpeta nueva de informe> <ruta> [<ruta>…]` | detecta cada archivo o carpeta, comprueba que no cambia (SHA-256 o instantánea) y abre los candidatos DS y Switch con su motor; solo lectura; documentación: [DETECTOR_PROYECTOS_B1.md](DETECTOR_PROYECTOS_B1.md) |
| `--universal-open-live-probe` | `<carpeta nueva de informe> <ruta> [<ruta>…]` | **interactiva**: abre cada ruta, en orden, desde el botón único «Abrir proyecto» con ratón y teclado reales (SendInput); necesita un escritorio desbloqueado; solo lee las entradas y comprueba que no cambian |
| `--nds-project-resilience-probe` | `<carpeta nueva de salida> <operaciones 20-5000> <ROM DS> [<ROM DS>…]` | la resistencia de `--nds-project-resilience-only` con volcados privados en solo lectura (los proyectos se escriben en la carpeta de salida) |
| `--nds-project-transactions-probe` | `<carpeta nueva de salida> <ROM DS> [<ROM DS>…]` | la equivalencia de `--nds-project-transactions-only` con volcados privados en solo lectura (el proyecto se escribe en la carpeta de salida); en Platino, además, eventos con script añadido y conflicto de scripts |
| `--nds-project-store-probe` | `<carpeta nueva en el disco que se quiere probar>` | las mismas comprobaciones de `--nds-project-store-only` en la carpeta indicada (por ejemplo, en el disco donde están las ROM); la carpeta se crea y se borra |
| `--nds-project-store-child` | `<modo> <proyecto> …` | uso interno: procesos hijo de `--nds-project-store-only` |
| `--nds-project-save-bench` | `<carpeta nueva de salida> <randomizaciones 1-200> <ROM DS> [<ROM DS>…]` | coste de confirmar una edición manual a medida que crece el historial (tras 0, 1, 5, 10, 20, 30, 40… randomizaciones completas), sus partes (serializar, verificar, leer) y la reapertura; volcados privados en solo lectura, proyectos en la carpeta de salida |
| `--nds-project-measure` | `<carpeta nueva de informe> <ROM DS> [<ROM DS>…]` | mide tamaño y coste de persistir operaciones representativas (instantánea completa frente a registro incremental) en copias privadas; solo lectura |
| `--nds-npc-script-probe` | `<carpeta nueva> <ROM privada Platino CPUE/1>` | copia la fuente; copia la enfermera de Jubileo a Hojaverde, exporta, reabre y restaura; añade una enfermera desde la plantilla de la ROM en la Ruta 201 y restaura |
| `--nds-live-ui-probe` | `<ROM privada Platino CPUE/1> <carpeta nueva> <ancho> <alto>` | **interactiva y local**: abre una ventana real y usa ratón y teclado reales (SendInput) sobre una copia; requiere el escritorio desbloqueado; no se ejecuta en CI |
| `--nds-events-probe` | `<salida nueva> <ROM privada Platino CPUE/1>` |  |
| `--nds-map-render-gallery-probe` | `<carpeta nueva> <ROM privada Platino CPUE/1> [nombres internos…]` | solo lectura: renderiza ciudades, interiores, cuevas y rutas con los eventos encima y mide bloques, triángulos, tiempo y alineación con las colisiones; guarda un PNG por mapa (privado) |
| `--nds-map-render-ui-probe` | `<carpeta nueva> <ROM privada Platino CPUE/1>` | editor fuera de pantalla con la capa gráfica sobre Hojaverde; guarda la captura (privada) en la carpeta |
| `--map3ds-gen7-ground-probe` | `<carpeta nueva> <carpeta extraída de Ultra Sol/Ultra Luna> <área> <píxeles por bloque>` | solo lectura: compara la cobertura de la geometría visual con la de la colisión de suelo (parte 6), muestra las variantes de la matriz y guarda imagen visual, de colisión y superpuesta (privadas) |
| `--map3ds-gen7-warps-probe` | `<carpeta extraída de Ultra Sol/Ultra Luna>` | solo lectura: resuelve el destino de todos los warps (zona → área → warp con ese evento) y cuenta destinos encontrados, retornos verificados, llegadas cercanas y transiciones |
| `--map3ds-gen7-npc-move-probe` | `<copia de Ultra Sol/Ultra Luna dentro de una carpeta de pruebas> <área> <n.º de NPC>` | **escribe en la copia**: busca una casilla vecina válida, previsualiza (sin escribir), mueve el NPC con el historial, reabre, comprueba que el resto del área y los demás miembros del GARC son idénticos, deshace (GARC idéntico) y deja el movimiento aplicado para la prueba jugable |
| `--map3ds-gen7-npc-move-rebuild` | `<copia con el movimiento> <imagen 3DS nueva> <carpeta nueva de reextracción> <área> <n.º de NPC> <x> <y>` | reconstruye la copia, verifica la imagen con la reextracción y comprueba que el NPC sigue en la casilla nueva |
| `--map3ds-gen7-ui-probe` | `<carpeta nueva> <carpeta extraída de Ultra Sol/Ultra Luna> <área> [diagnostico]` | solo lectura: abre el editor fuera de pantalla en un área real con la capa gráfica y los eventos, espera al render y guarda la captura (privada) |
| `--map3ds-gen7-render-probe` | `<carpeta nueva> <carpeta extraída de Ultra Sol/Ultra Luna> <mundo> <píxeles por bloque>` | solo lectura: dibuja un mundo con sus modelos y texturas reales (PNG privado), lee los modelos y texturas de todos los mundos y comprueba que los GARC de origen no cambian |
| `--map3ds-gen6-render-probe` | `<carpeta nueva> <carpeta extraída de X/Y u OR/AS> <zona> <píxeles por bloque>` | solo lectura: dibuja todos los bloques de una zona con sus modelos y texturas reales (PNG privado) y lee todos los modelos GR usados y todas las texturas de área de la copia |
| `--map3ds-gen6-ui-probe` | `<carpeta nueva> <carpeta extraída de X/Y u OR/AS> <zona> [graficos]` | solo lectura: abre el editor fuera de pantalla sobre una zona real (con `graficos`, activa la capa de modelos y espera al render) y guarda la captura (privada) |
| `--map3ds-npc-move-rebuild` | `<copia con el movimiento> <imagen 3DS nueva> <carpeta nueva de reextracción> <zona> <npc> <x> <y>` | reconstruye la copia (3DS completa), verifica la imagen con la reextracción y comprueba que el NPC sigue en la casilla nueva; no prueba el juego |
| `--map3ds-npc-move-probe` | `<copia de trabajo X/Y u OR/AS dentro de una carpeta de pruebas> <zona> <npc> <x> <y>` | **escribe en la copia**: previsualiza (sin escribir), mueve el NPC con el historial, reabre desde disco, comprueba que solo cambia su posición y que las demás zonas son idénticas, deshace (GARC idéntico al original) y deja el movimiento aplicado para la prueba jugable |
| `--map3ds-trainer-facing-probe-gen6` | `<copia> <zona> <npc> <grados> [alcance]` | copia X/Y u OR/AS dentro de una carpeta de pruebas: vista previa, guardado, reapertura, solo 0x0C–0x0F cambiados, demás zonas idénticas, deshacer y cambios externos; deja el cambio aplicado |
| `--map3ds-trainer-facing-probe-gen7` | `<copia> <área> <n.º de entrenador> <grados>` | copia USUM dentro de una carpeta de pruebas: igual que la anterior con restauración por miembro original; deja el cambio aplicado |
| `--map3ds-add-nurse-probe` | `<copia> <zona> <x> <y> [grados]` | copia X/Y u OR/AS dentro de una carpeta de pruebas: vista previa, guardado, reapertura con la enfermera (script 2100), resto de registros y partes idénticos, demás zonas idénticas, lista de modelos del área con la enfermera y las demás idénticas, deshacer de los dos archivos y cambios externos; deja la enfermera añadida. Con `x` = -1 solo lista la zona (o todas con zona -1) |
| `--map3ds-add-nurse-rebuild` | `<copia> <imagen> <reextracción> <zona> <x> <y>` | reconstruye la imagen 3DS y comprueba la enfermera y la lista de modelos en la reextracción; con `-` solo busca en la imagen el archivo de zonas y la lista de modelos editados byte a byte |
| `--map3ds-catch-zone-probe` | `<copia> <zona> <x1> <y1> <x2> <y2> <origen> [--sin-dejar]` | copia X/Y u OR/AS dentro de una carpeta de pruebas: vista previa, guardado, reapertura con las casillas y la tabla, solo esas casillas y esa zona cambiadas, editor de encuentros con una ubicación más si se creó la tabla, copia compartida EN de OR/AS idéntica a todas las zonas, deshacer de los dos archivos y editor con datos antiguos; deja el cambio aplicado salvo con `--sin-dejar` |
| `--map3ds-catch-zone-rebuild` | `<copia> <imagen>` | reconstruye la imagen 3DS y busca en ella byte a byte el archivo de zonas y el de bloques |
| `--map3ds-gen7-catch-zone-probe` | `<copia> <área> <x1> <y1> <x2> <y2> <origen>` | copia USUM dentro de una carpeta de pruebas: la hierba 0x1F del rectángulo recibe una región con la tabla de origen (área×1000+tabla); vista previa, guardado, reapertura, solo cambian los miembros de eventos y tablas del área, editor de encuentros, restauración exacta y editor con datos antiguos; deja el cambio aplicado |
| `--map3ds-gen7-grass-editor-probe` | `<copia> <área> <x> <y> <plantilla>` | copia de pruebas, a través del editor: panel, plantilla, clic, vista previa sin escritura (y captura PNG), guardado (solo el bloque destino), reapertura con la hierba, zonas de captura iguales, deshacer desde el panel byte a byte y reapertura sin la hierba |
| `--map3ds-gen7-grass-scan` | `<raíz> <área> <plantilla> <paso>` | solo lectura: centros donde la plantilla se puede colocar, por bloque, y motivos de rechazo |
| `--map3ds-gen7-grass-summary` | `<raíz> <áreas>` | solo lectura: resumen de hierba visible y encuentros de cada área (propia, de áreas vecinas, encuentros sin hierba) |
| `--map3ds-gen7-nurse-probe` | `<copia> <área> <x> <y> <dirección>` | escribe en una copia de pruebas una enfermera: vista previa, guardado, un NPC más, demás NPC intactos, modelo cargado, script 30571, solo eventos y personajes del área, historial, restauración exacta y editor con datos antiguos; deja la enfermera para la prueba jugable |
| `--map3ds-gen7-nurse-plan` | `<raíz> <área> <x> <y> <direcciones>` | solo lectura: plantilla de la enfermera y comprobación de una casilla en varias direcciones |
| `--map3ds-gen7-nurse-restore` | `<copia> <área>` | restaura los NPC y modelos del área antes de la última enfermera, si sus originales están guardados |
| `--map3ds-gen7-area-names` | `<raíz> <áreas>` | solo lectura: nombre de cada área en el editor |
| `--map3ds-gen7-reach-audit` | `<raíz> <áreas separadas por comas o all>` | solo lectura: NPC del juego cuya zona de conversación es alcanzable a pie desde los warps (regla de paredes de la enfermera) |
| `--map3ds-gen7-visible-grass-probe` | `<copia> <área> <x> <y> <plantilla> <suelo> <fuente>` | escribe en una copia de pruebas una mancha de hierba original (bloque plantilla de una malla) centrada en una casilla: vista previa, guardado, solo cambia el bloque destino, deshacer del historial byte a byte, editor con datos antiguos; con suelo (hex) añade la zona de captura bajo la hierba (experimental si ninguna región original usa ese suelo solo) |
| `--map3ds-gen7-image-blocks` | `<copia> <imagen>` | busca en la imagen 3DS, byte a byte, el archivo de bloques (a/0/8/6) de la copia |
| `--map3ds-gen7-grass-models` | `<raíz> <bloque o -1>` | solo lectura: mallas del modelo de hierba de un bloque (o resumen de todos) con su extensión y formato |
| `--map3ds-gen7-grass-verify` | `<raíz>` | solo lectura: todos los bloques se reconstruyen byte a byte y todos los modelos de hierba se desplazan y se releen |
| `--map3ds-gen7-grass-plan` | `<raíz> <área> <x> <y> <plantilla>` | solo lectura: plantillas con texturas del área y plan de una mancha (bloque, desplazamiento, casillas, suelos y eventos debajo) |
| `--map3ds-gen7-ground-map` | `<raíz> <área> <x1> <y1> <x2> <y2>` | solo lectura: mapa de atributos de suelo de un rectángulo con NPC y warps |
| `--map3ds-gen7-catch-zone-ui-probe` | `<proyecto> <nombre>` | solo lectura: abre el proyecto en el editor de mapas y recorre el panel de zona de captura de las áreas con ese nombre (casillas disponibles, con encuentros y vista previa) |
| `--map3ds-gen7-catch-zone-rebuild` | `<copia> <imagen>` | reconstruye la imagen 3DS y busca en ella byte a byte el archivo de campo editado |
| `--map3ds-gen7-encounter-research` | `<raíz> <salida.tsv>` | investigación de solo lectura: tablas, atributos de suelo y partes de ED por área, y para cada región los triángulos que cubre |
| `--map3ds-gen7-encounter-triangles` | `<raíz> <área> <atributo>` | solo lectura: casillas con triángulos de un atributo |
| `--map3ds-gen7-encounter-verify` | `<raíz>` | solo lectura: reconstrucción byte a byte de ED y EA en todas las áreas y escritura en memoria en Pueblo Lilii |
| `--map3ds-gen7-encounter-overlap` | `<raíz> <área> <otra>` | solo lectura: matrices de las dos áreas, bloques compartidos y, por mancha de hierba de la primera, sus encuentros en la otra |
| `--map3ds-gen7-encounter-candidates` | `<raíz>` | solo lectura: manchas de hierba donde el editor permite crear una zona de captura, por área |
| `--map3ds-gen7-encounter-box-scale` | `<raíz>` | solo lectura: cobertura de cada caja de hierba leyendo su tamaño como ancho completo o como semieje |
| `--map3ds-gen7-encounter-mask-bits` | `<raíz> <área>` | solo lectura: atributos que usan las máscaras originales y suelos del terreno propio de un área |
| `--map3ds-gen7-encounter-attribute-candidates` | `<raíz> <atributos>` | solo lectura: por suelo, regiones que lo usan y manchas libres en terreno propio de cada área |
| `--map3ds-gen7-encounter-area-info` | `<raíz> <área> <atributo>` | solo lectura: matriz, eventos, regiones con las casillas que cubren y manchas libres del área |
| `--map3ds-gen7-encounter-reach` | `<raíz> <área> <atributo> <escalón>` | solo lectura: casillas de regiones y manchas libres sobre suelo conectado con los warps (indicio de que se pueden pisar) |
| `--map3ds-gen7-encounter-plan` | `<raíz> <área> <x1> <y1> <x2> <y2>` | solo lectura: casillas de hierba del rectángulo y comprobación de la zona de captura |
| `--map3ds-trainer-facing-rebuild` | `<copia> <imagen> <reextracción> <zona/área> <npc/n.º> <grados> [alcance]` | reconstruye la imagen 3DS, la reextrae y comprueba la orientación (y el alcance en Gen6). Con `-` en lugar de la carpeta de reextracción (sin espacio para una reextracción completa) solo comprueba que la imagen contiene byte a byte el archivo de campo editado |
| `--map3ds-gen6-facing-probe` | `<carpeta extraída>` | evidencia de la orientación Gen6: para cada valor 0–7, proporción de casillas de la línea de visión iguales a la del entrenador en cada dirección; solo lectura |
| `--map3ds-gen7-event-dump` | `<carpeta extraída USUM> <CSV>` | volcado de los registros de NPC (120 B) y entrenador (84 B) de todas las áreas, con sus formas y el archivo de zona del entrenador; solo lectura |
| `--map3ds-gen6-probe` | `<carpeta nueva> <carpeta extraída de X/Y u OR/AS>` | solo lectura: lee todas las zonas, matrices y bloques GR de una copia, cuenta eventos por tipo y su posición respecto a la matriz, y comprueba que los GARC de origen no cambian |
| `--nds-event-editing-probe` | `<carpeta nueva> <ROM privada Platino CPUE/1>` | edita un warp, un objeto y un activador de Hojaverde, guarda (v5), reabre, exporta, reabre la ROM exportada y restaura; escribe `MANUAL.txt` |
| `--nds-gen4-map-editor-probe` | `<carpeta nueva> <ROM privada Diamante ADAE/5>` | copia la fuente; comprueba huellas, warps, terreno, estructuras y gráficos; en Hojaverde gira un NPC, cambia el destino del warp 0, mueve un objeto de fondo y amplía un activador, y gira un entrenador de la Ruta 202; guarda, reabre, exporta, reabre la ROM exportada, restaura y compara SHA-256; escribe `MANUAL.txt` |
| `--nds-gen4-hgss-measure` | `<ROM privada HeartGold IPKE/0>` | solo lectura: mide los CRC32 de cada tabla, las tres huellas de eventos y el censo (mapas, NPC, Poké Ball, warps, entrenadores, máximos) y los compara con el perfil |
| `--nds-gen4-hgss-probe` | `<carpeta nueva> <ROM privada HeartGold IPKE/0>` | mide como la anterior; abre terreno, estructuras y gráficos; en Pueblo Primavera cambia el destino de la puerta de la casa del suroeste y amplía el activador de la salida oeste; gira un NPC «mirar fijo» de la Ruta 29; en la Ruta 30 mueve un objeto oculto y gira un entrenador; comprueba que una Poké Ball no se mueve y que no se crean NPC; guarda, reabre, exporta, reabre la ROM exportada, compara ARM9 y el resto de archivos, restaura y compara SHA-256; escribe `MANUAL.txt` |
| `--nds-gen5-black-probe` | `<carpeta nueva> <ROM privada Negro IRBO/0>` | perfil y censo; terreno y gráficos; en Pueblo Arcilla una puerta de casa vecina lleva a la otra; en la Ruta 1 gira un NPC y un entrenador «mirar fijo» y mueve un objeto oculto; comprueba que una Poké Ball no se mueve y que no se crean NPC; amplía y restaura un activador; guarda, reabre, exporta, reabre la ROM exportada, compara ARM9 y el resto de archivos, restaura y compara SHA-256; escribe `MANUAL.txt` |
| `--nds-gen5-black-view-probe` | `<carpeta nueva> <ROM privada Negro IRBO/0>` | abre el editor real fuera de pantalla con la copia (terreno y capa gráfica), selecciona un entrenador de la Ruta 1, previsualiza su giro sin guardar y escribe una captura PNG |
| `--nds-gen5-white-probe` | `<carpeta nueva> <ROM privada Blanco IRAS/0> <ROM privada Negro IRBO/0>` | compara las tablas de mapa con Negro (idénticas) y los scripts (propios); censo, terreno y gráficos; en Pueblo Arcilla la casa vecina de la derecha lleva a la de la izquierda; gira un entrenador de la Ruta 1 y un NPC de Pueblo Terracota; mueve un objeto oculto de la Ruta 3; rechaza Poké Ball y NPC nuevos; amplía y restaura un activador; guarda, reabre, exporta, reabre, compara ARM9 y archivos, restaura con SHA-256 idéntico; escribe `MANUAL.txt` |
| `--nds-gen5-buildings-probe` | `<carpeta nueva> <ROM privada Negro IRBO/0, Blanco IRAS/0 o Blanco 2 IRDO/0>` | solo lectura, sin copiar la ROM (SHA-256 comparado antes y después): censo de edificios de todos los bloques de mapa, avisos, triángulos sin textura, casillas bloqueadas bajo los edificios frente al total, warps que tocan un edificio, cachés acotadas; PNG comparativos sin/con edificios, con eventos y con las cuatro estaciones (privados) |
| `--nds-gen5-white2-probe` | `<carpeta nueva> <ROM privada Blanco 2 IRDO/0>` | censo y apertura de las 615 matrices; terreno y gráficos; en Ciudad Engobe una casa lleva a otra y un NPC sin movimiento mira arriba; en la Ruta 20 gira un entrenador y mueve un objeto oculto; edita y restaura un activador y un NPC del mapa 593; rechaza Poké Ball y NPC nuevos; guarda, reabre, exporta, reabre, compara ARM9 y archivos, restaura con SHA-256 idéntico; escribe `MANUAL.txt` |
| `--nds-map-render-probe` | `<carpeta nueva> <ROM privada Platino CPUE/1>` | solo lectura: decodifica todas las texturas de mapa y de edificios, lee los 666 modelos de terreno y los 590 de edificios, comprueba los materiales de Hojaverde y guarda un atlas y el render cenital del bloque (privados) |
| `--nds-trainer-vision-probe` | `<carpeta nueva> <ROM privada Platino CPUE/1>` | copia la fuente; comprueba las huellas y el censo de entrenadores; gira un entrenador «mirar fijo» de la Ruta 202 y amplía su visión, guarda (checkpoint v4), reabre, exporta, reabre la ROM exportada en el editor, restaura y compara SHA-256; escribe `MANUAL.txt` |
| `--nds-gen5-items-probe` | 2 o más |  |
| `--nds-gen5-random-probe` | 2 o más |  |
| `--nds-gen5-starters-probe` | 2 o más |  |
| `--nds-gen5-w2-probe` | exactamente 2 |  |
| `--nds-romhack-probe` | 2 o más |  |
| `--nds-roundtrip-probe` | 2 o más |  |
| `--nds-ui-probe` | exactamente 2 |  |
| `--pokefeur-probe` | exactamente 3 |  |
| `--read-facilities` | 1 o más |  |
| `--story-probe` | 1 o más |  |
| `--trainer-dump` | exactamente 2 |  |
| `--template-load-probe` | exactamente 3 |  |
| `--read-field6` | 1 o más |  |
| `--read-hm6` | 1 o más |  |
| `--read-machine7-recipes` | 1 o más |  |
| `--read-only-roms` | 1 o más |  |
| `--read-static-profiles` | 1 o más |  |
| `--read-text-classification` | 1 o más |  |
| `--read-title-movies` | 1 o más |  |
| `--read-titles6` | 1 o más |  |
| `--read-tm6-compatibility` | 1 o más |  |
| `--relearner-real` | exactamente 3 |  |
| `--requested-real` | 1 o más |  |
| `--review73-real` | 1 o más |  |
| `--single-purchase-backend` | exactamente 1 |  |
| `--single-purchase-copies` | exactamente 2 |  |
| `--sword-surprise-audit` | exactamente 1 |  |
| `--sword-surprise-probe` | exactamente 2 |  |
| `--swsh-divine-probe` | exactamente 1 |  |
| `--swsh-expansion-probe` | exactamente 2 |  |
| `--swsh-gameplay-probe` | exactamente 2 |  |
| `--swsh-global-probe` | exactamente 2 |  |
| `--swsh-parity-probe` | exactamente 1 |  |
| `--swsh-polish-probe` | exactamente 1 |  |
| `--swsh-probe-mod` | exactamente 2 |  |
| `--swsh-random-probe` | exactamente 3 |  |
| `--swsh-read-only` | exactamente 1 |  |
| `--swsh-table-probe` | exactamente 1 |  |
| `--swsh-version-probe` | exactamente 2 |  |
| `--swsh-visual-preview` | exactamente 2 |  |
| `--template-feedback-copies` | 1 o más |  |
| `--template-studio-shops-copies` | 1 o más |  |
| `--verify-existing-rebuild` | exactamente 3 |  |

## Suites incluidas en el CI

Eventos DS fase 1: `--nds-events-only` ejecuta los casos sintéticos de mapas, archivos de eventos, límites, scripts, preservación de bytes y checkpoints inmutables. Sus casos se incluyen en `--nds-only`. `--nds-events-ui-only` comprueba la vista WPF fuera de pantalla y escribe capturas de fixtures en `bin/.../nds-events-ui/`; no acredita interacción real ni juego. El CI ejecuta ambas rutas mediante `--nds-only` y `--nds-events-ui-only`.

`--nds-events-probe <salida nueva> <ROM privada Platino CPUE/1>` copia la fuente a la carpeta nueva y verifica guardado/checkpoint, reapertura, reconstrucción, restauración y bytes ajenos. Prepara `MANUAL.txt` y una ROM aislada de prueba. No ejecuta un emulador ni acredita jugabilidad. No pasar una ROM de trabajo o guardado como salida; la carpeta debe ser nueva.

`.github/workflows/3ds-progressive-ci.yml` ejecuta las opciones marcadas "sí" y después `--synthetic-only`. Las comprobaciones con datos reales no pueden correr en CI porque no hay ROMs disponibles.

### Mentas fase 2

`--usum-mints-distribution-only`: pools opcionales, tiendas y BP, EI/EB, Pickup, dependencias, recetas Global/Batch, determinismo y deshacer sobre fixtures sintéticos US/UM. No acredita validación jugable.

`--usum-mints-distribution-real-copies <salida nueva> <Ultra Sol fase 1.1> <Ultra Luna fase 1.1>`: copia ambos proyectos privados, ejecuta la receta combinada con el motor Global/Batch real y verifica reapertura y conservación de BP, precios e iconos. No reconstruye ROM ni acredita juego en emulador.

Matrices DS, fase visual 1: `--nds-map-layout-only` valida secciones opcionales, propiedad de celdas, coordenadas de 32×32, límites y lectura inmutable. `--nds-map-layout-probe <informe JSON nuevo> <ROM privada Platino CPUE/1>` exige el perfil acreditado, lee las 289 matrices y 593 contextos de cabecera, comprueba el NPC 0 de T01 y compara el SHA-256 de la fuente y todos los bytes del archivo de eventos antes/después. El directorio padre del informe debe existir; el archivo debe ser nuevo. No crea sesiones, checkpoints ni ROMs. Véase [NDS_MAP_LAYOUT_PHASE1.md](NDS_MAP_LAYOUT_PHASE1.md).

Lienzo DS de solo lectura, fase visual 2: `--nds-map-canvas-only` valida el modelo espacial (conversión tile/píxel, zoom, desplazamiento, ajuste, hit-test, selección, celdas explícitas/contextuales/ajenas y eventos fuera de la máscara o de la matriz). `--nds-map-canvas-ui-only` abre la vista Eventos con una ROM sintética tipo 411 (matriz 30×30, una celda explícita), comprueba sincronización tabla↔lienzo, cambio de mapa, modos, capas, accesibilidad, tiempo de dibujo, ausencia de cambios en la sesión y la fuente, y que previsualizar/guardar siguen funcionando; guarda capturas en `nds-map-canvas/` junto al ejecutable. Ninguna sustituye una prueba en el juego. Véase [NDS_MAP_CANVAS_PHASE2.md](NDS_MAP_CANVAS_PHASE2.md).

Terreno DS, fase visual 3: `--nds-map-terrain-only` valida el lector de `land_data` (orden fila-mayor z×32+x, solo el bit 15 como colisión, validación estricta de cabeceras y longitudes, celdas vacías o fuera de tabla = desconocido, perfil CPUE/1 con CRC32), la consulta espacial (Permitido / Bloqueado / Indeterminado con procedencia y límites), matrices compartidas entre cabeceras, el caso 411 y el rendimiento. `--nds-map-terrain-ui-only` abre la vista con y sin datos de terreno, comprueba la capa, la leyenda, el inspector, el dibujo con la capa activada y desactivada, el tiempo de dibujo de 960×960 y que previsualizar y guardar siguen funcionando; guarda capturas en `nds-map-canvas/`. `--nds-map-terrain-probe <informe JSON nuevo> <ROM privada Platino CPUE/1>` exige el perfil, lee los 666 miembros de `land_data`, comprueba el caso 411 y la alineación con los eventos reales y el SHA-256 de la fuente antes y después. No crea sesiones, checkpoints ni ROMs. Véase [NDS_MAP_TERRAIN_PHASE3.md](NDS_MAP_TERRAIN_PHASE3.md).

Mover NPC DS, fase visual 4: `--nds-npc-drag-only` valida la política de bloqueo/advertencia (colisión, límites del editor, casilla ocupada, otra cabecera, rango de activador, sin datos), la conversión píxel→tile con distintos zoom y desplazamientos y que el arrastre no muta eventos. `--nds-npc-drag-ui-only` abre la vista con una ROM sintética tipo 411 y comprueba herramienta Seleccionar/Mover NPC, arrastre, fantasma, cancelación (Esc, herramienta, mapa, modo), propuesta antes/después, aceptación mediante la previsualización existente, checkpoint con la posición arrastrada, restauración de los bytes originales, archivos de eventos compartidos y que durante el arrastre no hay escrituras. Guarda capturas en `nds-map-canvas/`. No sustituye la prueba en el juego. Véase [NDS_NPC_DRAG_PHASE4.md](NDS_NPC_DRAG_PHASE4.md).

NPC estructurales DS, fase 5A: `--nds-npc-structure-only` valida la construcción del registro de 32 bytes, las reglas (ID libre, gráfico y movimiento acreditados, límites, tope de 51), la reconstrucción del NARC con todos los demás miembros idénticos, el checkpoint versión 2 (y que la versión 1 no cambia), documentos inválidos, propuestas obsoletas, fallos de escritura, concurrencia, exportación con `NdsRom.Save` y la política de pegar/cortar/eliminar. `--nds-npc-structure-ui-only` abre la vista con una ROM sintética tipo 411 y recorre botones, colocación, formulario, propuesta, guardado y restauración, pegar/cortar, rechazo de eliminar, cancelaciones y archivo compartido; guarda capturas en `nds-map-canvas/`. `--nds-npc-structure-probe <carpeta nueva> <ROM privada Platino CPUE/1>` hace el recorrido real añadir → checkpoint v2 → exportar → reabrir → restaurar sobre una copia, compara cada archivo ajeno y el SHA-256 de la fuente. No sustituye la prueba en el juego. Véase [NDS_NPC_STRUCTURAL_PHASE5A.md](NDS_NPC_STRUCTURAL_PHASE5A.md).

NPC con comportamiento: `--nds-npc-script-only` valida la lectura de la tabla de scripts (0xFD13), el reconocimiento exacto de la enfermera y de los mensajes simples, la ampliación de un archivo de scripts sin alterar los demás, el checkpoint versión 3, la exportación de eventos y scripts, la combinación con scripts de otros módulos y el rechazo de operaciones no acreditadas. `--nds-npc-script-ui-only` recorre en la vista el pegado de la enfermera en otro mapa (comportamiento preseleccionado y dependencias), la duplicación de un NPC de mensaje (script compartido) y la restauración. `--nds-npc-script-probe <carpeta nueva> <ROM privada>` hace el recorrido real con la enfermera de Jubileo en Pueblo Hojaverde y con «Añadir enfermera» en la Ruta 201 ([NDS_ANADIR_ENFERMERA_A1.md](NDS_ANADIR_ENFERMERA_A1.md)). No sustituye la prueba en el juego. Véase [NDS_NPC_SCRIPTS_PHASE6.md](NDS_NPC_SCRIPTS_PHASE6.md).

Corrección de NPC estructurales: `--nds-live-ui-probe <ROM privada> <carpeta nueva> <ancho> <alto>` reproduce con entrada real de Windows el flujo del propietario (buscar T01, duplicar con fantasma, menú contextual «Pegar aquí», copiar desde otro mapa, eliminar un NPC añadido con Supr, exportar y restaurarlo todo) sobre una copia de la ROM, guarda capturas y un registro en la carpeta nueva y comprueba que la fuente no cambia. Es una prueba de diagnóstico local; las comprobaciones automáticas siguen siendo `--nds-npc-structure-only` y `--nds-npc-structure-ui-only`.

Estructuras DS, fase 5B: `--nds-map-props-only` valida el lector de la sección de *map props* de `land_data` (48 bytes por entrada, coma fija 20.12, origen en el centro del bloque), la entrada parcial que el motor ignora, la validación estricta de archivos, el perfil CPUE/1, la colocación en una matriz (celdas vacías, bloques compartidos, cabeceras) y la capa del modelo. `--nds-map-props-ui-only` comprueba la capa «Estructuras» (y que «Modelos/texturas» figura como no disponible), la selección con procedencia y límites, que al seleccionar una estructura el evento deja de estar seleccionado y Copiar/Cortar/Duplicar/Eliminar no pueden actuar sobre él, la prioridad de los eventos, la ausencia de datos y el rendimiento. `--nds-map-props-probe <informe JSON nuevo> <ROM privada Platino CPUE/1>` analiza los 666 miembros reales, contrasta el origen y los ejes con la colisión y comprueba el caso 411; compara el SHA-256 de la fuente. `--nds-map-props-ui-probe <carpeta nueva> <ROM privada Platino CPUE/1>` abre el editor de eventos real, fuera de pantalla, sobre una copia en esa carpeta: capa en 411 · T01, clic en una estructura del bloque (3,27), operaciones de NPC desactivadas, Supr sin efecto, capturas a 1280, 1100 y 760 y SHA-256 de la fuente y de la copia sin cambios. No lee modelos NSBMD ni texturas. Véase [NDS_MAP_PROPS_PHASE5B.md](NDS_MAP_PROPS_PHASE5B.md).
