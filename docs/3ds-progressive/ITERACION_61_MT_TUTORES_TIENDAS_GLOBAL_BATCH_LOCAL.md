# Iteración 61 — MT y tutores Gen7 y tiendas USUM en Global/Batch

Rama feature/3ds-progressive-ui. Trabajo local sin commit, push ni publicación. Se preservaron las modificaciones de Wild y los archivos personales; los 12 hashes protegidos coinciden. Los originales de cada bloque se respaldaron antes de editar.

## Integración

- MT Gen7: receta bidireccional con aleatorización, Sanity y Follow Evolutions. Replay nativo en el ejecutor empaquetado; asignaciones y descripciones localizadas guardadas conjuntamente. Sanity y herencia usan las MT recién guardadas.
- Tutores USUM: aleatorización de 67 movimientos, precios gratuitos, Sanity y Follow Evolutions. Sincronización de Shop.cro y tabla ARM en code.bin mediante la rutina existente. Se valida la configuración y los formatos antes de escribir en la copia Batch. No se habilitan estas acciones avanzadas en SM.
- Tiendas USUM: aleatorización de inventario normal/BP, caramelos raros, vitaminas 252 EV, megapiedras gratis y prohibición de Cápsula Habilidad. Se conservan los filtros de tiendas especiales y objetos X. Los cambios de Shop.cro y objetos se guardan juntos cuando el preset lo requiere.
- Nuevas opciones de receta en las vistas WPF. Navegar, importar y exportar conserva la selección. Estas opciones describen acciones para Global/Batch; las ediciones manuales de filas mantienen su guardado directo existente.
- Nueva receta Gen7 incluye MT; USUM incluye también tutores y tiendas. Las opciones recordadas tienen prioridad y no se duplican acciones. Desmarcar acciones sincroniza el snapshot. Habilitar una acción nueva en un editor ya no la pierde al exportar una receta cuya lista anterior no la tenía.
- Un template sin RandSettings necesita snapshots de todas sus acciones nativas. Se rechazan opciones ausentes, alcance parcial, flags divergentes y combinaciones de perfiles originales que el puente nuevo no representa. Los templates originales completos conservan su ruta legacy.

## Pruebas

- Debug local: 0 errores; 79 advertencias heredadas.
- Suite completa final: **2906 regresiones** superadas con datos sintéticos XY/ORAS/SM/USUM y WPF, incluida la guarda final de preflight.
- SM/USUM e idiomas 0/6 sintéticos: replay tipado original de MT y descripciones, semilla exacta, opciones incompletas y alcance inválido. Captura/restauración WPF, metadata y selección de acciones.
- UltraSol real sobre copia independiente: tutores gratuitos → preflight → proceso empaquetado → Trimmed3DS → extracción. Resultado idéntico a la propuesta manual y 750 hashes e inventario de entrada intactos.
- UltraSol real sobre copia independiente: caramelos raros + vitaminas 252 EV + megapiedras gratuitas en el mismo recorrido. Shop.cro y GARC de objetos idénticos a las propuestas; 750 hashes e inventario de entrada intactos.
- Prueba real de solo lectura: semillas idénticas generan las mismas propuestas de MT y tutores y tabla ARM. Todas las opciones de máquinas/tutores y tiendas validan; MainWindow y ambas vistas WPF restauran/capturan los valores exactos. Se comprobaron seis archivos fuente antes/después, todos intactos.
- Aleatorización de tiendas/BP con filtros y bloqueo de Cápsula reproduce semilla sobre bytes separados del disco.
- Ningún diagnóstico ejecutó Batch sobre las ROM originales de D:\Nintendo. Entradas bajo D:\Progra\DefinitiveRandomizer-validation. Las pruebas en juego siguen pendientes.

## Límites y cierre pendiente

Este bloque cierra estas integraciones, pero no declara completa ni estable toda la aplicación. Todavía deben cerrarse los perfiles separados Gen6 y sincronización de textos de estáticos/iniciales, recetas nativas restantes y combinaciones legacy, compatibilidad con formatos antiguos, catálogos/opciones especiales y escritura de formatos gráficos pendientes. Falta la matriz final completa de juegos y contenedores y el empaquetado candidato. Los presets y replay aquí ensayados no prueban todas las combinaciones posibles ni su comportamiento en juego.

Los perfiles originales de expansión de tiendas con parámetros propios no se convierten automáticamente al snapshot nuevo; deben usarse con su receta original completa. La edición manual de ranuras ampliables está disponible en el editor nativo.