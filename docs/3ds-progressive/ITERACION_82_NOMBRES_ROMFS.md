# Iteración 82 — preservar nombres RomFS al guardar

2026-10-15. Rama feature/3ds-progressive-ui.

El usuario confirmó que el control B continúa fallando. Se leyó el registro Lime3DS: error fatal 0xC8804470 a las 00:57:55, justo después de descargar DllField; no se ve una carga posterior de DllBattle.

Hallazgo comprobado: el original PokemonAS contiene DllBattle.cro, mientras ASU extraído contiene DLLBattle.cro. Una reproducción independiente con FileTransaction.Commit y destino escrito DLLBattle.cro cambia el nombre físico de un DllBattle.cro existente. El control A restauraba el contenido, pero no la escritura del nombre, por lo que no descartaba este defecto.

Corrección: FileChange resuelve el nombre existente en el directorio antes de guardar o recuperar la transacción. GameMoveTemplateService utiliza DllBattle.cro para Gen6. No se eliminaron otras modificaciones del repositorio.

Se añadieron tres regresiones: conservar nombre y bytes tras guardar con ruta de diferente capitalización, fallo inyectado, y recuperación conservando nombre y bytes.

Validación: compilación Debug local 0 errores / 79 advertencias; 91 comprobaciones de regresión Batch aprobadas, incluidas las tres nuevas. La prueba previa con la DLL anterior reproduce el cambio de nombre y falla, por lo que la regresión detecta el defecto real.

Control C: D:\Progra\DefinitiveRandomizer-validation\DiagnosticoORAS81\ASU-C-nombre-modulo-corregido.3ds. SHA256 8E4A7366710B4728D158096AC2A2B2D8EA60C9BDCCB280ED22BC4207A865494C. Único cambio de entrada respecto al B: nombre DllBattle.cro corregido, contenido conservado. Pendiente prueba de combate por el usuario. No afirmar aún que esta sea la única causa ni que la ROM integral esté validada.

No se controló escritorio/emulador, no se modificaron originales, no hubo commit, push ni publicación.

## Confirmación y candidato local
El usuario confirmó que el primer combate funciona con el control C. Como C solo cambia la escritura del nombre frente a B, queda confirmado el impacto de ese nombre en el fallo observado. Se reconstruye además un control D desde una extracción nueva del ASU original completo; conserva todas las opciones del ASU y corrige únicamente DllBattle.cro. La validación integral de D permanece pendiente.

Regresión adicional tras la corrección: 132 comprobaciones Global aprobadas. Candidato local nuevo: D:\Progra\DefinitiveRandomizer-validation\20261015-iteration82-local-candidate\DefinitiveRandomizer.exe. App DLL SHA256 4A874F4748EC68EF3397C47CE531A884B68654338B9CD3A6F8121F40B598E449. Incluye carga Global en segundo plano y la corrección de nombres. Los proyectos antiguos que ya contengan un nombre incorrecto no se reparan automáticamente por abrirlos; el cambio evita introducirlo en guardados nuevos.

Control D integral terminado: D:\Progra\DefinitiveRandomizer-validation\DiagnosticoORAS81\ASU-D-integral-corregido.3ds. SHA256 462A8BE61BC351DA5A1D6CE1CB54F805162DBD7676D21344FE0192E00C6205C8. Procede de una extracción independiente de ASU_01.3ds (SHA 67EEF041402DA0DD479A422A0235AE721979AE60E455634420EED382E17B8690), cambiando solamente el nombre DllBattle.cro antes de reconstruir. No se restauraron archivos vanilla en este control: conserva todas las opciones del ASU original. Pendiente comprobar combate salvaje y entrenador. También pasan las 45 comprobaciones native master batch con la versión corregida (268 comprobaciones locales sumando las tres suites).
