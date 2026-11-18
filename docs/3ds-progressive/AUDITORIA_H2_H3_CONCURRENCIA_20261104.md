# Auditoría H2/H3 — carreras entre comprobación y reemplazo

**Estado:** investigación; **base:** `main` en `9f7b5a2c289f73ca4fe5498fc12ff69259a9963b` (release 0.14.0-preview.3). **Fecha:** 2026-11-04.

## Objetivo y límites

Reproducir de forma determinista dos ventanas de concurrencia antes de diseñar una corrección. Todas las pruebas deben usar archivos binarios sintéticos bajo una carpeta temporal propia y **nunca** apuntar a una ROM real, extracción del usuario, carpeta de mods o releases. La auditoría no modifica el código de producción ni afirma que los defectos estén corregidos.

## Código examinado

- `3DS.Progressive.App/Services/FileTransaction.cs`: `Commit` comprueba `Fingerprint` de `FileChange`, crea temporales, comprueba otra vez todos los archivos, vuelve a ejecutar `ReadUnchanged` inmediatamente antes de `File.Replace` y relee el resultado. En excepción, verifica `Proposed` antes de escribir `Original` mediante otro `File.Replace`.
- `3DS.Progressive.App/Services/RecoveryJournal.cs`: `Begin` crea manifest y originales duraderos; `Recover` primero valida el conjunto y luego, archivo por archivo, vuelve a comparar hashes antes de `File.Replace`.
- `3DS.Progressive.Tests/TemplateChecks.cs`: verifica fallos de guardado y rollback ordinario.
- `3DS.Progressive.Tests/RecoveryChecks.cs`: verifica interrupción entre reemplazos y rechazo de cambios externos que **ya estaban presentes** al validar.
- `3DS.Progressive.Tests/UndoToctouChecks.cs`: dispone de una barrera con oplock de Windows para una ventana anterior de `RestorePoints.Undo`. Es una referencia de técnica, **no** una prueba de H2/H3.

## H2 — FileTransaction.Commit, escrituras de terceros

**Ventana de riesgo:** `GarcTransaction.ReadUnchanged(f.Path, f.Fingerprint)` comprueba una instantánea; después se llama a `File.Replace(temporary[f.Path], f.Path, null)`. Esas operaciones no son atómicas entre sí.

**Reproducción requerida, sin sleeps como mecanismo de ordenación:**

1. Crear `first.bin` y `second.bin` sintéticos; preparar `FileChange` originales/propuestos y el journal.
2. Intercalar una escritura de un segundo actor **después de finalizar la última lectura de verificación** de `first.bin` pero **antes de su reemplazo**. Usar un punto de sincronización verificado del sistema operativo (por ejemplo oplock u otro mecanismo determinista); no modificar el código de producción únicamente para generar el fallo.
3. Comprobar si el tercero queda sobrescrito y si el commit comunica éxito indebidamente. Registrar SHA-256 y bytes observados.
4. Repetir con dos archivos y fallo inducido después del primer reemplazo, de modo que rollback examine un destino ya editado por un tercero. Asegurar que un fallo parcial no se silencie.
5. Casos de control: sin escritor externo el commit termina; con un cambio **antes** de la verificación se rechaza sin escribir; con un cambio tras el reemplazo, rollback rechaza sobrescribirlo cuando detecta bytes diferentes.

**Propiedad esperada:** nunca sobrescribir cambios de terceros desconocidos; no registrar éxito si no se conservan los bytes propuestos; ante imposibilidad de rollback dejar diagnóstico claro y material recuperable, sin alterar archivos adicionales.

## H3 — RecoveryJournal.Recover, carreras en restauración

**Ventana de riesgo:** las lecturas `Hash(File.ReadAllBytes(file.Path))` del preflight y la segunda verificación por archivo no bloquean la escritura de otros procesos entre comprobar y `File.Replace`. Además, si un tercero escribe entre restauraciones, una recuperación parcial debe conservar información suficiente para revisión/reintento.

**Reproducción requerida:**

1. Construir un journal real con `Begin` para dos archivos sintéticos y dejar un guardado interrumpido (usar proceso hijo, como en `RecoveryChecks`).
2. Intercalar una edición externa entre la relectura de `After` y el `File.Replace` de `Recover`, mediante barrera determinista.
3. Comparar archivo original, edición externa, historial y estado del journal antes y después; registrar si se pierde la edición o se limpia el respaldo de forma prematura.
4. Repetir alterando un archivo posterior durante la restauración del primero: documentar si la recuperación queda parcialmente aplicada y qué opciones de reintento existen.
5. Casos de control: recuperación sin tercero; tercero que modifica **antes** del preflight; original corrupto; intento repetido después de una interrupción.

**Propiedad esperada:** validar todo el conjunto antes de modificarlo y conservar cualquier edición desconocida. El journal no debe borrarse cuando todavía se necesita reconciliar un estado parcial.

## Metodología de pruebas

- Preferir pruebas locales Windows/NTFS con sincronización explícita que demuestre el punto de pausa (no temporizadores ni bucles que dependen de la velocidad de CPU).
- Watchdogs de 15–30 s solo para detectar bloqueos, no para ordenar intercalaciones.
- Capturar observaciones y fallar con un diagnóstico claro, separando «vulnerabilidad reproducida» de «propiedad verificada».
- Integrar un modo aislado en `3DS.Progressive.Tests/Program.cs`, actualizar `RunnerCatalog.cs` y `RUNNER_PRUEBAS.md`; posteriormente incluirlo en CI Windows.
- No introducir cambios de producción en la primera PR de reproducción. Los cambios de seguridad se harán en PR aparte tras tener casos reproducibles.
- Los tests diseñados para exhibir un defecto pueden fallar inicialmente; mantener su PR como draft o utilizar un runner de diagnóstico claramente segregado, sin desactivar el CI de `main`.

## Límites de una eventual solución

Un lock entre instancias del propio Randomizer **no** impide que otro editor o antivirus modifique un archivo. Un hash adicional reduce la ventana pero no la elimina. Una solución debe estudiar handles, `FileShare`, semántica de `File.Replace` en NTFS, garantía multiarquivo y tratamiento de borrado/renombrado/symlinks; evitar afirmar atomicidad multiarchivo. Evaluar el efecto sobre editores externos legítimos y sobre escenarios de recuperación tras cierre forzado.

## Criterio para cerrar H2/H3

1. Reproducción determinista documentada en el código actual.
2. Corrección mínima respaldada por las pruebas antes fallidas, sin pérdida de cambios externos.
3. Controles normales, rollback, interrupción y recuperación pasan.
4. CI Windows sin regresiones y revisión del comportamiento al reiniciar.
5. Limitaciones residuales y decisiones explícitas documentadas en notas de release.

**Resultado de esta etapa:** solo localización de las ventanas por inspección de código; **no se ha ejecutado una reproducción en Windows ni implementado una corrección**.
