# Iteración 80 · Global/Batch y etiquetas de textos

## Cambios

- MT/MO Gen6: el escritor de descripciones conserva espacios iniciales/finales mediante una opción explícita del códec TextFile. El modo vanilla conserva su comportamiento anterior por defecto. Se mantiene la comprobación de relectura de cada descripción; ante un fallo se indica el objeto y el contenido que no coincide.
- Cerrojo Feérico USUM: al reaplicar la mecánica se validan sus 60 bytes de instrucciones, hasta POP. Los 28 bytes de relleno posteriores no son instrucciones y pueden ser reutilizados por el asignador de código de Giro Vil. Se conservan firmas, destino de handler, evento y todas las instrucciones. Se reprodujo el fallo al instalar todos los efectos sobre Battle.cro vanilla y volver a instalarlos.
- Batch: las validaciones previas pesadas se ejecutan fuera del hilo WPF, tanto al comprobar como al construir. El ejecutor informa del número real de archivos copiados y archivo actual durante la copia.
- Progreso: la lectura permite reemplazo atómico del snapshot en Windows. Un bloqueo temporal de ese canal de notificaciones no debe abortar la construcción. Se probó reteniendo deliberadamente un lector sin permiso de borrado y comprobando la recuperación posterior.
- Textos: el listado utiliza etiquetas y cantidad de líneas, sin el extracto largo. Se puede filtrar por etiqueta y añadir/quitar etiquetas del archivo seleccionado. Intercambios, NPCs, Historia principal y Diálogos importantes se asignan manualmente cuando se identifica el contenido. Las referencias verificadas de Game Text aportan etiquetas de nombres/descripciones, combate y Pokédex. El resto aparece Sin clasificar; no se deduce el orden argumental del número de archivo. La búsqueda por contenido y el salto a archivo/línea se conservan.
- Las etiquetas se guardan en LocalAppData/DefinitiveRandomizer/TextTags, separadas por raíz de proyecto, banco e idioma. No se insertan en la ROM.

## Pruebas

Debug App/Core: 0 errores, 79 advertencias. Ejecutor: 0 errores, 57 advertencias.

Suite general: 3964 comprobaciones aprobadas tras las correcciones y optimizaciones. Tras la última mejora de liberación de buffers: 88 comprobaciones específicas de Batch y 16 de Wild Gen7 aprobadas; además, 56 de textos aprobadas sin cambios posteriores en su lógica. Prueba adicional real de bloqueo y recuperación del progreso aprobada. Persistencia/reapertura de etiquetas en una copia de ORAS: aprobada, SHA-256 del banco sin cambios.

Las recetas VALIDACION_ORAS.json y VALIDACION_USUM.json de la iteración 74 se ejecutan sobre copias físicas independientes. La primera repetición ORAS detectó el fallo adicional del snapshot de progreso y se detuvo; se corrigió y se inició otra repetición. Resultado final: ambas recetas completaron sus acciones y generaron una ROM con verificación de tamaño y SHA-256. ORAS: 24 acciones, Zafiro Alfa, 1 890 459 648 bytes. USUM: 34 acciones, Ultra Sol; consultar el manifiesto adjunto. No se ha probado su arranque ni su comportamiento in-game.

La revisión automática rechazó una ejecución que abría los originales antes de invocar Batch por no demostrar suficiente aislamiento. Se sustituyó por copias físicas creadas previamente bajo Integracion80-copias. Ningún original se usa como proyecto modificable de las pruebas.

No se controló el escritorio ni se manejó Lime3DS. La revisión visual y la validación in-game corresponden al usuario. Sin commits, push ni release publicada.

## Optimización y uso final

Se eliminan vistas previas redundantes de módulos ajenos al ejecutar una acción de Master/Field, y la segunda generación innecesaria de Wild en replay. Se mantienen la validación completa inicial y las validaciones del documento/opciones de la acción aplicada. La app libera explícitamente los grandes buffers de validación antes de iniciar el ejecutor.

El ejecutor usa TEMP/TMP dentro de una carpeta GUID del disco de salida y la limpia al terminar. PackGARC limpia su temporal incluso si falla una escritura. Una repetición intermedia USUM se detuvo por falta de espacio en C:; la repetición final, con temporales en D:, completó la reconstrucción. Se corrigieron también assets omitidos y referencias antiguas del arnés antes de la repetición final, verificando hashes de sus DLL.

ROMs generadas para comprobación manual:
- ORAS: D:\Progra\DefinitiveRandomizer-validation\Integracion80-final-v2\ORAS\validation80_01.3ds
- USUM: D:\Progra\DefinitiveRandomizer-validation\Integracion80-USUM-verificado\USUM\validation80_01.3ds

Los proyectos editables y manifiestos están junto a cada ROM. Los cambios finales de administración de buffers y eliminación de la vista previa Wild duplicada se verificaron con regresiones específicas; no cambian los parámetros de randomización de estas ROMs.

Para etiquetar: abrir Game Text/Story Text, localizar un diálogo mediante búsqueda, seleccionar el archivo, elegir una etiqueta y pulsar Añadir etiqueta. El desplegable de la izquierda filtra por etiqueta. Se permiten varias etiquetas por archivo. Las ubicaciones y la importancia argumental requieren clasificación manual; no se etiquetaron automáticamente a partir de suposiciones.

Candidata: D:\Progra\DefinitiveRandomizer-validation\20261014-iteration80-local-candidate\DefinitiveRandomizer.exe. Los informes de esta iteración están en PRUEBAS. SHA-256 App DLL: D6A91C5E56935E29A687127380380A87D664C1F61FFBEE9C4D99F112F21AF8F9.
