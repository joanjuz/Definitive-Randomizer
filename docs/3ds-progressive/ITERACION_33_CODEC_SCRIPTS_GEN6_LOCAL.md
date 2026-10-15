# Iteración 33 · recompresión verificada de scripts Gen6

Se implementa `Gen6ScriptCodec` como base para migrar Field Items XY/ORAS. La herramienta original `FieldItemDumper` edita palabras de scripts y vuelve a codificarlas con grupos de siete bits, además de escribir objetos ocultos ORAS en code.bin. El editor Gen6 nuevo sigue pendiente; esta iteración aporta el lector/reconstructor necesario y sus pruebas, sin habilitar aún el botón Field Items Gen6.

## Implementación

El lector valida cabecera, firmas normal/debug, offsets de instrucciones/movimientos/final, alineación, cantidad declarada y límites de cada palabra. Decodifica hasta cinco grupos de siete bits por instrucción con la misma extensión de signo y truncación uint32 del lector Core. Una interpretación inicialmente más estricta de rango con signo falló en scripts XY reales; se corrigió antes del cierre. No se modifica Core.

La recompresión admite cambios de valor conservando el número de instrucciones. Mantiene cabecera, tabla de punteros y datos anteriores al flujo; actualiza solo la longitud física declarada. Mantiene también el sufijo posterior al flujo y cada secuencia comprimida original de instrucciones ajenas a la edición. Las instrucciones editadas se codifican en el formato firmado de siete bits y se verifica la lectura del resultado. Una operación sin cambios devuelve exactamente los bytes originales, incluidas codificaciones no mínimas.

No se modifica la ROM durante estas operaciones: el codec trabaja sobre arrays. Todavía faltan detección validada de slots Field Items, opciones/pools/plantillas, reconstrucción GARC, cambios conjuntos con code.bin en ORAS, interfaz y validación real posterior. El tail sospechoso de pociones ORAS indicado por el dumper original deberá protegerse al incorporar el randomizador.

## Pruebas

Se verifican límites positivos/negativos de todos los tamaños de grupo, int.MinValue/int.MaxValue y mil palabras aleatorias contra el decodificador Core. Se prueban scripts normal y debug, cambios de longitud comprimida, campos de cabecera ajenos, sufijos opacos, no-op, codificaciones alternativas y conservación de una instrucción alternativa al editar otra. Se rechazan cabeceras/firma inválidas, cantidades excesivas, palabras truncadas, instrucciones sin alineación y cambios de cantidad.

En la referencia XY del usuario se leen los scripts 17 (1.923 palabras) y 26 (1.163 palabras): ambas lecturas coinciden exactamente con Core y ambas reconstrucciones sin cambios conservan los bytes originales. Esta prueba no sustituye editar una recogida y ejecutarla en juego. No se dispone de referencia ORAS identificada para probar sus scripts 39/57.

La auditoría real ampliada mantiene 145 hashes XY (incluido el GARC de scripts) y 143 USUM, además del inventario completo de cada raíz. Catch Zones XY continúa rechazando los 26 orígenes incompatibles documentados en la iteración 32.

Los logs de Debug, suite completa, lecturas reales y hashes de archivos protegidos se guardan en `ITERACION_33_PRUEBAS/`. No se guardan allí binarios de juego.

Rama `feature/3ds-progressive-ui`; sin commits, push ni publicación, y sin cambios en Eris-Ultranova, Wild protegido ni archivos personales. La migración general y la validación in-game permanecen abiertas.

Resultado final: **1.591 comprobaciones aprobadas; Debug 0 errores / 22 advertencias existentes; 12/12 hashes protegidos conservados.**
