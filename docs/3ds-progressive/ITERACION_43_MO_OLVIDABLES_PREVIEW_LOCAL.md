# Iteración 43 — MO olvidables en vistas previas y plantillas

Fecha: 2026-10-13. Rama local `feature/3ds-progressive-ui`.

«Permitir olvidar MO» ya estaba implementado en el editor MT/MO Gen6, con detección de firmas XY/ORAS y restauración exacta. Esta iteración añade a las opciones masivas una política explícita: conservar estado actual, permitir olvidar MO o restaurar la restricción original. Se puede generar una propuesta únicamente para el parche, desactivando randomización de MT y MO. La opción se conserva en el snapshot nativo de Global Template; no se atribuye un control inexistente al formulario WinForms ni se habilita Batch Gen6 antes de tener ejecutor.

El valor predeterminado conserva el estado existente, de modo que randomizar tablas no revierte accidentalmente un parche previo. Se mantienen comprobaciones de firma, fuentes, propuesta obsoleta e historial conjunto. La función solo está expuesta en Gen6.

## Validación

Debug y suite completa local, con nuevas pruebas de vista previa sin randomizar tablas, preservación de MO ya olvidables, snapshot Global y restauración exacta. Evidencia en ITERACION_43_PRUEBAS.

El diagnóstico `--read-hm6` revisa las cuatro carpetas proporcionadas PokemonX, PokemonY, PokemonOR y PokemonAS: busca la función conocida, cambia solo sus ocho primeros bytes en memoria y restaura el contenido inicial; verifica el SHA-256 del code.bin original. No llama a Apply ni abre un historial con recuperación automática.

## Tutores Gen6 pendientes

TutorEditor6 original incluye expresamente el aviso «Changes made do not reflect ingame» y «Still needs more research». Su tabla de code.bin no basta para declarar funcional el módulo. Hace falta investigar la tabla efectiva de runtime y su relación con la compatibilidad antes de habilitar escritura. No se añade un editor que reproduzca ese fallo conocido.

Continúan pendientes el ejecutor Batch Gen6, herramientas vanilla restantes, rebuild/empaquetado final y validación en juego. Sin commits, push, publicación ni modificaciones de Wild protegido, archivos personales, Eris-Ultranova o ROM originales.
