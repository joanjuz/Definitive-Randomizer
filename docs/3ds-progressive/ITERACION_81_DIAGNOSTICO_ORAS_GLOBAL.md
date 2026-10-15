# Iteración 81 — diagnóstico de combate ORAS y carga Global

Fecha: 2026-10-15.

## Resultado actual
El usuario confirmó que ASU-A-sin-movimientos-especiales.3ds sigue cerrándose al entrar en combate. Restaurar movimientos y DLLBattle originales no basta para resolver el fallo. No atribuir el cierre a Cerrojo Feérico: el motor Gen6 no instala ese parche Gen7.

Se preparó ASU-B-sin-catch-zones.3ds en D:\Progra\DefinitiveRandomizer-validation\DiagnosticoORAS81. Mantiene los movimientos y DLLBattle originales del control A; restaura además desde PokemonAS los miembros encdata/mapGR y el recurso raw enumerados en el manifiesto ORAS de Catch Zones. Esta restauración también elimina cualquier otra edición en esos mismos miembros; un resultado positivo requerirá aislar más la causa. SHA256: F8EFE39D3FB1C90503BCBD08AF45C42EF10B26E1938A08EA8A2603E9CDDF5732. Pendiente prueba de combate por el usuario. No se controló el escritorio ni Lime3DS.

La extracción original y ASU_01.3ds se conservaron. Solo se modificó ASU-extraido, copia diagnóstica propia.

## Global Template
La importación desde el botón prepara y valida las opciones fuera del hilo UI, y aplica el estado visual después. Se preservó la ruta síncrona para los consumidores existentes. Importar opciones no ejecuta vistas previas de randomización de movimientos, máquinas ni tiendas. La validación completa permanece en la ejecución del lote.

Prueba con recetas reales: preparación USUM 23718 ms; ORAS 857 ms. Ambas producen el delegado de aplicación y conservan el SHA del archivo de movimientos. La preparación USUM sigue tardando; no se ha demostrado una carga instantánea ni se ha validado visualmente la interacción completa.

## Verificación
- Compilación Debug local: 0 errores, 79 advertencias.
- Global options: 132 comprobaciones aprobadas.
- Native master batch: 45 comprobaciones aprobadas.
- Auditoría de la copia ORAS: 721 especies y 826 learnsets, sin registros inválidos en tipos, habilidades, estadísticas, niveles e IDs de movimientos según límites Gen6.
- La reconstrucción y su SHA verifican el archivo generado, no su funcionamiento en juego.

## Pendiente
Obtener resultado in-game del control B; continuar con ablaciones y el registro fatal de Lime3DS si persiste. No considerar resuelta la estabilidad de combate ni validada la aplicación completa.
