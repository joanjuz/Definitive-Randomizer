# Apertura universal de proyectos (B2)

Fase **B2** de la [hoja de ruta](HOJA_RUTA_EDITOR_Y_1_0.md), requisito de 1.0.0. Una **única acción «Abrir proyecto»** abre cualquier archivo o carpeta compatible sin que el usuario elija antes Nintendo DS, 3DS o Switch. Usa el [detector de B1](DETECTOR_PROYECTOS_B1.md) para identificar la entrada y el motor existente de cada plataforma para abrirla. No se modificó ningún parser ni ningún motor de randomización.

## 1. Cómo funciona la interfaz

El botón **Abrir proyecto…** de la cabecera (y las entradas «Abrir proyecto…» y «Abrir otro proyecto…» de la paleta Ctrl+K y del inicio) muestran el mismo diálogo, esté en pantalla el editor de 3DS, el de DS o el de Switch.

| Elemento | Función |
|---|---|
| Ruta del archivo o carpeta | admite pegar o escribir; se analiza al dejar de escribir |
| Elegir archivo… / Elegir carpeta… | selectores del sistema; la ruta elegida se analiza al instante |
| Juego · Plataforma | lo que el detector identificó (o «—») |
| Mensaje de compatibilidad | «✔ Compatible», «✖ No admitido / Incompleto / Ambiguo / No accesible / No reconocido» con la explicación de B1 |
| Advertencia | solo en demos de 3DS |
| Admitir romhacks | solo aparece para una ROM DS compatible; es la opción del motor DS, sin relajar sus comprobaciones |
| **Abrir** | habilitado **solo** si hay un candidato compatible y no hay una apertura en curso; Intro lo pulsa |
| Cancelar / ✕ / Esc | durante una apertura, descarta el resultado y cierra el diálogo |

Un archivo o carpeta compatible nunca abre otra cosa que lo detectado: antes de abrir, el coordinador vuelve a ejecutar el mismo detector sobre la ruta y exige que plataforma, tipo y juego coincidan.

**Carreras.** Cada edición de la ruta invalida la anterior (versión y cancelación): el resultado de una detección lenta de una ruta antigua se descarta, y mientras se analiza la ruta nueva «Abrir» queda deshabilitado, de modo que un candidato obsoleto no se puede abrir. Detección y apertura corren fuera del hilo de WPF.

**Alternativas por plataforma.** Se conservan hasta completar las pruebas: «Abrir ROM» del espacio DS, «Abrir extracción…» del de Switch y, desde la paleta, **«Abrir por plataforma…»** (el selector anterior, que decide por la plataforma en pantalla). No hay arrastrar y soltar global (B3).

## 2. Cómo se detecta y se abre cada plataforma

| Entrada | Detector | Motor que abre |
|---|---|---|
| ROM `.nds`, checkpoint `.ndsevents.json` v1–v3 | DS | `NdsProject.Open(ruta, admitirRomhacks)` |
| Carpeta con RomFS y ExeFS (incluye demos ORASDEMO y SMDEMO) | 3DS | `GameProjectSession.Open(ruta, recuperar:false)` y, solo con permiso, `recuperar:true` |
| Carpeta con `exefs` y `romfs` de Espada/Escudo | Switch | `SwordShieldProject.Open(ruta)` |

Código: `Services/ProjectDetection/ProjectOpening.cs` (`ProjectOpenCoordinator`, sin interfaz y probable con un anfitrión falso), `OpenProjectDialog.cs` (diálogo) y `MainWindow.UniversalOpen.cs` (anfitrión WPF).

## 3. Orden de apertura y conservación del proyecto anterior

El orden es fijo y el proyecto de la plataforma destino **no se toca hasta el final**:

1. Comprobar que la aplicación no está ocupada (operación en curso, ventana cerrándose, copia de seguridad…).
2. Volver a detectar la ruta y comparar con lo mostrado.
3. Si hay advertencia (demo), pedir confirmación.
4. Si la vista que está en pantalla es de **otra** plataforma, resolver sus ediciones de página sin guardar (su proyecto **no** se sustituye).
5. Si ya existe una sesión de la plataforma destino, pedir confirmación cuando tenga cambios pendientes (DS: operaciones sin exportar; Switch: cambios sin exportar o formulario sin aplicar; 3DS: formulario sin guardar y vistas auxiliares). Cancelar conserva todo y no abre nada.
6. Abrir el proyecto nuevo en una **variable temporal** (`PreparedProject`) fuera del hilo de WPF.
7. Validar (plataforma coincidente y juego identificado).
8. Instalarlo en la interfaz.
9. Solo entonces liberar el proyecto anterior.

Si algo falla o se cancela en los pasos 1–8, el proyecto preparado se libera (`Dispose`) y el estado anterior se conserva: sesión, cambios pendientes, navegación y pantalla.

**Una sesión por plataforma.** Abrir un juego de DS no cierra el de Switch ni el de 3DS: solo se sustituye la sesión de la plataforma destino. Cada espacio conserva su proyecto aunque otro esté a la vista.

**Instalación recuperable.** Compartir `SwitchHost` entre DS y Switch no garantiza que sus proyectos permanezcan intactos, así que cada instalación toma una instantánea del estado visual (visibilidad de contenedores, contenido del host, estado y textos de la cabecera, espacios de trabajo) y la restaura si falla:

- **DS** (`NdsWorkspaceView.InstallAsync`): restaura proyecto, páginas, ruta, estado y la casilla de romhacks; el proyecto antiguo solo se libera tras el éxito.
- **Switch**: restaura la vista y vuelve a presentar el proyecto anterior (que no se ha liberado y conserva su historial).
- **3DS**: reconstruye la interfaz con la sesión anterior.

Se corrigió además un defecto latente que esta función habría expuesto: el evento de contexto del espacio Switch reescribía la cabecera aunque estuviese visible el de DS (`RefreshSwitchHeader` ahora exige que el contenido visible sea el espacio Switch).

## 4. Recuperación de guardados interrumpidos en 3DS

`GameProjectSession.Open` puede recuperar un guardado interrumpido y escribir en la carpeta. B2 abre **siempre primero sin recuperar** (`recoverInterruptedSave:false`).

- Se introdujo **`InterruptedSaveException`** (subclase de `IOException` con el mismo mensaje de siempre y la ruta del registro). La lanzan `ProjectHistory.RejectPending` y `StandaloneTransactions.RecoverPending(recover:false)`. Los demás llamadores (copia de seguridad, reconstrucción, diagnósticos) no cambian de comportamiento. El coordinador la reconoce **por tipo**, nunca por el texto.
- Con esa excepción en un proyecto 3DS se muestra una confirmación que dice qué se hará: restablecer a su estado anterior los archivos del proyecto que quedaron a medio escribir y limpiar el registro (si el guardado ya había terminado, solo limpiar el registro), tocando únicamente esa carpeta y sin modificar ningún otro proyecto abierto, con la recomendación de hacer antes una copia.
- Si se rechaza, no se escribe nada (comprobado por instantánea de la carpeta) y el proyecto no se abre.
- Si se acepta, se reintenta con `recuperar:true` una sola vez; un conflicto (archivos de otro programa) o cualquier otro error se informa y no se instala nada.
- Solo el motor 3DS puede ofrecer recuperación; la misma excepción en DS o Switch se trata como rechazo.

## 5. Demos de 3DS

ORASDEMO (301 GARC) y SMDEMO (239) son candidatas **con advertencia**: la aplicación la muestra en el diálogo y pide confirmación antes de abrir. Aceptar solo permite intentar abrirla con el motor existente; no garantiza compatibilidad ni añade funciones. Si el motor la rechaza (por ejemplo, el `code.bin` de Gen 6 sin la tabla MT/MO), se muestra el error con una nota de que es una demo y el proyecto anterior se conserva.

## 6. Pruebas

| Prueba | Qué cubre |
|---|---|
| `--universal-open-only` (sintética y fuera de pantalla, en el CI) | coordinador con anfitrión falso; recuperación 3DS con copias desechables; carreras y cierres del diálogo; la ventana principal real con los tres motores reales |
| `--universal-open-live-probe <carpeta nueva> <ruta> [<ruta>…]` | sonda **interactiva** con ratón y teclado reales (SendInput) sobre copias privadas: abre en orden las rutas dadas desde el mismo botón |

La suite sintética cubre: DS→3DS, DS→Switch, 3DS→DS, 3DS→Switch, Switch→DS, Switch→3DS; sustitución en la misma plataforma (DS, 3DS y Switch); cancelar con cambios pendientes; ROM DS con tablas distintas, demo rechazada por el motor y extracción Switch dañada; carpeta incompleta, ambigua y ruta inexistente; demos con y sin aceptar la advertencia; checkpoints v1, v2 y v3 (por su propia ruta); recuperación 3DS (con y sin permiso, por el registro del historial y por el individual, sin tocar otro proyecto); rutas modificadas durante una detección; cancelar, cerrar con el botón ✕ y cerrar la ventana durante una apertura; excepciones al preparar y al instalar (inyectadas en el cambio de vista y en el cambio de proyecto, para las tres plataformas).

Limitación: no hay un Platino sintético que supere las tablas del perfil, por lo que los checkpoints reales solo se abren con motor en la sonda con archivos privados; en la suite sintética se comprueba su detección y el enrutado al motor DS con su propia ruta.

## Arrastrar y soltar (B3)

Soltar **un** archivo o **una** carpeta sobre la ventana principal lo abre con el mismo flujo que «Abrir proyecto»: `MainWindow.HandleDropAsync` llama a `OpenPathAsync`, que usa el mismo detector, los mismos motores y las mismas confirmaciones (`ProjectOpenCoordinator`). No hay lógica de apertura propia.

- Se admite lo mismo que el diálogo: ROM `.nds`, proyecto `.drproj`, checkpoint `.ndsevents.json`, carpeta 3DS extraída y extracción de Espada/Escudo. El contenido lo juzga el detector después de soltar, nunca el nombre.
- Mientras se arrastra, el cursor indica si se aceptará: no se acepta con una operación en curso, con el diálogo «Abrir proyecto» abierto, al cerrar la aplicación ni con más de un elemento.
- Lo que no se puede abrir no cambia nada y explica el motivo (barra de estado y, salvo cancelación, un aviso).

Comprobaciones en `--universal-open-only` (113): ROM DS soltada abre con su proyecto, otro ROM reemplaza sin preguntar a un proyecto guardado, un `.drproj` soltado reanuda su trabajo, carpeta 3DS y extracción de Switch, varios elementos, archivo no reconocido, ruta inexistente, soltar vacío y bloqueo durante una operación. Son fuera de pantalla: **el gesto real con el ratón no está probado** y conviene comprobarlo a mano.

## Proyectos recientes (A4)

El diálogo «Abrir proyecto» muestra, cuando hay alguno, los últimos proyectos abiertos (hasta 12, el más reciente primero, sin repetir). Seleccionar uno pone su ruta en el campo y **pasa por el mismo detector y los mismos motores** que cualquier otra ruta: una entrada antigua o manipulada no abre nada por sí misma. Para DS, reanudar una ROM o un `.drproj` reciente continúa su proyecto persistente.

- **Entradas que ya no existen** se muestran como «No disponible»: no se pueden abrir y se pueden quitar.
- **«Quitar de la lista»** solo edita la lista. No borra ni modifica ningún archivo del proyecto.
- **Dónde se guarda:** `recent-projects.json` en los datos locales del usuario (`%LOCALAPPDATA%\3DS-Progressive`), en un archivo propio. Contiene ruta, juego, plataforma y fecha; nada del contenido. No se escribe en registros, informes, proyectos ni ROM, y no sale del equipo. Un archivo dañado o ilegible equivale a una lista vacía; si no se puede escribir, se ignora.
- Las comprobaciones automáticas solo usan la carpeta de estado que se les indica (`PROGRESSIVE_STATE_DIR`) y nunca la lista real de quien las ejecuta.

Se registra al abrir con el diálogo, con el botón de apertura del editor DS y al soltar un archivo. Comprobaciones en `--universal-open-only` (121): orden y ausencia de repetidos, entrada inexistente marcada y no abrible, quitar una entrada sin borrar archivos, reanudar un proyecto reciente (sube al principio), lista dañada, límite y comparación sin distinguir mayúsculas.
