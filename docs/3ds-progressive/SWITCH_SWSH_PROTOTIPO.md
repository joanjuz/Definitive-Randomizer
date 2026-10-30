# Espada/Escudo: primer prototipo de exportación de mods

Estado: implementación experimental en `feature/switch-swsh-prototype`. 21 de octubre de 2026. No implica compatibilidad completa ni jugabilidad certificada.

## Alcance implementado

En Inicio aparece «Espada/Escudo · prototipo Switch…». Abre una ventana WPF independiente de la sesión 3DS. Selecciona «Abrir extracción…» y una carpeta con `exefs/main.npdm`, `exefs/main` y `romfs/bin/pml/personal/personal_total.bin`.

El prototipo permite leer entradas presentes, seleccionar estadísticas base, tipos o ratio de captura, previsualizar un valor, añadirlo al borrador y exportar un mod. Las entradas se identifican por especie, forma e índice; todavía no se cargan nombres localizados de Pokémon. El rango de tipos se muestra en la ventana.

El borrador y su historial permanecen en memoria. «Añadir al mod» NO escribe la extracción original. «Descartar cambios» vuelve a los bytes originales de la sesión. Cerrar con cambios sin exportar requiere confirmación. El manifiesto exportado contiene los cambios netos y el orden de las operaciones confirmadas. Todavía no se permite reabrir un borrador desde ese manifiesto.

Al exportar se crea una carpeta nueva con:

```text
Definitive-SWSH-.../
  atmosphere/contents/<TitleID>/romfs/bin/pml/personal/personal_total.bin
  definitive-mod.json
  LEEME.txt
```

No contiene ExeFS, NSP/XCI ni una ROM completa. Para un emulador, usar su carpeta de mods y copiar la carpeta `romfs` que hay bajo el Title ID; el perfil específico del emulador aún requiere comprobación manual.

## Identificación y límites

Se valida META/ACI0 mediante sus offsets y tamaños, sin asumir una ubicación fija del Title ID. Se admiten los Title ID de Espada y Escudo, combinados con el Build ID completo de cada perfil 1.3.2. Un ejecutable desconocido se puede inspeccionar pero no editar ni exportar. Se comprueba también la cabecera NSO0.

La tabla debe tener 1192 entradas de 0xB0 bytes. Las entradas presentes se validan por estadísticas, tipos e índices de formas. Solo se permite modificar uno de los nueve bytes documentados por operación. No se reserializan las compatibilidades, flags, formas ni datos opacos restantes.

Reconocer el Build ID no prueba que RomFS y la actualización se hayan mezclado correctamente. No se detectan las licencias DLC instaladas ni se certifica que todos los recursos de la extracción pertenezcan a esa versión. El manifiesto y la ventana lo indican. Los paquetes NSP/XCI no se extraen ni se descifran en este prototipo.

No se incluyen aún randomización de especies/estadísticas, entrenadores, encuentros, movimientos, tiendas, parches de ejecutable ni integración con Global Template o Batch de Switch. Las funciones 3DS conservan sus servicios y formatos actuales. El experimento cancelado de efectividad no se ha recuperado.

## Integridad

- El origen se abre en lectura; no se genera historial dentro de su RomFS/ExeFS.
- Cada sesión tiene su propio borrador y propietario de vistas previas. No utiliza FileMitm ni rutas globales de pkNX.
- Se conservan SHA-256 del recurso, NPDM y NSO. Si cambian, la previsualización, aplicación y exportación rechazan la operación.
- La vista previa congela sus bytes y queda invalidada cuando cambia el borrador o el valor del formulario.
- Sin diferencias netas no se produce un mod.
- La salida debe ser nueva y estar fuera del origen y de sus directorios ancestros. Se rechazan enlaces/junctions en las rutas comprobadas.
- Se prepara el resultado en staging junto al destino, se reabre y compara el recurso y finalmente se publica mediante un movimiento de directorio. Un error previo no publica el mod ni sobrescribe destinos existentes.
- Un contenedor completo puede ser necesario aunque solo cambie un byte: aquí el juego carga `personal_total.bin` entero.

## Referencias y atribución

El código es una implementación acotada y separada; no se incorporó la dependencia completa pkNX ni sus gestores globales.

- [pkNX PersonalInfo8SWSH](https://github.com/kwsch/pkNX/blob/master/pkNX.Structures/Personal/Info/PersonalInfo8SWSH.cs): referencia GPL-3.0 de estructura binaria y offsets. Se preserva atribución en el lector local.
- [pkNX GameFileMapping](https://github.com/kwsch/pkNX/blob/master/pkNX.Game/File/GameFileMapping.cs): ruta del recurso personal.
- [pkNX README](https://github.com/kwsch/pkNX#readme): edición por carpeta LayeredFS y versión de referencia.
- [Prelude Nextendo, módulo SWSH](https://github.com/NextendoNetwork/Prelude-Nextendo/tree/main/romfs/sd/atmosphere/exefs_patches/pkmnswsh-nextendo): IDs completos de módulos asociados por el proyecto a SWSH 1.3.2. Se usan únicamente como identificadores de perfil; no se copiaron sus parches IPS ni su lógica de red.
- [Atmosphère RomFS](https://github.com/Atmosphere-NX/Atmosphere/blob/master/stratosphere/ams_mitm/source/fs_mitm/fsmitm_romfs.cpp): redirección de recursos.

## Archivos de implementación

- `pk3DS.Core/NX/SwordShieldIdentity.cs`: identidad de ejecutable y perfil.
- `pk3DS.Core/NX/SwordShieldPersonalTable.cs`: lector y edición de bytes de personal.
- `3DS.Progressive.App/Services/Switch/SwordShieldProject.cs`: borrador, validación, historial y exportador aislados por sesión.
- `3DS.Progressive.App/SwordShieldPrototypeWindow.cs`: interfaz WPF del prototipo.
- `3DS.Progressive.App/MainWindow.Switch.cs`, `MainWindow.Guidance.cs`, `StartView.cs`: acceso independiente desde Inicio.
- `3DS.Progressive.Tests/SwordShieldChecks.cs`, `Program.cs`: suites propias, lecturas reales y generación de un mod de diagnóstico fuera del origen.
- `.github/workflows/3ds-progressive-ci.yml`: ejecuta `--swsh-only` y `--swsh-ui-only`, además de las suites anteriores. Esta modificación no equivale a una ejecución real en GitHub Actions.

## Verificaciones manuales pendientes

1. Partir de una extracción limpia y coherente de Espada 1.3.2 y otra de Escudo 1.3.2. Las rutas facilitadas de `Juegos/Pokemon Espada` y `Juegos/Pokemon Escudo` contienen paquetes NSP, no una extracción lista.
2. La lectura real disponible se hizo con `D:/Nintendo/Switch/Randomizado`: es Espada y ya contiene modificaciones anteriores. No representa una prueba vanilla ni de Escudo.
3. Probar el mod exportado en una copia del entorno habitual, sin otro mod que reemplace personal_total.bin. Comprobar arranque, carga, equipo, selección de movimiento, combate y eventos; retirar el mod y comprobar restauración.
4. Las estadísticas visibles en un Pokémon ya existente pueden necesitar recalcularse o subir de nivel. Para una comprobación determinista puede usarse un cambio de tipo de una especie disponible y confirmar dentro del juego; no se ha automatizado esa comprobación.
5. Revisar la ventana con teclado y escalados reales 100/125/150/200 %. Las pruebas de componentes fuera de pantalla no son pruebas de interacción ni de DPI real.
6. Validar perfiles de instalación para emulador y consola. No se realizó instalación automática ni modificación de distribuciones, juegos o guardados.

## Resultados ejecutados en esta iteración

- Compilación Debug de App, Tests, Core y backend WinForms transitivo: 0 errores. Compilación Release con `--no-incremental`: 0 errores, 78 advertencias en archivos anteriores; no quedan advertencias en los archivos nuevos de Switch.
- `--swsh-only`: 87 comprobaciones aprobadas de identidad, límites, datos opacos, borrador, historial, destino y hashes. Incluye, entre otras, `Swsh_Identity_Sword132`, `Swsh_Identity_Shield132`, `Swsh_Personal_OnlySelectedByte_0_255`, `Swsh_Project_PreviewBytesAreFrozen`, `Swsh_Project_ApplyIsolatedAndJournaled`, `Swsh_Project_RejectChangedSourceApply`, `Swsh_Export_OriginalExtractionByteIdentical` y `Swsh_Export_ManifestMatchesBytesAndHistory`.
- `--swsh-ui-only`: 12 comprobaciones aprobadas de componentes fuera de pantalla: acceso desde Inicio, estados de edición, etiquetas, previsualización, aplicación, alto contraste y acción de exportar visible a 600 y 950 DIP. Casos: `Swsh_UI_InicioInvokesIndependentPrototype`, `Swsh_UI_PreviewWaitsForApply`, `Swsh_UI_ApplyUpdatesDraftAndEnablesExport`, `Swsh_UI_ExportVisible_600`, `Swsh_UI_HighContrastUsesSharedResources`. No se ejecutó interacción de escritorio ni escalado real.
- `--synthetic-only`: 3849 regresiones aprobadas. El total incluye las 87 comprobaciones nuevas del backend Switch; el resto ejercita familias y módulos existentes. No equivale a 3849 pruebas de Switch.
- `--visual-redesign-only`: 95 aprobadas; `--guidance-only`: 233; `--single-purchase-only`: 6; `--item-icons-only`: 17.
- Lectura real de `D:/Nintendo/Switch/Randomizado`: Espada, Title ID `0100ABF008968000`, perfil 1.3.2, tabla de 1192 entradas y 785 presentes. Solo se leen los recursos indicados bajo exefs/romfs; no se combinan automáticamente carpetas de otros mods. La limpieza del resto de esa extracción no está certificada.
- Exportación de diagnóstico bajo la carpeta temporal `DR-SWSH-prototype-20261021/diagnostic-mod-bulbasaur-hp-plus1`: PS base de la entrada 1 de 45 a 46; diferencia exacta de un byte, resto idéntico y origen conservado. SHA-256 origen `A1D7FBEE6A2C729D5BD9E26241C76CF12F961AA0468D060894D225E59AE7A8AC`; exportado `1F5C6DBBD0CCBB4685FC2AC1AB2FF6224A3A75ECB7A62C45E1F4DE9D4329CB56`.
- `git diff --check`: sin problemas. Las suites de CI se configuraron, pero no se ejecutó GitHub Actions ni se publicó el prototipo.

No se ha realizado una prueba real de Escudo, instalado el mod en un emulador/consola ni validado la jugabilidad. Los fallos iniciales de compilación por aliases WPF/WinForms y del NameScope al construir la ventana se corrigieron antes de las suites finales.

Skills aplicadas: pk3ds-progressive-safety, wpf, dotnet-test/code-testing, dotnet-test/run-tests y dotnet-msbuild/binlog-generation. No se añadió un nuevo proyecto de pruebas ni un framework ajeno al runner propio.

## Seguimiento: extracción de Espada facilitada y mod visible en Ryujinx

La extracción facilitada por el usuario en `D:/Nintendo/Switch/RomEspadaEX/0100ABF008968000` se abrió en lectura: Title ID Espada y Build ID del perfil 1.3.2 reconocidos, 1192 registros y 785 presentes. El hash de personal_total.bin coincide con el de la extracción examinada antes; esto no certifica la limpieza de todos los demás recursos.

La preparación del nuevo diagnóstico detectó un error del prototipo: se utilizaba el ModelID gráfico de 0x4C como especie nacional. En esta tabla, Grookey ocupa la entrada 810 pero su ModelID es 948; buscar ModelID 810 seleccionaba incorrectamente la entrada 765. La validación del tipo original abortó antes de generar o instalar un mod equivocado.

Se corrigió SwordShieldPersonalTable: las filas base 1–898 se identifican por su índice nacional y las formas por los vínculos de la fila base (FormStatsIndex/FormCount), sin deducir especie ni forma a partir de identificadores gráficos. Se rechazan vínculos ambiguos o dirigidos a filas base; las formas no resueltas se identifican como tales y no se editan. La corrección no altera ningún byte durante la lectura.

Cobertura añadida: `Swsh_Personal_NationalSpeciesDoesNotUseGraphicsModelId`, `Swsh_Personal_IdentityResolutionPreservesOpaqueBytes`, `Swsh_Personal_AlternateFormsResolveFromBaseLinks`, `Swsh_Personal_FormEditPreservesBaseEntry`, `Swsh_Personal_RejectAmbiguousFormOwners`, `Swsh_Personal_RejectFormLinkIntoBaseSpecies`, `Swsh_Personal_UnmappedFormNotMisidentified`, `Swsh_Personal_RejectUnmappedFormEdit`. Resultado actual: 95 comprobaciones SWSH y 12 componentes WPF aprobados. Compilación Release con --no-incremental: 0 errores, 78 advertencias en archivos previos. No se repitió la suite sintética general después de esta corrección exclusiva del lector NX; el resultado de 3849 pertenece a la iteración anterior.

Por petición expresa del usuario se instaló una subcarpeta nueva en:

`C:/Users/Joanj/AppData/Roaming/Ryujinx/mods/contents/0100abf008968000/DR-Diagnostico-Iniciales-Hada`

Contiene `romfs/bin/pml/personal/personal_total.bin`, manifiesto y LEEME. Solo se cambiaron tres bytes: Type1 de las entradas base 810, 813 y 816 a Hada (17). Grookey queda Hada/Planta; Scorbunny Hada/Fuego; Sobble Hada/Agua. No cambia sus evoluciones ni otras especies. Se verificó independientemente el diff exacto, que no cambia la longitud y que la extracción fuente permanece idéntica. SHA-256 exportado e instalado: `E9DB6BA917A3B19D221FD49DC9B6B6A6B71EBA50548114F65695B5FFCBF93C46`.

Se detectó un recurso en `MODS/Divine Sword/romfs/bin/pml/personal/personal_total.bin`. No se modificó ni desactivó ese mod. Debe estar desactivado durante la prueba si el usuario lo tiene habilitado, porque reemplaza la misma tabla completa. No se inspeccionó el estado de activación de Ryujinx ni se operó su interfaz.

Prueba pendiente: detener y volver a abrir el juego con el diagnóstico activo, comprobar la ficha de uno de esos iniciales sin evolucionar y confirmar que aparece Hada. No hace falta guardar encima de la partida para comprobarlo. Desactivar el diagnóstico y reiniciar restaura los tipos de los recursos base. No se ha validado la jugabilidad ni se han modificado ROMs, guardados o mods anteriores. Sin commit ni push de esta corrección.

## Validación manual confirmada por el usuario — 21 de octubre de 2026

El usuario confirmó que aparece Hada dentro del juego al probar el diagnóstico instalado en Ryujinx. Se considera aprobada la comprobación visible de carga del mod y del cambio de tipo en Espada. Es una validación realizada por el usuario, no una sesión de emulador controlada por el agente.

Esta confirmación sustituye el estado pendiente de la comprobación del tipo indicado en el apartado anterior. No acredita todavía combates y eventos prolongados, escalados o navegación de la aplicación, restauración tras desactivar el mod ni compatibilidad real de Escudo. Tampoco valida otras operaciones de randomización.

Siguiente hito propuesto: ampliar el prototipo con randomización de tipos y habilidades sobre entradas presentes, semilla reproducible, vista previa, aplicación al borrador y exportación mediante el mismo flujo ya comprobado. Mantener los parches de ejecutable, entrenadores, encuentros y Batch fuera de ese hito inicial.

## Siguiente entrega implementada

Se añadió [randomización de tipos y habilidades](SWITCH_SWSH_RANDOMIZACION.md), con semilla reproducible, alcance, protección de mecánicas de formas y exportación directa Ryujinx/Atmosphère. Este documento conserva el estado histórico del primer prototipo. El objetivo de ampliar hacia la mayoría de herramientas 3DS se desarrolla en [la hoja de ruta](HOJA_RUTA_SWITCH.md). La validación del cambio de tipo por el usuario no debe confundirse con la validación jugable de las nuevas habilidades aleatorias.
