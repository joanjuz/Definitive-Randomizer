# Iteración 86 — Encuentros Gen7 y bloqueo de interacción

2026-10-15. Rama feature/3ds-progressive-ui.

## Encuentros
ArchiveProjectEditor descomprimía todos los miembros de encdata en cada lectura. En Gen7 el mismo archivo contiene mapas/eventos; las tablas EA están únicamente en los índices 9 + 11*n. Se añadió una selección protegida de miembros: el editor Gen7 solo lee esas tablas, y el guardado ignora los miembros opacos sin descomprimirlos ni reemplazarlos. Gen6 y otros editores conservan su selección completa. Se mantiene la comprobación de fingerprint antes de leer/guardar.

La navegación prepara GameEncounterService mediante Task.Run y crea la vista WPF en el hilo UI; se preservan las opciones recordadas. Un identificador de solicitud y la identidad de sesión evitan aplicar una carga obsoleta. Durante la preparación se bloquea la interacción para evitar operaciones concurrentes sobre el proyecto, manteniendo el hilo UI libre.

Medición de solo lectura sobre Eris_01_Project, con 794 entradas: antes constructor 15,454 s / primera lectura 10,964 s; después constructor 3,806 s / primera lectura 0,414 s. No incluye el renderizado completo de WPF y no representa una garantía de tiempo en cualquier disco.

## Interfaz
Se eliminó «compatible con DEFINITIVO.json» de la etiqueta de selección de Global Template en Batch.

Rebuild y Batch (también su preflight) adquieren un bloqueo global de interacción mediante IDisposable. MainWindow deshabilita su contenido raíz: cabecera, navegación y controles internos. Los porcentajes/estado continúan actualizándose. Al terminar o fallar se libera el bloqueo, sin habilitar accidentalmente controles que ya estaban deshabilitados. Los bloqueos anidados no se liberan antes de tiempo.

## Pruebas
Debug local 0 errores / 79 advertencias. 18 comprobaciones Wild Gen7 (SM/USUM), incluidas dos nuevas que guardan sin decodificar ni cambiar un miembro comprimido ajeno a encuentros. 96 regresiones Batch, que incluyen conservación de encuentros comprimidos Gen6/Gen7 y de los miembros no modificados. 5 comprobaciones de interacción global: bloqueo de cabecera/contenido, anidación, recuperación de estados e idempotencia, liberación tras error. Estas últimas crean una ventana WPF sin mostrarla; no se controló el escritorio ni el emulador.

Candidato nuevo: D:\Progra\DefinitiveRandomizer-validation\20261015-iteration86-encuentros-bloqueo\DefinitiveRandomizer.exe. App SHA256 3A4627EB2C7BD0CA2DE28E6F6412C3DF0E494D7169F74ADE5E90EA37C5B59390. Conserva las optimizaciones Batch de 84 y los assets/DEFINITIVO del usuario.

El episodio de congelación de la escena de Lillie reportado antes fue retirado por el usuario: confirmó que Eris 1 y 2 pasan esa escena. No se cambió su template ni la ROM por ese episodio. Los proyectos originales y los archivos personales permanecen intactos. No hubo commit, push ni publicación.
