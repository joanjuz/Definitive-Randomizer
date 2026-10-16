# Iteración 39 — objetos de campo, plantillas y recuperación

Fecha: 2026-10-13. Trabajo local en `feature/3ds-progressive-ui`. Nombre visible: Definitive Randomizer.

## Implementación

- Editor nativo de objetos de campo Gen6: objetos visibles y ocultos XY/ORAS y megapiedras ORAS. Detección de la tabla de ocultos ORAS mediante firma única en code.bin; si falta, no se inventan offsets. Mezcla o catálogo Progressive, exclusiones de máquinas y objetos clave, alcance y vista previa congelada. Exportación CSV y conservación de IDs fuera del catálogo.
- Guardado conjunto de scripts GARC y código cuando corresponde, con comprobación de hashes, historial y conservación de instrucciones y sufijos ajenos al cambio. El modo seguro omite colas sospechosas y objetos desconocidos. Los filtros personalizados completos del antiguo FieldItemDumper siguen pendientes.
- Global Template conserva opciones y alcance de entrenadores, estáticos, objetos, evoluciones, movimientos huevo y objetos de campo; Gen6 añade máquinas, tiendas e intercambios. Importación atómica y conservación de secciones desconocidas. Proyección del formulario original de movimientos huevo Gen7. Batch rechaza las opciones nativas que todavía no puede reproducir para evitar perderlas silenciosamente.
- Historial con respaldo persistente antes de reemplazar archivos y recuperación al abrir el proyecto. Comprueba todos los destinos y respaldos antes de restaurar; rechaza modificaciones externas. Temporales identificados por la operación se eliminan tras recuperar. Las lecturas diagnósticas de ROM deshabilitan expresamente la recuperación.

## Validación local

- Compilación Debug: cero errores y 79 advertencias.
- Suite: 1820 comprobaciones con datos sintéticos XY, ORAS, SM y USUM, incluidos controles WPF, exportación CSV, propuestas obsoletas y preservación de bytes.
- Un proceso hijo terminó deliberadamente entre dos reemplazos: la recuperación restauró los originales y eliminó temporales. También se probaron respaldo alterado, edición externa, rollback normal y destinos fuera del proyecto. Esto no constituye una prueba de corte eléctrico o fallo físico del disco.
- Lectura y vista previa sin Apply en PokemonX y PokemonY: 557 entradas por carpeta; PokemonOR y PokemonAS: 603. Inventario y hashes de fuentes preservados. Las ROM originales no fueron modificadas.
- Archivos protegidos Wild y notas personales conservados según el inventario SHA-256 local. Sin commits, push, publicación ni modificación de Eris-Ultranova.

## Pendientes para una versión estable completa

La compilación no se declara estable completa. Faltan la proyección de todos los módulos y perfiles Gen6 a Global/Batch, filtros personalizados completos de objetos de campo, espejo localizado de textos de iniciales y funciones vanilla pendientes como tutores Gen6, instalaciones de combate y herramientas de imágenes/archivos. También faltan validar procesos prolongados, extracción/rebuild y empaquetado local, además de pruebas finales con las variantes compatibles y validación en juego.

Las pruebas reales de esta iteración verifican lectura y propuestas; no demuestran el funcionamiento en juego de los nuevos objetos de campo. No se solicita todavía la validación final del usuario.
