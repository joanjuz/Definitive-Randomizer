# Iteración 37: compatibilidad y reparación de zonas de captura Gen6

La comprobación inicial reproduce el problema con las cuatro extracciones indicadas. X coincide con los 61 orígenes XY y Omega Ruby con los 174 ORAS. Y tiene 26 miembros incompatibles; Alpha Sapphire, 29. Todas las diferencias de versión se concentran en tablas de encuentros, incluyendo la tabla compartida ORAS; los mapas afectados coinciden entre las versiones de cada familia.

## Reparaciones implementadas

1. Variantes explícitas de origen y destino, verificadas mediante tamaño y SHA-256. Las variantes Y/AS se generan ejecutando el mismo programa de cambios sobre sus fuentes, comprobando cada salida y conservando bytes fuera de los fragmentos del parche. Antes de reparar EN, se conservaron 92 bytes propios de Y y dos de AS fuera de esos fragmentos. No se aceptan revisiones desconocidas ni se ignoran hashes. No se deduce la compatibilidad del nombre de la carpeta.
2. Conversión del orden de encuentros Gen6. El editor antiguo ordena primero los miembros sin compresión (zonas y EN), seguidos por dec_NNN. En las ROMs reales los mapas comprimidos están al principio del GARC; XY tiene zonas en el miembro 360, ORAS zonas en 536 y EN en 537. ArchiveProjectEditor convierte a orden lógico al leer y revierte a orden físico al guardar. Esto corrige nombres de mapas, selección de registros y escritura del espejo EN. GameEncounterService y los otros doce archivos protegidos permanecen idénticos; Gen7 no se reordena.
3. EN ORAS completo. La plantilla anterior añadía encuentros a mapas 007, 009, 010, 011, 016, 022 y 447, pero mantenía sus intervalos EN vacíos. Se insertan siete bloques de 260 bytes tomados de los respectivos mapas del resultado original, preservando los 150 bloques anteriores. Se recalculan los 537 punteros de las 536 zonas. EN pasa de 41.152 a 42.972 bytes. Se verifica cada bloque y sus límites contra el mapa correspondiente.
4. Migración desde plantilla anterior aplicada. Dos variantes adicionales de EN reconocen las salidas anteriores OR y AS y utilizan deltas de migración específicos. En copias con el parche anterior, la reparación modifica únicamente el archivo GARC de encuentros. Una segunda ejecución es un no-op. No se reemplaza geometría ni otros archivos de esas copias.

El motor nativo y el lector clásico de parches admiten variantes y deltas de migración. El lector clásico compila y se distribuye en LegacyBatch; la aplicación de extremo a extremo se prueba mediante el servicio nativo. No se declara una prueba completa de interfaz/guardado WinForms o Batch ROM.

Los manifiestos siguen siendo formato 2 y contienen hashes, fragmentos y deltas; no se agregan binarios completos de juegos. Las operaciones originales se conservan en todos los archivos salvo encdata/537.bin ORAS, cuyo resultado se corrige deliberadamente.

## Comprobación con datos reales en copias privadas

| Referencia | Archivos verificados | Tablas legibles antes → después | Miembros GARC excluidos preservados | EN sincronizados |
| --- | ---: | ---: | ---: | ---: |
| PokemonX | 61/61 | 53 → 56 | 1.331 | No aplica |
| PokemonY | 61/61 | 53 → 56 | 1.331 | No aplica |
| PokemonOR | 174/174 | 150 → 157 | 1.222 | 157/157 |
| PokemonAS | 174/174 | 150 → 157 | 1.222 | 157/157 |

Se comprueban vista previa sin escrituras, aplicación exacta de la propuesta, descompresión y hashes finales de todos los miembros seleccionados, conservación de todos los excluidos, segunda aplicación sin cambios e historial único de Catch Zones. En OR y AS también se edita, guarda y relee el mapa nuevo 007 mediante GameEncounterService y se vuelven a comprobar las 157 tablas EN. Se verifican once hashes de archivos fuente por juego y el inventario completo antes/después de cada comprobación; las extracciones originales no se modifican.

Las cifras provisionales ORAS 151 → 158 obtenidas antes de corregir el orden del editor incluían un miembro de zonas interpretado erróneamente como encuentro. La tabla anterior utiliza el resultado corregido 150 → 157.

El diagnóstico opcional reproducible del proyecto de pruebas es `--check-catch-real-copies <carpeta-nueva-de-copias> <raíces-originales...>`. Crea copias parciales privadas con los archivos necesarios; no son ROMs completas para jugar y no se incluyen en evidencias o distribución. El diagnóstico `--audit-catch-templates <raíz> <respaldo-v3.8>` sigue siendo de solo lectura y emite metadatos.

## Relación con las plantillas antiguas

En XY se mantienen idénticos los 60 destinos presentes del respaldo. El archivo adicional raw/a/1/4/9 sigue sin estar en ese respaldo, aunque sus dos fuentes y su resultado se verifican contra el manifiesto existente. En ORAS se mantienen idénticos 173 destinos; el destino EN cambia por la reparación descrita. La comprobación final informa cero fuentes incompatibles en las cuatro extracciones.

Las plantillas originales sustituyen encuentros dentro de las zonas afectadas. Se conserva ese comportamiento; esta reparación no promete conservar los encuentros vanilla dentro de fragmentos que la propia plantilla reemplaza. Los bytes ajenos al programa de cambios sí se preservan.

## Pruebas y límites

Debug: cero errores y 79 advertencias existentes. Regresión completa: 1.706 comprobaciones aprobadas con fixtures XY, ORAS, SM y USUM, incluyendo variantes, migraciones, rechazo de orígenes desconocidos, orden real de GARC y espejo EN al guardar. Los doce hashes protegidos se conservan. Se mantiene feature/3ds-progressive-ui, sin commits, push o publicación; archivos personales y Eris-Ultranova no se modifican.

Las evidencias de ITERACION_37_PRUEBAS son texto/JSON con metadatos, sin archivos de ROM. Las pruebas en copias restauran la compatibilidad técnica de Catch Zones para estas cuatro referencias; todavía no certifican captura efectiva, colisiones, scripts ni DexNav dentro del juego. Otras revisiones y ROMs ya randomizadas requieren una fuente compatible y continúan rechazándose si no coinciden con un origen o destino conocido. La migración completa de la aplicación sigue abierta y no se solicita aún validación final en juego.
