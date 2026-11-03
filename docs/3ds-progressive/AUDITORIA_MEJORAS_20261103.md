# Mejoras de la auditoría de la aplicación — 2026-11-03

Se aplican las recomendaciones de la auditoría posterior a 0.13.0-preview.1. La validación en el juego la está haciendo el propietario con tres ROMs y el mismo template; este trabajo no toca ninguna ROM ni proyecto real (todas las pruebas usan copias sintéticas).

## Usabilidad

### Historial legible (`HistoryView`)

- **Nombres legibles:** cada fila dice qué se hizo (por ejemplo, «Randomizar entrenadores»), su sección, cuántos valores cambió, a quién afectó, la fecha y el estado de comprobación.
- **Receta agrupada:** una aplicación de Global Template es un solo bloque con sus acciones agrupadas por sección. Antes eran unas 40 filas con códigos.
- **Filtros:** por sección (con recuento), por tipo (ediciones manuales, randomizaciones y acciones, Global Template) y por texto. El texto busca en títulos, detalles, módulos y valores cambiados.
- **Qué cambió:** antes → después, con el valor nuevo resaltado (hasta 300 por entrada).
- **Detalles técnicos plegados:** módulo, acción, parámetros (los de más de 1.500 caracteres se recortan) y huellas completas.
- **Se conservan:** «Exportar historial (JSON)», «Comprobar archivos actuales» y los textos de estado.

### Deshacer (`RestorePoints`)

- **Punto de restauración:**
  - Tras cada guardado verificado (y tras aplicar un Global Template) se copian los archivos del juego tal como estaban antes.
  - Se guardan en `.progressive-restore`, junto al historial, y el backup del proyecto excluye esa carpeta.
  - Es una copia de mejor esfuerzo: nunca puede hacer fallar un guardado.
  - Se conservan los 8 últimos; un cambio de más de 256 MB no tiene punto.
- **Deshacer:**
  - Dónde: «Deshacer este cambio» (o «Deshacer toda la receta») en el detalle del Historial, y «Deshacer el último cambio» en el buscador.
  - Comprobación previa: los archivos deben seguir exactamente como los dejó la entrada (SHA-256). Si un cambio posterior los modificó, se explica el motivo y no se hace nada.
  - Escritura: los originales se escriben en una sola transacción verificada, con copia de seguridad del historial.
  - Registro: se anota como «Restaurar archivos · deshacer «…»» en la sección original. Las recetas no lo reproducen, porque `ManualResourceReplay` excluye las restauraciones.
  - Después se recarga el proyecto.

### Registro del randomizado (`RandomizationLog`)

- **Qué es:** un archivo de texto (spoiler) con el estado actual del juego:
  - los cambios guardados por sección;
  - los regalos, iniciales y encuentros estáticos (especie y nivel);
  - los equipos de todos los entrenadores (especie, nivel y objeto);
  - los encuentros salvajes, con el mismo reporte que «Exportar reporte de encuentros».
- **Cómo funciona:** solo lee el proyecto. Si una parte no se puede leer, lo indica y sigue con las demás.
- **Dónde:** tarjeta «Registro del randomizado» en Herramientas ROM, y en el buscador.

### Editores

- **Evoluciones:**
  - Según el método, el parámetro es un desplegable de objetos (intercambio con objeto, usar objeto, objeto equipado…), movimientos, especies o tipos, o un número.
  - Usa la misma correspondencia que el creador de templates (`IEvolutionArgumentCatalog`).
  - Tarjeta «Rutas de evolución» con sprite de origen y destino, método y parámetro. Se actualiza al editar.
  - El valor y la clave del campo no cambian.
- **Level Up (3DS):**
  - Columnas de solo lectura con tipo, categoría, potencia y precisión de cada movimiento, que siguen la edición (`LevelUpMoveEntry` notifica sus cambios).
  - El botón de la especie pasa a «Randomizar esta especie».
  - La búsqueda tiene texto de ejemplo.
- **Encuentros estáticos (Gen7):**
  - **Corrección:** el juego guarda naturaleza, género y habilidad con 0 = sin forzar y el valor desplazado en uno (`EncounterStatic7`). El desplegable de naturaleza empezaba en 0, así que mostraba cada naturaleza una posición desplazada. Ahora: 0 Aleatoria, 1 Fuerte… Género: Aleatorio, Macho, Hembra, Sin género. Habilidad: Cualquiera, 1, 2, oculta.
  - **Regalos:** habilidad (Aleatoria, 1, 2, oculta) y naturaleza (Aleatoria o su nombre) por nombre.
  - La tarjeta del sprite muestra el nivel.
  - Los valores guardados no cambian.
- **Pokémon:**
  - Barras de las seis estadísticas sobre 255 en «Especie seleccionada»; la mayor, en color de acento.
  - El «Ratio de género» se explica en vivo («Siempre macho», «50 % macho · 50 % hembra», «Sin género»).
- **Desplegables cortos** (12 opciones o menos: género, habilidad, modo de combate…): muestran solo el nombre, sin el número delante.

### Primeros pasos y otros espacios

- **Inicio sin proyecto:** tarjeta «Primeros pasos» con los cuatro pasos (extraer, abrir, randomizar y generar) y botones para los dos primeros.
- **Nintendo DS y Espada/Escudo:** Ctrl+K abre el buscador con las secciones de su propia barra. No llevan marcas de cambios, porque sus operaciones pendientes no registran sección.

## Mantenimiento

- **Catálogo único de secciones (`SectionCatalog`):** nombre, hub, palabras clave y modo simplificado de cada destino.
  - Lo leen la barra, el buscador, la miga de pan, la guía (`SimpleRoutes`), los filtros del Historial y el registro.
  - Sustituye a `HubOf`, `HubLabels`, `SearchKeywords` y la lista de `GuidanceCatalog`.
  - La lista del modo simplificado es la misma que antes (se comprueba).
- **Sección guardada en el historial:**
  - `HistoryEntry.Section`, opcional, guarda la sección al confirmar cada entrada nueva.
  - Se omite si es nula, así que las entradas antiguas se serializan igual (mismos hashes) y las versiones anteriores la ignoran.
  - `SectionProgress` la usa primero y, si no está, sigue con el identificador de acción y el módulo.
- **Componentes comunes (`UiKit`):** tarjeta, etiqueta, texto secundario, chip navegable, encabezado, caja de búsqueda y `LambdaConverter`, con colores siempre referidos al tema. Sustituyen a copias locales de Entrenadores y Field Items.
- **Colores y tema:** de los 307 `FindResource` de pinceles, la gran mayoría ya se reasignaba al tema al cargar cada control (`AccessiblePalette.BindPalette`), así que no hacía falta migrarlos. Las piezas nuevas usan siempre `SetResourceReference`.
- **Pruebas:**
  - `UiPump.Idle()` es la espera común hasta `ContextIdle`, usada por las comprobaciones nuevas y por la prueba de la celda «Argumento», cuyo fallo de tiempo detectó el CI.
  - El CI ejecuta ahora también `--visual-audit`. Sube `issues.txt` y `warnings.txt`, no las capturas.

## Rendimiento

- **Tiempos locales (`TimingLog`):** en `%LOCALAPPDATA%\3DS-Progressive\logs\timings.log` se anotan:
  - abrir un proyecto;
  - recuperar las opciones aplicadas;
  - abrir cada sección.

  Solo se guardan el paso, el juego y la duración. Se desactiva en las pruebas y rota a partir de 2 MB.
- **Marcas de la barra:** no vuelven a leer el historial si no cambiaron su tamaño ni su fecha de escritura.

## Pruebas

`AuditImprovementChecks`, dentro de `--visual-redesign-only`, cubre:

- el catálogo;
- la agrupación de recetas;
- el ratio de género;
- la sección guardada;
- el punto de restauración, el deshacer, el motivo cuando no se puede y su búsqueda en el Historial;
- los filtros y el desplegable técnico del Historial;
- el registro;
- el argumento de evolución por catálogo y las rutas;
- las columnas de Level Up;
- la naturaleza desplazada de los estáticos;
- los primeros pasos;
- el buscador de DS.

Capturas: `audit-*.png`.

Resultados, en Release y sin advertencias:

| Suite | Correctas |
|---|---|
| `--visual-redesign-only` | 187 (antes 165) |
| `--all-ui-only` | 408 |
| `--requested-ui-only` | 55 |
| `--synthetic-only` | 4101 |
| `--guidance-only` | 233 |
| `--template-latest-ui-only` | 152 |
| `--global-options-only` | 134 |
| `--global-apply-only` | 48 |
| `--history-inspection-only` | 20 |
| `--swsh-only` / `--swsh-ui-only` / `--swsh-parity-only` | 316 / 117 / 239 |
| `--nds-only` / `--nds-ui-only` | 161 / 16 |
| Resto de suites del CI | sin fallos |

`--visual-audit`: 568 vistas, 0 problemas, 77 avisos (los mismos).

Al escribir las pruebas apareció un fallo, ya corregido: las barras de estadísticas capturaban la variable del bucle en su manejador de tamaño.

## Límites

- **Interfaz:** todo se comprobó fuera de pantalla y con datos sintéticos. Falta la prueba manual de deshacer, el registro y el Historial con un proyecto real.
- **Deshacer:** solo existe para los cambios guardados desde esta versión (los anteriores no tienen punto de restauración) y solo para los 8 últimos.
- **Marcas de cambios:** no existen en DS ni en Espada/Escudo.
- **Field Items de XY/ORAS:** siguen sin el resumen.
- **Sin cambio:** el género de los regalos de Gen7 (un bit cuyo significado no está documentado en el núcleo) y la habilidad de los encuentros estáticos con valores de 4 a 7 («personalizado»).
- **No se hizo:**
  - La migración completa de vistas a un registro de fábricas en `MainWindow`. Hay un catálogo único de metadatos, pero la creación de vistas sigue en `Section_Click`.
  - La reescritura de las líneas largas existentes. Se evitó tocar código estable sin pruebas específicas.
