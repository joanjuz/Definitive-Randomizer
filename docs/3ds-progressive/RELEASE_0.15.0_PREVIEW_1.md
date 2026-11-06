# Definitive Randomizer v0.15.0-preview.1

Candidato Windows x64 **prerelease**, no estable. Incluye el sistema de mentas Gen7 integrado en `main` mediante los PR #50, #52 y #53, con base funcional `cd636308e3384748638913bddcc88732894b70e1`. La preparación de esta versión solo cambia metadatos y documentación. Este documento no autoriza una publicación ni la creación del tag definitivo.

## Novedades desde 0.14.0-preview.4

- **21 mentas funcionales para Ultra Sol y Ultra Luna**, con perfiles separados y comprobaciones de identidad, huellas del ejecutable y datos originales. Cambian la **naturaleza real** del Pokémon; no implementan el sistema estadístico independiente de generaciones posteriores.
- **Instalación segura, historial y Deshacer:** propuesta antes de escribir, verificación de dependencias e integridad CRO/CRR, transacciones y restauración de los archivos afectados. Un estado parcial, una revisión no acreditada o cambios externos incompatibles bloquean la operación. Deshacer archivos del proyecto no revierte naturaleza ni inventario ya guardados en una partida.
- **Iconos opcionales desde PokéAPI:** descarga y conversión local de 21 imágenes con SHA-256 fijados; instalación en el proyecto sin reemplazar los iconos anteriores. Los gráficos oficiales **no vienen empaquetados** ni se descargan al abrir la aplicación. La primera preparación requiere conexión; se puede reutilizar la caché validada. Crédito de los gráficos: © The Pokémon Company; alojamiento: PokéAPI/sprites. La descarga no concede permiso para redistribuir esos gráficos ni ROMs que los contengan.
- **Distribución y pools opcionales:** tiendas normales y BP, objetos visibles/ocultos EI/EB y Recogida/Pickup. La inclusión está desactivada de origen y requiere el parche funcional reconocido. BP se activa expresamente y conserva los precios de sus ranuras; Pickup conserva probabilidades. Los filtros de objetos siguen aplicándose.
- **Acceso a las 21 mentas:** la acción «21 mentas en stocks generales (0–7)» añade las que falten a los ocho stocks generales sin reemplazar sus objetos ni activar BP. El stock visible depende del progreso de la partida. Los precios normales se ajustan con el editor de objetos existente.
- **Global Template y Batch Builder:** guardan inclusión y distribución, comprueban dependencias en cada copia y conservan overrides manuales. No instalan automáticamente el parche funcional ni los iconos durante replay. Las recetas antiguas sin el nodo de mentas no las habilitan; se conservan contratos, opciones y nodos desconocidos. Con la opción desactivada se mantienen las rutas y llamadas RNG anteriores.
- Se conservan **Definitive Game Data v1**, H1/H2/H3, historial, Deshacer y las funcionalidades anteriores de 3DS, Nintendo DS y Espada/Escudo. Esta versión no modifica Progressive Tracker ni la web.

## Instalación y actualización

1. Extrae el ZIP completo en una carpeta nueva de Windows x64. No ejecutes desde el ZIP ni sobrescribas una instalación anterior.
2. Ejecuta `DefinitiveRandomizer.exe`. El paquete es autocontenido; no requiere instalar .NET.
3. Conserva juntas `LegacyBatch`, `Tools`, `Templates`, `custom_balance_templates`, licencias y demás dependencias.
4. Respalda proyectos, recetas personales y partidas. Trabaja con copias aisladas de las extracciones; no edites ROMs originales.
5. En Ultra Sol/Ultra Luna, abre **Mecánicas y CRO → Mentas Gen7 — Ultra Sol / Ultra Luna**, comprueba el estado e instala las mentas. Los iconos se preparan e instalan por separado, de forma opcional. Después configura tiendas/pools o la receta Global/Batch y reconstruye por el procedimiento existente.
6. Para Definitive Game Data v1, guarda primero los cambios y exporta desde Herramientas ROM o Ctrl+K. El JSON procede de los archivos guardados y no contiene gráficos ni binarios del juego.

**Los ejecutables no tienen firma digital.** Windows o SmartScreen pueden mostrar advertencias de editor desconocido. Comprueba el origen del paquete y su SHA-256; no desactives las protecciones del sistema para instalarlo. Una suma de comprobación acredita integridad respecto al archivo publicado, no sustituye una firma digital.

`VERSION.txt` identifica versión y commit de compilación. El ZIP contiene un manifiesto SHA-256 por archivo y se entrega un `SHA256SUMS.txt` externo para el ZIP. El registro de procedencia y el informe de validación del candidato acompañan al paquete, con el estado del PR de metadatos. No se incluyen ROMs, extracciones, partidas, recetas privadas ni la caché de iconos.

## Validación y alcance

- Las fases funcional, visual y de distribución tienen confirmación satisfactoria del propietario **dentro del emulador en Pokémon Ultra Sol**. Para fase 2 confirmó distribución de mentas, Global Template, Batch Builder y ROM generada el 06/11/2026.
- **No se ha acreditado una prueba jugable equivalente en Ultra Luna.** Sus perfiles y resultados sintéticos/con copias privadas no sustituyen esa prueba. Tampoco se declara que todos los casos manuales, eventos o combinaciones estén probados en Ultra Sol.
- Se reutiliza el CI aprobado del código funcional. El CI final del PR #53, ejecución `42309089791`, aprobó el head `821e833d542a4931ba6abc2d177afd73834c43a0`, cuyo árbol coincide con la base funcional de esta versión. Incluyó compilación, integración, ARM de mentas, regresión sintética y auditoría visual.
- La validación final de distribución se limita a compilación/empaquetado oficial, integridad, dependencias, versiones, ausencia de datos privados/iconos y arranque del ejecutable desde una extracción limpia. Sus resultados concretos se registran junto al candidato. No se inicia otra auditoría general ni se repiten suites completas locales.

## Limitaciones conservadas

- Mentas solo para los perfiles acreditados de Ultra Sol/Ultra Luna; no Sol/Luna, Gen6, DS ni Switch. No se fuerza la instalación sobre ejecutables desconocidos. No añade Ability Patch, nuevos editores de recompensas/eventos ni mentas a pools de objetos equipados.
- Global/Batch exige una copia con las dependencias ya instaladas. Restaurar un respaldo anterior al parche puede bloquear la receta; no se reinstala silenciosamente.
- Definitive Game Data v1 admite Ultra Sol/Ultra Luna y mantiene el alcance de especies/formas, estadísticas base, tipos, habilidades, propiedades básicas de movimientos y evoluciones. No identifica por sí solo la ROM que ejecuta el emulador ni amplía RAM, DS o Switch.
- Las transacciones conservan sus límites ante apagones, fallos de almacenamiento y ediciones externas posteriores a liberar los handles; no se promete recuperación universal.
- Se mantienen los pendientes USUM documentados: cuelgue en la entrega de pétalos pendiente de reproducción y diagnóstico localizado del movimiento Z del primer combate de Tilo.
- Switch mantiene perfiles separados de Espada/Escudo y el alcance Ryujinx documentado. No se acredita soporte completo de DLC, Atmosphère, hardware real ni un guard runtime general para dobles con un único Pokémon utilizable. DS conserva sus perfiles y restricciones de código original.

Para reportar problemas, indica versión y commit, juego/perfil/revisión/idioma, pasos, semilla y registros pertinentes. No adjuntes ROMs, extracciones ni partidas personales.
