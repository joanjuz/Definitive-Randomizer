# Iteración 26 — O-Powers Gen6 y auditoría de tutores

Implementación local en `feature/3ds-progressive-ui`. No hubo commits, push, publicaciones ni cambios en Eris-Ultranova. Las pruebas con ROMs reales XY/ORAS/USUM quedan para el final de toda la migración, según la indicación del usuario.

## O-Powers

Nuevo editor WPF desde Mecánicas y CRO para XY/ORAS, basado en `pk3DS.WinForms/Subforms/Gen6/OPower.cs`. Localiza una firma única `49461845` con terminador cero desde 0x400000, omite el padding FF con control de límites y valida la tabla de 65 registros de 22 bytes. Se editan los IDs 1–64; el registro cero permanece intacto.

Los campos escritos coinciden con SetEntry vanilla: coste jugador y otro jugador, etapa, requisito de subida, eficacia ushort, duración byte y usabilidad. Solo se permiten valores nuevos 0/2/254 de usabilidad; un valor original desconocido puede conservarse al cambiar otros campos. El valor especial 255 de duración se conserva como byte, sin atribuirle una semántica no comprobada.

Se mantienen calidad, byte 2, bytes desconocidos 5/21, orden, referencias de texto, padding, firma y registros ajenos. Las referencias se muestran con los textos del proyecto pero no se pueden cambiar. El guardado exige confirmación y utiliza relectura, detección de cambios externos e historial; repetir un guardado sin cambios no añade otra entrada. El shell oculta el botón y alcance de randomización de este editor, dado que vanilla no ofrece esa operación. No se modificó RecordEditorView ni los archivos Wild protegidos.

O-Powers vanilla advierte que dar S/MAX en juego requiere más investigación. Migrar la tabla editable no resuelve esa entrega; no se declara implementada ni validada.

## Auditoría de tutores Gen6

Se revisó `TutorEditor6.cs`, su llamada desde `Main.cs`, referencias en Core y las lecturas de tutores del nuevo GameProjectSession. Vanilla busca FormatType/EdgeID o una cadena de compilación alternativa y escribe cuatro grupos 15/17/16/15 con terminadores en code.bin. Su constructor avisa explícitamente que esos cambios no se reflejan en juego y que falta investigación.

No se encontró en las rutas revisadas otro escritor Gen6 que sincronice esa tabla con la fuente activa del juego. Esto es un resultado limitado de la auditoría del repositorio, no una demostración de que no exista otra fuente en la ROM. Los tutores Gen6 permanecen pendientes; copiar esa edición y marcarla como funcional completa sería insuficiente. La investigación debe contrastar tablas activas/scripts y llamadas o aportar una implementación respaldada por datos.

## Validación y pendientes

Debug local: **0 errores / 22 advertencias existentes**. Suite completa: **1.368 comprobaciones aprobadas**. Los nuevos fixtures XY/ORAS con/sin padding FF verifican últimos registros, campos exactos, bytes desconocidos, registro cero, valores límite, referencias de texto protegidas, usabilidad desconocida, idempotencia, historial, cambios externos, firmas ausentes/duplicadas, tabla truncada y padding sin fin. Se verificó navegación WPF, ocultación del randomizador y captura del tema oscuro.

Logs, captura y SHA-256 de los 12 archivos protegidos se conservan en `ITERACION_26_PRUEBAS/`. La matriz actualiza O-Powers como editor implementado con validación real posterior; la entrega S/MAX y los tutores siguen pendientes.

La migración completa continúa abierta: tutores y Sanity/Follow Gen6, Field Items/scripts, herramientas vanilla restantes, opciones exhaustivas Static/entrenadores, Global Template bidireccional/Batch integral, recuperación durable, rendimiento y empaquetado. No se solicita prueba en ROM real todavía.
