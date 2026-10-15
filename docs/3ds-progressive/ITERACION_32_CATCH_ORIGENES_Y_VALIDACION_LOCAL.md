# Iteración 32 · procedencia Catch Zones y validación ampliada

Se revisa en modo lectura el respaldo facilitado por el usuario: `D:\Progra\pk3DS-public-release-backup\v3.8-catch-zones\catch_zone_templates`. No se copian sus binarios al repositorio, salida de la aplicación ni evidencias. Se mantienen intactos el respaldo, las extracciones reales y los manifiestos actuales.

## Comparación con la plantilla actual

| Familia | Entradas del manifiesto | Destinos antiguos idénticos (SHA-256 y tamaño) | Ausentes del respaldo | Diferentes |
| --- | ---: | ---: | ---: | ---: |
| XY | 61 | 60 | 1 | 0 |
| ORAS | 174 | 174 | 0 | 0 |

El destino XY ausente es `raw/a/1/4/9`. El respaldo XY solo incluye encdata y mapGR, mientras el manifiesto contiene además ese archivo raw. No se atribuye esa diferencia a una conversión defectuosa sin evidencia de su procedencia.

En la referencia `PokemonY`, la comparación completa de orígenes XY encuentra **35 parches aplicables y 26 archivos incompatibles**, ninguno ya aplicado. Todos los incompatibles pertenecen a encdata. Los 35 aplicables se evalúan individualmente y verifican su SHA-256 de salida mediante el motor nuevo, sin escribirlos. Las 60 coincidencias de destinos antiguos respaldan que esas conversiones conservan exactamente los targets; no prueban compatibilidad del origen con todas las variantes XY.

La sugerencia de que la validación original pudo realizarse solo en ORAS es una hipótesis del usuario consistente con la necesidad de investigar XY, pero no queda demostrada por estos hashes. No se crea un parche para Y cambiando solamente hashes ni copiando el target antiguo encima. Es necesario establecer las variantes/revisiones de origen y cómo conservar sus diferencias. ORAS no se declara validado con una ROM real: aquí se compararon los destinos del respaldo, sin disponer de una extracción ORAS identificada para validar sus orígenes.

## Mejora implementada

Catch Zones ahora revisa todos los orígenes cargados antes de evaluar cambios y comunica el número de incompatibilidades y las primeras ocho rutas, en vez de fallar únicamente en la primera. Continúa rechazando la propuesta completa. Se añaden pruebas XY/ORAS con dos orígenes incompatibles simultáneos que verifican el diagnóstico y la preservación de archivos.

Se añade la auditoría opcional `--audit-catch-templates` al proyecto de pruebas, que compara hashes/tamaños, comprueba los parches aplicables y emite únicamente metadatos. No llama a Apply ni genera salida binaria de ROM. El diagnóstico `--read-only-roms` se amplía a Pickup, Type Chart, Shiny Rate, primera/última entrada de entrenadores, primera/última tienda Gen6 y primera/última entrada de Field Items Gen7.

## Validación real ampliada

Ambas raíces se identifican como familia XY y USUM mediante Core, sin distinguir las versiones dentro de cada familia. Pasan las nuevas lecturas anteriores en las dos referencias, además de las comprobaciones ya realizadas de economía, balance, MT/MO, O-Powers, estáticos e intercambios Gen6. Field Items USUM indexa 538 entradas y entrenadores USUM 652. Estos conteos no equivalen a revisar todos los registros: las pruebas de lectura de entrenadores, tiendas y objetos de campo son muestras de extremos.

Se conservan **144 SHA-256** de archivos monitorizados XY y **143 USUM**, además del inventario completo de ambos árboles. Las vistas previas nunca se aplicaron. Catch Zones XY informa las 26 incompatibilidades; se mantiene pendiente. Los hashes se toman tras la apertura; la apertura se cubre por inventario y auditoría de inicialización de lectura.

Los logs, la comparación de targets (metadatos) y la preservación de los 12 archivos protegidos se guardan en `ITERACION_32_PRUEBAS/`. No se guardan binarios antiguos o de las ROMs en esa carpeta.

La migración global sigue abierta: variantes compatibles Catch Zones, opciones exhaustivas Static/entrenadores, tutores/scripts/herramientas Gen6, Global Template/Batch integral, recuperación, rendimiento y entrega. No se solicita aún validación en juego. Se mantiene la rama `feature/3ds-progressive-ui`, sin commits, push ni publicación y sin modificar Eris-Ultranova o archivos personales.

Resultado final: **1.571 comprobaciones aprobadas; Debug 0 errores / 22 advertencias existentes; 12/12 hashes protegidos conservados.**
