# Mejoras del editor — 2026-11-02

Seis arreglos pedidos por los testers y el diagnóstico de un séptimo.

## Búsqueda en los desplegables

Antes, en los desplegables largos (objetos de tiendas, especies, movimientos, naturalezas…) solo se podía buscar recorriendo la lista. Ahora se puede escribir para filtrarla.

- **Cuándo aparece:** en todos los desplegables de la app con 10 elementos o más, en todas las secciones (3DS, DS y Espada/Escudo). Se activa con:
  - un clic sobre el desplegable;
  - escribir sobre él;
  - F4, Espacio o Alt+↓.
- **Cómo funciona:** se abre una lista con un campo de búsqueda (`ComboSearch.cs`).
  - El texto filtra por cualquier parte del nombre o por el número, sin importar mayúsculas ni tildes. Con varias palabras, deben aparecer todas.
  - ↑/↓ y Re Pág/Av Pág mueven la selección; Intro o un clic eligen el resultado; Esc cierra sin cambiar nada.
  - **La lista no se cierra sola:** se cierra al elegir, con Esc o Tab, con un clic en otra parte de la ventana, o al cambiar de ventana, moverla o cambiarle el tamaño. Un clic sobre el mismo desplegable la cierra. La primera versión se cerraba al soltar el clic, porque la ventana emergente perdía la captura del ratón.
- **Qué no cambia:**
  - El desplegable, sus elementos y sus bindings son los mismos, y el valor solo cambia al elegir un resultado.
  - Los desplegables editables (como los de Template Studio) mantienen su búsqueda propia.

## Copiar y pegar un Pokémon de entrenador

Cada tarjeta «Pokémon N» del editor de entrenadores tiene «Copiar Pokémon» y «Pegar Pokémon». Las tarjetas son las mismas en 3DS, DS y Espada/Escudo.

- **Qué se copia:** todos los campos del Pokémon: especie, forma, nivel, objeto, movimientos, IV/EV, naturaleza, habilidad, género y shiny, según el juego.
- **Dónde se puede pegar:** en cualquier posición de cualquier entrenador del mismo editor.
  - Se pegan los campos que tenga la posición de destino.
  - Si la posición está fuera del equipo, «Pokémon en el equipo» sube para incluirla.
- **Guardado:** el resultado queda como cambios sin guardar. Se confirma con «Guardar», que lo registra en el historial como cualquier edición.
- **Límite:** no se pega entre juegos o editores distintos, porque los IDs no serían los mismos.

## Numeración de encuentros estáticos

La lista de estáticos de 3DS (regalos, encuentros e intercambios en Gen7; iniciales, regalos y estáticos en Gen6) numeraba desde 1. Ahora cada sección numera desde 000, como pk3DS y la tabla de Totems.

- **Ejemplo:** el encuentro 4 de la tabla de Totems es «#004 · Encuentro estático» en la lista.
- **Compatibilidad:** el ID interno no cambia, así que el historial y los templates siguen siendo compatibles.
- **DS:** ya numeraba desde 0.

## Cargar como Global Template un historial exportado

«Exportar historial (JSON)» guarda el historial del proyecto, que es un formato distinto al de un Global Template. Por eso «Cargar template global…» lo rechazaba.

- **Qué hace ahora:** al cargar un historial exportado, se construye el mismo template que «Crear desde historial»:
  - el último Global Template aplicado (o uno parcial vacío);
  - las acciones registradas después;
  - las modificaciones manuales.
- **Otro juego:** un historial de otro juego se rechaza con un mensaje claro.
- **Guardado:** se puede guardar como template normal, en un archivo nuevo.
- **Dónde se aplica:** `GlobalTemplateDocument.Load`, así que vale para todas las entradas que cargan templates.
- **Comprobado con los archivos reales** (copias, solo lectura), los dos exportados del historial de UltraLuna:
  - `HISTORIAL.json`: 40 acciones y 21 modificaciones exactas;
  - `TEMPLATETOTAL.json`: 40 acciones y 20 modificaciones exactas.

## Archivos del template con otro SHA-256: aviso en lugar de bloqueo

Antes, si un archivo usado por el Global Template (por ejemplo, un CSV de movimientos o de estadísticas) había cambiado desde que se guardó el template, la vista previa se detenía.

- **Qué hace ahora:** la vista previa usa el archivo actual y muestra un aviso con cada archivo distinto, en el panel y en la línea de estado.
- **Qué sigue bloqueando:** un archivo que no se encuentra.
- **Dónde se cambió:**
  - la vista previa (`GlobalTemplateApplication`);
  - el ejecutor de la vista previa en `LegacyBatch` (solo ignora ese aviso concreto);
  - la comprobación del CSV de Movimientos/Personal (`NativeMasterBatchBridge`);
  - el marcado de recetas completas (`NativeRecipeCompletion`).
- **Sin cambios:**
  - La construcción por lotes de ROMs ya mostraba el aviso, con una casilla para usar los archivos actuales.
  - Las dependencias manuales `.bin` se nombran por su SHA-256, así que uno distinto significa un archivo dañado y sigue bloqueando.

## Combates de prueba antes de cada Totem (Sol/Luna y USUM)

Con «Reemplazar especies Totem por evoluciones finales» (`CHK_ForceTotem`), los combates de la propia prueba salían rotos. Por ejemplo, antes de la primera prueba salía un Metagross o un Swoobat a nivel 11.

- **Causa:** el Totem se reconocía por su especie. Las entradas sin aura que comparten especie con su Totem recibían una evolución final al azar, sin el filtro de BST:

  | Entradas | Especie | Dónde |
  |---|---|---|
  | 6 | Gumshoos | madrigueras de la primera prueba |
  | 11 | Raticate | madrigueras de la primera prueba |
  | 15, 16 y 18 | Wishiwashi | prueba de Lana |
  | 20 y 21 | Marowak | prueba de Kiawe |
  | 250 | Salazzle | USUM |

  La «Totem BST» del template solo corrige los Totems reales por su ID, así que estas entradas quedaban sin corregir.
- **Corrección:** solo es Totem la entrada que además tiene aura de Totem.
  - **Totems reales:** las entradas 4, 9, 14, 19, 24, 33, 39, 45, 137, 146, 162, 229, 231 y 249, comprobadas en Sol y UltraSol originales.
  - **El resto:** sigue la randomización normal, con el BST similar si está activado.
  - **Dónde:** en el motor de la app (`GameStaticEncounterService`) y en el que ejecuta el Global Template (`StaticEncounterEditor7` de `LegacyBatch`).
- **Template:** el template no cambia; basta con volver a aplicarlo.
- **Límite:** con BST similar, una evolución final con un BST parecido al del original (Gumshoos tiene 418) puede seguir saliendo. Es la misma fuerza que el original.

## Movimiento Z en un entrenador editado a mano (diagnóstico)

Caso: el primer combate de Tilo, con su Pokémon equipado con un cristal Z, no usa el movimiento Z. Según el tester, en los demás combates sí se usa el movimiento Z que se le pone.

Se compararon los datos originales de Sol y de UltraSol.

- **Combates de Tilo:** los de antes de la prueba (52, 215 y 216) y el que sí usa movimiento Z (396) tienen la misma clase (38), la misma IA (0x87), el mismo «Flag» (1) y los mismos bytes restantes. Solo cambian el equipo y el objeto.
- **Resto de entrenadores con cristal Z:** varios tienen «Flag» 0 o IA distintas, así que ningún campo del entrenador decide si se usa el movimiento Z.
- **Conclusión:**
  - No hay un dato de entrenador que activar.
  - Como solo falla ese combate, lo más probable es que ese combate concreto tenga los movimientos Z desactivados por el script o por el punto de la historia (en la historia, ningún rival usa movimientos Z tan pronto), y no por `trdata`. No está verificado.
  - Ese primer combate tiene tres entradas con el mismo equipo (52, 215 y 216). Si solo se editó una, puede que el juego use otra.
  - No se cambió código.
- **Pendiente en juego:**
  - el mismo Pokémon con cristal en un combate posterior a la primera prueba;
  - que el Pokémon sepa un movimiento del tipo del cristal.

## Validación

- **`--requested-ui-only`:** 15 comprobaciones nuevas, fuera de pantalla.
  - **Búsqueda:** filtra sin cambiar el valor, sin tildes, por palabras y por número. Intro elige y una búsqueda sin resultados no cambia nada.
  - **Copiar/pegar en ORAS y USUM:** botones en las 6 posiciones; pegar en otro entrenador, en una posición fuera del equipo, y guardar.
- **`--global-options-only`:** en SM y USUM, el Totem con aura usa evoluciones finales y la misma especie sin aura no.
- **`--global-apply-only`:** un archivo cambiado avisa y se usa la versión actual; uno inexistente sigue deteniendo la vista previa.
- **`--history-inspection-only`:** un historial exportado carga como template con su receta y acciones, y se rechaza si es de otro juego.
- **`--template-load-probe <archivo> <juego> <generación>`:** nueva opción de solo lectura para comprobar la carga de un template o un historial real.
- **Sin probar en juego.**
