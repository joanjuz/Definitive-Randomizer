# Definitive Randomizer 0.14.0 Preview 3

Esta actualización de mantenimiento de la versión WPF corrige una incoherencia entre **Deshacer**, los templates exportados y las opciones mostradas como aplicadas. Está basada en la versión 0.14.0-preview.2; no cambia los motores de randomización, los formatos de historial o templates ni la versión estable WinForms v4.17.1.

## Corrección: cambios deshechos en templates y opciones

- **Problema anterior:** tras deshacer una edición manual o una receta Global Template, las exportaciones podían incluir operaciones ya revertidas. La interfaz también podía recordar como aplicadas opciones de cambios deshechos.
- **Ahora:** la exportación de templates y las opciones aplicadas utilizan únicamente las entradas del historial cuyos efectos siguen vigentes.
- **Historial:** conserva todas las entradas, incluidas las restauraciones, para que el registro de auditoría siga completo.
- **Deshacer un deshacer:** si se restaura una operación que había sido revertida, esa operación vuelve a contar como vigente.
- **Recetas Global:** al deshacer una aplicación completa, se excluyen tanto su snapshot como las acciones hijas asociadas, sin excluir recetas distintas que compartan el mismo tipo de acción.
- Los historiales anteriores sin entradas de deshacer conservan su comportamiento.

## Validación

- PR #40 aprobado e integrado: compilaciones Release sin errores ni advertencias.
- Regresión H1: **45/45 expectativas** aprobadas.
- Regresión sintética general: **4.093 comprobaciones** aprobadas.
- Auditoría visual CI: **8/8 comprobaciones; 554 vistas, 0 problemas y 77 avisos**.
- Las pruebas utilizan datos sintéticos. No equivalen a una partida completa ni a validar todas las ROMs reales y variantes.
- El paquete final de esta preview requiere comprobación de archivos, hashes, versión, commit y arranque antes de publicarse.

## Actualización

1. Descarga `DefinitiveRandomizer-0.14.0-preview.3-win-x64.zip` desde la release oficial cuando esté disponible.
2. Extrae el archivo completo en una carpeta nueva.
3. Ejecuta `DefinitiveRandomizer.exe` en Windows x64. El paquete es autocontenido y no requiere instalar .NET.
4. Mantén juntas las carpetas `LegacyBatch`, `Tools`, `Templates`, `custom_balance_templates` y las demás dependencias suministradas.
5. Conserva una copia de seguridad de proyectos y templates personales; no sobrescribas tus ROMs originales.

No se distribuyen ROMs, partidas ni datos personales.

## Limitaciones conocidas

- La corrección afecta al cálculo de operaciones vigentes cuando se exporta desde el historial. La cobertura de templates previamente cargados y combinaciones poco habituales de restauraciones sigue limitada.
- Se mantiene el riesgo conocido de escritores externos entre una comprobación de `FileTransaction` y el reemplazo de un archivo. No existe exclusión absoluta de escrituras simultáneas.
- Quedan 77 avisos visuales no bloqueantes de auditoría CI.
- Las pruebas automatizadas no sustituyen la validación en juegos o consolas reales.

## Informar de problemas

Incluye la versión y el commit de `VERSION.txt`, juego, idioma, pasos para reproducir, semilla y registros pertinentes. No adjuntes ROMs ni archivos extraídos del juego.

## Reproducir el paquete

Desde el commit aprobado en `main`, en Windows con .NET 10 y las dependencias verificadas:

```powershell
.\tools\Publish-Preview.ps1 -OutputDirectory D:\Releases\Preview3 -Version 0.14.0-preview.3
```

Antes de publicar, verifica `DefinitiveRandomizer.exe`, `LegacyBatch/pk3DS.WinForms.exe`, `VERSION.txt`, `LEEME.md`, `SHA256SUMS.txt`, la integridad del ZIP y el arranque del paquete. Solo entonces crea `v0.14.0-preview.3` y marca la release como **prerelease**.
