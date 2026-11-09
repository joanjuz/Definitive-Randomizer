# Respaldo y recuperación

Qué protege Definitive Randomizer, qué no, y qué hacer si un guardado falla. Es una guía de usuario; los detalles de implementación están en [HISTORY_UNDO_BUTTON_20261105.md](HISTORY_UNDO_BUTTON_20261105.md), [CORRECCION_H2_H3_20261104.md](CORRECCION_H2_H3_20261104.md) y [H2_H3_GUARDADOS_INDIVIDUALES_20261104.md](H2_H3_GUARDADOS_INDIVIDUALES_20261104.md).

**Regla de oro:** la aplicación ayuda a recuperar archivos del proyecto, pero **tu copia limpia del juego original y tus partidas guardadas son responsabilidad tuya**. Ninguna función de este programa las respalda por ti.

## Qué copias debes hacer tú

| Qué | Cuándo | Por qué |
|---|---|---|
| Extracción o ROM **original** sin tocar | Una vez, fuera de la carpeta de trabajo | Es el único punto de partida seguro. |
| Copia de trabajo del proyecto | Antes de empezar a editar | Así el original nunca se modifica. |
| Tus partidas guardadas | Antes de probar cualquier ROM generada | Un parche puede dejar datos guardados que Deshacer no revierte (ver más abajo). |
| Tus recetas y templates personales | Antes de actualizar la aplicación | Actualiza extrayendo en una carpeta nueva y copia después las tuyas. |

## Qué protege el historial (3DS)

Cada vez que guardas un cambio en un proyecto 3DS, la aplicación:

- **Registra la operación** en el Historial: qué sección, cuántos valores cambiaron, quién y cuándo, con el antes y el después.
- **Guarda un punto de restauración** con los archivos tal como estaban antes. Se conservan los **8 últimos**, en la carpeta `.progressive-restore` del proyecto. El respaldo del proyecto no incluye esa carpeta.
- **Escribe de forma transaccional**: los archivos de un mismo cambio se reemplazan juntos y, si algo falla a mitad, la aplicación intenta devolver el conjunto al estado anterior.

## Qué puedes deshacer

- Un cambio guardado, desde **Historial → «Deshacer este cambio»**.
- Una receta completa aplicada, con **«Deshacer toda la receta»**.
- El último cambio, desde el buscador (**Ctrl+K**).

Deshacer queda anotado en el historial como una restauración.

### Cuándo Deshacer no actúa

Deshacer es conservador a propósito:

- Solo actúa si los archivos siguen **exactamente** como los dejó ese cambio. Si cambiaste después los mismos archivos —desde la aplicación o con otro programa—, no toca nada y te explica el motivo. Deshaz primero los cambios posteriores.
- Solo existe para los **8 últimos** cambios guardados, y solo para los guardados con una versión que ya creaba el punto de restauración.
- Si un archivo está abierto o retenido por otro programa, la comprobación falla y no se modifica nada.

### Lo que Deshacer no revierte

- **Tus partidas guardadas.** Deshacer restaura archivos del proyecto, no datos que ya guardaste dentro del juego. Esto importa sobre todo con las **mentas de Ultra Sol/Ultra Luna**: deshacer no revierte la naturaleza ni el inventario ya guardados en una partida. Prueba siempre con una partida de prueba o con una copia de tu guardado.
- **Una ROM o salida ya generada.** Si reconstruiste, regenera después de deshacer.
- **Archivos fuera del proyecto**, como la carpeta de mods de un emulador o una carpeta de salida que copiaste.
- **Más de lo que el punto de restauración contiene:** restaura los archivos de ese cambio, no cualquier archivo del juego.

## En Nintendo DS y Espada/Escudo

El sistema de puntos de restauración y «Deshacer» descrito arriba es el de **3DS**; no asumas que existe igual en las otras plataformas.

- **Nintendo DS:** abrir un proyecto **no modifica la ROM original**. Las ediciones y randomizaciones se acumulan en el proyecto y «Exportar ROM…» escribe una ROM **nueva** más un `.cambios.txt` con las operaciones y semillas. DS tiene Historial, Global Template y Batch, pero **esta guía no documenta un «Deshacer» con puntos de restauración como el de 3DS**. Para volver atrás antes de exportar usa «Descartar cambios pendientes». Si exportaste algo que no te gusta, borra esa ROM y vuelve a exportar desde el original.
- **Espada/Escudo:** tiene su propio Historial (paginado y exportable), Global Template y Batch de mods. Se exporta un **mod**; la extracción de origen se abre en solo lectura y no se genera historial dentro de su RomFS/ExeFS. Para quitar un mod, desactívalo o bórralo en la carpeta de mods del emulador. Eso restaura los recursos originales del juego.

## Si falla un guardado

Esta recuperación está documentada y probada para los guardados de **proyectos 3DS** (historial y guardados individuales). En DS y Switch las salidas son una ROM nueva o un mod: si una exportación falla, borra la salida incompleta y vuelve a exportar desde el original. Un guardado de proyecto 3DS puede interrumpirse (corte de energía, disco lleno, otro programa escribiendo en el proyecto). La aplicación intenta no dejar el proyecto a medias:

1. **Lee el mensaje de error completo** y no cierres nada todavía. Indica la causa, los archivos implicados y la ubicación del registro de recuperación.
2. **No guardes, no reconstruyas ni borres archivos temporales** (`.tmp`, `.old.tmp`, `.pending`) después del error. Conserva también la carpeta del registro que menciona el mensaje.
3. **Cierra los demás programas** que puedan escribir en el proyecto (editores, sincronizadores de nube, antivirus en análisis).
4. **Vuelve a abrir el mismo proyecto en la aplicación.** Si no hay conflicto, la recuperación pendiente se completa automáticamente al abrir y devuelve el conjunto de archivos al estado anterior al guardado interrumpido.
5. Si sigue habiendo un conflicto, es porque **otro programa dejó contenido propio en un archivo de destino**. En ese caso:
   - La aplicación no lo sobrescribe.
   - Mueve ese archivo **fuera del proyecto**, comprueba que lo conservas intacto y vuelve a abrir el proyecto.
   - No copies `.old.tmp` a ciegas ni borres el registro de recuperación.
6. Mientras haya un guardado pendiente, los diagnósticos, la copia de respaldo y la reconstrucción **se niegan a continuar** para no empaquetar un estado intermedio. Es normal.
7. **No muevas ni renombres la carpeta del proyecto** mientras la recuperación está pendiente: el registro guarda rutas absolutas.
8. Si nada de esto resuelve el problema, **conserva todo tal como está** y repórtalo (ver más abajo). Restaurar desde tu copia de trabajo es siempre una salida válida.

### Límites que debes conocer

- La protección cubre el guardado de archivos dentro de la aplicación. **No garantiza** recuperación ante cualquier fallo físico: apagones en mitad de una escritura, discos o sistemas de archivos defectuosos, o ediciones externas **después** de que la aplicación libere los archivos.
- La recuperación de restos anteriores a esta protección (sin registro) puede requerir revisión manual; la aplicación no adivina a qué cambio pertenecen.
- No se ha certificado el comportamiento en todos los sistemas de archivos. Se probó en Windows con NTFS.

## Qué enviar cuando reportes un problema

Abre un reporte en el repositorio ([Issues](https://github.com/joanjuz/Definitive-Randomizer/issues)) con:

- **Versión y commit**, tal como los muestra `VERSION.txt` o el pie de la ventana.
- **Juego, región/código, revisión e idioma** (por ejemplo «Ultra Sol, extracción 3DS» o «Platino CPUE»).
- **Plataforma** (3DS, DS o Switch) y, si juegas, el **emulador** y su versión.
- **Qué hiciste, paso a paso**, y qué esperabas frente a lo que ocurrió.
- **Semilla y opciones** de la randomización, o la receta si usaste Global Template o Batch.
- **El texto del mensaje de error** o del diagnóstico, copiado tal cual, y una captura si ayuda.
- Si exportaste el **historial** o el **registro del randomizado**, adjunta lo pertinente (el registro tiene spoilers).

**No adjuntes** ROMs, extracciones, archivos del juego, partidas guardadas ni material con derechos de autor. Si hace falta comprobar un binario, la persona que mantiene el proyecto te dirá qué valor (por ejemplo, un hash) enviar.
