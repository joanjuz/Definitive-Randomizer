# Iteración 83 — compatibilidad DEFINITIVO antiguo

2026-10-15.

Origen preservado: D:\Progra\3DS-Progressive-Preview-win-x64\custom_balance_templates\DEFINITIVO.json.

Causa reproducida: TrainerLogicHost.Restore no admitía Trainer.MoveSettings.Source como string (DontModify) aunque el formato antiguo sí lo serializaba así. Error real: JsonException en $.MoveSettings.Source. Corrección: JsonStringEnumConverter en el importador, compatible con enums numéricos y nombres. La validación posterior sigue rechazando valores no definidos.

Los cuatro hashes Assets del JSON antiguo no coinciden con los cuatro archivos proporcionados en su carpeta. No es posible asumir que esos archivos sean los mismos usados en octubre 10. El paquete compatible usa deliberadamente las copias actuales aportadas por el usuario y actualiza sus hashes. El manifiesto compatibilidad.json conserva hashes anteriores y actuales.

Paquete: D:\Progra\DefinitiveRandomizer-validation\Template-DEFINITIVO-compatible83\DEFINITIVO_COMPATIBLE_USUM.json. Se mantienen las 35 acciones, RandSettings, Wild, Trainer y LevelCaps; únicamente Source se convierte al número equivalente y se actualizan las cuatro firmas. Se copian exactamente moves_gen7.csv, evolutions_gen7.csv, pokemon_stats_gen7.csv y field_items.txt. Exclusivo USUM, Gen7, conforme al original.

Aplicación local: D:\Progra\DefinitiveRandomizer-validation\20261015-iteration83-DEFINITIVO\DefinitiveRandomizer.exe. Incluye el importador corregido, la corrección previa de nombres RomFS, carga Global en segundo plano y los cuatro assets del usuario en custom_balance_templates. Usar este candidato para evitar resolver assets homónimos de otra versión. Ninguna instalación anterior se sobrescribió.

Pruebas: Debug 0 errores, 79 advertencias; 96 regresiones Batch aprobadas, incluidas cuatro variantes string/número de Source y rechazo de valor 999. Importación preparada del original con DLL nueva aprobada. Importación preparada de la copia compatible aprobada contra una copia real USUM, sin cambiar SHA de movimientos. Preflight Batch: 35 acciones, cero avisos, assets y backend encontrados. No se construyó ni se probó en juego una ROM nueva con este template: preflight no prueba comportamiento in-game.

El usuario confirmó previamente que el control ORAS C entra en combate; el control integral ORAS D también funciona según su último mensaje. No se controló el escritorio ni se realizaron commit/push/publicaciones.
