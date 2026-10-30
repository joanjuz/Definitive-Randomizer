# Definitive Randomizer 0.8.0 Preview 2

Prerelease para testers, desde `feature/3ds-progressive-ui`, sin integrar en `main` ni reemplazar la versión estable WinForms.

## Cambios

- Rebuild junto a Generar backup. Muestra el historial guardado al abrir y confirmar la reconstrucción, distinguiendo el estado actual de las opciones aún sin aplicar.
- Acciones acumulables de tiendas y BP: previsualización inmediata, retirada individual y restablecimiento. Aplicar guarda exactamente la propuesta revisada y registra las acciones para Global/Batch.
- Field Items y encuentros usan snapshots pequeños de lectura y catálogos reutilizables. Se mantienen verificaciones SHA-256 antes de escribir y el rechazo de fuentes modificadas externamente, incluso con igual tamaño y fecha.
- Field Items se carga en segundo plano con cancelación y guardas de sesión. Se conservan los cambios pendientes y el bloqueo de interacción durante operaciones.
- Caché ExeFS por sesión: evita recomprimir entradas idénticas al volver a Rebuild. Conserva BLZ, ExHeader y claves de contenido.
- RomFS reutiliza buffers y procesa los hashes IVFC por lotes, con tiempos separados por fase. Conserva la verificación SHA independiente de Batch.
- Incluye la corrección anterior de compresión LZ11 de Pickup Gen7, validada por el usuario con la receta completa de UltraSol.

## Descarga

Extraer completamente `DefinitiveRandomizer-0.8.0-preview.2-win-x64.zip` y abrir `DefinitiveRandomizer.exe`. Windows x64, autocontenido: no requiere instalar .NET. Mantener las DLL y las carpetas LegacyBatch, Tools, Templates, custom_balance_templates y catch_zone_patches juntas.

Conservar los templates y ajustes propios al actualizar. El paquete contiene solo defaults públicos del repositorio: no incluye ROMs, extracciones, sprites locales ni recetas personales. Las imágenes Pokémon se solicitan en línea; sin conexión pueden no aparecer. No se declara soporte nativo Linux para WPF/WinForms.

Las ROMs generadas con el defecto anterior de Pickup no se reparan al actualizar la aplicación; deben reconstruirse. No es necesario modificar los templates por esta corrección. El prototipo pendiente de compras únicas no está incluido.

## Evidencia y límites

En Debug pasaron 3.661 comprobaciones sintéticas XY/ORAS/SM/USUM, 98 de rendimiento, 95 visuales fuera de pantalla, 65 de Rebuild/tiendas, 18 de replay Wild Gen7 y 5 del bloqueo de interacción. Las suites se solapan; no se suman como casos distintos. En Release también pasaron las 3.661 comprobaciones sintéticas y las suites focalizadas de 98, 95, 65, 18 y 5 comprobaciones. La compilación Release terminó con 0 errores y 88 advertencias; no se presenta como una compilación libre de advertencias.

Las propuestas reales con semilla 123 de Field Items y encuentros son idénticas a la versión publicada en USUM y ORAS. Un RomFS sintético produjo exactamente los mismos bytes antes/después. Una reconstrucción real de UltraSol conservó 747 archivos y el ejecutable, y pasó la comprobación independiente de 901.638 bloques IVFC.

Las mediciones de tres ejecuciones mostraron selección de registros mucho más rápida y menor asignación de memoria; no son tiempos de la interfaz completa ni garantías para todos los discos. El informe `docs/3ds-progressive/OPTIMIZACION_LECTURA_REBUILD.md` contiene los datos, cambios y riesgos residuales.

No se ha jugado una partida completa con esta nueva reconstrucción ni se declara validación interactiva de teclado, cancelación, DPI y diálogos. Las pruebas visuales son fuera de pantalla. Verificar primeros eventos y combates en emulador/consola con copias propias y conservar los guardados.

## Reportar problemas

Indicar versión y commit de VERSION.txt, juego, idioma, pasos, semilla, resultado esperado y logs. No adjuntar ROMs ni recursos extraídos. Verificar la descarga con SHA256SUMS.txt.

## Reproducir

Con Windows, SDK .NET 10, Git y las dependencias Mobipeg documentadas:

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.8.0-preview.2
```

Icono proporcionado por el propietario del proyecto: Icons8, https://icons8.com/.
