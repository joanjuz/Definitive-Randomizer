# Corrección H2/H3: transacciones de archivos frente a escritores externos

Rama `fix/h2-h3-transaction-races`, sobre `test/h2-h3-deterministic-races` (`a831077`). Corrige los defectos del [issue #43](https://github.com/joanjuz/Definitive-Randomizer/issues/43) reproducidos en [REPRODUCCION_H2_H3_20261104.md](REPRODUCCION_H2_H3_20261104.md). No cambia la versión del programa ni el formato del journal, del historial o de Global Template.

## Causa

Commit, rollback y Recover comprobaban un archivo **por ruta** y lo reemplazaban después, también **por ruta**. Entre ambas operaciones no había nada que impidiera escribir. Añadir otra comprobación de SHA-256 solo acorta esa ventana: el defecto es la separación, no la falta de comprobaciones.

## Semántica de Windows comprobada

Medida con una sonda aislada en Windows 11, NTFS, .NET 10, antes de elegir el diseño.

| Hecho comprobado | Consecuencia |
|---|---|
| Un handle `READ\|DELETE` con `FileShare.Read` rechaza escritura, truncado, `File.Delete`, `File.Move` y `File.Replace` | Es un bloqueo real frente a todos esos actores |
| Abrirlo falla si ya existe un escritor, aunque comparta todo, o una vista mapeada escribible sin handle | No hay que confiar en que nadie escribía antes |
| El handle sigue bloqueando el archivo después de renombrarlo | El bloqueo acompaña a los bytes, no al nombre |
| Renombrar sin `ReplaceIfExists` falla si el nombre está ocupado | Funciona como compare-and-swap sobre el nombre |
| `File.Replace` no puede reemplazar un destino con ese bloqueo | No sirve: exige soltar el bloqueo, y eso reabre la ventana |
| El rename POSIX exige `FileShare.Delete` en el destino | Dejaría pasar a otro programa que guarde mediante rename o `ReplaceFile`, y su edición se perdería |
| `SetFileInformationByHandle` falla con rutas largas y resuelve un nombre simple contra el directorio de trabajo del proceso | Se usa `NtSetInformationFile` con ruta NT completa |
| `File.Replace` rechaza destinos de solo lectura y symlinks, y conserva atributos, fecha de creación y ACL | El contrato anterior se mantiene de forma explícita |

## Solución

`ExclusiveFile` encapsula ese handle. `FileTransaction.Commit` y `RecoveryJournal.Recover` siguen este orden:

1. **Preparar** los temporales sin bloquear nada.
2. **Retener todos los destinos** y comprobar los hashes **por el mismo handle**. Si uno no se puede retener o cambió, la operación termina sin ningún cambio.
3. **Reemplazar** cada destino con dos renames por handle: el original pasa a `<ruta>.studio-<token>.old.tmp` y el temporal toma el nombre **sin reemplazar**.
4. **Marcar el éxito** con todo retenido y limpiar después.

El rollback invierte los renames con los mismos handles: no reescribe bytes ni compara y luego reemplaza.

Queda un único instante en que un tercero puede actuar: el nombre vacío entre los dos renames. No hay nada que sobrescribir, porque el rename final no reemplaza. Si otro programa crea el archivo ahí, su contenido se conserva y la transacción falla conservando el journal y el original retirado.

El bloqueo no reduce la ventana entre comprobación y reemplazo: la elimina, porque mientras dura el retenido los bytes comprobados no pueden cambiar.

### Recuperación multiarchivo

Antes, un conflicto descubierto a mitad de Recover dejaba una restauración parcial. Ahora se comprueba y se retiene el conjunto completo antes de restaurar, así que una edición anterior al retenido rechaza todo sin cambios. Solo un conflicto en el nombre vacío deja un estado parcial, y entonces se conservan el journal, los respaldos y el archivo retirado.

Recover admite un estado nuevo: destino ausente con su archivo retirado, resultado de morir entre los dos renames. Un destino ausente **sin** archivo retirado sigue siendo un cambio externo y se rechaza. `GameProjectSession.Open` ya recupera antes de leer datos, y los diagnósticos rechazan un guardado pendiente.

### Compatibilidad

- Manifest sin cambios: `Version=1`. El nombre del archivo retirado se deriva del `Token`. Los journals escritos por versiones anteriores se recuperan (`H3_PreviousVersionJournalRecovers`).
- El temporal copia atributos Hidden/System/NotContentIndexed, fecha de creación y ACL del destino, que un rename no conservaría.
- Siguen rechazados los destinos de solo lectura y los symlinks. Se conserva la grafía existente y funcionan las rutas largas y con prefijo `\\?\`.
- Sin cambios en los servicios que llaman a `FileTransaction`, en `ProjectHistory` salvo un comentario, ni en los datos de los proyectos.

## Antes y después

| Caso | Antes | Después |
|---|---|---|
| Commit, escritura durante la ventana | External perdido, éxito, journal eliminado | Escritura rechazada mientras el destino está retenido; si cae en el nombre vacío se conserva External, la transacción falla, el resto se restaura y queda el journal |
| Rollback, escritura durante la ventana | External sustituido por Before, journal eliminado | Igual: rechazada, o conservada con el journal y el original retirado |
| Recover, un destino | External perdido, sin error, journal eliminado | Igual |
| Recover, tras restaurar otro destino | External perdido, sin error, journal eliminado | Igual |
| Recover, edición en destino posterior | Rechazo con restauración parcial | Antes del retenido: rechazo sin ningún cambio. En el nombre vacío: parcial con evidencia, reintento y conciliación |
| Escritor abierto o vista mapeada | Sin comprobar | Se detiene antes de cualquier cambio |
| Reemplazo por rename, truncado o borrado durante la ventana | Sin comprobar | Rechazados |
| Proceso muerto entre renames, en Commit y en Recover | No existía ese estado | Recupera; verificado matando procesos reales |
| `--transaction-races-only` | 18 correctas, 8 fallidas, tres ejecuciones | 56 correctas, 0 fallidas, 50 ejecuciones |

## Cambios en la suite

Las pruebas originales no podían ejecutarse sin cambios: pausaban una apertura del temporal que Commit ya hace **con el destino retenido**, y el rollback ya no abre ese temporal, de modo que el escritor recibía un rechazo y el arnés abortaba. Hacer pasar la suite aceptando ese rechazo como éxito habría sido debilitarla. Por eso:

- **Conservan nombre y expectativa** las ocho invariantes que fallaban, incluido `Is(victim,External)`. Ahora se pausan en el nombre vacío, único momento en que el escritor consigue escribir, y su edición confirmada debe sobrevivir.
- **Pausa nueva sin hooks de producción:** un oplock RH sobre el archivo que se renombra sobrevive a la apertura productiva y solo detiene el rename. Los demás casos siguen usando el oplock de nivel 1.
- **Cambio de escenario, no de expectativa:**
  - `H3_PartialRecoveryRejectsLaterConflictAndRetainsJournal` pasa del destino editable antes a un conflicto en el nombre vacío, único camino que aún produce parcial.
  - Las recuperaciones usan cuatro archivos para disponer de un punto de armado.
- **Control sustituido:** `H2_ControlExternalBeforeRollbackComparison` pasó a ser `H2_ControlWriterRefusedBeforeRollbackComparison`. Su escenario, un escritor que lo consigue antes del rollback, ya no es alcanzable.
- **Casos añadidos:**
  - rechazo real de seis actores tras la verificación final;
  - edición anterior al retenido;
  - escritor previo y vista mapeada;
  - procesos muertos a mitad;
  - journal antiguo;
  - retirado alterado o destino borrado;
  - metadatos, solo lectura, symlink, grafía, rutas largas;
  - temporal oculto residual.

`H2_EditBeforeHold*` y `H3_EditBeforeHold*` por sí solos pasarían con una simple relectura; la garantía nueva la demuestran `*HeldTargetsRefuseEveryWriter*`, `*HeldSetRefusesEveryWriter*` y los casos de nombre vacío.

## Mutaciones de producción

Para demostrar que las pruebas detectan la debilidad se debilitó la producción y se volvió a ejecutar la suite. La producción se restauró después de cada mutación.

| Mutación | Resultado |
|---|---|
| Compartir escritura y borrado en el handle | Detectada: falla el rechazo de escritores y la suite aborta |
| Rename con `ReplaceIfExists` | Detectada: 14 invariantes |
| Quitar la comprobación bajo retenido en Commit | Detectada: `H2_EditBeforeHoldRejectedWithoutWrites` |
| Quitar la comprobación bajo retenido en Recover | Detectada: 3 invariantes |
| Borrar el destino ocupado antes de devolver el original | Detectada: 2 invariantes |
| No eliminar el temporal oculto residual | Detectada: `Compat_HiddenStagedLeftoverDoesNotBlockRecovery` |
| Soltar el retenido y usar `File.Replace` | **Sin valor como evidencia:** falló por el conflicto de handles de la propia mutación, no por una aserción |

Las pruebas y la revisión también detectaron defectos reales durante el desarrollo, ya corregidos: la limpieza del journal con handles abiertos ocultaba la causa original del fallo, la ACL no se copiaba porque .NET solo persiste secciones marcadas como modificadas, un temporal oculto bloqueaba el reintento de Recover, y una comprobación duplicada del archivo retirado se eliminó por redundante.

## Validación ejecutada

Windows 11, NTFS local, SDK .NET `10.0.301`, Release, datos sintéticos. Sin ROMs ni extracciones reales.

| Comprobación | Resultado |
|---|---|
| `--transaction-races-only`, 30 ejecuciones seguidas | 56/56 en todas, salida 0 |
| `--transaction-races-only`, 5 rondas de 4 procesos simultáneos | 56/56 en las 20, salida 0 |
| `--undo-toctou-only` | 6, salida 0 |
| `--editor-modules-only` | 408, salida 0 |
| `--synthetic-only` | 4101, salida 0 |
| Sin argumentos, con WPF | 4509, salida 0 |
| Las suites del CI y `--template-studio-only`, `--undo-template-regression-only` | Salida 0 en todas |

`--transaction-races-only` se incorpora al CI, ya que deja de ser una suite que falla por diseño y fue estable en estas repeticiones.

## Limitaciones residuales

**Propiedades que no se pueden garantizar:**

- **Escritores después de liberar:** terminado el guardado, un programa que conserve una copia antigua puede sobrescribir el resultado. Pierde el guardado propio, no una edición ajena, y nada lo detecta.
- **Escrituras por debajo del sistema de archivos:** el acceso directo al volumen y los controladores en modo kernel que ignoren la compartición no quedan bloqueados. Se detectan solo si ocurren antes de la comprobación bajo retenido.
- **Atomicidad entre varios archivos:** no existe. Un observador puede ver parte del conjunto ya sustituido, y tras un cierre forzado el conjunto solo se reconcilia al abrir el proyecto en la aplicación.

**Efectos de comportamiento nuevos:**

- **Nombre vacío:** durante un instante el destino no existe y un lector recibe «no encontrado». Antes no ocurría. Tras un cierre forzado en ese punto, sigue ausente hasta recuperar, y otras herramientas verán la carpeta incompleta. El original está en `.old.tmp` y en el journal.
- **Conflicto en el nombre vacío:** gana el archivo externo, y el proyecto no se recupera hasta que alguien lo reconcilie a mano, con los bytes propuestos u originales. No hay interfaz para esto; es la misma política anterior de «se conserva para revisión».
- **Lectores durante el retenido:** quien abra sin `FileShare.Delete`, como `File.ReadAllBytes`, recibe un error de compartición (`Limit_ReadersNeedFileShareDeleteDuringTheHold`). Si ese lector ya estaba abierto, el guardado se detiene sin cambios. Con `File.Replace` ese lector también hacía fallar el guardado, ya a mitad de la transacción. No hay reintento automático.
- **Metadatos no conservados:** flujos alternativos (ADS) y atributos de compresión o cifrado del destino, que `File.Replace` sí trataba. Los archivos de proyecto no los usan.
- **Residuo posible:** si tras el éxito no se puede eliminar el original retirado, queda un `.old.tmp`, sin journal que lo limpie.

**No comprobado:**

- Cortes de alimentación: los procesos matados no equivalen a perder la caché de disco.
- FAT, exFAT, ReFS y unidades de red o UNC. El diseño no depende de NTFS, pero la cuenta sin elevación no permitió montar un volumen de prueba.
- Antivirus reales y enlaces duros.
- Datos reales y partidas. No se requieren para esta lógica, pero ninguna prueba sintética sustituye a una edición real en un proyecto copiado.

Las pausas con oplocks alargan ventanas reales para hacerlas repetibles; no miden su frecuencia en uso normal.
