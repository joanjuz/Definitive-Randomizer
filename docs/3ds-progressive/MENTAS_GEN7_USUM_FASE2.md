# Mentas Gen7 USUM — fase 2: distribución y recetas

La inclusión de mentas es **opcional y desactivada de origen**. Requiere una instalación funcional de fase 1 reconocida en el proyecto actual. Los iconos de fase 1.1 son opcionales: su ausencia muestra un aviso visual y no impide utilizar los objetos. Las mentas siguen cambiando la **naturaleza real** del Pokémon; no reproducen el sistema estadístico independiente de generaciones posteriores.

## Fuentes implementadas

| Fuente | Integración | Restricciones |
| --- | --- | --- |
| Tiendas normales | Pool de randomización y acción de distribución garantizada | Se respetan «Solo tiendas especiales», conservación de objetos X y objetos protegidos |
| Tiendas BP | Pool opcional al seleccionar expresamente «Randomizar BP» | No se activan por incluir mentas ni por distribuirlas en stocks generales; conserva los precios BP de cada ranura |
| EI / EB | Objetos visibles y ocultos que ya expone el editor de recogibles Gen7 | Respeta selección de zonas, EI/EB, exclusiones y plantilla de objetos permitidos/prohibidos |
| Recogida / Pickup | Pool opcional con probabilidades originales | No añade filas ni cambia tasas al randomizar objetos |

No se implementan nuevos editores de regalos de objetos, recompensas de eventos, guiones ni otras tablas de distribución. Los objetos equipados de entrenadores, Pokémon salvajes y regalos de Pokémon conservan sus pools de combate: una menta es un consumible usado desde la bolsa, no una mejora equipada. No se anuncia integración de mentas en esos contextos.

## Activación y tiendas accesibles

1. Instalar las mentas desde **Mecánicas y CRO → Mentas Gen7 — Ultra Sol / Ultra Luna**. Comprobar «Instalado y reconocido». Si se desea, instalar también los iconos.
2. Activar **Incluir mentas Gen7 (requiere parche instalado)**. El mismo ajuste de la sesión está disponible en Mentas, Tiendas, Recogida, recogibles EI/EB. No instala el parche ni escribe archivos.
3. En Tiendas, seleccionar **Randomizar tiendas** y generar una vista previa. Las mentas son candidatos, por lo que esta opción no garantiza que aparezcan las 21 ni una tienda accesible con cualquier progreso.
4. Para garantizar acceso a las 21 desde cualquier número de pruebas superadas, seleccionar **21 mentas en stocks generales (0–7)**. Añade solamente las mentas que faltan en cada uno de los ocho stocks: Sin pruebas, 1 prueba…7 pruebas. Conserva el orden y los objetos existentes; no altera tiendas especiales ni BP. Si se combina con randomización, la adición ocurre después. Si alguna tabla supera el límite de 127 ranuras, se rechaza toda la operación.
5. Revisar la vista previa y pulsar **Aplicar y verificar**. Reabrir Tiendas para consultar el resultado.

La acción de stocks generales es una solicitud explícita de distribución y puede utilizarse sin activar su inclusión en pools aleatorios. Siempre valida el parche. Repetirla no duplica las mentas presentes. En un Centro Pokémon, consultar al dependiente del stock general de Poké Mart; el vendedor de objetos especiales o de BP utiliza otras tablas. El stock concreto depende del progreso, por eso la acción cubre los ocho niveles. No modifica la progresión ni la partida.

Los precios normales proceden de los registros de objetos del proyecto. Esta fase **no cambia sus precios**: pueden ajustarse en el editor de precios existente antes de probar o guardar una modificación manual. Los precios BP pertenecen a las ranuras de esas tiendas y se conservan al randomizar. No se añade una política de precios paralela.

## Pools y filtros

Los IDs se toman exclusivamente de `UsumNatureMintCatalog`: 113, 114, 115, 123–133, 426, 427, 622 y 837–840. No constituyen un rango libre continuo. Se incorporan una sola vez al pool activado. El pool desactivado conserva el orden y las ponderaciones históricas de los candidatos anteriores, excluyendo los IDs de mentas en USUM.

Se consideran consumibles de naturaleza. Los IDs 426 y 427 reutilizados por el parche no se clasifican como MT por su número cuando son mentas instaladas. En EI/EB, una lista explícita `AllowedItems` continúa restringiendo los candidatos: para incluir mentas en ella deben figurar sus IDs. `BannedItems` prevalece. Con mentas activadas, EI/EB genera candidatos a partir del pool ampliado y después mezcla las posiciones; desactivadas conserva el algoritmo anterior. Los recogibles que ya contienen mentas se preservan fuera de esa mezcla desactivada.

## Global Template y Batch Builder

Global Template conserva el contrato v1 y añade este nodo opcional:

```json
"ProgressiveApp": {
  "Gen7MintItems": { "Include": true }
}
```

El nodo ausente equivale a `false`; una receta antigua no habilita mentas al importarla. Se conservan otros nodos y campos desconocidos. La acción `marts.add-nature-mints` se guarda con las opciones nativas de `Gen7Marts`; no se ejecuta mediante un fallback heredado que la pudiera ignorar.

Configurar las opciones de los editores, **Crear/actualizar desde el historial**, revisar acciones y **Guardar Global Template** en un archivo nuevo. La receta guarda la opción central elegida en los editores; para cambiarla, ajustar el control allí y actualizar/guardar la receta antes de previsualizar. La receta puede incluir tiendas, EI/EB y Pickup, o solo los módulos deseados. Las modificaciones manuales conservan el orden existente y sustituyen los resultados aleatorios donde corresponda. Si se solicita la distribución garantizada, al final se añaden las mentas que falten en los ocho stocks sin reemplazar esas filas manuales; después se reconcilia la compra única existente.

En Batch Builder, elegir ese JSON y comprobar la previsualización. Se utilizan las opciones guardadas en la receta, no ajustes implícitos de otra sesión. El preflight comprueba la dependencia antes de ejecutar y el ejecutor vuelve a comprobarla en cada copia de trabajo, después de restaurar respaldos si se solicitó hacerlo. Si el respaldo carece del parche, se bloquea esa copia; no se reinstala automáticamente ni se sustituyen mentas silenciosamente. Las escrituras del proyecto origen siguen pasando por la transacción Global; Batch usa sus copias aisladas y los mecanismos existentes de publicación de resultados.

## Semillas, historial y seguridad

- Sin mentas no se añaden llamadas al generador aleatorio. Los pools anteriores de tiendas y Pickup conservan orden y multiplicidades; EI/EB conserva su mezcla anterior con los mismos datos y opciones.
- Con mentas, la misma semilla, receta, versión y datos iniciales produce el mismo resultado. La acción de añadir las 21 a los ocho stocks no consume aleatoriedad.
- No se promete reproducir resultados partiendo de tablas diferentes, otras opciones, otra versión o un estado distinto del parche. Global/Batch conserva la política existente de semillas por ejecución y las registra; no se redefine el generador ni el formato general de recetas.
- El historial registra inclusión/exclusión, archivos realmente modificados, las opciones nativas de tiendas y el snapshot completo de Global. La elección explícita se recupera de las entradas efectivas; una instalación previa sin esa elección no la activa. Las acciones de instalación de las fases 1 y 1.1 permanecen en el historial, pero se consideran dependencias al construir la receta, no instalaciones automáticas. Se conservan el orden de operaciones y los overrides manuales. Exportar historial no inventa opciones nativas faltantes: la receta debe completarse con las opciones de los editores antes de ejecutarse automáticamente.
- Preparar una operación dependiente captura hashes del parche funcional, identidad, objetos, bolsillos, textos y CRO/CRR. Al aplicar se vuelven a comprobar; las dependencias que no se escriben se mantienen abiertas sin permitir escritores concurrentes. Los archivos modificados usan las transacciones y verificaciones existentes. Se bloquea un estado parcial, externo o desconocido.
- **Deshacer** utiliza los respaldos de los archivos afectados y sus verificaciones existentes. No modifica guardados del emulador ni garantiza recuperación ante todas las averías externas.

## Compatibilidad de código y reconstrucción

Ultra Sol y Ultra Luna conservan perfiles y fingerprints independientes. Para permitir recetas que combinan mentas con MT y Shiny Rate, el reconocimiento acredita también el ejecutable original con variaciones restringidas a las 100 MT, las instrucciones Shiny ya soportadas por el editor y el mapeo conocido de los 21 iconos. Se validan los IDs de movimientos y las instrucciones antes de normalizar esos datos; el resto del ejecutable debe coincidir con el fingerprint del título. No se aceptan otras revisiones, parches de código desconocidos ni offsets de Gen6/SM.

Las salidas siguen siendo los GARC, CRO/CRR y datos de ExeFS del proyecto, consumidos por el proceso de reconstrucción existente. Esta fase no introduce un formato de ROM ni distribuye ROMs o gráficos comerciales.

## Comprobaciones y prueba manual

La suite `--usum-mints-distribution-only` utiliza fixtures sintéticos independientes de Ultra Sol y Ultra Luna. Verifica pools, exclusiones, determinismo, dependencias, escritores concurrentes, los ocho stocks, conservación de BP, guardado, reapertura, deshacer y recetas nativas. La suite privada `--usum-mints-distribution-real-copies` ejecuta el motor Global/Batch sobre copias de proyectos, combinando MT, Shiny, tiendas, EI/EB y Pickup. Sus archivos de juego nunca se incorporan al repositorio. Ninguna de estas comprobaciones acredita juego real en Ultra Luna.

Procedimiento de referencia para la prueba manual en Ultra Sol:

1. Duplicar un proyecto con fase 1 reconocida e iconos instalados; conservar ROM y guardado originales.
2. Activar inclusión de mentas. En Tiendas marcar randomización normal y **21 mentas en stocks generales (0–7)**; dejar Randomizar BP desmarcado. Configurar un precio asequible con el editor existente si es necesario.
3. Crear y guardar una receta Global; cerrar y volver a cargar ese JSON. Comprobar inclusión y acción de los ocho stocks. Generar vista previa, revisar y aplicar. Reabrir Tiendas: los ocho stocks contienen las 21 y BP conserva sus filas y precios.
4. Seleccionar la misma receta en Batch Builder, comprobar dependencias y construir una única ROM en una carpeta nueva. Desactivar restauración de respaldos si estos son anteriores a la instalación de mentas.
5. Abrir esa copia en el emulador. En el dependiente general del Centro Pokémon de Ciudad Kantai comprobar las 21 mentas, precios e iconos; comprar y utilizar una. Confirmar el cambio de naturaleza real y que otros iconos permanecen correctos.

El 06/11/2026, el propietario confirmó una prueba manual satisfactoria de la fase 2 en **Pokémon Ultra Sol**: la distribución de mentas, Global Template, Batch Builder y la ROM generada funcionan correctamente en su prueba. Esta confirmación se suma a la validación jugable previa de las fases 1 y 1.1 en Ultra Sol y permite dar por validada manualmente la fase 2 con ese alcance. No se atribuye esta prueba a Ultra Luna ni se declaran comprobaciones individuales adicionales que el propietario no haya confirmado.

Comprobaciones de esta entrega: 122 casos específicos sintéticos de fase 2; 18 comprobaciones de la receta combinada con el motor real en copias privadas US/UM; dos comprobaciones adicionales del finalizador público sobre esas copias, con fila manual conservada, repetición sin cambios y restauración del stock de prueba mediante el editor. Regresión general sintética ejecutada una vez: 4142 comprobaciones. Interfaz fuera de pantalla: 23 casos de Mentas/Global/Batch y 145 de tiendas/reconstrucción. Los logs privados se conservan fuera de Git; no se incluyen datos comerciales ni se presenta la inspección de tablas como validación jugable.
