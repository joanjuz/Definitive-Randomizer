> Documento del hito v0.5.0. La navegación, semillas y templates actuales se describen en [Hito 06](HITO_06_INSPECTOR_Y_TEMPLATES.md).

# Hito 05 — acciones masivas y guardado verificado

Fecha: 2026-10-11. Rama: `feature/3ds-progressive-ui`. Preview v0.5.0.

Este bloque continúa desde `7858bca`. Mantiene el ejecutable WPF independiente, el tema Dark Graphite, la navegación integrada y los guardados de Pokémon/Movimientos ya validados por el usuario. No modifica `pk3DS.Core` ni `pk3DS.WinForms`, ni hace merge a `Eris-Ultranova`.

## Interfaz

- Se elimina el distintivo **Editor integrado · 3DS Progressive** de la cabecera de Movimientos que señaló el usuario. Se oculta también el recuadro obsoleto **HITO 01** del lateral.
- Ambos editores incorporan **Herramientas masivas**, un panel desplegable dentro de la misma ventana.
- Las acciones tienen alcance **Todas las entradas / Lista filtrada / Entrada seleccionada**. La búsqueda filtra el alcance; nunca se incluye la entrada cero.
- La randomización dispone de semilla entera. Su resultado es reproducible con los mismos registros, opciones, alcance y runtime.
- **Generar vista previa** no escribe archivos. La propuesta muestra nombre, campo y valores antes/después, además de conteos de entradas y cambios.
- **Aplicar cambios** confirma la escritura. Modificar opciones, semilla, alcance, búsqueda, selección o campos del editor invalida la propuesta. Una propuesta queda invalidada también tras guardar.
- Si la especie/movimiento actual tiene cambios individuales, se solicita descartarlos antes de generar la propuesta. Se puede cancelar y guardarlos con el flujo individual.
- **Copiar tabla guardada** copia todos los campos del modelo del alcance seleccionado, con cabeceras en español y nombres para tipos, habilidades, objetos y movimientos. No incorpora borradores sin guardar.
- **Copiar cambios** permite revisar o conservar el listado antes/después pegándolo en Excel o texto.

## Pokémon

Se reutiliza directamente `PersonalRandomizer`, reseedando **antes** de construirlo; trabaja sobre clones del archivo y no sobre la tabla viva.

Opciones de randomización: seis estadísticas seleccionables (orden Core PS/AT/DF/VE/AE/DE), desviación, barajado conservando BST, tipos y porcentaje monotipo, habilidades y Wonder Guard, exclusión de Shadow Tag/Arena Trap, objetos equipados, captura, grupos huevo y porcentaje de repetición, compatibilidad MT, MO de Gen 6, tutores por tipo, tutores ORAS o de playa USUM. Se conserva la protección de Pokémon con 1 PS del motor original.

Modificaciones: quitar los seis EV otorgados, crecimiento lento, escalar EXP base, quitar compatibilidades, compatibilidad completa MT/MO/tutores, eclosión en un ciclo, captura fija y ratio de llamada SOS en Gen 7.

La exclusión de habilidades de atrapamiento también es una acción independiente. Reutiliza el criterio determinista del editor Progressive y queda activa para las siguientes acciones masivas de esta sesión, dentro del alcance elegido. No modifica las tres habilidades de un registro cuando ninguna está prohibida.

La modificación de compatibilidades conserva bits reservados. Quitar MT en Gen 6 mantiene **todas** las MO; se corrige el desfase del bucle antiguo que alcanzaba también la primera MO. Las opciones de Gen 6, ORAS y USUM se muestran solo en el juego correspondiente. La edición masiva no cambia asignaciones MT/tutores en `code.bin`/`Shop.cro`.

## Movimientos

- Randomización de tipos y categoría física/especial, conservando la categoría de los movimientos de estado. Protege los IDs reales de **Forcejeo (165)** y **Maldición (174)**.
- Modo Metrónomo: **118 → 40 PP**, **33 → 1 PP**, resto del alcance → 0 PP; solo altera el byte PP. Respeta el comportamiento de los índices 117/32 del selector original, que elimina la entrada cero. Usar esos índices como IDs habría modificado otros movimientos.
- Se conservan potencia, efectos, flags, datos Z y demás bytes al randomizar tipo/categoría o aplicar Metrónomo.

**Diferencia auditada:** el randomizador antiguo comprueba índices de selector 165/174 aunque su comentario protege Struggle/Curse. Aquí la protección se realiza por IDs reales; no se traslada esa confusión al selector nuevo. La paridad byte a byte demostrada con el motor original corresponde al randomizador de Pokémon; para Movimientos se verifican sus campos, exclusiones y semántica de PP con registros sintéticos.

## Transacciones

`GarcTransaction` se reutiliza en acciones masivas y guardados individuales. Verifica hash de origen, número y tamaño de registros, regenera el GARC temporal utilizando Core, relee y compara **todos** los registros, vuelve a comprobar el origen y reemplaza el archivo. Confirma también los bytes del archivo final y elimina temporales.

Personal reconstruye la tabla maestra y rechaza una tabla incoherente. Movimientos respeta los archivos directos X/Y y WD de ORAS/Gen 7. Las propuestas tienen registros clonados y están ligadas a una sesión/servicio y a su hash; no se acepta una propuesta de otra sesión o un archivo cambiado externamente.

No se añaden backups automáticos, guardado al azar, extracción de ROMs ni publicación de recursos del usuario.

## Verificación

`3DS.Progressive.Tests` referencia la aplicación real. Usa exclusivamente datos generados, sin ROMs ni recursos comerciales.

- **188 comprobaciones de servicios superadas localmente**, con fixtures independientes X/Y, ORAS, SM y USUM y GARC v4/v6/WD: paridad exacta del randomizador Core de Pokémon, reproducción de semilla, protección de 1 PS, alcance, cambios precisos, tabla maestra, bits reservados, límite de EXP, habilidades de atrapamiento, datos Z/flags, PP por ID real, relectura de guardados, guardado individual después de masivo, propuestas obsoletas, cambios externos, tabla dañada, copia de tablas y aislamiento entre sesiones.
- Build local de la app y tests: sin errores; las advertencias CA2022 pertenecen a Core y ya existían.
- En Windows se ejecutan además **18 comprobaciones WPF** en STA: inicialización y layout del shell/editor real, opciones por generación, validación de semilla, habilitación de Aplicar, invalidación de propuestas y Metrónomo integrado. Estas pruebas se ejecutan en CI antes de publicar la preview; el resultado específico se registra en el issue #1 y Actions.
- Las pruebas sintéticas y de inicialización no sustituyen la prueba del usuario en su copia del juego.

## Pendiente y siguiente bloque

No se declara paridad completa: faltan Balance Pokémon y Move Balance desde templates, parches Battle/code/CRO y descripciones multiarquivo, importación de perfiles, Entrenadores, Encuentros y los restantes editores. Los botones de módulos sin implementación siguen deshabilitados.

Siguiente prioridad: **Entrenadores completos** (lector/escritor, equipo/metadatos, reglas por título, Better Movesets/Smart Items/Progressive), seguido de Encuentros, manteniendo la matriz `PARIDAD_ACCIONES.csv`. Move Balance debe migrarse con sus dependencias de textos y Battle/CRO, nunca como un botón parcial que anuncie cambios que no aplica.

## Prueba manual de esta preview

1. Abrir copia de Ultra Sol/Ultra Luna y repetir una stat, una MT y un movimiento/efecto Z; comprobar desde pk3DS.
2. Pokémon → Herramientas masivas → seleccionar una opción y alcance **Entrada seleccionada**; generar y revisar antes/después. Descartar y comprobar que nada cambió en disco.
3. Regenerar con la misma semilla; aplicar y reabrir/comparar desde pk3DS. Probar después un guardado individual.
4. Filtrar lista y revisar que los conteos corresponden a la búsqueda. Cambiar la semilla o un campo: Aplicar debe deshabilitarse.
5. Movimientos → probar tipos/categorías y Metrónomo en alcance acotado. Metrónomo debe corresponder al ID 118, no 117.
6. Copiar tabla y cambios, pegar en una hoja y revisar columnas y nombres. Confirmar que el distintivo señalado ya no aparece.
