# Iteración 22 — Extracción y hashes CRO/CRR

Implementación local en `feature/3ds-progressive-ui`. No se realizaron commits, push ni publicaciones.

## Funciones incorporadas

- Extracción WPF disponible antes de abrir proyecto: CXI y partición primaria de 3DS descifrados, ExeFS, RomFS, cabeceras, región plain y logo. Respeta el offset real NCSD; permite nombres Unicode y directorios vacíos.
- Descompresión BLZ de `.code.bin` cuando corresponde. Lectura del original sin escritura, salida en carpeta nueva, preparación temporal y publicación mediante movimiento al concluir. Cancelación y bloqueo de navegación/cierre durante la operación.
- Validación de límites, nombres Windows, colisiones, ciclos y hashes de archivos ExeFS. Manifiesto SHA-256 del original y de los archivos extraídos, con relectura de la salida.
- Reconstrucción manual de hashes CRO, comparada byte a byte con la fórmula de Core vanilla. CRR opcional y desactivado por defecto; tabla ordenada con hashes de CRO. Vista previa, detección de cambios externos e inventario, aplicación conjunta e historial.

## Evidencia local

Debug: 0 errores y 22 advertencias existentes. Suite: 1.054 comprobaciones con datos sintéticos XY, ORAS, SM y USUM. Incluye extracción comprimida/sin comprimir CXI/3DS, offset NCSD no estándar, integridad de salida, entradas corruptas, cancelación, preservación del original/proyecto, hashes CRO/CRR, historial y navegación WPF. Se revisaron capturas de ambas pantallas y se corrigió el ancho mínimo de las columnas CRO.

La prueba inicial detectó un bloqueo por captura del contexto WinForms al retornar del trabajo de extracción. Se corrigió con `ConfigureAwait(false)` en el servicio y se repitió la suite completa. Los registros finales y la comprobación SHA-256 de los 12 archivos locales protegidos se conservan en `ITERACION_22_PRUEBAS/`.

## Límites y trabajo pendiente

No descifra ROMs ni requiere claves. No verifica todo el árbol IVFC de RomFS, no firma CRO/CRR y no modifica la consola. Las comprobaciones sintéticas no acreditan extracción/rebuild ni ejecución en las ocho ROMs reales. La reparación CRO es explícita; editar Static Gen6 no la aplica automáticamente.

Rebuild conserva su implementación y regresiones anteriores; queda ampliar validación real por formato. Siguen pendientes la paridad exhaustiva de Static/entrenadores, herramientas vanilla y Gen6, Global Template bidireccional/Batch integral, recuperación durable, rendimiento y empaquetado final. Esta iteración no declara terminado el proyecto ni solicita aún la validación final en juego. Eris-Ultranova y archivos personales se mantienen fuera de los cambios.
