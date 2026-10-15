# Iteración 50: servicios nativos en Batch Gen6

Batch Gen6 suma ocho acciones (25 en total): static-encounters.randomize, static-encounters.modify-levels, starters.randomize, tms.randomize, field-items.randomize, marts.randomize, marts.add-rare-candies, items.fix-economy.

El ejecutor original carga NativeBatchGen6Bridge desde 3DS.Progressive.dll del paquete y lo aplica al proyecto temporal del lote, después de guardar cambios previos y antes de entrenadores. Se conservan ProgressiveApp al serializar la plantilla original y las propuestas/transacciones verificadas de los servicios nativos. El runner recarga su configuración después del puente para ver los datos efectivos.

Los cuatro servicios aleatorios ofrecen una entrada interna con semilla explícita; el uso manual conserva su semilla aleatoria. Batch deriva semillas de su generador y cada módulo usa una subsecuencia determinada por el orden de acciones nativas. La reproducción se comprueba pasando por el tipo de plantilla del ejecutor original.

Estáticos incluyen regalos e iniciales, selector sincronizado, filtros, atributos y niveles con suma/multiplicador. Tras estas acciones se reconstruyen hashes CRO; CRR queda con comportamiento predeterminado vanilla (sin actualización opcional). MT/MO incluyen descripciones y restricción de MO olvidables. Objetos de campo y tiendas usan las mismas opciones del editor. Caramelos Raros incluye stock y precio. Fix Economy utiliza el preset histórico Gen6; no reproduce una tabla de precios personalizados ni multiplicadores distintos del valor neutro.

Preflight exige snapshots completos, alcance Todos, opciones conocidas y acciones compatibles; valida la disponibilidad de los módulos sobre el proyecto origen sin aplicar propuestas. Recetas exclusivamente nativas no requieren controles RandSettings artificiales. Se admite una sola acción por módulo nativo: no se pretende representar dos operaciones distintas del mismo módulo con un único snapshot. Las plantillas Gen6 antiguas de estas acciones necesitan guardar la configuración del editor nuevo.

34 pruebas específicas en XY/ORAS: iniciales básicos y sincronización, exclusión de otras tablas, niveles de todos los registros, CRO hashes, alcance parcial rechazado, MT/MO y textos deterministas, MO olvidables, opciones desconocidas rechazadas sin escritura, campo determinista conservando objetos, preset de economía y tiendas/Caramelos Raros. Pruebas sintéticas; todavía no se ejecutó un lote completo con el proceso original cargando un proyecto real y produciendo .3ds. El puente tampoco declara compatibilidad de todas las combinaciones de acciones.

Pendiente para paridad completa: Batch Wild Gen6, tutores y las demás acciones vanilla/Progressive no incluidas, compatibilidad de recetas antiguas/operaciones múltiples, lotes reales, rendimiento, empaquetado y validación in-game. Se conserva el editor manual existente y Wild protegido. Sin commits/push/release ni cambios a Eris-Ultranova o ROMs de referencia.

Resultado final: Debug 0 errores y 79 advertencias; 2222 regresiones completadas; 34 pruebas específicas del puente nativo incluidas en la batería; 12 archivos protegidos preservados. git diff --check sin errores.
