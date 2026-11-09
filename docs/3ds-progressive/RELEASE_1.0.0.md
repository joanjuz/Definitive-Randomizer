# Definitive Randomizer v1.0.0 — preparada, todavía no publicada

> **v1.0.0 está preparada, pero NO publicada.** La distribución generada desde la rama del PR es una candidata privada. Estas notas no autorizan integración, tag ni publicación. Después de integrar, será obligatorio construir y validar de nuevo desde el HEAD real de `main`; el commit de esta candidata no debe presentarse como el de una versión pública.

Versión del paquete: **1.0.0**. Commit exacto compilado: `@BUILD_COMMIT@` (se resuelve al empaquetar, junto con `VERSION.txt` y `BUILD_PROVENANCE.json`). La procedencia mantiene `publicRelease=false` para esta entrega privada.

Primera versión **estable** de la aplicación WPF para Windows x64. Reúne la edición y randomización de **Nintendo 3DS**, **Nintendo DS** (Gen4 y Gen5) y **Espada/Escudo** (Switch) acreditadas en las versiones preliminares, y las acompaña de documentación de uso, respaldo y compatibilidad. No sustituye a la línea estable WinForms v4.17.1, que es independiente.

> `v0.15.0-preview.2` se publicó desde una rama de release; la corrección funcional de las mentas ya está integrada en `main`, que es la base de esta versión. Los SHA y versiones de las previews no son los de la estable.

## Qué incluye

- **Nintendo 3DS:** X/Y, Omega Ruby/Alpha Sapphire, Sol/Luna y Ultra Sol/Ultra Luna. Editores y randomizadores de Pokémon, movimientos, evoluciones, entrenadores, encuentros, objetos y tiendas; funciones Progressive (sobre todo en Ultra Sol/Ultra Luna); Global Template y Batch; historial con Deshacer; extracción y reconstrucción.
- **Mentas Gen7** en Ultra Sol y Ultra Luna: 21 mentas funcionales con instalación segura, historial y Deshacer, distribución opcional en tiendas y pools, iconos opcionales, Global Template y Batch. La instalación se basa en dependencias nativas concretas del parche (PR #57), no en hashes completos de `code.bin`.
- **Nintendo DS:** Diamante, Platino, HeartGold, Negro, Blanco (ES) y Blanco 2 (perfiles exactos de ROM original). Entrenadores, con tarjetas y edición de equipo compartidas con 3DS (PR #56), encuentros, iniciales, objetos y tiendas, datos, evoluciones, estáticos e intercambios, movimientos huevo y tutores, ajustes, Global y Batch. Modo romhack en beta.
- **Espada y Escudo 1.3.2:** editores de datos, entrenadores, encuentros, objetos, tiendas, movimientos/Level Up/evoluciones, compatibilidad MT/TR y tutores, Global/Batch de mods y runtime propio por juego. Exporta un mod en formato Ryujinx.
- **Seguridad de guardado:** escritura transaccional con recuperación de guardados interrumpidos, historial legible y Deshacer (3DS), registro del randomizado.
- **Definitive Game Data v1** (Ultra Sol/Ultra Luna) para Progressive Tracker.
- **Documentación nueva:**
  - [Primeros pasos](PRIMEROS_PASOS.md)
  - [Respaldo y recuperación](RESPALDO_Y_RECUPERACION.md)
  - [Matriz pública de compatibilidad](COMPATIBILIDAD_PUBLICA.md)
  - [Limitaciones de la estable](LIMITACIONES_ESTABLE.md)

## Qué no incluye

Editor de eventos y mapas de DS (PR #55 excluido) · mentas fuera de Ultra Sol/Ultra Luna · caramelos raros y repelentes infinitos · perfiles de ROM hacks y biblioteca comunitaria · Negro 2, SoulSilver y Perla · consola real, Atmosphère y DLC en Switch. Detalle en `LIMITACIONES_ESTABLE.md`, incluida en la carpeta `docs` del paquete y junto a estas notas en el repositorio.

## Cómo se comprobó

Tres niveles de evidencia, indicados por separado en `COMPATIBILIDAD_PUBLICA.md`:

- **Automática:** suites del repositorio y CI. Se reutilizan las 200 comprobaciones focalizadas aprobadas para RC.1 (13 de paridad LegacyBatch y 187 de interfaz WPF); esta preparación no cambia los motores ni repite la aceptación funcional. El CI del PR final se registra por separado en ese PR.
- **Con archivos reales:** copias de juegos del propietario, sin modificar los originales.
- **Dentro del juego**, por el propietario y limitada a lo indicado:
  - DS: fases 0–14 (31/10/2026) y edición de entrenadores en Platino.
  - 3DS: mentas en Ultra Sol con ROM randomizada.
  - Switch: Espada y Escudo en Ryujinx.

Ultra Luna, consola real de Switch, Atmosphère, DLC y una partida completa de cualquier juego **no** están comprobados jugando.

RC.1 tuvo 525 hashes verificados y arranque WPF del ZIP exacto con cierre normal. Esas cifras pertenecen a RC.1: para el ZIP privado 1.0.0 se verifican de nuevo compilación, contenido, dependencias, hashes y arranque desde una extracción independiente. El informe privado de entrega identifica sus resultados y SHA-256. Ni las pruebas sintéticas ni el arranque sustituyen la validación dentro del juego.

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

Los ejecutables **no tienen firma digital**: Windows o SmartScreen pueden mostrar advertencias. Comprueba el origen y el SHA-256 con `SHA256SUMS.txt`. `VERSION.txt` identifica versión y commit, y `BUILD_PROVENANCE.json` documenta la compilación privada. No se incluyen ROMs, extracciones, partidas, recetas privadas ni gráficos oficiales. Mantén también la carpeta `docs` con las guías y `VALIDATION_REPORT.md`; el informe externo de entrega acredita el ZIP exacto sin una referencia circular a su propio hash.

## Limitaciones principales

- Mentas solo en Ultra Sol/Ultra Luna; Ultra Luna sin prueba jugable.
- Incidencias USUM sin resolver: cuelgue al entregar los pétalos (causa sin determinar, sin reproducción nueva; se espera una partida previa) y diagnóstico del movimiento Z en el primer combate de Tilo.
- Deshacer solo en 3DS, solo los 8 últimos cambios, y no revierte partidas guardadas ni ROM ya generadas.
- La recuperación de guardados no cubre todo fallo físico ni toda edición externa posterior.
- DS: romhack en beta; solo perfiles exactos.
- Switch: solo 1.3.2; sin hardware real, Atmosphère ni DLC; sin protección de runtime para dobles con un solo Pokémon utilizable en combates posteriores.

## Reportar problemas

Indica versión y commit, juego/región/revisión/idioma, pasos, semilla y mensajes de error. **No adjuntes ROMs, extracciones ni partidas.** Ver `RESPALDO_Y_RECUPERACION.md` en las guías incluidas.

## Condiciones de una futura publicación

La integración y la publicación requieren autorizaciones expresas independientes del propietario. Una vez integrado el PR, hay que generar de nuevo el paquete desde el HEAD real de `main`, resolver su commit en los metadatos y notas, verificar todos los hashes y probar ese ZIP exacto. No se reutiliza ni se renombra como público el paquete privado de la rama del PR.

La ROM específica del tester de mentas sigue pendiente. Las regiones y revisiones concretas de los juegos 3DS y de `IRBO` no están determinadas y no se enumeran. Esas limitaciones se mantienen sin atribuir comprobaciones nuevas.
