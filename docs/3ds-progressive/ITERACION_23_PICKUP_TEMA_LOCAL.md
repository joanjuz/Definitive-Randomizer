# Iteración 23 — Tema CRO/CRR y Pickup Gen6/Gen7

Trabajo local en `feature/3ds-progressive-ui`; no hubo commits, push ni release.

## Implementación

La tabla CRO/CRR usa ahora fondo, cabeceras, filas alternadas, texto y selección del tema oscuro. Se comprobó con tabla vacía y con filas seleccionadas. El helper `DarkGridTheme` también se usa en el nuevo editor Pickup.

Pickup se abre desde Herramientas Pokémon y permanece integrado en la ventana principal. Incluye edición, restauración del borrador al estado de apertura, randomización de objetos con el pool legal original de cada versión, vista previa, confirmación, relectura e historial persistente. La navegación/cierre pide descartar cambios pendientes.

- XY/ORAS: firma vanilla única desde 0x400000; 18 objetos comunes y 11 raros. Solo se escriben los 58 bytes de objetos. Se conserva la tabla de probabilidades y el resto de code.bin; código comprimido, firma ausente/duplicada o datos incompatibles se rechazan.
- SM/USUM: miembro cero del GARC Pickup; objeto ushort y diez porcentajes byte por fila. Todas las columnas deben sumar exactamente 100 %. Se permite añadir una fila con probabilidades cero y quitar la última, con al menos una fila restante. El encabezado se actualiza; el trailer opaco y los demás miembros se conservan. Se utiliza LazyGARC para la serialización.
- El randomizador baraja el pool sin repetir objetos dentro de una pasada, preserva las probabilidades y no escribe hasta confirmar. Las propuestas pertenecen al editor que las creó y se invalidan ante cambios externos o edición posterior del borrador.

## Validación

Debug local: **0 errores / 22 advertencias existentes**. Suite completa: **1.122 comprobaciones**, con fixtures XY, ORAS, SM y USUM. Las nuevas regresiones cubren conservación exacta de bytes, probabilidades, otros miembros y trailer; cambio de tamaño; columnas inválidas; pool legal; historial; propuestas ajenas/obsoletas; cambios externos; firmas ausentes/duplicadas; tema y navegación real WPF. Las capturas verificadas y logs están en `ITERACION_23_PRUEBAS/`.

El SHA-256 de los 12 archivos locales protegidos vuelve a verificarse. Los parches Wild validados y archivos personales no se modificaron. Eris-Ultranova no se tocó.

## Paridad pendiente

Se actualiza la fila Pickup existente de la matriz, sin presentar el proyecto completo como terminado. Pickup aún requiere prueba en ROM real/juego en las ocho variantes.

La auditoría siguiente contrastó `GameMachineTutorService`, `GameMartService`, `TMHMEditor6` y `TutorEditor6`. MT/MO Gen6 sigue deshabilitado; el orden vanilla separa MT01–92, MO y MT93–100 y además sincroniza descripciones de objetos. Tiendas estructurales solo soporta USUM. TutorEditor6 vanilla avisa que su edición no se refleja en juego: escribir esa tabla no acreditaría por sí solo funcionalidad runtime. Estas áreas requieren una iteración con pruebas específicas, no solo habilitar sus botones.

También continúan pendientes opciones exhaustivas de Static/entrenadores, otras herramientas vanilla, Global Template bidireccional, Batch integral, recuperación durable, rendimiento, validación real de extracción/rebuild y empaquetado final. No se solicita todavía la validación final de la aplicación.
