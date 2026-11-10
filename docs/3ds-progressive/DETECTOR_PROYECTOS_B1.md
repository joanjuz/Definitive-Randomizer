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

Cada resultado incluye la plataforma, el tipo (`DsRom`, `DsEventCheckpoint`, `Extracted3dsFolder`, `SwitchExtraction`), el juego, un mensaje para el usuario y la lista de evidencias.

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
| 3DS con 301 o 239 archivos (demos) | Unsupported | «Las demos no están en la matriz de compatibilidad pública.» |
| 3DS con otro número de archivos | Unsupported | «estructura RomFS no reconocida (300 archivos GARC)…» |
| 3DS sin ExeFS/code o sin RomFS | Incomplete | «falta ExeFS con el archivo «code»…» |
| Carpeta RomFS o ExeFS suelta | Incomplete | «Sube un nivel y selecciona la raíz» |
| Switch con otra compilación | Unsupported | «reconocido, pero su compilación no es la 1.3.2 acreditada» |
| Switch con otro Title ID o `main.npdm` dañado | Unsupported | «no es Pokémon Espada ni Escudo o está dañada…» |
| Switch sin `personal_total.bin`, `main` o `main.npdm` | Incomplete | «falta: exefs/main.npdm…» |
| Piezas completas de 3DS y Switch en la misma raíz | Ambiguous | «contiene a la vez una extracción de 3DS y una de Switch» |
| Carpeta con `.nds` | Unknown | «contiene 2 archivo(s) .nds, pero no es un proyecto. Selecciona directamente el archivo» |
| Ruta inexistente, bloqueada por otro programa o inválida | Inaccessible | «La ruta no existe…», «¿está abierta en otro programa…?» |

**Decisión que debes revisar:** las **demos de 3DS** (`ORASDEMO`, `SMDEMO`) se detectan como no admitidas porque no figuran en la matriz pública. Hoy el motor las abriría, así que, si B2 bloquea los candidatos «no admitidos», dejarían de abrirse. B2 puede permitirlas con un aviso si prefieres conservar ese comportamiento.

## 5. Pruebas

| Prueba | Resultado |
|---|---|
| `--project-detector-only` (sintética, en el CI) | **82** comprobaciones |
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

## 7. Plan de B2: botón único «Abrir proyecto»

Sin implementar aquí. Cambios previstos en `MainWindow` y en los espacios de trabajo, en un PR aparte:

1. **Un solo flujo de apertura** (`OpenProject_Click` y el equivalente de Nintendo DS/Switch), con una ventana de elección en dos pasos solo cuando Windows lo exige:
   - Un cuadro de «Abrir archivo» (filtro: `.nds`, `.ndsevents.json`, todos) y, en el mismo botón, una segunda opción «Elegir carpeta…» (`OpenFolderDialog`). El usuario nunca elige la consola.
2. **Detección** con `ProjectDetector.Detect` fuera del hilo de interfaz.
3. **Resultado:**
   - `Compatible` → cambia al espacio de trabajo de esa plataforma y llama al servicio de apertura **existente** (`NdsProject.Open`, `GameProjectSession.Open`, `SwordShieldProject.Open`).
   - Cualquier otro estado → ventana con el mensaje y las evidencias, sin cambiar nada.
4. **Cambios pendientes:** antes de abandonar el proyecto actual se reutilizan `ConfirmDiscard`, `ConfirmAuxiliaryLeave`, `CanLeave` y la confirmación de DS.
5. **El proyecto actual no se pierde nunca:** el proyecto nuevo se abre primero en una variable local. Solo se sustituye el actual cuando la apertura termina bien; si falla o se cancela, el actual sigue como estaba.
6. **Mensajes:** los de la sección 4, y los errores de los motores con una explicación en lugar de excepciones técnicas.
7. **Compatibilidad:** los botones por plataforma del inicio se mantienen como atajos hasta que B2 se pruebe. Se conservan las preferencias (`DefaultProjectFolder`) y el modo romhack de DS.
8. **Arrastrar y soltar (B3):** el flujo recibe una ruta y devuelve una detección, así que el arrastre solo tendrá que llamar a la misma función. B2 deja el método de apertura público para esa llamada.

Pruebas de B2:
- Suites de interfaz fuera de pantalla para cada plataforma y para el rechazo.
- Cancelar o fallar sin perder el proyecto actual.
- Cambios pendientes.
- Una sonda con ratón y teclado reales.
- Una comprobación del 3DS sobre una copia.
- Que los proyectos y checkpoints antiguos abren igual.
