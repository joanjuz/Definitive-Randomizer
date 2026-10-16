# Iteración 42 — evoluciones originales y Global Template

Fecha: 2026-10-13. Rama local `feature/3ds-progressive-ui`.

## Implementación

El editor de evoluciones conserva los ajustes personalizados existentes y añade tres operaciones de EvolutionRandomizer original de pk3DS para Gen6 y Gen7: randomizar destinos, evolucionar en cada nivel y eliminar evoluciones por intercambio. Expone los filtros originales BST, crecimiento de experiencia, tipo, legendarios y míticos. No sustituye el motor original por una aproximación nueva.

La conversión original de intercambio simple usa nivel 30; intercambio con objeto pasa a subir de nivel con el objeto; Shelmet/Karrablast conserva la condición de especie en el equipo. El modo personalizado sigue ofreciendo el nivel configurable previamente implementado.

Las operaciones producen una vista previa congelada, con semilla interna, y solo trasladan a la propuesta los registros incluidos en el alcance. El guardado utiliza las comprobaciones de fuentes y el historial existente. Importar o exportar Global Template no escribe datos de juego.

Global Template importa y exporta los cinco filtros desde EvolutionEditor6/7 y deduce el modo desde las acciones del perfil original. Los snapshots nativos antiguos, sin Operation, mantienen el modo personalizado. Batch Gen7 acepta alcance Todos y exactamente una acción de evolución coincidente con el modo original elegido; rechaza mezclas, modo personalizado, selección parcial y recetas Gen6, cuyo ejecutor aún falta. Los controles desconocidos se conservan.

## Validación local

Comparación byte a byte contra EvolutionRandomizer con la misma semilla en XY, ORAS, SM y USUM, para las tres operaciones y con los filtros activados y desactivados. Comprobaciones de preservación de fuentes, registros fuera de alcance, conversión de los tres métodos de intercambio, importación de controles y rechazo de recetas incompatibles. Compilación Debug y suite completa local; logs en ITERACION_42_PRUEBAS.

No se ejecutó un lote real ni se solicitaron pruebas en juego. Siguen pendientes el ejecutor Batch Gen6, proyecciones de módulos restantes, herramientas vanilla y la validación final de rebuild, empaquetado y juego. Sin commits, push, publicación ni cambios en Wild protegido, ROM originales, archivos personales o Eris-Ultranova.
