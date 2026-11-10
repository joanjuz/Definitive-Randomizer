# Detector universal de proyectos (B1)

Fase **B1** de la [hoja de ruta](HOJA_RUTA_EDITOR_Y_1_0.md), requisito de 1.0.0. Es un **servicio sin interfaz** que, a partir de una ruta, dice qué es la entrada (Nintendo DS, Nintendo 3DS o Switch), qué juego es y si es candidata a abrirse. **No abre nada ni lo escribe**: el cambio de la interfaz y el botón único «Abrir proyecto» son B2 (sección 7). Hasta B2 la aplicación sigue abriéndose igual que antes.

## 1. Arquitectura

`3DS.Progressive.App/Services/ProjectDetection/ProjectDetector.cs`, método `ProjectDetector.Detect(string? ruta) → ProjectDetection`. No lanza excepciones: cualquier fallo se convierte en un resultado clasificado con un mensaje en castellano.

```text
ruta ─┬─ archivo ── firmas de contenedor (NSP, XCI, 3DS, CXI, CIA, zip/7z/rar) ───────────────► no admitido, con consejo
      │          ├─ JSON ── checkpoint de eventos DS (v1–v3) ── ROM de origen (cabecera) ────► DS · checkpoint
      │          └─ resto ── cabecera NDS (CRC16) + código/revisión ────────────────────────► DS · ROM
      └─ carpeta ─┬─ RomFS/«a» + ExeFS/«code» ── nº de GARC ── GameConfig ──────────────────► 3DS
                  └─ exefs/main.npdm + exefs/main ── título y build (cabeceras) ───────────► Switch
```

La detección se basa en el **contenido**, no en el nombre ni en la extensión: una ROM llamada `.txt` se reconoce; un `.nds` que es un NSP se rechaza como NSP; un checkpoint llamado `.nds` o una ROM llamada `.ndsevents.json` se declaran inconsistentes en lugar de adivinar.

### Qué reutiliza

| Plataforma | Lector reutilizado | Lectura mínima |
|---|---|---|
| DS (ROM) | `NdsRom.Open` (CRC16 de la cabecera, tabla de archivos) y `NdsIdentity.Profiles` (código y revisión de los seis perfiles) | cabecera y tablas; **sin** SHA-1 de todo el archivo |
| DS (checkpoint) | `NdsEventDocument` y `NdsEventSession.Format` | JSON de hasta 8 MiB, y la cabecera de la ROM de origen |
| 3DS | las mismas reglas de carpetas que `GameProjectSession.Open` y `GameConfig(nº de GARC)` | nombres de carpetas y recuento de archivos de un carácter bajo `a` |
| Switch | `SwordShieldProject.PersonalRelativePath`, `CheckNoLinks` y `SwordShieldIdentity.Read` | `main.npdm` y los primeros 256 bytes de `main` |

No se modificó ningún parser ni ningún motor.

### Qué no hace

- No escribe, recupera guardados interrumpidos, extrae, descifra, construye ROM ni exporta mods.
- No calcula SHA-1 ni SHA-256 de archivos completos: eso lo siguen haciendo `NdsProject.Open` (identidad y tablas, y el modo romhack), el checkpoint al cargar y `SwordShieldProject`/`GameProjectSession` al abrir.
- Una entrada **Compatible** es solo **candidata**. El motor correspondiente conserva toda su responsabilidad y ninguna de sus comprobaciones se relaja.

## 2. Clasificación

| Estado | Significado | ¿Candidata? |
|---|---|---|
| `Compatible` | tipo reconocido y juego o revisión admitidos | sí |
| `Unsupported` | tipo reconocido, pero juego, revisión, compilación o contenedor no admitidos | no |
| `Incomplete` | parece admitida, pero falta o está dañada una pieza obligatoria | no |
| `Ambiguous` | evidencias contradictorias (dos plataformas, o nombre y contenido que no coinciden) | no |
| `Unknown` | nada reconocible | no |
| `Inaccessible` | la ruta no existe o no se puede leer (permisos, bloqueo, ruta no válida) | no |

Cada resultado incluye la plataforma, el tipo (`DsRom`, `DsEventCheckpoint`, `Extracted3dsFolder`, `SwitchExtraction`), el juego, un mensaje para el usuario, la lista de evidencias y, solo para entradas reconocidas con compatibilidad limitada, una **advertencia** (`Warning` / `HasWarning`). Hoy solo las demos de 3DS la llevan; es un campo opcional, así que el contrato no cambia para el resto.

## 3. Ejemplos de detección (copias privadas del propietario, solo lectura)

| Entrada | Resultado |
|---|---|
| Platino USA rev. 1 (`.nds`) | Compatible · Nintendo DS · `CPUE/1` · «Pokémon Platino (USA, rev. 1)» |
| Diamante rev. 5, HeartGold, Negro, Blanco 2 (`.nds`) | Compatible · Nintendo DS · `ADAE/5`, `IPKE/0`, `IRBO/0`, `IRDO/0` |
| Checkpoint `.ndsevents.json` v3 de Platino | Compatible · Nintendo DS · checkpoint, con su ROM de origen identificada |
| Carpeta de Sol, Luna | Compatible · Nintendo 3DS · 311 archivos GARC |
| Carpeta de Alfa Zafiro, Omega Rubí | Compatible · Nintendo 3DS · 299 archivos GARC |
| Carpeta de UltraLuna | Compatible · Nintendo 3DS · 333 archivos GARC |
| Extracción de Espada | Compatible · Nintendo Switch · `0100ABF008968000`, build de la 1.3.2 |
| Extracción de Escudo | Compatible · Nintendo Switch · `01008DB008C2C000`, build de la 1.3.2 |

Blanco (España) no estaba en la carpeta de originales: solo está cubierto por la prueba sintética (`IRAS/0`).

## 4. Casos de rechazo y mensajes

| Entrada | Estado | Mensaje (resumen) |
|---|---|---|
| ROM `ADAE` revisión 4, `CPUJ`, `BPRE` | Unsupported | «ROM de Nintendo DS reconocida, pero el juego no está admitido: ADAE revisión 4…» (no se asume por generación ni región) |
| `.nds` con la cabecera dañada, truncado o basura | Incomplete | «Tiene extensión .nds pero su cabecera no es válida: el CRC de la cabecera NDS no coincide…» |
| Archivo vacío, texto o imagen sin extensión de ROM | Unknown | «No es una ROM de Nintendo DS ni un formato conocido…» |
| NSP, XCI | Unsupported | «No abre NSP/XCI: usa una extracción de la 1.3.2 con exefs y romfs.» |
| `.3ds`/`.cci`, CXI, CIA | Unsupported | «No descifra ni extrae ROM comerciales: usa una extracción ya descifrada.» |
| zip, 7z, rar | Unsupported | «Extráelo primero y selecciona el archivo .nds o la carpeta extraída.» |
| Checkpoint de versión 4, de otro juego o revisión | Unsupported | «esta aplicación lee las versiones 1, 2 y 3», «solo existe el editor de eventos de Platino USA» |
| Checkpoint cuya ROM de origen falta o no es Platino | Incomplete / Unsupported | «la ROM de origen que indica no existe: …» |
| Checkpoint truncado | Incomplete | «El archivo JSON está truncado o dañado» |
| JSON de otro formato o plantilla | Unknown | «no es un checkpoint de eventos de Nintendo DS…» |
| Checkpoint con nombre `.nds`, o ROM con nombre `.ndsevents.json` | Unsupported / Ambiguous | «el motor solo abre archivos «*.ndsevents.json»; el detector no renombra nada» |
| 3DS con otro número de archivos (300, 302, 238…) | Unsupported | «estructura RomFS no reconocida (300 archivos GARC)…»; no se confunde con una demo |
| 3DS sin ExeFS/code o sin RomFS | Incomplete | «falta ExeFS con el archivo «code»…» |
| Carpeta RomFS o ExeFS suelta | Incomplete | «Sube un nivel y selecciona la raíz» |
| Switch con otra compilación | Unsupported | «reconocido, pero su compilación no es la 1.3.2 acreditada» |
| Switch con otro Title ID o `main.npdm` dañado | Unsupported | «no es Pokémon Espada ni Escudo o está dañada…» |
| Switch sin `personal_total.bin`, `main` o `main.npdm` | Incomplete | «falta: exefs/main.npdm…» |
| Piezas completas de 3DS y Switch en la misma raíz | Ambiguous | «contiene a la vez una extracción de 3DS y una de Switch» |
| Carpeta con `.nds` | Unknown | «contiene 2 archivo(s) .nds, pero no es un proyecto. Selecciona directamente el archivo» |
| Ruta inexistente, bloqueada por otro programa o inválida | Inaccessible | «La ruta no existe…», «¿está abierta en otro programa…?» |

## 4 bis. Demos de 3DS (decisión del propietario: se conservan con advertencia)

Las demos que ya reconoce `GameConfig` por su número de archivos GARC se siguen abriendo con el motor existente:

| Estructura | Resultado |
|---|---|
| 301 archivos GARC | **Compatible con advertencia** · «Pokémon Rubí Omega/Zafiro Alfa (demo)» |
| 239 archivos GARC | **Compatible con advertencia** · «Pokémon Sol/Luna (demo)» |
| cualquier otro número (p. ej. 300 o 240) | `Unsupported`: estructura no reconocida, **no** es una demo |

La advertencia dice que es una **demo**, que **no figura en la matriz pública de compatibilidad** ni es un juego acreditado, que el motor puede no soportar todas sus funciones o datos y que se trabaje sobre una copia. El detector no habilita ninguna función: la matriz pública no cambia y B2 mostrará la advertencia antes de abrir.

**Comprobación del motor.** No hay extracciones reales de demos, así que se comprobó con proyectos sintéticos en disco que contienen todos los GARC que lee `GameProjectSession.Open` (personal, level-up, textos, movimientos, evoluciones):
- Las dos demos se abren y el motor devuelve `ORASDEMO` o `SMDEMO`, sin modificar ningún archivo.
- Como control, un OR/AS completo con la misma estructura se abre igual.
- **Limitación del motor (no del detector):** en Gen 6 el motor exige un `code.bin` de al menos 4 MiB, alineado a 512 bytes y con la firma de la tabla MT/MO exactamente una vez. Esa exigencia se aplica igual a X/Y, OR/AS y a la demo de OR/AS. Una demo real cuyo `code.bin` no la contenga sería rechazada por el motor con «code.bin comprimido o incompleto», y no se fuerza su apertura. Con datos reales de una demo no se ha comprobado.
- Sol/Luna (demo) no tiene esa exigencia: sin la tabla, el motor continúa sin nombres de MT.
- B2 debe traducir ese rechazo del motor a un mensaje útil.

## 5. Pruebas

| Prueba | Resultado |
|---|---|
| `--project-detector-only` (sintética, en el CI) | **102** comprobaciones |
| Sonda real `--project-detector-probe <informe nuevo> <ruta>…` | 21 entradas del propietario (13 candidatas y 8 negativas: contenedores, texto, imagen y carpetas equivocadas) y una repetición de Negro con ruta de Windows, todas sin cambios; los candidatos DS y Switch se abren con su motor |

La suite sintética cubre:
- Los seis perfiles DS, revisiones y códigos no admitidos, y cabeceras con CRC dañado, truncadas, vacías o con una sola alteración (en 150 entradas aleatorias nunca hay un candidato y nunca se lanza una excepción).
- Los checkpoints v1, v2 y v3 y sus casos inválidos.
- Las extracciones 3DS de X/Y, OR/AS, Sol/Luna y UltraSol/UltraLuna, y sus incompletas y ambiguas.
- Switch: Espada y Escudo, otras compilaciones y los archivos que faltan.
- Extensiones engañosas, rutas inexistentes, bloqueadas o inválidas, y rutas con comillas.

**Evidencia de que no modifica las entradas:**
- Las pruebas calculan una instantánea (ruta, tamaño, fecha y SHA-256 de cada archivo) de todos los fixtures antes y después, y comprueban que es idéntica.
- También funcionan sobre archivos de solo lectura y no crean archivos al lado.
- En la sonda real, cada ROM se compara por SHA-256 completo antes y después. En las carpetas, la comparación es por rutas, tamaños y fechas de todas las entradas, más el SHA-256 de `main.npdm` y de los archivos `code`.
- Platino conserva su SHA-256 `FBCE4C4D…81BDB`.

La sonda no incluye ROM ni extracciones en el repositorio.

## 6. Notas de implementación

- **Rutas con `[` y `,` en Git Bash:** al pasar rutas con corchetes desde Git Bash, MSYS puede dejar de convertir la ruta y el ejecutable recibe `/d/...`. Es un artefacto del shell, no del detector (con rutas Windows se detecta bien).
- **3DS en la sonda:** no se abre con `GameProjectSession.Open` porque, por defecto, recupera guardados interrumpidos y eso escribe. B2 lo comprobará sobre una copia.

## 7. Diseño de B2: una sola acción «Abrir proyecto»

Sin implementar aquí; es un PR aparte y **no empieza hasta cerrar este**. B2 solo cambia la entrada de proyectos: no toca motores, mapas, NPC, mentas, Tracker ni scripts.

### 7.1 Lo que ya hace la aplicación (punto de partida comprobado en el código)

- **Los espacios de trabajo coexisten.** Abrir Nintendo DS o Switch no descarta el proyecto 3DS abierto: el comentario de `MainWindow.Nds.cs` dice que no se toca, y Switch y DS comparten el `SwitchHost`. Cada plataforma conserva su proyecto en memoria. Solo **reemplazar el proyecto de la misma plataforma** descarta cambios.
- **DS (`NdsWorkspaceView.OpenAsync/LoadAsync`)** ya confirma antes de abrir, abre en una variable local y sustituye el proyecto solo después (`Project?.Dispose(); Project = project`). Si falla, lo anterior queda. Pero llama a `NavigateAsync("Home", force: true)` aun cuando falla, un cambio visual parcial que B2 debe evitar.
- **3DS (`OpenProject_Click`)** hace `ConfirmDiscard()` y `ConfirmAuxiliaryLeave()`, elige carpeta y llama a `GameProjectSession.Open(carpeta)`, que **recupera guardados interrumpidos** por defecto (escribe en la carpeta del proyecto que se abre). Con `recoverInterruptedSave: false` no escribe: si hay un guardado interrumpido lanza `IOException` («El proyecto tiene un guardado interrumpido…»).
- **Switch (`SwitchWorkspaceView.OpenAsync`)** abre `SwordShieldProject.Open`, que solo lee.

### 7.2 Flujo

```text
1. Elegir ruta ── diálogo «Abrir proyecto» (7.3)
2. ProjectDetector.Detect (Task.Run, con indicador y bloqueo de interacción)
     └ no candidata → mensaje con las evidencias; nada cambia
3. Advertencia de compatibilidad limitada (demo) → confirmación explícita
4. Guardia de la plataforma destino: solo si ya tiene un proyecto con cambios pendientes
     o una página con ediciones sin confirmar → confirmar. No se descarta nada todavía.
5. Solo 3DS: Open(carpeta, recoverInterruptedSave: false)
     ├ abre → no había guardado interrumpido; esa sesión ES el resultado (sin escribir nada)
     └ guardado interrumpido → diálogo propio (7.4); si acepta, Open(carpeta, true)
6. Abrir en una variable local (Task.Run): NdsProject.Open / GameProjectSession.Open / SwordShieldProject.Open
     └ falla → cerrar lo abierto (NdsProject.Dispose), mensaje claro, el proyecto actual sigue intacto
7. Sustituir (hilo de interfaz, sin esperas): liberar el proyecto anterior de esa plataforma,
     instalar el nuevo, cambiar de espacio de trabajo, actualizar cabecera y navegación, una sola vez
```

El paso 6 no toca ninguna vista: se prepara el candidato completo y el paso 7 solo lo instala, de modo que no hay estados visuales a medias. Si el usuario cancela en 3, 4 o 5, o el paso 6 falla, la aplicación queda exactamente como estaba. La guardia del paso 4 va **antes** de escribir nada y no descarta; el descarte real ocurre en el paso 7, solo tras una apertura correcta.

### 7.3 Interfaz

- **Un solo botón visible «Abrir proyecto…»** en la cabecera (el existente). Abre un diálogo propio, con el estilo de `StudioDialog`, con tres elementos:
  - un cuadro de ruta que se puede pegar o dejar caer (arrastrar y soltar encaja aquí sin rediseñar nada);
  - **«Elegir archivo…»** (`.nds`, `.ndsevents.json`) y **«Elegir carpeta…»** (extracciones), porque Windows no ofrece un único selector para ambos;
  - una línea de **reconocimiento en vivo** («Pokémon Platino (USA, rev. 1) · Nintendo DS» o el motivo del rechazo y sus evidencias), con el botón **Abrir** activo solo para candidatas.
- El usuario **nunca elige la consola**: elige archivo o carpeta, o pega una ruta. La ruta elegida no se renombra, mueve ni modifica.
- Detección y apertura fuera del hilo de interfaz, con `UiProgress` y `BlockInteraction`; Ctrl+K, la navegación y los indicadores no cambian.
- Los accesos por plataforma del inicio y el «Abrir proyecto» de DS/Switch se mantienen sin cambios hasta que B2 esté probado.
- **Arrastrar y soltar (B3)** reutiliza el mismo método: `OpenDetectedAsync(ruta, origen)`; B2 lo deja público para esa llamada.

### 7.4 Nintendo 3DS: recuperación de guardados interrumpidos

- El detector no recupera nada y no se afirma que abrir sea «de solo lectura» cuando puede haber recuperación real.
- El paso 5 distingue los dos casos **sin escribir**: `Open(carpeta, false)`. Solo si hay un guardado interrumpido se avisa: «Este proyecto tiene un guardado interrumpido. Abrirlo lo **completará o revertirá** escribiendo en esa carpeta. ¿Continuar?». Con «No», se cancela sin cambios.
- La recuperación solo afecta a la carpeta del proyecto que se abre, nunca al proyecto actual.
- Cambio mínimo en el motor: una excepción tipada o `HasInterruptedSave(raíz)`, en lugar de reconocer el texto del mensaje actual. No cambia el comportamiento.
- Se prueba solo con **copias desechables** creadas por las pruebas, nunca con tu única copia.

### 7.5 Pruebas

| Prueba | Cubre |
|---|---|
| Suites de interfaz fuera de pantalla, con proyectos sintéticos de cada plataforma (el generador en disco de las demos de esta entrega sirve para 3DS) | cambios DS → 3DS, 3DS → Switch y Switch → DS; cada uno conserva el proyecto de las otras plataformas |
| Cancelar en cada paso | el proyecto, la vista y los cambios pendientes quedan idénticos |
| Fallo de apertura (ROM con cabecera dañada, 3DS incompleto, Switch con otra compilación) | mensaje específico, recursos cerrados (`NdsProject.Dispose`), el proyecto anterior intacto, sin navegación parcial |
| Cambios pendientes en cada plataforma al reemplazarla | confirmación, y el descarte solo tras abrir bien |
| Detección ambigua y rechazos | mensaje con evidencias; no se abre nada |
| Demos | advertencia y confirmación antes de abrir |
| Guardado interrumpido en una copia desechable | aviso, aceptar completa o revierte, rechazar no escribe |
| Sonda con ratón y teclado reales | diálogo, elegir y pegar rutas, cambio de plataforma y cancelación |
| Regresión | los accesos por plataforma, Ctrl+K, la navegación, DS v1/v2/v3, Switch 1.3.2 y los proyectos antiguos |
