# Hoja de ruta: editor de mapas, apertura universal y 1.0.0

Actualizada el 10/11/2026 con las prioridades del propietario. Este documento **planifica**: ninguna de las fases B–H está iniciada. Cada fase se entrega en PR pequeños e independientes.

**Prioridad:** funcionalidad comprobada, facilidad de uso y paridad real antes que complejidad visual.

Niveles de evidencia usados en todo el documento:

| Nivel | Significado |
|---|---|
| **I** | implementado en `main` o en un PR abierto |
| **A** | pruebas automáticas (sintéticas o de interfaz fuera de pantalla) |
| **R** | sonda con archivos reales (copia privada, origen intacto) |
| **J** | comprobación jugable del propietario en el emulador |
| **P** | propuesto, sin implementar |

## 1. Estado confirmado (editor de eventos de Platino CPUE/1)

| Función | Estado | Dónde |
|---|---|---|
| Lectura de mapas, matriz, cabeceras y eventos; lienzo esquemático | I A R | PR #62, #63 |
| Colisión de terreno (`land_data`) y consulta espacial | I A R | PR #64 |
| Mover NPC (arrastrar, coordenadas) dentro del mapa, incluidos entrenadores | I A R J | PR #65, #68 |
| Añadir y duplicar NPC (script 0, flag 0), colocación directa en el mapa | I A R J | PR #66, #68 |
| Copia básica entre mapas (apariencia), portapapeles, menú contextual | I A R J | PR #68 |
| Eliminar NPC añadidos por el editor (los originales están protegidos) | I A R | PR #68 |
| **Enfermera Joy con comportamiento** (script local nuevo con el ID reasignado) | I A R **J** | PR #69, abierto, con visto bueno funcional y sin fusionar |
| Compartir script de mensaje simple en el mismo archivo | I A | PR #69 (sin prueba jugable propia) |
| Checkpoints v1/v2/v3, exportación verificada, restauración exacta | I A R J | PR #66, #68, #69 |
| Capa de estructuras (*map props*) de solo lectura | I A R | PR #67, abierto y pendiente de integración |

Evidencia jugable del propietario (10/11/2026, PR #69): la enfermera copiada de Jubileo a Hojaverde aparece, dialoga y cura (PS y PP) y la conversación termina bien. La original de Jubileo, los demás NPC y eventos comprobados, la exportación y la restauración funcionan. Esa evidencia cubre **solo ese caso en Platino CPUE/1**.

## 2. Lo que falta en Platino (fase A)

| Hueco | Por qué importa | Propuesta |
|---|---|---|
| No hay un botón para **añadir una enfermera** sin ir a buscarla a un Centro Pokémon | el uso pedido es «poner una Joy donde quiera» | botón «Añadir enfermera» con un gráfico tomado de una enfermera real de la ROM y el mismo enlace de script acreditado |
| Al mover un **entrenador** no se ve ni se valida su **línea de visión** | puede quedar tapada por terreno o por otro NPC, o cortar un camino obligatorio | dibujar el alcance de visión (campo de visión y dirección), avisar si hay bloqueo o solapamiento y mostrar en el inspector su ID y clase (solo lectura) |
| No se puede mover el NPC seleccionado **con el teclado** (solo Ctrl+C/X/V/D y Supr) | accesibilidad y precisión | flechas = mover una casilla con propuesta; Esc = cancelar colocación; orden de foco revisado |
| Ventanas pequeñas: las pruebas cubren 1100×760 y 760×760, no 1024×640 ni 150 % de escala | portátiles | pruebas de interfaz con esas medidas y DPI, y una sonda con entrada real |
| Sesión: para continuar hay que **reabrir a mano** el último checkpoint | facilidad de uso | «Reabrir último checkpoint» y lista de recientes, con la misma validación de reproducción |
| «Compartir script» sin prueba jugable | evidencia | prueba del propietario (duplicar un NPC de mensaje en el mismo mapa) |
| Sin «Rehacer» | comodidad | solo si no complica el modelo de checkpoints inmutables (opcional) |

Fuera de la fase A: editor universal de scripts, entrenadores nuevos y renderer.

## 3. Matriz inicial de paridad del editor de mapas DS

Perfiles que el randomizer ya admite: `ADAE`/5, `CPUE`/1, `IPKE`/0, `IRBO`/0, `IRAS`/0 e `IRDO`/0. Perla, SoulSilver y Negro 2 no tienen perfil y solo se añadirán tras acreditarlo.

| Operación | Platino | Diamante | HeartGold | Negro | Blanco | Blanco 2 |
|---|---|---|---|---|---|---|
| Lectura de mapas y eventos | I A R | P | P | P | P | P |
| Colisión / consulta espacial | I A R | P | P | P | P | P |
| Seleccionar y mover NPC, coordenadas | I A R J | P | P | P | P | P |
| Añadir y duplicar NPC | I A R J | P | P | P | P | P |
| Copia básica entre mapas | I A R J | P | P | P | P | P |
| Enfermera con comportamiento | I A R J | P (patrón por acreditar) | P (patrón por acreditar) | P (scripts Gen V distintos) | P | P |
| Checkpoints, exportación, restauración | I A R J | P | P | P | P | P |

Notas para los adaptadores, todas **por acreditar en cada copia** antes de escribir:

- **Gen IV (Diamante, HeartGold).** La familia de formatos es la de Platino, pero las rutas de los archivos, la tabla de cabeceras, el tamaño de los registros y los comandos de script pueden variar entre juegos. Ninguna dirección de Platino se traslada a otro juego, y cada adaptador fija sus CRC32 en el perfil del juego.
- **Gen V (Negro, Blanco, Blanco 2).** Los formatos de eventos, cabeceras y scripts son propios de la generación. No se asume que los scripts funcionen como en Platino. El ARM9 de estos juegos (DSi-enhanced) no se modifica; la edición de eventos se limita a sus archivos de datos.

## 4. PR pequeños propuestos

### Fase A: completar Platino

| PR | Contenido | Aceptación |
|---|---|---|
| A1 | «Añadir enfermera» directo (plantilla tomada de una enfermera real y enlace `AppendScript` existente) | pruebas sintéticas y de interfaz, sonda real, prueba jugable en un mapa sin Centro Pokémon |
| A2 | Entrenadores existentes: alcance de visión en el lienzo, avisos al moverlos e información de solo lectura en el inspector | pruebas de modelo e interfaz, sonda real con todos los entrenadores de Platino, prueba jugable: el entrenador movido sigue viendo y combatiendo |
| A3 | Teclado y ventanas pequeñas (flechas, Esc, foco, 1024×640, 150 %) | suites de interfaz y sonda con entrada real |
| A4 | Reabrir el último checkpoint y checkpoints recientes | checkpoints v1/v2/v3 reabren igual; uno manipulado se rechaza |

### Fase C: Gen IV y Gen V (después de A y de B)

| PR | Contenido |
|---|---|
| C0 | Extraer una interfaz de adaptador de eventos a partir de `PlatinumEvents` **sin cambiar el comportamiento**: las suites de Platino dan los mismos resultados. Los checkpoints registran el perfil del juego de forma compatible; los antiguos se interpretan como Platino. |
| C1 / C2 | Diamante: lectura (mapas, eventos, colisión) y luego edición (mover, añadir, duplicar, copia básica, checkpoints) |
| C3 / C4 | HeartGold: lectura y edición |
| C5 | Enfermera Gen IV en Diamante y HeartGold, solo si el patrón se acredita igual de exacto |
| C6–C8 | Negro/Blanco (un adaptador, dos perfiles), Blanco 2, enfermera Gen V si es viable |

Cada PR de C actualiza la matriz de la sección 3 con su evidencia. Solo se marca J con la confirmación del propietario.

## 5. Apertura universal de proyectos (fase B, requisito de 1.0.0)

### Situación actual

- **Abrir proyecto…** en la cabecera depende del espacio de trabajo visible. En 3DS pide una carpeta raíz (RomFS + ExeFS, `GameProjectSession.Open`). En DS abre `.nds` o `.ndsevents.json` (`NdsProject.Open`). En Espada/Escudo abre una extracción (`SwordShieldProject.Open`).
- Hoy hay que **elegir primero la plataforma** en la navegación.

### Propuesta

1. **Detector** (servicio puro, sin interfaz): a partir de una ruta devuelve un candidato (plataforma, tipo de entrada, juego y motivo) con lecturas mínimas: cabecera NDS, estructura de carpetas, marcadores que ya usan los proyectos 3DS y Switch, y el formato del checkpoint DS. Si la entrada es ambigua o desconocida, la rechaza con una explicación y no adivina.
2. El detector **solo enruta**. La identidad, la revisión y el perfil los siguen validando las funciones `Open` existentes, **sin relajar ninguna comprobación**: una ROM o extracción no admitida se rechaza exactamente igual que hoy.
3. **Un único «Abrir proyecto…»** con dos opciones, archivo (`.nds`, checkpoint DS) o carpeta (3DS, Espada/Escudo). Antes de cambiar de espacio de trabajo pide las confirmaciones que ya existen para cambios pendientes (3DS, DS y Switch). Luego carga el espacio correcto.
4. **Arrastrar y soltar** sobre la ventana principal, con el mismo detector y las mismas confirmaciones. Va en un PR aparte y solo si encaja con el bloqueo de interacción actual.

| PR | Contenido | Aceptación |
|---|---|---|
| B1 | Detector y pruebas: entradas sintéticas de cada tipo, entradas ambiguas, desconocidas, ROM con código no admitido y carpeta incompleta | suite nueva sin interfaz; sondas reales con los seis perfiles DS, una carpeta 3DS y las extracciones de Espada y Escudo, sin escribir nada |
| B2 | Abrir proyecto universal en la ventana principal | suites de interfaz y sonda con entrada real; los proyectos y checkpoints antiguos abren igual; los cambios pendientes nunca se pierden al cambiar de plataforma |
| B3 | Arrastrar y soltar | sonda con entrada real; rechazo claro de archivos no admitidos |
| B4 | Documentación (primeros pasos, compatibilidad pública, notas de 1.0.0) | revisión del propietario |

## 6. Fases posteriores (sin iniciar)

- **D. Paridad del editor con 3DS** (X/Y, ROZA, Sol/Luna, USUL): primero edición de entidades y eventos existentes, con adaptadores por familia e interfaz común. Empieza cuando el editor DS esté consolidado.
- **E. Progressive Tracker:** ampliar Definitive Game Data sin romper v1 para exportar los Level Caps manuales. En Tracker: límite actual, etapa, siguiente límite, selección manual de la etapa si no se detecta con fiabilidad, aislamiento por partida y juego, y distinción entre límites importados y manuales. Los Level Caps de entrenadores no se confunden con límites del jugador.
- **F. Perfiles compartibles:** exportar e importar perfiles de randomización y configuración versionados, con previsualización, aplicación selectiva, conflictos y reproducibilidad, sin ROMs ni recursos comerciales. Primero se investiga la reutilización de Global Template y Batch. El Hub en línea viene después.
- **G. Paridad de randomización DS frente a 3DS:** partir de `NDS_PARIDAD_INTERFAZ_3DS.md`, `NDS_ENTRENADORES_PARIDAD.md`, `PARIDAD_ACCIONES.csv` y `COMPATIBILIDAD_PUBLICA.md`; listar solo las diferencias concretas y priorizar las sencillas. UPR-ZX sirve de referencia para opciones de datos y DSPRE para mapas.
- **H. Editor gráfico auténtico:** terreno, modelos, texturas, edificios y sprites. Antes, decidir si el PR #67 (estructuras de solo lectura) aporta valor inmediato, por ejemplo para ver dónde hay edificios al colocar NPC, e integrarlo por separado.
- **Entrenadores nuevos:** diseño en `NDS_NPC_SCRIPTS_PHASE6.md` (ranuras DUMMY, datos y equipo, textos, flag de derrota). **Pospuestos.**

## 7. Dependencias, riesgos y criterios generales

| Dependencia o riesgo | Mitigación |
|---|---|
| El PR #69 introduce el checkpoint v3; las fases A y C se apoyan en él | fusionarlo, con orden del propietario, antes de A1 |
| El PR #67 está en conflicto con `main` | integrarlo en un PR propio o descartarlo, sin mezclarlo con A ni con B |
| Trasladar supuestos de Platino a otros juegos | adaptador y perfil por juego; sondas reales antes de escribir; marcado J solo con confirmación |
| El abrir universal podría saltarse validaciones | el detector no abre nada; las funciones `Open` existentes siguen siendo la única puerta |
| Cambios pendientes al cambiar de plataforma | se reutilizan `ConfirmDiscard`, `ConfirmAuxiliaryLeave`, `CanLeave` y la confirmación DS existentes |
| Espacio en disco y worktrees antiguos | un solo worktree activo por PR; nada de compilaciones duplicadas |

Criterios de aceptación de todo PR: compilación sin errores, suites propias y de regresión DS y visual, sonda real cuando toque archivos del juego (origen con SHA-256 intacto), CI aprobado, documentación e índice actualizados, sin ROMs ni extracciones en Git y **sin fusionar sin orden expresa**.

**1.0.0** necesita la fase B completa (B1–B4), sin corrupción conocida, con la compatibilidad documentada y la recuperación probada. No espera a las fases C, D ni H. Los módulos experimentales, como el editor de eventos DS, siguen identificados como tales.

## 8. Ya implementado: no repetir

- Randomización DS de los seis perfiles (fases 0–14, confirmadas en juego) y editor de entrenadores compartido con 3DS (PR #56, comprobado en combate en Platino).
- Paridad de interfaz DS ya documentada en `NDS_PARIDAD_INTERFAZ_3DS.md`, incluidos Level Caps y Move Rules configurables en el randomizer.
- Global Template y Batch de 3DS, DS y Switch, con sus esquemas, historial y recuperación transaccional de 3DS.
- Definitive Game Data v1 de Ultra Sol/Ultra Luna para Tracker (fase E lo **amplía**, no lo rehace).
- Las tres aperturas por plataforma, que el detector **reutiliza**.
- Editor de eventos de Platino (sección 1), enlaces de script y checkpoint v3 (PR #69), y capa de estructuras (PR #67).
