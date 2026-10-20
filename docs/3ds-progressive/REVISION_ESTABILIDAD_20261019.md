# Revisión de estabilidad — 19/10/2026

Base: `526143af5178243a6aed9db773fe232dedec32e9` (preview.2), rama `feature/3ds-progressive-ui`. Cambios en código fuente, sin commit, push, publicación ni actualización de la instalación. El prototipo de compras únicas y sus archivos pendientes permanecen intactos.

## Problemas reproducidos y correcciones

1. **Retención de ventanas (media).** `MainWindow` se suscribía con lambdas a cuatro eventos estáticos de `UiProgress` y no se desuscribía. Ahora conserva los delegados, los retira en `Closed`, detiene el temporizador y cancela las cargas de sección/sprites. Las actualizaciones ya encoladas comprueban que la ventana siga abierta. Cinco ventanas cerradas fueron realmente recolectadas por GC en el proceso de pruebas; no equivale a medir todas las asignaciones de una sesión larga.
2. **Recuperación de Rebuild (alta).** Un archivo temporal de solo lectura provoca `UnauthorizedAccessException` al borrar staging. Antes podía informar fallo después de publicar el archivo y dejar tomado el semáforo global, bloqueando futuras reconstrucciones. Ahora el semáforo se libera en un `finally` independiente. Los errores de limpieza de E/S o permisos se informan con la ruta temporal, conservando tanto el éxito de una salida verificada como el error original cuando falla la construcción. La siguiente ejecución limpia el aviso anterior. No se borran archivos fuera del staging propio.
3. **Resultados tardíos y apertura simultánea (media, endurecimiento preventivo).** La ventana cerrada rechaza navegación, publicación de proyectos y actualizaciones de sprites de otra sesión. Cargas obsoletas no publican vista ni error modal, y su finalización no termina el progreso de una carga sucesora. Abrir proyecto y recargar idioma mantienen el bloqueo de interacción existente durante la lectura; después del cierre se descarta el resultado. No se cambia cómo se leen o editan los datos del juego.
4. **Aislamiento del runner WPF (pruebas).** Al bombear el dispatcher, el `StartupUri` podía crear otra ventana; su presencia también ocultaba la dependencia del cierre automático al cerrar ventanas de fixtures. `WpfTestHost` suprime ese arranque y usa cierre explícito exclusivamente en los runners afectados. Conserva las trazas originales de los fallos entre hilos mediante `ExceptionDispatchInfo`. El arranque de la aplicación distribuida no se modifica. La reflexión sobre `_startupUri` es exclusiva de pruebas y falla explícitamente si WPF cambia esa implementación.

## Archivos

- `3DS.Progressive.App/MainWindow.xaml.cs`: suscripciones y temporizador, resultados tardíos, apertura/idioma y cierre.
- `3DS.Progressive.App/Services/GameRomRebuildService.cs`: liberación garantizada del semáforo y aviso de limpieza.
- `3DS.Progressive.App/RomRebuildView.cs`: muestra el aviso sin ocultar el resultado o error original.
- `3DS.Progressive.Tests/InteractionLockChecks.cs`: cierre, cancelación, actualizaciones encoladas y GC.
- `3DS.Progressive.Tests/RebuildStabilityChecks.cs`: errores de limpieza, colisiones y reintentos.
- `3DS.Progressive.Tests/WpfTestHost.cs`: aislamiento del host de pruebas.
- `3DS.Progressive.Tests/Program.cs`: suite focalizada, inclusión sintética y trazas.
- `3DS.Progressive.Tests/VisualRedesignChecks.cs`: aislamiento de su arranque.
- Este informe.

No se modifican motores, formatos, compresión, templates, historial, Batch ni recursos originales. Las pruebas de escritura usan fixtures y temporales aislados; no ROMs personales.

## Evidencia de regresión

Antes de cambiar producción, las pruebas añadidas fallaron por `WindowLifecycle_CloseRemovesAllStaticProgressHandlers` y `RebuildStability_CleanupFailureDoesNotInvalidatePublishedOutput`. Tras corregir, ambas pasan. Los demás casos nuevos validan los límites y reintentos del comportamiento corregido; no se afirma evidencia rojo/verde individual para todos ellos.

| Requirement | Evidence |
|---|---|
| Liberación de eventos y memoria | `WindowLifecycle_CloseRemovesAllStaticProgressHandlers`, `WindowLifecycle_CloseStopsTimer`, `WindowLifecycle_RepeatedClosedWindowsAreCollectible`, `WindowLifecycle_RepeatedCloseDoesNotAccumulateSubscriptions` |
| Rechazar actualizaciones y publicaciones tras cerrar | `WindowLifecycle_ClosedWindowIgnoresProgress` (incluye actualizaciones encoladas antes del cierre), `WindowLifecycle_ClosedWindowRejectsNavigation`, `WindowLifecycle_ClosedWindowRejectsProjectPublication` |
| Cancelación y desbloqueo | `WindowLifecycle_CloseCancelsAsyncLoad_Encounters`, `WindowLifecycle_CloseCancelsAsyncLoad_FieldItems`, `WindowLifecycle_CanceledLoadCannotPublishAndUnlocks_Encounters`, `WindowLifecycle_CanceledLoadCannotPublishAndUnlocks_FieldItems` |
| Salida válida con fallo de limpieza | `RebuildStability_CleanupFailureDoesNotInvalidatePublishedOutput`, `RebuildStability_CleanupWarningIdentifiesTemporaryFolder`, `RebuildStability_RetryAfterCleanupFailureReleasesGate`, `RebuildStability_RetryClearsOldWarning` |
| No sobrescribir y poder reintentar | `RebuildStability_ConcurrentDestinationIsNeverOverwritten`, `RebuildStability_RetryAfterPublishFailure` |
| Conservar error original de construcción | `RebuildStability_CleanupPreservesOriginalBuildError`, `RebuildStability_RetryAfterBuildAndCleanupFailure` |
| Recuperación de guardados existente | `recovery restores every file after actual process interruption`, `ordinary rollback preserves bytes and removes completed recovery work` |

## Ejecuciones reales

Compilaciones con binlogs únicos fuera del repositorio:

- `dotnet build ./pk3DS.slnx -c Release --no-incremental`: 0 errores, 56 advertencias.
- `dotnet build ./3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release -p:UseAppHost=true --no-incremental`: compilación final 0 errores, 78 advertencias. Incluye WPF y dependencias; la solución original no incluye esos dos proyectos.
- Compilaciones Debug: 0 errores; la final sin incremental mostró 78 advertencias. Otras ejecuciones incrementales mostraron 88. Los recuentos distintos no se presentan como eliminación de advertencias; continúan advertencias del backend y código heredado, sin advertencias reportadas en los archivos de producción modificados.

Runner `./3DS.Progressive.Tests/bin/Release/net10.0-windows/3DS.Progressive.Tests.exe`:

| Opción | Resultado |
|---|---|
| `--synthetic-only` | 3.669, éxito, fixtures XY/ORAS/SM/USUM |
| `--interaction-lock-only` | 19, éxito; cierre y cancelación WPF fuera de pantalla |
| `--rebuild-stability-only` | 8, éxito |
| `--visual-redesign-only` | 95, éxito, sin errores de bindings detectados |
| `--rebuild-marts-ui-only` | 65, éxito |
| `--performance-only` | 98, éxito |

En Debug también pasaron `--editor-modules-only` (408, incluida interrupción real del proceso de guardado y recuperación) y `--rebuild-cache-only` (13). Las suites se solapan: no sumar sus cifras como casos únicos. `git diff --check` pasó.

Incidencias durante la validación: se corrigió el aislamiento del host WPF descrito arriba; una compilación intermedia falló al copiar DLLs usadas por una prueba en ejecución (MSB3021/MSB3027). Se esperó a su finalización y se recompiló sin procesos usando la salida: compilación y suites finales correctas. No era un fallo del guardado ni del motor de ROM. Una conversión accidental de codificación se detectó en el diff y se corrigió antes de la compilación final; textos originales conservados.

## Límites y revisión manual pendiente

- Los casos de cancelación ejercitan el método asíncrono real y el dispatcher, cerrando antes de la ejecución del trabajo pesado. No prueban todos los puntos de una lectura de varios cientos de MB ya en curso. `File.ReadAllBytes` sigue sin poder interrumpirse a mitad de esa llamada; se impide publicar resultados obsoletos.
- Abrir proyecto y recargar idioma descartan resultados tardíos y bloquean la interacción durante la lectura; sus selectores nativos y la recuperación visual tras error requieren comprobación manual.
- No se ha hecho una sesión de varias horas, una captura de heap de producción ni una medición completa de memoria al rotar todos los editores. GC de cinco ventanas no certifica ausencia de toda fuga posible.
- No se añadió cancelación a mitad de BLZ/IVFC ni se cambiaron algoritmos o bytes de ROM. Se mantienen los bloqueos y protecciones de cierre de Rebuild y Batch.
- Si no se puede limpiar staging por permisos o un archivo bloqueado, la carpeta puede permanecer; se muestra su ruta. No se presenta la salida válida como fallida ni se pierde el error original.
- Falta comprobación física de teclado, ventanas modales, DPI y una sesión con proyectos grandes: abrir/cambiar/cerrar editores, provocar un destino no escribible y reintentar, y comprobar que no se pierde estado ni queda bloqueada la aplicación.
- La release publicada y la instalación local continúan en preview.2. Esta revisión queda disponible en código para validar y publicar por separado.

No aparecieron fallos de regresión en las suites finales ejecutadas. Hay una base mejor protegida para continuar con compras únicas después de esta comprobación manual acotada; no se afirma estabilidad absoluta.
