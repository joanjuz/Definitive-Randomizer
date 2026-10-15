# Iteración 55: Shiny Rate en Batch Gen6

Batch Gen6 admite 33 acciones. `shiny-rate.apply` se incorpora a la lista WPF, al ejecutor original y al puente nativo. Reutiliza GameShinyRateService y exige los mismos nombres de parámetros que el Batch original: `rerolls` y `everythingShiny`, ambos cadenas JSON. El número debe ser un entero de 1 a 9000; el booleano debe ser válido. No se aceptan parámetros adicionales ni snapshots ShinyRate alternativos.

La prevalidación construye la propuesta sin escribir, antes de aplicar cualquiera de los módulos nativos seleccionados. Una firma PID desconocida bloquea el lote nativo. Durante ejecución se recrea el servicio para respetar modificaciones previas de code.bin; el parche modifica únicamente las instrucciones previstas, usa la tabla ARM original para redondear rerolls y evita registrar una aplicación vacía cuando ya coincide.

24 pruebas dirigidas XY/ORAS comprueban preflight sin escritura, igualdad byte por byte con la propuesta manual, lectura de ambas opciones, idempotencia independiente de semilla, rechazo de parámetros ausentes/incorrectos/desconocidos y preservación de economía y código ante una firma PID incompatible. Suite completa: 2324 regresiones sintéticas XY/ORAS/SM/USUM. Debug: 0 errores y 79 advertencias. Los doce archivos protegidos permanecen intactos; git diff --check pasó.

## Límites y pendientes

Esta iteración habilita la reproducción de recetas Global con parámetros explícitos. La captura automática de Shiny Rate desde la interfaz/historial sigue pendiente; un historial sin parámetros no se convierte silenciosamente en receta reproducible. Los snapshots marcados PartialSnapshot siguen bloqueados. No se añade shiny-rate.restore a Batch ni se altera el ejecutor Gen7.

No se ha verificado esta integración en ROMs reales ni en juego. Siguen pendientes tutores efectivos ORAS, otras acciones Progressive, captura Global completa, carga del puente en un proceso original independiente, Batch completo hasta .3ds, empaquetado y validación final de extracción/rebuild. No se declara paridad completa ni estabilidad final.

Sin commits, push, publicación, cambios en Eris-Ultranova, ROMs originales o archivos personales.
