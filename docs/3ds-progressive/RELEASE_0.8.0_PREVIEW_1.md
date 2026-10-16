# Definitive Randomizer 0.8.0 Preview 1

Primera prerelease pública de la interfaz WPF actual, destinada a testers. Se publica desde `feature/3ds-progressive-ui`, sin integrar esta rama en `main` y sin reemplazar la versión estable v4.17.1.

## Cambios incluidos

- Interfaz adaptable, navegación agrupada, formularios y tablas más consistentes, diálogos desplazables y mejoras de accesibilidad.
- Alto contraste dinámico para tablas ya abiertas, preservando selección, alternancia de filas, estados deshabilitados y bindings.
- Creación, importación y aplicación de templates de movimientos, evoluciones, estadísticas, Field Items y tiendas según la compatibilidad del juego.
- Historial y reproducción de cambios manuales mediante Global Template y Batch ROM Builder.
- Clasificación y búsqueda de Game Text y Story Text, catálogo de comandos y previsualización de descripciones con recursos de la ROM.
- Icono de la aplicación actualizado y subtexto «Editor de ROMS».
- CI con compilación explícita de WPF y del runner propio, regresiones visuales fuera de pantalla y ruta de LegacyBatch ajustada a las compilaciones con identificador de runtime.

## Descarga y ejecución

Descargar `DefinitiveRandomizer-0.8.0-preview.1-win-x64.zip`, extraerlo completamente y abrir `DefinitiveRandomizer.exe`. Conservar sus carpetas: `LegacyBatch`, `Tools`, `Templates`, `custom_balance_templates` y `catch_zone_patches` forman parte de la aplicación. El paquete es una carpeta autocontenida para Windows x64: no requiere instalar .NET por separado. El ejecutable y sus DLL deben permanecer juntos; se evita el empaquetado en un único archivo para conservar las rutas de recursos y herramientas externas.

No incluye ROMs, proyectos de juego, sprites ni templates personales. Las imágenes Pokémon se solicitan en línea y pueden no aparecer sin conexión. La interfaz WPF/WinForms requiere Windows; no se declara soporte nativo para Linux.

## Qué probar

1. Apertura de proyectos propios de XY, ORAS, SM y USUM, comprobando las opciones disponibles para cada juego.
2. Navegación, tablas, búsqueda, diálogos y formularios con ventanas pequeñas y escalados 100 %, 125 %, 150 % y 200 %.
3. Uso mediante teclado, recuperación de foco al cerrar diálogos y cambios reales del alto contraste de Windows.
4. Ediciones manuales, historial, creación/importación/exportación de templates y reproducción de cambios exactos.
5. Global Template y Batch con semillas reproducibles, bloqueo de controles, progreso, salida y reconstrucción.
6. Verificación en emulador o consola de las ROMs producidas, incluyendo primeros combates y eventos de historia.

Al reportar un problema, indicar versión, juego, idioma, pasos, resultado esperado, semilla y mensaje o log. Adjuntar templates y capturas solo si no contienen información privada; no adjuntar ROMs ni datos extraídos de juegos.

## Alcance de la validación

Validación local para esta publicación: compilación Release sin errores (90 advertencias ya presentes en el código), 3.587 comprobaciones con `--synthetic-only` y 1.333 comprobaciones en 15 suites focalizadas, incluidas 95 del rediseño visual y 13 de imágenes remotas. Se corrigieron dos expectativas antiguas de tests: el nombre `DllBattle.cro` y la ausencia intencional de sprites locales. No se cambió el motor para satisfacer esas pruebas.

Las comprobaciones focalizadas usan datos sintéticos y renderizado WPF fuera de pantalla. La ejecución inicial del runner sin flags también abrió su ventana de smoke test y se detuvo en la expectativa antigua de sprites; después se ejecutó el modo sintético explícito. La suite interactiva completa no se ha vuelto a validar. Sus resultados se indican en la publicación. No equivalen a pruebas interactivas completas ni garantizan que todas las combinaciones de cambios funcionen dentro del juego. Esta versión se distribuye como prerelease precisamente para ampliar esa validación.

## Reproducir el paquete

En Windows, con .NET SDK 10, Git y los binarios de Mobipeg preparados según `3DS.Progressive.App/Tools/Mobipeg/README.md`, ejecutar:

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.8.0-preview.1
```

El script crea una carpeta nueva y un ZIP, incluye ambas aplicaciones autocontenidas y sus dependencias y genera hashes SHA-256. No publica en GitHub ni modifica instalaciones existentes. El código correspondiente se encuentra en el tag `v0.8.0-preview.1`.

Icono de la aplicación: Icons8, imagen proporcionada por el propietario del proyecto. Créditos: https://icons8.com/.
