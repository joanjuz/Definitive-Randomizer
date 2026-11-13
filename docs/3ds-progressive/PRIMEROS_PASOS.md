# Primeros pasos con Definitive Randomizer

Guía breve para quien usa la aplicación WPF por primera vez. Describe el flujo habitual; la lista de juegos y el nivel de comprobación de cada uno están en [COMPATIBILIDAD_PUBLICA.md](COMPATIBILIDAD_PUBLICA.md), y las limitaciones, en [LIMITACIONES_ESTABLE.md](LIMITACIONES_ESTABLE.md). Para copias de seguridad y qué hacer si algo falla, lee [RESPALDO_Y_RECUPERACION.md](RESPALDO_Y_RECUPERACION.md).

## Antes de empezar

- **Sistema:** Windows x64. La aplicación es autocontenida: no hace falta instalar .NET.
- **Tu propio juego:** necesitas una copia legítima de un juego admitido. La aplicación **no incluye** ROMs, archivos del juego ni recursos de Nintendo, y no descifra ni descarga juegos.
- **Un juego, una carpeta de trabajo:** nunca edites tu archivo original. Trabaja siempre con una copia.

## 1. Instalar desde el ZIP

1. Descarga el ZIP desde [Releases](https://github.com/joanjuz/Definitive-Randomizer/releases) y, si quieres, comprueba su SHA-256 con el archivo `SHA256SUMS.txt` que acompaña a la publicación.
2. **Extrae el ZIP completo en una carpeta nueva.** No lo ejecutes desde dentro del ZIP ni lo copies sobre una versión anterior.
3. Abre `DefinitiveRandomizer.exe`. Mantén juntas las demás DLL y las carpetas `LegacyBatch`, `Tools`, `Templates`, `custom_balance_templates` y `catch_zone_patches`: la aplicación las necesita.
4. Si actualizas, extrae en otra carpeta y copia después tus recetas personales.

Los ejecutables **no tienen firma digital**. Windows o SmartScreen pueden avisar de «editor desconocido». Comprueba que descargaste el ZIP de este repositorio y que su SHA-256 coincide; no desactives las protecciones de Windows.

Recomendación práctica: extrae en una carpeta de ruta corta (por ejemplo `D:\DefinitiveRandomizer`) para evitar problemas con rutas muy largas de Windows.

## 2. Preparar el proyecto

La preparación depende de la plataforma.

| Plataforma | Qué necesitas | Cómo se abre |
|---|---|---|
| **3DS** (X/Y, OR/AS, Sol/Luna, UltraSol/UltraLuna) | Una extracción **ya descifrada** del juego (RomFS, ExeFS y cabeceras). La aplicación no descifra ROMs comerciales. | «Abrir proyecto…» → la carpeta extraída. |
| **Nintendo DS** (Gen4 y Gen5) | Un volcado `.nds` de un juego con perfil admitido. | «Abrir proyecto…» → el archivo `.nds`. |
| **Espada/Escudo** (Switch) | Una extracción de la versión **1.3.2** con `exefs/main.npdm`, `exefs/main` y la carpeta `romfs`. | «Abrir proyecto…» → la carpeta de la extracción. |

Hay un único **«Abrir proyecto…»** para las tres plataformas (en Inicio y en la cabecera): la aplicación detecta el juego y abre su editor. También puedes **arrastrar** el archivo o la carpeta a la ventana (o sobre `DefinitiveRandomizer.exe`, o usar «Abrir con…» de Windows), o elegirlo en **Proyectos recientes** del mismo diálogo. Si el juego o la revisión no están admitidos, la aplicación lo rechaza en lugar de editarlo a ciegas. Eso es intencionado.

**Modo de trabajo (3DS).** La primera vez que abres un proyecto 3DS, Inicio pregunta si quieres el **Modo simplificado** (secciones principales) o el **Modo completo** (todas las herramientas). Tu elección se recuerda en tu usuario de Windows —no en el proyecto ni en la ROM—, así que los siguientes proyectos, también tras reiniciar la aplicación, se abren directamente en ese modo. Para cambiarlo, pulsa «Modo … · Cambiar» en la barra lateral (vuelve a Inicio) o busca «modo» con **Ctrl+K**; cambiar de modo no descarta cambios pendientes.

## 3. Haz copias de seguridad antes de editar

Antes de cambiar nada:

1. Guarda una **copia limpia de la extracción o de la ROM original** fuera de la carpeta de trabajo.
2. Trabaja sobre una **copia de esa extracción**, no sobre la única que tienes.
3. Haz también copia de tus partidas guardadas y de tus recetas personales.

La aplicación guarda puntos de restauración en proyectos 3DS (ver la [guía de respaldo](RESPALDO_Y_RECUPERACION.md)), pero **no sustituyen una copia tuya**. En DS y Switch el comportamiento es distinto y está explicado allí.

## 4. Editar y randomizar

- **Edición manual:** elige una sección (Pokémon, movimientos, entrenadores, encuentros, objetos, tiendas…), cambia valores y guarda. El buscador (**Ctrl+K**) lleva a cualquier sección o acción.
- **Randomización:** cada sección tiene su randomizador con opciones y semilla. Revisa la vista previa antes de aplicar. Anota la semilla si quieres repetir el resultado.
- **Global Template y Batch:** permiten guardar una receta y aplicarla a otras copias o generar varias salidas. Una receta de 3DS no sirve para Switch ni para DS, y al revés.
- **Comprueba qué se cambió:** el **Historial** muestra cada operación con su antes y después. «Registro del randomizado» (Herramientas ROM o Ctrl+K) exporta un texto con los cambios; **contiene spoilers**.

No todas las funciones existen en todos los juegos. Si una opción aparece desactivada, la interfaz indica el motivo; consulta también la [matriz de compatibilidad](COMPATIBILIDAD_PUBLICA.md).

## 5. Guardar y reconstruir

- **3DS:** guarda los cambios en el proyecto y reconstruye con la herramienta de reconstrucción de la aplicación. Es un paso aparte: guardar no genera por sí solo una ROM jugable.
- **Nintendo DS:** al abrir una ROM se crea o se retoma su proyecto persistente (`Juego.nds.drproj`, junto a la ROM), que se guarda solo después de cada operación confirmada; puedes cerrar la aplicación y continuar otro día. «Exportar ROM…» escribe una ROM **nueva** y un archivo `.cambios.txt` con las operaciones y semillas, solo cuando lo pides. La ROM original no se modifica.
- **Espada/Escudo:** no se reconstruye una ROM. Se **exporta un mod** en formato Ryujinx (carpeta con `romfs` y, si procede, el runtime). Cópialo a la carpeta de mods del emulador. Si ya usas otro mod que reemplaza los mismos archivos, desactívalo durante la prueba.

Guarda siempre y espera a que termine antes de reconstruir o exportar. No cierres la aplicación ni apagues el equipo mientras se guarda.

## 6. Comprobar el resultado

1. **Prueba primero en un emulador**, con una **partida nueva o una copia** de tu guardado. No uses tu guardado principal ni tu instalación principal.
2. Verifica los cambios que hiciste: por ejemplo un entrenador editado, un encuentro, una tienda o un objeto.
3. Si algo falla, no sigas jugando sobre ese guardado: vuelve a la copia, revisa el Historial y consulta [RESPALDO_Y_RECUPERACION.md](RESPALDO_Y_RECUPERACION.md).

Las pruebas automáticas de este proyecto no sustituyen jugar la ROM generada. La [matriz de compatibilidad](COMPATIBILIDAD_PUBLICA.md) indica, por juego, qué se confirmó dentro del juego y qué no.

## Reportar un problema

Sigue la lista de «Qué enviar» de [RESPALDO_Y_RECUPERACION.md](RESPALDO_Y_RECUPERACION.md). **No adjuntes ROMs, extracciones ni partidas.**
