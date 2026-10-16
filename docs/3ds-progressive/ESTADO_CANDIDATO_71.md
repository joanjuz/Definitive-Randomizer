# Definitive Randomizer — candidato local de validación

Estado de integración a 14 de octubre de 2026. Este documento consolida el alcance actual; las filas antiguas de PARIDAD_ACCIONES.csv conservan el historial y no describen por sí solas el estado vigente.

## Compatibilidad acordada

| Función | XY | ORAS | SM | USUM |
|---|---|---|---|---|
| Editores y randomizadores de Pokémon, movimientos, evoluciones, huevo y entrenadores | Sí | Sí | Sí | Sí |
| Encuentros salvajes e iniciales/estáticos según las tablas de cada juego | Sí | Sí | Sí | Sí |
| Progressive Wild y perfiles/snapshots Global/Batch | Sí | Sí | Sí | Sí |
| Balance de movimientos Progressive y mecánicas del template | Sí | Sí | Excluido por el usuario | Sí |
| Zonas de captura, olvidar MO, intercambios Progressive, O-Powers | Sí | Sí | No corresponde | No corresponde |
| Tutores BP Gen6 | No corresponde | Sí | No corresponde | No corresponde |
| MT, tiendas y economía de cada juego | Sí | Sí | Sí, ranuras fijas | Sí, expansión USUM |
| Tutores avanzados, level caps y relearner USUM | No corresponde | No corresponde | No corresponde | Sí |
| Global Template bidireccional y Batch ROM | Sí | Sí | Sí | Sí |
| Extracción de contenedores compatibles, rebuild Full/Trimmed3DS y hashes CRO/CRR | Sí | Sí | Sí | Sí |

La exclusión SM se aplica al botón, a las herramientas de movimientos, a la importación Global, al preflight y al ejecutor de lotes. Un CSV personalizado no elude la comprobación. El randomizador vanilla y la edición manual de movimientos siguen disponibles.

## Integración consolidada

Los bloques históricos de estáticos, entrenadores, Wild, herramientas Gen6, MT/tutores/tiendas, Global Template y Batch tienen implementaciones y regresiones locales. Los perfiles separados Gen6, intercambios sin RandSettings, tiendas SM y gráficos XLIM/paleta se cerraron en las iteraciones 64–70. La iteración 71 delimita movimientos por juego y preserva líneas de texto originales al actualizar descripciones: el escritor vanilla recorta espacios en líneas ajenas y UltraLuna permitió detectar ese caso.

La restauración completa y selectiva de respaldos de la aplicación comprueba manifiestos y hashes; Batch también valida y superpone respaldos pk3DS originales. El visor OWSE conserva el alcance de lectura/desarrollo del original; no se presenta como un editor completo de mapas. No se añaden funciones que el motor original no implementaba, como O-Powers S/MAX. La extracción requiere contenedores compatibles ya descifrados; no es un descifrador de ROMs comerciales.

## Evidencia de esta iteración

- Compilación Debug habitual del repositorio: **cero errores y 79 advertencias heredadas**. Publicación únicamente en carpeta local de candidato.
- Suite completa: **3707 comprobaciones**, también ejecutadas usando las DLL exactas del paquete. Las pruebas del balance SM se sustituyeron por comprobaciones de rechazo conforme al alcance acordado; la cifra no es directamente comparable con las 3734 anteriores.
- Lectura/propuesta en las ocho extracciones reales: encuentros salvajes y estáticos, entrenadores, evoluciones, huevo, Pickup, tipos, shiny, economía y herramientas correspondientes a cada juego. Huellas monitorizadas: X/Y 145 cada uno; OR/AS 157 cada uno; Sol/Luna 126 cada uno; UltraSol/UltraLuna 143 cada uno. Inventarios originales intactos.
- La auditoría inicial detectó únicamente el rechazo de descripciones de UltraLuna. Tras conservar las líneas originales, la propuesta completa pasó: 149 cambios y dos archivos adicionales; 143 huellas e inventario intactos. El log inicial se conserva junto con la repetición corregida.
- Propuestas de iniciales/estáticos en X/Y/OR/AS/Sol/Luna/UltraSol/UltraLuna: ocho resultados PASS, dos huellas principales e inventario preservados en cada proyecto; no se aplican a los originales.
- Sol y Luna: preflight WPF → proceso original/nativo → Trimmed3DS → extracción. Comparación exacta de la modificación shiny con la propuesta manual y de todos los demás archivos; **668 hashes de entrada e inventario intactos en cada copia**.
- Tiendas Luna con el candidato: caramelos, objetos de entrenamiento y megapiedras gratuitas → lote → rebuild → extracción; Shop.cro y objetos coinciden con la propuesta y todos los demás archivos conservan sus hashes. 668 huellas e inventario de entrada intactos; randomización/BP/filtros reproducen la semilla en bytes aislados.
- Ejecutable legacy del candidato: puente nativo cargado y **tres cambios de intercambios** idénticos a la propuesta en una copia privada de X. Su copia de entrada permanece intacta.
- Once archivos protegidos coinciden exactamente con el baseline. RecordEditorView.xaml.cs conserva únicamente la extensión de captura de excepciones documentada en la iteración 63, comprobada contra su respaldo. Lógica Wild 001–003 y archivos personales preservados.
- Paquete comprobado: ejecutor, dependencias, sprites y templates presentes; ausencia de ROMs, archivos CRO/CRR y binarios extraídos. Manifiesto SHA-256 incluido. Sin commits, push, publicación ni modificaciones a Eris-Ultranova.

Evidencias de esta iteración: `ITERACION_71_PRUEBAS`. Matriz vigente: `PARIDAD_CONSOLIDADA_71.csv`. Los informes de iteraciones anteriores conservan la evidencia específica de tutores, entrenadores, zonas de captura, rebuild y otros módulos; las pruebas actuales no representan todas sus combinaciones en juego.

Candidato: `D:\Progra\DefinitiveRandomizer-validation\20261014-iteration71-local-candidate\DefinitiveRandomizer.exe`. Requiere .NET Desktop Runtime 10. Conservar la carpeta completa.

## Comprobación manual del candidato

1. Abrir cada extracción X/Y/OR/AS/Sol/Luna/UltraSol/UltraLuna desde una copia de trabajo. Confirmar identificación y que las opciones exclusivas de otra versión no aparecen.
2. Recorrer editores; editar una fila, generar propuesta, guardar, deshacer y rehacer. Revisar niveles, formas, objetos, movimientos y textos localizados.
3. En Gen6 comprobar zonas de captura, olvidar MO e intercambios. En USUM comprobar balance y mecánicas de movimientos, SOS, tutores y caps. En SM comprobar tiendas sin expansión y ausencia del balance Progressive.
4. Exportar Global Template, reabrirlo y comparar opciones; generar un lote con semilla fija. Confirmar progreso, archivos de salida y que el proyecto de entrada permanece intacto.
5. Probar en juego encuentros, iniciales/regalos, entrenadores, tiendas, movimientos, captura e intercambios de las ROMs generadas. Las comparaciones de archivos y los fixtures no sustituyen esa prueba.

El candidato está destinado a esta validación manual y en juego. No se publica ni se declara una release estable certificada antes de resolver los errores que esa validación encuentre. Wild 001–003 ya validado por el usuario mantiene su lógica y se protege mediante hashes.
