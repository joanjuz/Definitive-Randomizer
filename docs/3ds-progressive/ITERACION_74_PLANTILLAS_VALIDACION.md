# Iteración 74 · plantillas para validación

Se preparan cuatro Global Templates compatibles por familia (XY, ORAS, SM, USUM), con una receta amplia de opciones Progressive y vanilla compatibles. No se intenta combinar modos excluyentes. Los JSON no contienen binarios de juegos. El paquete incluye CSV de prueba Gen6, acciones por familia y lista de pruebas manuales.

Durante la comprobación con extracciones reales se detectaron y corrigieron dos problemas de SM:

1. `GameMartSm.AddSmTrainingItems` buscaba ranuras de curación después de aleatorizar las tiendas. La identificación se realiza ahora sobre el inventario original, conserva los cambios aleatorios en las demás ranuras y respeta la ranura final reservada al caramelo. Se añaden regresiones con 16 semillas para la combinación de aleatorización regular/BP, objetos EV, caramelos y prohibición de cápsula.
2. `GameMachineTextSynchronization` copiaba espacios finales de algunas descripciones españolas de movimientos a la descripción de la MT. El codificador de texto los elimina y la comparación estricta rechazaba la operación. Se normaliza exclusivamente la descripción copiada mediante `Trim()`, igual que el codificador; se conserva la comparación del resto del banco. El caso de prueba incluye espacios finales.

Pruebas locales del candidato 74:

- Debug: 0 errores, 79 advertencias.
- 48 comprobaciones de tiendas SM.
- 56 comprobaciones de descripciones de MT.
- 3.868 regresiones con datos sintéticos XY/ORAS/SM/USUM.

Los resultados de importación, restauración y preflight de los ocho juegos se registran en `generation74-final.log` del paquete cuando termina la generación. Los hashes supervisados corresponden a los archivos editables enumerados por el generador, no a todos los archivos de las extracciones.

No se han ejecutado las cuatro recetas completas de Batch ni validado las ROM resultantes en juego en esta iteración. Queda esa comprobación al usuario, junto con las herramientas y alternativas detalladas en la lista manual. No se publica release ni se realizan commits/push.

El CSV Gen6 modifica la Velocidad base de Pikachu a 95 como marcador verificable de importación; no representa balance final. Mantener el paquete en su ruta original para resolver la ruta absoluta del CSV.
