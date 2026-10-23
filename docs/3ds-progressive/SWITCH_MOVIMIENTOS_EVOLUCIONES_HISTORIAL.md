# Switch: movimientos, MT/TR, Smart Items, evoluciones e historial

Fecha: 23/10/2026. Rama de trabajo: `feature/switch-swsh-prototype`.

## Causa del bloqueo y comportamiento corregido

La potencia mínima 0 no imponía un mínimo. El bloqueo procedía de una comprobación conjunta que rechazaba el equipo cuando no quedaban ataques ofensivos después de filtrar los aprendizajes por nivel, categoría o permiso de estado. Una especie elegida por randomización puede no aprender ningún ataque ofensivo al nivel 3; otra puede tener ataques de la categoría descartada por la estadística más fuerte.

El generador conserva las preferencias cuando son realizables y emplea esta alternativa con aviso cuando no hay ataque ofensivo:

1. Relaja potencia mínima y categoría, conservando ataques aprendidos al nivel actual.
2. Si no existen, permite el primer nivel con aprendizaje ofensivo de esa especie. Esto puede adelantar un ataque respecto al juego normal; no cambia el nivel del Pokémon.
3. Si solo quedan movimientos de estado, permite conservarlos aunque la preferencia de estado estuviera desactivada.
4. Sin aprendizajes utilizables, puede usar el movimiento de emergencia #165 si existe, está disponible y no está prohibido. Si no queda ningún recurso válido, rechaza los datos en vez de asignar un ID inexistente.

Los movimientos prohibidos siguen excluidos en todas las alternativas. Los avisos aparecen resumidos en la propuesta, se conservan al aplicar y se muestran en historial, previsualización Global y manifiestos de exportación. No se ha eliminado la validación de datos incompatibles.

Los aprendizajes y las entradas personales se indexan una vez al preparar el contexto del equipo, evitando recorrer tablas completas por cada Pokémon.

## Better Movesets y Smart Items

Better Movesets incorpora **Incluir MT y TR compatibles**. Usa las banderas de compatibilidad de la tabla personal actual, por lo que respeta las ediciones de compatibilidad de la sesión. Solo añade movimientos disponibles y no prohibidos. La opción está desactivada por defecto y no se aplica cuando Better Movesets no está activo para el entrenador.

La correspondencia de 100 MT y 100 TR procede de `Tables8` de pkNX, revisión `d191cd0e5c05f2af81d9a41c1f1d82e6621b351a`, con atribución GPL-3.0. Corresponde al perfil SWSH 1.3.2 admitido. No interpreta modificaciones externas de las definiciones de MT del ejecutable ni añade tutores a esta opción.

Smart Items permite calidad **Normal**, **Relevantes** o **Competitivos** por separado para normales, importantes y líderes de gimnasio. El alcance sigue siendo independiente por categoría. Las calidades se resuelven por ID de entrenador y se congelan junto a la propuesta. Los templates anteriores que guardaban una sola calidad la heredan en las tres categorías. Las nuevas opciones se conservan en historial, Global y Batch.

## Evoluciones por intercambio

Se revisó la enumeración de métodos de pkNX y el formato de evolución de SWSH: los métodos de intercambio son **5 (normal), 6 (con objeto) y 7 (Shelmet/Karrablast)**.

El botón **Eliminar evoluciones por intercambio · nivel 37**, en el inspector de Evoluciones, genera una propuesta para todas las entradas. Reutiliza el editor validado: método 4 (subir de nivel), argumento 0 y nivel 37. Conserva destino, forma, métodos ajenos y ranuras vacías. Requiere pulsar Aplicar cambios; crea historial y una acción reproducible mediante Global/Batch.

En Espada real se encontraron **26 rutas: 10 normales, 14 con objeto y 2 Shelmet/Karrablast**. Las 26 se convirtieron y reabrieron en memoria.

## Historial y rendimiento

La navegación anterior construía una cadena gigante y la dibujaba en un TextBlock, mientras el editor de Pokémon reconstruía otro texto completo incluso al no verse.

Ahora el historial agrupa operaciones y muestra **200 líneas como máximo por página**, con listas virtualizadas y filas compactas. Las cadenas de detalle se generan al visitar cada página. Anterior/Siguiente permiten consultar todos los registros; Exportar historial escribe todas las páginas sin truncarlas y hace la escritura fuera del hilo de interfaz. El resumen del editor de Pokémon está limitado a 50 cambios de previsualización y las últimas tres semillas, con acceso al historial completo. El historial conserva también metadatos de randomizaciones, runtime y avisos.

La prueba con 10.000 registros verifica que solo se formatean 200 al abrir y otros 200 al visitar la última página. No es una medición de milisegundos ni una promesa de un porcentaje concreto de aceleración.

## Archivos afectados

- Core: `pk3DS.Core/NX/SwordShieldBattlePolicy.cs`, `SwordShieldGameplay.cs`, nuevo `SwordShieldMachines.cs`.
- Servicios: `3DS.Progressive.App/Services/Switch/SwordShieldRecordEditor.Trainers.cs`, `SwordShieldRecordEditor.cs`, `SwordShieldOptionsStore.cs`, `SwordShieldProject.Gameplay.cs`, `SwordShieldProject.Global.cs`.
- WPF: `RecordEditorView.xaml.cs`, nuevo `RecordEditorView.SwitchActions.cs`, nuevo `SwitchHistoryView.cs`, `SwitchWorkspaceView.cs`, `SwordShieldPrototypeWindow.cs`, `SwitchAutomationView.cs`.
- Pruebas: nuevo `SwitchPolishChecks.cs`; `SwitchParityChecks.cs`, `SwitchExpansionChecks.cs`, `SwitchVisualChecks.cs`, `Program.cs`.

Se preservaron los cambios locales anteriores. No se modificaron el JSON del usuario, la extracción original, mods instalados ni los motores de 3DS. No hay commit ni push.

## Validación

- Solución Release: 0 errores y 56 emisiones de advertencias conocidas. Proyecto de pruebas Release: 0 errores y 88 emisiones conocidas. Comparación normalizada: 27 advertencias únicas, ninguna nueva.
- Runner: `--swsh-parity-only` 192; `--swsh-ui-only` 94; `--swsh-only` 312; `--visual-redesign-only` 95; `--all-ui-only` 398; `--guidance-only` 233; `--synthetic-only` 4055. Total: **5379 comprobaciones satisfactorias**.
- 24 renders finales WPF a 96 DPI, sin errores de binding. Se inspeccionaron MT/TR, calidad por categoría, botón de evoluciones y ambas versiones de espaciado del historial.
- `git diff --check`: correcto.

Durante la implementación, las regresiones detectaron la pérdida de las semillas en el nuevo resumen: se corrigió conservando las últimas tres y el acceso al historial completo. Se actualizó la prueba de navegación que esperaba el antiguo ScrollViewer para verificar el contenido de la vista paginada. También se corrigió una referencia ambigua WPF/WinForms al ajustar el estilo compacto. No quedan fallos de esas comprobaciones.

Datos reales de Espada, solo en memoria:

- `Switch_Polish_RealAllTradeEvolutionRoutesConvertedInMemory`: 26 rutas verificadas.
- `Switch_Polish_RealAllRandomizedTrainerMovesAndCategoryItems`: 7479 cambios y 4 avisos; proceso completo sin el bloqueo anterior.
- `Switch_Polish_RealGlobalReplayWithMachinesQualityAndWarnings`: receta reproducida en otra sesión.

Las capturas WPF incluyen una operación real de Level Up Moves con 12.233 diferencias, distribuida en 62 páginas. Son renders fuera de pantalla, no una prueba interactiva.

Pruebas clave: `Switch_Polish_LevelThreeWithZeroMinimumUsesFirstFutureAttackAndWarns`, `Switch_Polish_FallbackNeverBypassesBannedMoves`, `Switch_Polish_BetterMovesIncludesCompatibleTM`, `Switch_Polish_BetterMovesIncludesCompatibleTR`, `Switch_Polish_SmartItemQualityIsIndependentPerTrainer`, `Switch_Polish_AllThreeTradeMethodsBecomeLevelThirtySeven`, `Switch_Polish_TradeConversionPreservesDestinationsFormsAndOtherMethods`, `Switch_Polish_HistoryFormatsOnlyCurrentPageOfTenThousand`, `Switch_Polish_HistoryExportIncludesAllPages`.

Logs y binlogs: `C:/Users/Joanj/AppData/Local/Temp/DR-SWSH-rules-history-20261022`.

## Pendiente de validación manual

Jugabilidad de los equipos y evoluciones en el emulador/consola; Escudo con sus propios datos; navegación interactiva por teclado, alto contraste y DPI 125/150/200 %. Compilar, reabrir binarios y renderizar fuera de pantalla no certifica jugabilidad. No se instaló ningún mod para esta validación.

Skills utilizadas: `pk3ds-progressive-safety`, `wpf`, `dotnet-diag:analyzing-dotnet-performance`, `dotnet-test:code-testing`, `dotnet-test:run-tests` y `dotnet-msbuild:binlog-generation`.
