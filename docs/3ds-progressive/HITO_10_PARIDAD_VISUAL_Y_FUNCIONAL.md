# Hito 10 — Paridad real con pk3DS vanilla y Progressive

## Principio

La aplicación WPF es una migración del producto completo, NO un randomizador ligero. Se prohíbe marcar un módulo como completo solo porque abra una ventana o reutilice un motor. Cada opción de WinForms y cada extensión Progressive debe existir, guardar correctamente y quedar incorporada a la vista previa y al historial. Se respetan las diferencias de XY, ORAS, SM y USUM.

Las capturas comparativas de pk3DS/Progressive proporcionadas el 11/oct/2026 constituyen la referencia funcional. En WPF se conservará la identidad Dark Graphite: ningún TabControl, TabItem, popup, diálogo, tabla ni panel debe mostrar fondos blancos del tema Windows. Las etiquetas deben ser claras y prioritariamente en español.

## Inventario mínimo derivado de los editores originales

| Área | Funciones vanilla y Progressive exigidas | WPF al empezar este hito |
| --- | --- | --- |
| Entrenadores — Pokémon | Equipo de seis con sprites y formas; especie, nivel, habilidad, objeto, naturaleza, shiny, género; datos individuales | Campos portados; tarjetas con sprites iniciales |
| Entrenadores — Stats | IV, EV, stat/Hidden Power cuando proceda, totales y valores compatibles con la generación | Gen7 IV/EV; UX de stats y ayudas de potencia por verificar |
| Entrenadores — Moves | Cuatro movimientos por integrante; Clear; selección por nivel actual y nivel alto; Move Source; potencia mínima, ofensivos, STAB y exclusión de daño fijo | Campos de movimientos y motor importado; ayudas de edición y reglas globales sin paridad probada |
| Entrenadores — Trainer | Nombre, clase, dinero, objetos de combate, flags, máscara de IA por bits y modo de combate; edición al seleccionar entrenador | Campos base; máscara IA sin desglose visual; nombre editable por validar |
| Entrenadores — Randomización | Generaciones, legendarios normales/evento, random shiny y %, BST similar/Progressive, límites BST custom, especies/formas, número de equipo, trainers importantes, Mega, postgame, IA, tipos, habilidades, clases, doubles | Motor importado + cinco pestañas WPF; falta contraste exhaustivo de todos los controles |
| Entrenadores — Rules | Level Caps globales/por historia/por entrenador, curvas y predecesores; rules por entrenador; Better Movesets, MT, categorías de entrenadores, Strong Stat/tolerancia; Smart Items/categorías/calidad, objeto baneado, Item Clause, Max IV/AI, templates | Solo selección individual y captura JSON; gestor WPF avanzado de reglas de múltiples entrenadores pendiente |
| Encuentros salvajes | Ubicación, tablas día/noche, niveles, rates, slots iniciales, SOS 1–7, aliados clima; sprites/grillas y comparación día/noche; copiar/pegar tabla y área, exportar | Edición de registros y SOS/clima con cuatro parámetros random; sin grilla visual ni acciones de copia |
| Encuentros — random | Gen 1–7, legendarios normales/evento, Mega, similar BST, rangos Progressive BST, opciones de slots/SOS, aliado universal, escalado plano/%/conservar rango, randomize y template Wild JSON | Pendiente: reutilización de Wild7Randomizer/ProgressiveWildRandomizer con paridad de UI |
| Static Encounters | Gifts, Encounters, Trades, iniciales, Totem/aura, random habilidades y movimientos, objetos, Megas, flags, shiny lock, tipos, evolución forzada y legendarios; bloqueos de regalos/intercambios | Sin editor WPF; acciones Progressive de Totem Level Caps/BST, baseline, trade accept-any y ocultar nombres pendientes |
| MT | Edición de asignaciones, Randomize, TM Sanity y Follow Evolutions | Pendiente |
| Tutores | Asignaciones/ubicación/precios, Randomize, Free, Tutor Sanity y Follow Evolutions | Pendiente |
| Tiendas y BP | Editar localización, productos, precios; Randomize, Add, Delete, caramelos raros, vitaminas EV, Free Mega, prohibir cápsulas, exclusión de ranuras speedrun, restricciones Persistent | Pendiente |
| Player Level Caps | Presets, cap final, 21 hitos USUM, editar secuencia, sincronizar Trainer Caps, cargar/guardar, defaults, parche CRO y verificación de hooks | Pendiente |
| Shiny Rate | PID Generation Loop, probabilidades/cantidad rerolls, Re-roll Helper, Everything Shiny, restauración del original | Pendiente |
| Tabla de tipos | Matriz completa de efectividades, indicadores, selección por celda y guardado | Pendiente |
| Relearner USUM | Editor/parche y estado de compatibilidad comprobado en juego | Pendiente; NO declarar funcional sin prueba runtime |
| Log | Registro visible y acciones de guardado/errores; no modificar el historial estructurado existente | Pendiente |
| Evoluciones | Rutas, métodos, formas/niveles, templates CSV y normalización | Conservar implementación validada; revisar paridad futura |
| Historial | Cambios de edición individuales, operaciones masivas con parámetros y hashes, portable y futuras instrucciones Global Template | Conservar versión 2 y mantener cobertura de las nuevas acciones |

## Corrección visual aplicada al inicio del hito

- Estilos globales de WPF para `TabControl` y `TabItem` con fondos de `Bg/Panel/Surface`, bordes `Stroke`, selección `Selected/Accent` y estados deshabilitados del tema.
- Ya no se delega la presentación de las pestañas de Entrenadores al tema blanco de Windows.
- Regresión WPF y captura `entrenadores-bst-dark.png` para confirmar la aplicación del tema.

## Criterios de aceptación por módulo

1. Auditoría de controles y comportamiento de vanilla **y** Progressive, diferenciada por generación.
2. No ocultar opciones de motor, ni reemplazarlas con controles genéricos que cambien su significado.
3. Defaults/activaciones/dependencias, templates y guardado coherentes.
4. Edición visual con sprites locales, formas y accesibilidad adecuada.
5. Randomizar con vista previa congelada y alcance controlado; aplicar solo tras confirmación.
6. Historial de acciones masivas registra **opciones/reglas**, no solo resultados o semillas.
7. Roundtrip binario / compatibilidad de ROM y pruebas manuales; compilación Windows y capturas.
8. No hacer merge estable hasta validar las funciones migradas; los módulos no migrados deben permanecer explícitamente pendientes.

## Secuencia propuesta

- **Primera prioridad:** cerrar todas las opciones y reglas avanzadas de Entrenadores (incluyendo gestor general de caps/moves y ayudas).
- **Segunda:** editor de Encuentros con grillas y sprites + randomización Wild completa.
- **Tercera:** Static Encounters (Gifts/Trades/Totems/Starters) y su randomización.
- **Cuarta:** MT/Tutores, economía/tiendas, Player Caps y herramientas/editores restantes.
- **Transversal:** tema oscuro, navegación limpia, errores, rendimiento, historial y equivalencia contra pk3DS original.

Esta lista es una especificación de aceptación, no una declaración de que todo esté implementado.
