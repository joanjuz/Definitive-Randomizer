# Iteración 59 — Global/Batch, generaciones y descripciones de MT

Trabajo local en `feature/3ds-progressive-ui`, sin commit, push ni publicación. Los 12 archivos protegidos mantienen sus SHA-256; no se alteró Eris-Ultranova ni el material personal.

## Implementado

- Entrenadores: el control individual y la columna «Z-Move» se ocultan en XY/ORAS. Se conserva el estado importado que no corresponda a la generación, sin aplicarlo desde esos controles. El catálogo de movimientos Gen6 no presenta variantes Z ni IDs fuera de la tabla real.
- Global bidireccional de Pokémon y movimientos: captura alcance, acción, slots de estadísticas, porcentajes, opciones de tipos/categorías y campos numéricos desactivados que contienen valores válidos. Restaura estos controles sin guardar ROM. Conserva propiedades de snapshots desconocidas.
- Replay nativo de Pokémon/movimientos en XY/ORAS/SM/USUM: estadísticas aleatorias, modificadores, Metrónomo, balance CSV y estadísticas desde CSV. Configuración completa y una acción coincidente por módulo; alcance Todos para construir ROM. Las capturas parciales se conservan para restauración de UI y se rechazan en replay completo.
- CSV: SHA-256 capturado y verificado antes de replay. Cambios posteriores no se aceptan silenciosamente. Se mantiene el comportamiento de los templates incluidos y el motor ya existente de parches/descriciones de movimientos.
- Plantillas de estadísticas Gen6: soporte faltante migrado desde Progressive; CSV propio `pokemon_stats_gen6.csv`, con ejemplos comentados. SOS sigue siendo exclusivo de Gen7 y MO de Gen6.
- Objeto de campo Gen7: snapshots WPF reproducibles con semilla y los cuatro filtros existentes. Historial con `field-items.randomize`. No muestra MO en Gen7.
- Economía Gen7: replay del preset completo desde snapshot; rechaza confundir `items.fix-economy` con un porcentaje personalizado.
- Recetas nativas: botón para la generación abierta. Gen7 configura estáticos, campo y economía; incorpora Pokémon/movimientos cuando sus herramientas fueron usadas y capturadas. No inicia cambios de juego al crear la receta.
- MT Gen7: guardado WPF actualiza conjuntamente code.bin y la descripción del objeto MT afectado en el idioma activo. Verifica ambos archivos antes del commit, conserva bancos y descripciones ajenos, y recarga el texto. La API antigua sin sincronización explícita se conserva para callers anteriores.
- El ejecutor empaquetado guarda texto pendiente antes del replay nativo y recarga las tablas después; evita que una caché anterior sobrescriba cambios de módulos nuevos.

## Pruebas

- Debug final: 0 errores; 79 advertencias heredadas.
- **2751 regresiones** sintéticas XY/ORAS/SM/USUM. Incluyen visibilidad, Global WPF bidireccional, semilla, CSV modificado, alcance, preservación de tablas/bancos y descripciones MT en idiomas 0 y 6 para SM/USUM.
- Dos módulos combinados en copias de X y UltraSol: balance CSV de movimientos y modificadores de Pokémon (captura, incubación, experiencia y compatibilidad MT/MO según generación).
- Flujo: preflight WPF → proceso empaquetado → replay nativo → Trimmed3DS → extracción. Ambas salidas coinciden exactamente con las propuestas manuales de los dos GARC. Los **656 hashes de X** y **750 de UltraSol**, además del inventario de las copias de entrada, permanecieron intactos.
- Todas las entradas de esos procesos eran copias bajo `D:\Progra\DefinitiveRandomizer-validation`; no se ejecutó el constructor sobre las ROM originales de `D:\Nintendo`.
- Conservación SHA-256 de los 12 archivos protegidos. Evidencias en `ITERACION_59_PRUEBAS`.

Un build intermedio encontró DLL bloqueadas por nuestros ensayos que aún estaban corriendo. Se esperó su terminación y se volvió a compilar; no se cerraron procesos del usuario. El resultado final anterior corresponde al build corregido y la suite final.

## Trabajo pendiente

Este informe no declara completa la migración ni una versión estable. Siguen pendientes otras proyecciones avanzadas Global/Batch (máquinas/tutores Gen7, tiendas y combinaciones de perfiles), textos localizados de estáticos y perfiles separados Gen6, catálogos de scripts y opciones O-Power especiales, formatos antiguos de backups/templates, otros encoders gráficos, matriz completa de contenedores y versiones y empaquetado final. Las pruebas en juego se harán después de cerrar implementación; los ensayos reales aquí cubren acciones específicas, no todas las combinaciones.

Los CSV personalizados capturados conservan su ruta y huella. Si se traslada una receta a otra máquina, sus assets deben estar disponibles y coincidir; no se promete resolución portable de todas las rutas externas en esta iteración.
