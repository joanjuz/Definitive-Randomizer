# Definitive Randomizer 0.14.0 Preview 2

Actualización de mantenimiento de la aplicación WPF desde la línea `main`. Corrige una condición de concurrencia al deshacer cambios y refuerza la validación automatizada. No modifica los motores de randomización, los formatos de proyectos ni las plantillas. No sustituye a la versión estable WinForms v4.17.1.

## Corrección de seguridad al deshacer

- Al deshacer, la aplicación vuelve a comprobar que los bytes capturados del archivo de destino coinciden con el estado esperado antes de preparar la transacción.
- Si detecta una modificación externa, rechaza el deshacer sin sobrescribir ese cambio, sin restaurar parcialmente otros archivos y sin alterar el historial.
- Se incorpora una prueba de regresión determinista en Windows para evitar que reaparezca el defecto.

**Alcance:** se corrige la ventana reproducida durante la preparación de `RestorePoints.Undo()`. No se garantiza exclusión absoluta de escritores externos: subsisten ventanas entre la última validación de `FileTransaction` y el reemplazo, así como en rollback y recuperación.

## Mejoras en las pruebas

- El registro de reproducción manual comprueba exclusivamente los editores 3DS, sin considerar editores independientes de DS y Switch.
- La auditoría visual tolera diferencias mínimas de medición de fuentes que antes podían producir falsos positivos. Incluye pruebas dirigidas que verifican que un recorte real sí se detecta.
- Las pruebas sintéticas evitan descubrir accidentalmente entradas reales del entorno cuando se solicita el modo exclusivamente sintético.

## Validación registrada

- PR #37: corrección TOCTOU, CI aprobado.
- PR #38: estabilización de pruebas, CI aprobado con 21 suites y 4.093 comprobaciones sintéticas.
- Auditoría visual del PR #38: 8/8 comprobaciones, 554 vistas, 0 problemas y 77 avisos.
- Compilación Release WPF y LegacyBatch en CI: sin errores ni advertencias.
- Se verificó manualmente el arranque y funcionamiento general de la aplicación. Esto no equivale a validar un ciclo completo de deshacer de una receta sobre una ROM real ni todas las plataformas en juego.

## Requisitos y actualización

1. Descarga `DefinitiveRandomizer-0.14.0-preview.2-win-x64.zip` desde la release oficial cuando esté publicada.
2. Extrae el archivo completo en una carpeta nueva.
3. Ejecuta `DefinitiveRandomizer.exe` en Windows x64. No se requiere instalar .NET.
4. Mantén juntas las carpetas `LegacyBatch`, `Tools`, `Templates`, `custom_balance_templates` y demás dependencias incluidas.
5. Copia tus plantillas personales desde una copia de seguridad; no sobrescribas los archivos originales de tus juegos.

No se distribuyen ROMs, partidas, recursos de juegos ni datos personales.

## Límites conocidos y reportes

- Las comprobaciones de concurrencia no impiden cualquier escritura externa simultánea.
- Persisten 77 avisos de auditoría visual no bloqueantes.
- No se declara una partida completa ni validación de consola real para todos los juegos.
- Si encuentras un problema, indica versión y commit de `VERSION.txt`, juego, idioma, pasos, semilla y los registros pertinentes. No adjuntes ROMs ni archivos extraídos del juego.

## Reproducir el empaquetado

La publicación se genera desde el commit aprobado, en Windows con .NET 10 y las dependencias Mobipeg verificadas:

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.14.0-preview.2
```

Comprobar el ZIP, `SHA256SUMS.txt`, `VERSION.txt`, la presencia de `DefinitiveRandomizer.exe`, el ejecutable `LegacyBatch` y las dependencias antes de publicar. Marcar la versión como **prerelease** y crear el tag `v0.14.0-preview.2` únicamente después de esa comprobación.
