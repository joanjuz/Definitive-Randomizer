# Nintendo DS — editor de eventos, fase 1

Implementación acotada para **Pokémon Platino USA, CPUE/1**. Se abre desde **Nintendo DS → Eventos**, en la barra lateral y en la búsqueda de secciones. No se anuncia soporte de otras revisiones, idiomas, juegos, perfiles ampliados o mapas de romhacks. No modifica ARM9, overlays, partidas ni archivos del emulador.

## Flujo de uso

1. Abre una **copia** de la ROM compatible. En Eventos, busca por nombre, ID o nombre interno del mapa. Los nombres legibles se leen del banco 433 del juego; no se distribuyen textos o gráficos del juego.
2. Selecciona un registro en **NPC, Objetos, Warps o Scripts**. El inspector conserva el ID, índice, referencias y bytes brutos copiables. Objetos distingue eventos de fondo (incluidos ocultos) de NPC de tipo objeto de suelo; no los equipara a la tabla de Field Items del randomizer.
3. En un NPC existente, cambia **X, Y o dirección (0–3)**. Previsualiza las diferencias exactas. No se editan IDs, gráficos, comportamiento, flags, alcance, Z ni referencias de scripts en esta fase; se muestran como información. No se añaden/eliminan registros.
4. Pulsa **Guardar cambios…** y elige un archivo **nuevo** `*.ndsevents.json`. Se escribe, verifica y publica el checkpoint antes de cambiar el borrador en memoria. No se sobrescriben checkpoints: cada versión es una recuperación independiente. A continuación, **Exportar ROM…** reconstruye una ROM nueva mediante el escritor NDS existente.
5. Para continuar tras cerrar la aplicación, abre el checkpoint desde el selector de ROM/checkpoint de Nintendo DS. Se comprueba la ruta y SHA-256 de la ROM fuente, se vuelven a acreditar las tablas y se reproducen los cambios. La fuente debe seguir disponible, sin cambios. Abrir únicamente la ROM original no carga automáticamente un checkpoint.
6. **Restaurar último cambio…** prepara la diferencia inversa; guarda otro checkpoint para confirmarla y exporta a otra ROM. Repetir restaura cambios previos. La restauración completa de una sesión que solo editó eventos puede producir una ROM idéntica a su fuente.

El checkpoint guarda **solo eventos**, no ediciones pendientes de entrenadores, objetos, iniciales u otros módulos. Estas siguen su flujo de exportación habitual. La vista advierte esa separación. Al exportar desde la misma sesión se combinan el borrador DS existente y el NARC de eventos, conservando los archivos de los demás módulos. El historial muestra ambas clases de operaciones. Descartar cambios pendientes vuelve al estado de la fuente, sin eliminar checkpoints.

## Perfil y tablas verificados

La copia de referencia del propietario se identificó con el lector existente como CPUE/1, SHA-1 `0862ec35b24de5c7e2dcb88c9eea0873110d755c`. Solo se usó una copia aislada; no se incorpora al repositorio.

| Tabla | Ubicación / contrato CPUE/1 | Huella CRC32 |
|---|---|---|
| ARM9 descomprimido | 1.057.784 bytes; solo lectura | `4D104949` |
| Cabeceras de mapa | ARM9 `0xE601C`, 593 × 24 bytes | `BB8DDE9D` |
| Nombres internos | `fielddata/maptable/mapname.bin`, 593 × 16 bytes | `530174B6` |
| Eventos | `fielddata/eventdata/zone_event.narc`, 534 miembros | original `D8BB2D84` |
| Matrices | `fielddata/mapmatrix/map_matrix.narc`, 289 miembros | `90D98DF7` |
| Scripts | `fielddata/script/scr_seq.narc`, 1.124 miembros | `6D482515` |

Las cabeceras enlazan matriz (+2), archivo de scripts (+4), archivo de eventos (+16) y etiqueta de nombre (+18). Se validan todas esas referencias antes de habilitar el editor. La matriz proporciona ancho/alto en bloques de 32 baldosas y valida tamaño, nombre y secciones opcionales. En archivos compartidos por varias cabeceras, la vista lo indica y la edición usa los límites comunes más conservadores.

Los eventos modificados se reconocen al reabrir la ROM reconstruida mediante una huella SHA-256 del **NARC completo normalizado únicamente en dirección y X/Y de cada NPC**: `24D1E66E53C5D87DE91CAA26D6D19690C8AC04BAFDE1EAD6BFD7C25F2E1AA7DE`. Los restantes bytes, estructura, asignación, orden y padding deben coincidir con el perfil. No es una aceptación general de NARC alterados. Un cambio de ARM9, nombres, matrices, scripts o bytes no editables bloquea este editor; no deshabilita los otros módulos DS que admitan ese estado. Un resultado combinado con parches de ARM9/scripts puede requerir continuar desde el checkpoint y su fuente acreditada, no desde esa ROM combinada.

## Registros y escrituras permitidas

Cada miembro contiene cuatro bloques sucesivos: contador u32 little-endian y registros de **20 bytes** (fondo), **32 bytes** (NPC), **12 bytes** (warp), **16 bytes** (activador). Se comprueban longitudes exactas, contadores, alineación del NARC, ausencia de solapamientos y límites; se rechazan colas desconocidas. Se reutilizan `NdsRom` y `Narc`, sin introducir otro parser NDS.

En NPC se consultan ID (+0), gráfico (+2), movimiento (+4), tipo (+6), flag (+8), script (+10), dirección (+12), alcance visual (+14), rangos X/Y (+20/+22), coordenadas X/Y i16 (+24/+26) y altura bruta (+28). **Solo se escriben los seis bytes correspondientes a dirección, X e Y**, en el NARC original. Se exige dirección 0–3 y coordenadas no negativas dentro de la matriz. Los desconocidos (+16/+18), otros registros, contadores y padding quedan byte a byte iguales. Estos límites no validan permisos de terreno, colisiones, visibilidad ni comportamiento de guiones.

Warps muestra coordenadas, mapa destino (+4) e índice/ancla (+6). **No permite editar destinos**. «Ir al destino existente» solo se habilita si existen el mapa y el warp indicado; los destinos especiales/dinámicos o referencias inválidas se indican sin inventar un enlace. No se interpreta esto como una conexión jugable validada.

Scripts muestra el archivo de la cabecera, la referencia y, si puede acreditarla, la entrada local y offset. Se consulta la tabla de offsets relativos u32 terminada en `0xFD13`; no se descompilan instrucciones. Se reconocen las referencias de objetos de suelo 7000–7999 (archivo 404) y ocultos 8000–8799 (408), validando la entrada. Otras referencias comunes/externas se dejan explícitamente sin resolver. Los bytes de consulta corresponden a la fuente acreditada, no a posibles borradores de otros editores. No hay escritura de scripts ni lenguaje nuevo.

## Checkpoints, concurrencia y recuperación

Formato separado `DefinitiveRandomizer.NdsEvents`, versión 1. Almacena identidad CPUE/1, ruta local y SHA-256 completo de la fuente, campos antes/después y registro de operaciones. No incluye la ROM ni gráficos. No es un Global Template.

- La sesión usa el handle de lectura existente de `NdsRom` con `FileShare.Read`, que bloquea escritores y reemplazos de la fuente en Windows. Se comprueba además SHA-256 al guardar/restaurar/exportar.
- Las propuestas están ligadas a sesión y revisión. Una propuesta ajena o desactualizada falla. Los guardados de eventos de la misma sesión se serializan; no se mezcla el resultado de dos revisiones.
- Un checkpoint se escribe en staging exclusivo, se vacía al disco, se relee y se reproduce antes de moverlo al nombre nuevo sin sobrescritura. Si falla antes de publicar, permanece el checkpoint anterior y la memoria no cambia; se limpia solo el staging propio. Destinos existentes, enlaces, fuente cambiada, JSON truncado y versiones/referencias inválidas se rechazan.
- El escritor NDS existente construye una ROM nueva mediante `.partial` y publicación final; se verifica el NARC de eventos al reabrir la salida. El registro `.cambios.txt` sigue el flujo DS existente y no constituye una transacción de varios archivos con la ROM. Una avería del log puede dejar una ROM completa junto a checkpoints recuperables; no se promete atomicidad del conjunto, recuperación universal frente a apagones ni protección de todas las plataformas/sistemas de archivos.
- El checkpoint conserva el historial de eventos al reabrir. El historial DS anterior sigue siendo de sesión; no se anuncia una migración de todo el historial DS al contrato de 3DS.

**Global Template y Batch no incorporan estas operaciones.** Capturar una receta desde una sesión con historial de eventos se rechaza con un mensaje que remite a los checkpoints; no se omiten silenciosamente los eventos ni se alteran las recetas previas. Las recetas anteriores, sin operaciones de eventos, siguen funcionando.

## Fuentes y licencia

Se consultaron [DSPRE](https://github.com/DS-Pokemon-Rom-Editor/DSPRE/tree/e751187b45d6ddf237a24dab8407a5a6cf0d36dd), `RomInfo.cs`, `ROMFiles/MapHeader.cs`, `ROMFiles/EventFile.cs`, `ROMFiles/GameMatrix.cs` y las tablas de referencias de scripts, contrastando los datos binarios con la copia privada. DSPRE publica bajo **AGPLv3**, mientras este proyecto conserva su licencia GPLv3. **No se ha copiado ni enlazado código, UI, componentes ni assets de DSPRE**: implementación independiente con el contenedor existente y hechos de formato comprobados. No se agrega DSPRE como dependencia ni se distribuyen sus recursos. Se conserva esta atribución de la investigación.

## Validación y prueba jugable pendiente

Las suites específicas son `--nds-events-only` y `--nds-events-ui-only`; la primera se incluye en `--nds-only`. Comprueban lectura, límites, bytes desconocidos, referencias, propuestas, checkpoints, fallos antes de publicación, concurrencia, reapertura, restauración y UI fuera de pantalla. `--nds-events-probe <carpeta nueva> <ROM privada CPUE/1>` recorre el proyecto real, reconstruye y contrasta los archivos ajenos y el SHA-256 de la restauración completa. Ninguna sustituye una prueba en emulador.

Resultados de desarrollo de esta entrega: compilación Release del runner, WPF y dependencias con 0 errores y 0 advertencias; **42** casos sintéticos específicos; una regresión DS de **203** comprobaciones (incluye esos 42); **15** comprobaciones WPF DS anteriores y **12** del editor nuevo, fuera de pantalla. Se revisaron renders sintéticos a 1100 y 760 unidades lógicas. En la copia privada CPUE/1 se verificaron checkpoint/reapertura, reconstrucción y lectura del cambio, todos los archivos ajenos al NARC idénticos y la restauración completa con SHA-256 idéntico al origen. Se conserva la fuente privada intacta; no se ejecutó un emulador. Las capturas y resultados privados quedan fuera de Git.

Prueba manual corta, responsabilidad del propietario:

1. Copia la ROM CPUE/1 acreditada y respalda la partida; usa otra carpeta de salida. Abre Nintendo DS → Eventos.
2. Busca **411 · Twinleaf Town · T01**. La referencia privada contiene NPC índice 0, ID 0, X/Y 119/878. Anota los valores que muestre tu copia. Cambia la dirección de 2 a 3, manteniendo coordenadas y el resto. Previsualiza y guarda un checkpoint nuevo.
3. Cierra el proyecto y abre ese checkpoint. Comprueba que mantiene el cambio; exporta una ROM nueva y ábrela con una partida de prueba en el emulador. En Pueblo Hojaverde comprueba el NPC antes de interactuar. Un movimiento o guion puede imponer otra orientación durante el juego: no se declara validada esa interacción por la mera aceptación binaria. Alternativamente, mueve un NPC conocido a una baldosa adyacente transitable, tras comprobarla manualmente.
4. En el editor, prepara «Restaurar último cambio», guarda otro checkpoint y exporta a otra ROM. Reabre el checkpoint restaurado y comprueba los valores. Si solo se editaron eventos, compara SHA-256 con la copia fuente: deben coincidir.
5. Informa ROM/perfil, mapa/índice/ID, valores antes/después, resultado visible y restauración. No adjuntes ROMs ni partidas.

**No hay validación jugable declarada para esta fase.** No implica una campaña completa, un editor visual de mapas ni la paridad total con DSPRE.
