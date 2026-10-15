# Iteración 31 · referencias reales y Escudo Real Gen6

Se registran como referencias de solo lectura las carpetas indicadas por el usuario:

- Gen6: `D:\Nintendo\3DS\Juegos\PokemonY`.
- Gen7: `D:\Nintendo\3DS\Juegos\Pokemon`.

No se aplican propuestas ni se guardan cambios en estas carpetas. Los instaladores de las vistas previas trabajan sobre copias temporales. El nuevo diagnóstico opcional `--read-only-roms` del proyecto de pruebas abre, lee y prepara propuestas; no llama a Apply. Compara el inventario completo de archivos (rutas, tamaño y fecha de modificación) y hashes SHA-256 de los GARC/código/CRO monitorizados. Los hashes se toman después de abrir la sesión; la preservación durante la apertura se cubre por inventario y por la auditoría de las rutas Core de inicialización de solo lectura.

## Resultados de lectura real

Core identifica la primera carpeta como familia **XY** y la segunda como **USUM**. La detección por número de GARC no distingue X frente a Y ni Ultra Sun frente a Ultra Moon; los nombres de carpetas no amplían esa evidencia.

En XY se leen 798 entradas de especies y 618 nombres de movimientos. Pasan lectura de economía, MT/MO, O-Powers, estáticos y las vistas previas de intercambios y balance. Economía tiene 717 filas; incluye 122 precios históricos porque el ID 737 del preset no existe en este catálogo. Estáticos expone 32 entradas. La vista previa de balance produce 44 cambios y un archivo adicional.

**Catch Zones queda bloqueado para esta referencia concreta:** el hash del miembro `encdata/dec_038.bin` no coincide con el origen requerido por el manifiesto XY. La operación se rechaza sin aplicar cambios. No se altera el manifiesto ni se permite ignorar la verificación. La causa exacta requiere comparar variantes/revisiones y el origen utilizado para producir el manifiesto; no se atribuye automáticamente a modificaciones del usuario. No se declara compatible con esta ROM aunque pase la prueba sintética.

En USUM se leen 975 entradas de especies y 729 nombres de movimientos. Pasan economía (959 filas incluidas) y vista previa de balance (149 cambios, dos archivos adicionales). Se preservan 142 hashes de archivos monitorizados XY y 140 USUM, además del inventario completo de ambas raíces. Estas comprobaciones no sustituyen la ejecución en juego ni validan reconstrucción/guardado de ROM real.

## Corrección implementada

Se exige una firma ARM única original o ya parcheada antes de preparar el efecto Escudo Real −1 Gen6 en `DLLBattle.cro`. La ruta existente aplicaba la primera coincidencia; ahora una firma ausente, duplicada o mezcla de original y parcheada bloquea la propuesta. Los movimientos y el CRO siguen guardándose conjuntamente mediante historial. Gen7 conserva su ruta existente.

Los fixtures XY/ORAS verifican vista previa sin escrituras, cambio exacto del byte ARM, operación conjunta con movimiento, firma ya aplicada y rechazos sin modificar archivos ante firmas duplicadas, mixtas o ausentes. La suite completa y las nuevas lecturas reales posteriores a esta comprobación se conservan en `ITERACION_31_PRUEBAS/`.

Se mantiene la rama `feature/3ds-progressive-ui`; sin commits, push ni publicación. Wild protegido, archivos personales y Eris-Ultranova permanecen intactos. La migración completa y las pruebas in-game siguen pendientes. La incompatibilidad Catch Zones con esta referencia se añade explícitamente a la matriz.

Resultado final: **1.567 comprobaciones sintéticas aprobadas; Debug 0 errores / 22 advertencias existentes; 12/12 hashes protegidos conservados. Lecturas reales con ambas raíces preservadas; Catch Zones XY rechazado por origen incompatible.**
