# Definitive Randomizer 0.8.0 Preview 3

Prerelease para testers desde `feature/3ds-progressive-ui`. No integra cambios en `main` ni sustituye la versión estable WinForms.

## Novedades

- Inicio al abrir proyectos: elige Modo simplificado, Modo completo o Randomizado guiado. El logo y el nombre vuelven a Inicio.
- Randomizado guiado: selecciona secciones, configura opciones y confirma el resumen final antes de generar una ROM en una copia aislada. Conserva elecciones al retroceder, omite secciones sin acciones y presenta los rangos BST progresivos en una tabla separada. Las tiendas parten de Solo tiendas especiales y sin Proteger objetos X.
- Vista guiada opcional: resalta controles reales y explica secciones y subsecciones. Anterior, Siguiente y Saltar todo; F1 permite abrirla en el contexto actual.
- Global Template: Previsualizar cambios ejecuta la receta en una copia temporal sin reconstruir .3ds. Aplicar cambios guarda exactamente el resultado revisado y registra cada acción en el historial. Rebuild permanece como acción independiente de la cabecera.
- Historial contrastado con los archivos actuales mediante SHA-256. Distingue entradas coherentes, desfasadas y sin comprobación; conservar el registro no implica que los cambios sigan presentes tras limpiar o restaurar una ROM.
- Rebuild solicita su confirmación con historial una sola vez, después de elegir destino. Incluye correcciones de liberación del bloqueo y limpieza de temporales.
- About GARCs abre una ventana con el listado real del juego.
- Mejoras de cierre de ventanas, cancelación y protección frente a resultados de cargas antiguas.

## Descargar y usar

Extraer completamente `DefinitiveRandomizer-0.8.0-preview.3-win-x64.zip` y abrir `DefinitiveRandomizer.exe`. Windows x64, autocontenido; no requiere instalar .NET. Mantener juntas las DLL y las carpetas LegacyBatch, Tools, Templates, custom_balance_templates y catch_zone_patches.

Conservar los templates personales al actualizar. El paquete incluye únicamente defaults públicos: no contiene ROMs, extracciones, recetas privadas, estados locales ni sprites. Las imágenes Pokémon se solicitan en línea y pueden no aparecer sin conexión. No se declara soporte nativo Linux para WPF/WinForms. El prototipo de compras únicas permanece pendiente y fuera del release.

## Comprobaciones y límites

Antes de preparar esta versión pasaron 3.750 comprobaciones sintéticas XY/ORAS/SM/USUM, 233 de guía/asistente, 46 de aplicación global, 132 de compatibilidad Global Template, 95 visuales, 19 de bloqueo y 69 de Rebuild/tiendas. Las suites se solapan: no se suman como casos distintos.

Las pruebas de aplicación global usan subprocesos reales del backend sobre proyectos sintéticos: comprueban aislamiento, aplicación exacta, historial, rechazo de propuestas desfasadas y rollback por fallo de escritura. Los renders y comprobaciones de interfaz son fuera de pantalla, no pruebas interactivas de teclado, lector de pantalla o escalado físico. La compilación Release previa terminó con 0 errores y 88 advertencias existentes; no se presenta como libre de advertencias.

Pendiente: usar la receta completa del usuario en una ROM real, revisar el resultado, aplicarlo y reconstruir; validar eventos y combates en emulador o consola. Conservar copias propias y guardados. La vista previa global necesita espacio temporal para una copia del proyecto y tiempo para ejecutar la receta; Aplicar reutiliza el resultado sin sortearlo de nuevo. Las recetas que añaden o eliminan recursos de juego se rechazan en esta ruta de reemplazo para evitar instalaciones parciales.

Los hashes de entrada y salida, el historial y los assets se comprueban antes de aplicar. Las entradas por acción referencian el resultado conjunto: no se inventan diferencias binarias independientes para operaciones que el motor agrupa.

Informes: `GLOBAL_TEMPLATE_APLICACION_20261020.md`, `AJUSTES_ASISTENTE_VISTA_GUIADA.md`, `HISTORIAL_VERIFICADO_REBUILD.md` y `REVISION_ESTABILIDAD_20261019.md` en docs/3ds-progressive.

## Reportar problemas

Indicar versión y commit de VERSION.txt, juego, idioma, pasos, semilla y logs. No adjuntar ROMs ni recursos extraídos. Verificar la descarga con SHA256SUMS.txt.

## Reproducir el paquete

Con Windows, SDK .NET 10, Git y las dependencias Mobipeg documentadas:

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.8.0-preview.3
```

Icono proporcionado por el propietario del proyecto: Icons8, https://icons8.com/.
