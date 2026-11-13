# Definitive Randomizer v1.0.0 — notas de versión

@DISTRIBUTION_STATUS@

Versión del paquete: **1.0.0**. Commit exacto compilado: `@BUILD_COMMIT@`. `VERSION.txt` y `BUILD_PROVENANCE.json` identifican la misma compilación.

@DISTRIBUTION_METADATA@

Primera versión **estable** de la aplicación WPF para Windows x64. Reúne la edición y randomización de **Nintendo 3DS**, **Nintendo DS** (Gen4 y Gen5) y **Espada/Escudo** (Switch) acreditadas en las versiones preliminares, y las acompaña de documentación de uso, respaldo y compatibilidad. No sustituye a la línea estable WinForms v4.17.1, que es independiente.

> `v0.15.0-preview.2` se publicó desde una rama de release; la corrección funcional de las mentas ya está integrada en `main`, que es la base de esta versión. Los SHA y versiones de las previews no son los de la estable.

## Qué incluye

- **Nintendo 3DS:** X/Y, Omega Ruby/Alpha Sapphire, Sol/Luna y Ultra Sol/Ultra Luna. Editores y randomizadores de Pokémon, movimientos, evoluciones, entrenadores, encuentros, objetos y tiendas; funciones Progressive (sobre todo en Ultra Sol/Ultra Luna); Global Template y Batch; historial con Deshacer; extracción y reconstrucción.
- **Mentas Gen7** en Ultra Sol y Ultra Luna: 21 mentas funcionales con instalación segura, historial y Deshacer, distribución opcional en tiendas y pools, iconos opcionales, Global Template y Batch. La instalación se basa en dependencias nativas concretas del parche (PR #57), no en hashes completos de `code.bin`.
- **Nintendo DS:** Diamante, Platino, HeartGold, Negro, Blanco (ES) y Blanco 2 (perfiles exactos de ROM original). Entrenadores, con tarjetas y edición de equipo compartidas con 3DS (PR #56), encuentros, iniciales, objetos y tiendas, datos, evoluciones, estáticos e intercambios, movimientos huevo y tutores, ajustes, Global y Batch. Modo romhack en beta.
- **Proyectos DS persistentes:** al abrir una ROM se crea o se retoma `Juego.nds.drproj`, que se guarda solo tras cada operación confirmada, con respaldos y apertura en solo lectura de un proyecto dañado (PR #74–#83).
- **Editor de eventos y mapas DS (Platino CPUE/1):** mover NPC (campos o ratón), añadir, duplicar, copiar y pegar NPC, copiar una Enfermera Joy con su comportamiento y compartir el script de un NPC de mensaje, con checkpoints y restauración; lienzo de matrices, terreno y estructuras de solo lectura (PR #62–#71, #89).
- **Apertura universal:** un único «Abrir proyecto…» detecta DS, 3DS o Espada/Escudo y abre su editor; proyectos recientes y arrastrar y soltar (PR #72, #73, #79, #80).
- **Ultra Sol/Ultra Luna:** **Mega Ability Sync** para combates entre dos juegos con el parche (PR #104, #105; confirmado en un combate real, #106); Player Level Caps con Sync Trainer Caps corregido para Olivia (#90) y reproducible desde Global Template (#94); diagnóstico y comparador de megaevoluciones (#97, #100); informes de capacidades de solo lectura de Player Level Caps, Persistent Items y Shiny Rate (#96, #98, #100).
- **Perfiles compartibles** para los cuatro juegos 3DS (formato 1, PR #95, #98).
- **Tiendas 3DS en tres columnas** (lista, inventario y acciones; PR #88).
- **Espada y Escudo 1.3.2:** editores de datos, entrenadores, encuentros, objetos, tiendas, movimientos/Level Up/evoluciones, compatibilidad MT/TR y tutores, Global/Batch de mods y runtime propio por juego. Exporta un mod en formato Ryujinx.
- **Seguridad de guardado:** escritura transaccional con recuperación de guardados interrumpidos, historial legible y Deshacer (3DS), registro del randomizado.
- **Definitive Game Data v1** (Ultra Sol/Ultra Luna) para Progressive Tracker, con los bloques opcionales `playerLevelCaps` (#91–#93) y `profileProvenance` (#101–#103).
- **Pulido de interfaz para la estable** (detalle en [PULIDO_ESTABLE_1.0.0.md](PULIDO_ESTABLE_1.0.0.md)):
  - MT y tutores separa «Acciones para el proyecto actual» de «Opciones para Global Template / Batch».
  - El modo de trabajo elegido (simplificado o completo) se recuerda y se aplica al abrir proyectos.
  - Textos de etiquetas legibles sobre el tema oscuro (por ejemplo, en «Abrir proyecto»).
  - Inicio usa solo la apertura universal (sin accesos separados a DS y Switch, que siguen en Ctrl+K).
  - Deshacer permanece en Historial con el resultado visible.
  - Tarjeta propia de Mega Ability Sync en Mecánicas y CRO con su estado.
- **Documentación nueva:**
  - [Primeros pasos](PRIMEROS_PASOS.md)
  - [Respaldo y recuperación](RESPALDO_Y_RECUPERACION.md)
  - [Matriz pública de compatibilidad](COMPATIBILIDAD_PUBLICA.md)
  - [Limitaciones de la estable](LIMITACIONES_ESTABLE.md)

## Qué no incluye

Editor de eventos DS fuera de Platino CPUE/1, y edición de scripts arbitrarios, warps, geometría o entrenadores nuevos · parches de estabilidad en combates en línea (en investigación) · mentas fuera de Ultra Sol/Ultra Luna · caramelos raros y repelentes infinitos · perfiles de ROM hacks y biblioteca comunitaria · Negro 2, SoulSilver y Perla · consola real, Atmosphère y DLC en Switch. Detalle en `LIMITACIONES_ESTABLE.md`, incluida en la carpeta `docs` del paquete y junto a estas notas en el repositorio.

## Cómo se comprobó

Tres niveles de evidencia, indicados por separado en `COMPATIBILIDAD_PUBLICA.md`:

- **Automática:** suites del repositorio y CI (`build-wpf`) sobre el commit compilado, incluida la suite `--stable-polish-only` de las correcciones de interfaz y la auditoría visual de las cuatro familias 3DS. El CI del PR final se registra por separado en ese PR.
- **Con archivos reales:** copias de juegos del propietario, sin modificar los originales.
- **Dentro del juego**, por el propietario y limitada a lo indicado:
  - DS: fases 0–14 (31/10/2026), edición de entrenadores en Platino, eventos de Platino (mover/intercambiar NPC y una Enfermera Joy copiada) y proyecto persistente (Platino).
  - 3DS: mentas en Ultra Sol con ROM randomizada; Mega Ability Sync en un combate real por enlace entre dos Ultra Luna con el parche (caso principal).
  - Switch: Espada y Escudo en Ryujinx.

Las mentas en Ultra Luna, la consola real de Switch, Atmosphère, DLC y una partida completa de cualquier juego **no** están comprobados jugando. Las correcciones de interfaz de esta versión se comprobaron con pruebas automáticas fuera de pantalla y con el arranque del paquete; no son una prueba en juego.

RC.1 tuvo 525 hashes verificados y arranque WPF del ZIP exacto con cierre normal. Esas cifras pertenecen a RC.1: para cada distribución 1.0.0 se verifican compilación, contenido, dependencias, hashes y arranque desde una extracción independiente. El informe de validación de esa entrega identifica sus resultados y SHA-256. Ni las pruebas sintéticas ni el arranque sustituyen la validación dentro del juego.

### Aceptación técnica (09/11/2026)

Sobre `main` tras integrar los PR #56 y #57 se revisó la evidencia existente y se ejecutaron tres casos nuevos de guardar → reabrir → Deshacer con copias aisladas de proyectos reales, sin tocar el origen:

| Juego | Archivos comparados | Resultado |
|---|---:|---|
| Pokémon X | 659 | 9 comprobaciones aprobadas; restauración exacta |
| Pokémon Omega Ruby | 661 | 9 comprobaciones aprobadas; restauración exacta |
| Pokémon Sol | 671 | 9 comprobaciones aprobadas; restauración exacta |

Total: 27 comprobaciones aprobadas, ninguna fallida. Cada prueba cubre un solo título: **no se atribuye a Y, Alpha Sapphire ni Luna**, cuya evidencia es la histórica. No se repitió la reconstrucción ni se probó en juego. Para DS y Switch no se exige el Deshacer transaccional de 3DS: se acreditó que la salida (ROM nueva o mod) se genera, se relee y deja el origen intacto. Conclusión de la aceptación: no se identificó un defecto vigente y reproducido de corrupción o pérdida de datos dentro del alcance. Esto **no** publica ni certifica la estable, y la candidata debe construirse y probarse como paquete exacto.

## Instalación

1. Extrae el ZIP **completo** en una carpeta nueva de Windows x64. No lo ejecutes desde el ZIP ni lo copies sobre una versión anterior.
2. Abre `DefinitiveRandomizer.exe`. No requiere instalar .NET.
3. Mantén juntas `LegacyBatch`, `Tools`, `Templates`, `custom_balance_templates`, `catch_zone_patches`, las licencias y las demás dependencias.
4. Respalda proyectos, recetas y partidas. Trabaja con copias; no edites ROMs ni extracciones originales.

Los ejecutables **no tienen firma digital**: Windows o SmartScreen pueden mostrar advertencias. Comprueba el origen y el SHA-256 con `SHA256SUMS.txt`. `VERSION.txt` identifica versión y commit, y `BUILD_PROVENANCE.json` documenta la compilación y el modo de distribución. No se incluyen ROMs, extracciones, partidas, recetas privadas ni gráficos oficiales. Mantén también la carpeta `docs` con las guías y `VALIDATION_REPORT.md`; el informe externo de entrega acredita el ZIP exacto sin una referencia circular a su propio hash.

## Limitaciones principales

- Mentas solo en Ultra Sol/Ultra Luna; Ultra Luna sin prueba jugable de mentas.
- Mega Ability Sync necesita el parche en los dos juegos; sin aviso si el otro no lo tiene; varios casos sin confirmar en combate. La estabilidad en línea entre ROM randomizadas de forma distinta no está garantizada.
- Editor de eventos DS solo en Platino CPUE/1.
- Incidencias USUM sin resolver: cuelgue al entregar los pétalos (causa sin determinar, sin reproducción nueva; se espera una partida previa) y diagnóstico del movimiento Z en el primer combate de Tilo.
- Deshacer solo en 3DS, solo los 8 últimos cambios, y no revierte partidas guardadas ni ROM ya generadas.
- La recuperación de guardados no cubre todo fallo físico ni toda edición externa posterior.
- DS: romhack en beta; solo perfiles exactos.
- Switch: solo 1.3.2; sin hardware real, Atmosphère ni DLC; sin protección de runtime para dobles con un solo Pokémon utilizable en combates posteriores.

## Reportar problemas

Indica versión y commit, juego/región/revisión/idioma, pasos, semilla y mensajes de error. **No adjuntes ROMs, extracciones ni partidas.** Ver `RESPALDO_Y_RECUPERACION.md` en las guías incluidas.

## Identidad de la distribución

@DISTRIBUTION_WORKFLOW@

La ROM específica del tester de mentas sigue pendiente. Las regiones y revisiones concretas de los juegos 3DS y de `IRBO` no están determinadas y no se enumeran. Esas limitaciones se mantienen sin atribuir comprobaciones nuevas.
