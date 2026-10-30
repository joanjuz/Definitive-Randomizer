# Corrección de compresión de Pickup Gen7

Fecha: 2026-10-16. Alcance: GamePickupService, sin cambiar algoritmos, objetos elegidos ni probabilidades.

## Defecto

La tabla Pickup original de UltraSol probada contiene un miembro GARC almacenado en LZ11. Preview leía la tabla con una instancia LazyGARC y creaba otra para guardar. Al asignar a la segunda instancia sin leer antes su entrada original, el setter infería el estado de compresión desde los bytes nuevos, sin comprimir. Se perdía LZ11 aunque el contenido descomprimido se pudiera releer correctamente en el editor.

La corrección carga la entrada original antes de reemplazarla. LazyGARC conserva así su estado de almacenamiento. No se modifica el serializador general ni el camino Gen6.

## Evidencia

- ROM original y original reconstruida: usuario informa que funcionan.
- I, entrenadores y estáticos modificados, Pickup original: funciona.
- J, igual a I más Pickup aleatorizado sin LZ11: se congela.
- K, igual a J con el mismo contenido aleatorizado de Pickup, restaurando LZ11: funciona, confirmado por el usuario desde partida nueva.
- L, receta completa con únicamente el almacenamiento de Pickup corregido: funciona, confirmado por el usuario. No se ha validado una partida completa.

## Validación automatizada

Prueba roja antes de corregir: `SM Pickup compressed randomized preview preserves LZ11 storage` falla.

Compilación Debug de 3DS.Progressive.Tests con dependencias: 0 errores, 90 advertencias. No se ha clasificado exhaustivamente la antigüedad de todas las advertencias.

Runner propio: `dotnet 3DS.Progressive.Tests/bin/Debug/net10.0-windows/3DS.Progressive.Tests.dll --pickup-only`: 112 comprobaciones pasan. Incluye XY/ORAS sin cambios de formato, SM/USUM con fuente comprimida y sin comprimir, replay nativo con semilla, edición manual, probabilidades, miembros ajenos, tráiler opaco y rechazo de propuestas obsoletas. Las comprobaciones comprimidas no sustituyen juego real en SM; el control en emulador fue USUM y lo ejecutó el usuario.

El prototipo de compras únicas permanece pendiente y separado de esta corrección.

## Confirmación de la receta completa
El usuario confirmó que L funciona. Conserva todos los cambios del Batch original, con solo el almacenamiento de Pickup corregido. Verificación independiente: 747 archivos RomFS coinciden con su proyecto, cero diferencias y ejecutable BLZ descomprimido idéntico al original del proyecto (5914624 bytes). La secuencia I positivo, J negativo, K positivo y L positivo valida la corrección para los bloqueos iniciales reproducidos en USUM. No equivale a una partida completa ni a pruebas en SM.

Validación para publicar: Release con UseAppHost=true, 0 errores y 90 advertencias; runner --synthetic-only, 3651 comprobaciones correctas. Una ejecución previa sin apphost se detuvo por falta del ejecutable LegacyBatch, resuelta recompilando en la configuración de distribución; sin cambios en las expectativas.
