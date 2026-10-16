# Hito 12 — Paridad por módulos y correcciones visuales WPF

Rama de trabajo: `feature/3ds-progressive-ui`. Se conserva la base de pk3DS Progressive; no hay merge a la rama estable.

## Correcciones integradas en este hito

| Área | Estado | Comportamiento actual | Límites |
|---|---|---|---|
| Tablas Level Caps / Trainer Move Rules | Implementado en WPF | Vista a ancho completo **dentro de RecordEditorView**. Filtros independientes Important/Boss y datos del motor original. Aceptar vuelve al editor y modifica el template en memoria | La validación en ROM extraída real sigue pendiente |
| Estética de tablas | Implementado | Root oscuro explícito en paneles de tablas, WPF DataGrid en lugar de ventana modal blanca | Comprobar fuentes, scroll y contraste visual en varias resoluciones |
| BST progresivo de entrenadores | Implementado | Tabla de filas dinámicas. Añadir divide un rango; quitar fusiona. Validación 1–100 y límites BST; exporta todas las filas al template Progressive sin limitarse a 6 | Verificar todos los cambios de template en ambos juegos |
| BST progresivo de encuentros | Implementado | Tabla equivalente; el motor Wild obtiene todos los intervalos vigentes para randomizar | Todavía no replica todos los parámetros de ProgressiveWildRandomizer |
| MT y tutores | Mejora visual | Columnas de movimiento/precios utilizan el ancho disponible, sin grandes zonas vacías | Tutor moves no se modifican hasta portar el patch de sincronización USUM |
| Level Up Moves | Implementado (editor base) | Navegación activa Gen6/Gen7, aprender por nivel, añadir/quitar filas, guardar GARC de tamaño variable, Metrónomo, randomización sencilla por especie y exportación CSV | Faltan opciones vanilla de randomización: Allow HMs, Bias by Type / STAB %, Expand Pool, Start with 4, Spread Evenly, Stop Learning y No Fixed Damage, más bulk randomizer |
| Objetos | Implementado (editor base) | Todas las entradas del GARC Item con precio, bolsas, efectos, EV, boosts; transacción e historial | Faltan más campos de propiedades, Persistent Items y Export Table parity |
| Economía | Implementado (tabla) | Tabla in-app UPR-ZX Gen7 con 959 precios originales, buscar, incluir/excluir, editar objetivo, calcular venta, preview, aplicar transaccionalmente | La lista depende de la cantidad real de ítems del juego; scripts de ofertas y tiendas no están incluidos |
| Historia y textos | Implementado (editor base) | Navegación activa, listado de archivos gametext, editar líneas, exportar TXT; códec TextFile original, validación roundtrip y GARC variable con historial | No cambia el número de líneas, no implementa edición de scripts/control flow ni localización cruzada |
| Field Items | Implementado (Gen7 EI/EB) | Escanea ED/EI/EB en encdata. Edición individual, CSV, scope y shuffle con preview e historial; GARC Lazy para tocar solo áreas modificadas | Gen6 Scripts + code.bin, filtros completos y RandomPool con templates aún pendientes |
| Evoluciones e historial | Conservado | Sin regresiones intencionales ni merge prematuro | Continuar pruebas de no regresión |

## Funciones originales que todavía faltan en la interfaz

| Prioridad | Herramienta vanilla/Progressive | Fuente del repositorio | Acción necesaria |
|---|---|---|---|
| Alta | Poké Mart y BP Items; Expanded Marts / Rare Candies / EV Items / Free Mega / speedrun | `pk3DS.WinForms/Subforms/Gen7/MartEditor7*` | Editor nativo de tienda y precio + guardado CRO/GARC verificado |
| Alta | Completar Level Up randomizer | `pk3DS.WinForms/Subforms/Gen6/LevelUpEditor6.cs`, `Gen7/LevelUpEditor7.cs`, `pk3DS.Core/Randomizers/LearnsetRandomizer.cs` | Portar todos los checks originales y randomización masiva reproducible |
| Alta | Egg Moves | `pk3DS.WinForms/Subforms/Gen6/EggMoveEditor6.cs`, `Gen7/EggMoveEditor7.cs` | Editor completo de movimientos huevo |
| Alta | Static Gen7 avanzado / Totems / Trades | `pk3DS.WinForms/Subforms/Gen7/StaticEncounterEditor7*.` | Starter BST baseline, Totem BST, Totem Level Caps, Totem Aura, shiny locks, aceptar cualquier especie con patch real, diálogo trade y regalo |
| Alta | Wild visual completo | `pk3DS.WinForms/Subforms/Gen7/SMWE*.`, `Wild/ProgressiveWildRandomizer.cs` | Mapa día/noche, todos los sprites, tasas y columnas SOS, clima, copiar/paste, plantillas Wild y formas especiales |
| Alta | Tiendas y tutores USUM | `pk3DS.WinForms/Subforms/Gen7/TutorEditor7.cs`, `Misc/USUMTutorCodeSyncPatcher.cs` | Editar movimientos tutores sincronizando Shop.cro y code.bin, tutor sanity y follow evolutions |
| Media | Player Level Caps y Sync Trainer Caps | `pk3DS.WinForms/Subforms/Gen7/LevelCapManager7.cs`, `LevelCapEditor.cs`, `Main.LevelCaps.cs` | UI player caps y hooks de code.bin |
| Media | Type Chart Editor | Editor de tabla de tipos de pk3DS vanilla | Tabla visual con tipos, inmunidades/resistencias |
| Media | Shiny Rate | Editor shiny de pk3DS vanilla | Edición/restore de PID rerolls y compatibilidad del ejecutable |
| Media | Campo Items Gen6 | `pk3DS.WinForms/Misc/FieldItemDumper.cs` | Portar localizadores ORAS/XY de scripts, hidden code y pools |
| Media | Item Persistent / Export Table | `pk3DS.WinForms/Subforms/Gen7/ItemEditor7.PersistentConsumables.cs` | Integrar patches específicos de ítems y exportación |
| Media | USUM Relearner | `pk3DS.WinForms/Misc/USUMMoveRelearnerPatcher.cs` | Validar runtime antes de exponer patch al usuario |
| Media | Mecánicas, CRO y recetas | `pk3DS.WinForms/Main.*`, `pk3DS.Core/Modding/Research` | Herramientas con protección de versionado/offsets, tests de parches y rollback |
| Media | Story/script editing | `pk3DS.WinForms/Subforms/TextEditor.cs`, scripts | Vista de scripts y variables conservando códigos de control |
| Baja | Utilidades adicionales (Randomize All, dumper general, presets) | Botones y menús `pk3DS.WinForms/Main` | Revisión funcional de paridad menú por menú |

## Reglas de calidad para los siguientes hitos

1. No mostrar botones que guarden parcialmente un patch (por ejemplo, cambiar el ID de trade a 0 sin habilitar el selector Accept Any, o modificar un tutor sin sincronizar code.bin).
2. No presentar la existencia del motor original como si ya hubiese paridad de interfaz.
3. Todas las tablas dentro del layout principal; no crear ventanas modales blancas para editores masivos.
4. Cada cambio binario pasa por fingerprint, preview cuando es masivo, confirmación, historial y relectura/verificación.
5. Añadir regresiones Gen6/Gen7 y pruebas visuales WPF antes de considerar cada módulo validado. Probar runtime Ultra Sun/Ultra Moon antes del merge.

## Pruebas de este hito

`3DS.Progressive.Tests/ModuleChecks.cs` y `UiSmoke.cs` contienen pruebas de escritura/lectura de learnsets, texto, objetos, Field Items, economía, navegación y tablas BST dinámicas. El estado final depende del workflow Windows de la rama, no de un build local no realizado.
