# Iteración 56: integración y pruebas completas del flujo de ROM

Fecha: 2026-10-13. Rama `feature/3ds-progressive-ui`. Trabajo local; no commits, push, publicación ni merge con Eris-Ultranova.

## Cambios funcionales

- **Global Template:** captura y recuperación de Shiny Rate al navegar; exportación de ambas opciones explícitas; nueva receta nativa Gen6 sin modificar la ROM. Una receta parcial solo pasa a completa si todas sus acciones seleccionadas son nativas, su alcance es Todos, sus opciones son compatibles y sus assets se verifican. Los perfiles legacy incompletos siguen bloqueados.
- **Batch ROM:** se corrigió una condición del backend original que admitía únicamente Gen7 y bloqueaba los lotes Gen6. El lote utiliza ahora el servicio de reconstrucción que comprime `.code.bin` correctamente. Shiny Gen7 también usa el puente seguro. Game Text se conserva sin reserializar cuando sus líneas no cambian.
- **Shiny ORAS:** había tres coincidencias de la firma PID. Se reconoce solamente la candidata cuya instrucción ARM previa es compatible; dos candidatas válidas siguen siendo un error. Se verificó el proceso original independiente en X, Y, OR y AS.
- **Extracción/rebuild:** el extractor admite el cifrado con clave fija cero producido por el constructor original, mediante una copia privada. No se añaden claves de ROM retail. Se corrigió el hash ExeFS de 200 bytes a 0x200 bytes. RomFS usa un temporal único; el staging de reconstrucción reside en el volumen de salida.
- **Rendimiento:** eliminación de asignaciones repetidas en el bucle AES; caché de ExeFS comprimido con comprobación de hashes de todos sus archivos e invalidación ante cambios.
- **Story Text:** mezcla de diálogos literales de un archivo o del banco del idioma activo. Las líneas vacías, variables, escapes y códigos de control permanecen en su posición. Vista previa determinista, relectura de texto, fingerprint, historial y rechazo de propuestas de otro editor. No mezcla Game Text ni añade una acción Global no soportada.
- **Restauración selectiva:** selección de archivos existentes de RomFS/ExeFS de un backup ZIP verificado de la misma familia. No recupera archivos personales. Propuesta congelada, comprobación de todos los fingerprints antes de escribir, historial y recarga del proyecto. Máximo 128 MiB por operación; la restauración completa a carpeta nueva continúa disponible.
- **SMDH:** editor WPF de títulos, descripción y publisher de 16 idiomas; importación/exportación SMDH y PNG de 24×24/48×48. Codec RGB565 por tiles; edición de texto e imagen preserva las demás secciones. UTF-16 válido, tamaños y conflictos externos comprobados. Referencia del formato: https://3dbrew.org/wiki/SMDH#Icon_graphics.
- **BCLIM/BFLIM:** visor WPF y exportación PNG; conversión PNG a BCLIM RGBA8 en una salida independiente. No sustituye el archivo de origen. La escritura de otros formatos/ETC no está implementada en esta vista.
- **GARC Shuffler Gen6:** mezcla referencias FATB de archivos simples; preserva subarchivos de carpetas, payload y lista de protección XY/ORAS del original. Valida las tablas y requiere un backup ZIP fuera del proyecto antes de aplicar. Ese backup se recupera mediante restauración selectiva. Oculto para Gen7 porque el original no proporciona una lista equivalente de protección.

## Evidencia local

El directorio `ITERACION_56_PRUEBAS` contiene logs de Debug, regresiones, capturas de datos sintéticos y preservación. Los contenedores y copias con datos del usuario están únicamente en `D:\Progra\DefinitiveRandomizer-validation`; no se incluyen en documentación ni se publican.

1. Proceso original independiente: X y Y, 656 archivos cada uno; OR y AS, 658 cada uno. Propuesta Shiny y bytes ARM exactos; archivos ajenos de las copias y hashes de las fuentes preservados.
2. Flujo real WPF preflight → proceso original → ejecución → Trimmed3DS → extracción: X (656 archivos) y UltraSol (750). Cambio Shiny exacto; todos los demás archivos recuperados coinciden byte por byte; hashes e inventario de las fuentes intactos.
3. Full3DS de X → extracción: 656 archivos exactos. Antes del arreglo, este ensayo detectó que el extractor no entendía el cifrado del constructor original.
4. La última batería completa local comprueba datos sintéticos XY, ORAS, SM y USUM, UI WPF y ejecución del backend empaquetado. 2519 comprobaciones pasaron; el resultado final está al final de `iteration56-regressions-final.log`. Debug: 0 errores, 79 advertencias heredadas.
5. SHA-256 de los doce archivos protegidos conserva Wild 001–003 y archivos personales. `git diff --check` pasó.

Los lotes reales anteriores contienen una acción Shiny por ROM. No prueban todas las combinaciones de módulos ni un lote de múltiples semillas. Las pruebas sintéticas tampoco equivalen a validación en juego.

## Pendientes reales para el cierre estable

No se declara paridad completa ni versión estable final. Permanecen:

- Tutores efectivos ORAS: `TutorEditor6` original avisa explícitamente que sus cambios no se reflejan en juego. No se presenta esa tabla como parche funcional sin identificar y validar la tabla/instrucciones efectivas.
- Title Screen Editor Gen6: aún no hay un flujo WPF equivalente para los DARC de imágenes XY/ORAS.
- Proyecciones Global adicionales de opciones WPF a formatos originales y recetas nativas Gen7 completas. Alcances parciales y snapshots legacy incompletos siguen rechazados; no se sustituyen silenciosamente por opciones por defecto.
- Espejo localizado de textos de estáticos y controles legacy separados de perfiles Gen6; ayudas de selección de movimientos de entrenadores; catálogos personalizados completos de scripts/Field Items y otras variantes aún marcadas en la matriz.
- Backups antiguos ajenos al manifiesto ZIP, formatos gráficos adicionales y variantes de templates externos no reconocidas.
- Matriz de juego: X, Y, OR, AS, S, M, US y UM. Las validaciones Wild que el usuario ya realizó se conservan; estas incorporaciones aún necesitan pruebas in-game. No se solicita esa validación como cierre mientras resten módulos de implementación.
- Ensayos de Batch combinando todos los módulos, reconstrucción en todas las variantes y empaquetado final. No se ha publicado una release.

Las filas históricas de la matriz deben leerse con su iteración: algunas notas anteriores sobre Batch, Shiny y restauración se resuelven aquí; los límites anteriores que no se mencionan como resueltos siguen vigentes.
