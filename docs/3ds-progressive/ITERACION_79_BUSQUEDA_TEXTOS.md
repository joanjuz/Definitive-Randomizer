# Iteración 79 · Localizar y entender los textos

Game Text muestra categorías basadas en las referencias reales de cada versión: nombres y descripciones de movimientos, objetos, Pokémon, habilidades, entrenadores, lugares y Pokédex. Los archivos sin categoría verificada se denominan Otros textos. No se inventan relaciones entre diálogos y zonas.

Ambos editores muestran número de archivo, cantidad de líneas y muestra real de contenido. El listado admite descripciones multilínea y filtro por número, categoría o muestra. Al seleccionar un archivo se muestra también un extracto en el pie. Las muestras se actualizan tras guardar/importar/randomizar.

La búsqueda superior consulta frases en TODO el banco del idioma activo, sin distinguir mayúsculas. Se ejecuta en segundo plano y muestra hasta 500 coincidencias con archivo, línea original y contenido. Doble clic abre y desplaza hasta esa línea. La búsqueda consulta texto guardado, conserva índices/códigos y no escribe archivos. Durante ella se bloquean cambios concurrentes y salida del módulo. Los cambios pendientes requieren confirmación antes de navegar a un resultado.

La cabecera explica la diferencia entre los bancos y muestra juego, idioma y ruta GARC. En Story Text, los números no implican orden de la historia; la muestra y la búsqueda permiten localizar diálogos por frases visibles en el juego.

Validación: Debug 0 errores, 79 advertencias. Suite general inicial de esta iteración: 3954 regresiones aprobadas. Tras los ajustes de refresco de catálogo y presentación multilínea, 56 pruebas específicas de búsqueda/Story Text aprobadas sobre el binario final. Auditoría de lectura y búsqueda de ambos bancos reales en X, Y, OR, AS, Sol, Luna, Ultra Sol y Ultra Luna: 16 bancos aprobados y SHA-256 originales sin cambios. Catálogo y búsqueda de muestra entre 34 y 123 ms por banco en esta máquina. No se automatizó el escritorio ni se abrieron ventanas de prueba: la revisión visual corresponde al usuario.

Candidata: D:\Progra\DefinitiveRandomizer-validation\20261014-iteration79-local-candidate\DefinitiveRandomizer.exe

Comprobación manual: abrir Game Text, filtrar movimientos/objetos; buscar una frase del juego y abrir un resultado. Repetir en Story Text y verificar que los números de línea se mantienen al guardar. Las escenas y diálogos no están etiquetados por ubicación cuando no existe correspondencia verificada.

Se conserva la integración ORAS de las iteraciones 77–78. Sin commits, push ni publicación de release. No se modificaron ROM originales.
