# Referencia de variables y comandos de texto

Añadido el botón «Variables y comandos» a Game Text y Story Text. Abre una ventana modal con el tema oscuro de la aplicación, búsqueda por nombre/función y filtro de códigos presentes en el archivo guardado seleccionado.

La referencia muestra función, sintaxis, notas de parámetros y ejemplo copiable. Se alimenta de GameConfig.Variables del juego abierto y del lector TextFile original. Incluye los escapes de línea, desplazamiento, limpieza, barra y corchete literal, WAIT, el marcador reservado y todas las variables registradas para ese juego. Los identificadores hexadecimales observados sin nombre conocido aparecen explícitamente como no documentados.

Los ejemplos observados conservan todos sus parámetros. Las plantillas simples se distinguen de los ejemplos reales; su reversibilidad de codificación no garantiza que el evento tenga preparado el valor necesario. No se inventan parámetros copiables para variantes gramaticales complejas sin un uso original. Se explica la diferencia entre parámetros hexadecimales de VAR y valores decimales de WAIT; no se afirma una unidad temporal de WAIT no documentada.

Copiar solo escribe en el portapapeles cuando el usuario pulsa el botón; no inserta, guarda ni modifica la ROM. Si el portapapeles está ocupado, el usuario puede seleccionar y copiar el ejemplo manualmente. Los literales escapados no se confunden con comandos activos. Los ejemplos proceden del archivo guardado, no de cambios pendientes en la tabla.

Validación: 152 comprobaciones de catálogo y reversibilidad para XY, ORAS, SM y USUM, una de inicialización de la referencia sin mostrar ventana, ocho de integración de los editores y 56 regresiones de búsqueda/guardado: 217 en total. Debug compilado sin errores con UseAppHost=false. La distribución no se actualizó, ni se generó un .exe nuevo. No se controló el escritorio. Sin commit ni push.
