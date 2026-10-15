# Correcciones de reconstrucción y títulos · candidato 75

## Causa del cierre al abrir

El ExHeader de la extracción del usuario tenía el indicador BLZ a cero (byte 0x0D). El rebuild comprimía siempre `.code.bin`, pero reutilizaba ese encabezado sin actualizarlo. Se crea ahora un ExHeader temporal con ese bit activado; CTRUtil calcula su hash y cifra ese encabezado coherente con el ExeFS generado. Se conserva el archivo fuente. Cuatro pruebas cubren indicadores iniciales 0, 1, 0xA4 y 0xA5, incluyendo el resto de los bytes y la integridad del origen.

El usuario confirmó que `UltraSol-arranque-corregido.3ds` arranca tras corregir ese indicador, su hash NCCH y la copia del encabezado en Card Info. La primera ROM que solo reparaba las texturas seguía cerrándose: la expansión gráfica no era la causa del fallo inmediato de carga.

## Modo completo y tamaño de 4 GB

El cálculo del relleno multiplicaba dos valores uint antes de asignarlos a ulong. Al llegar a 4 GiB se desbordaba y omitía el relleno. Se convierte a ulong antes de multiplicar. Las salidas Trimmed pueden ocupar alrededor de 3,4 GiB; una salida completa debe coincidir con la capacidad indicada en su encabezado NCSD. El registro de la reconstrucción real especifica el resultado de esa comprobación.

## Protección de texturas

La extracción USUM del usuario tenía un único cambio registrado, en `a/0/9/9`, recurso ALYT 21 (Ultra Sol, español). Su hash anterior coincidía exactamente con el mismo GARC de la extracción UltraLuna sin editar. Se usó esa coincidencia para recuperar el formato original de las texturas sin extraer ni modificar la ROM original.

La comparación detectó 17 texturas modificadas. La importación de PNG forzaba RGBA8 con orientación cero y versión genérica. El recurso ALYT aumentó de 7.801.728 a 14.191.872 bytes. La relectura anterior comprobaba dimensiones y contenido, pero no protegía la asignación original ni el formato requerido por el título.

Corrección:

- `GameGraphicsCodec.EncodeLike` codifica con el formato y orientación originales, exige igual longitud y conserva los 40 bytes del footer original, incluyendo versión y alineación.
- El importador PNG de títulos usa esa conversión. La recompresión ETC puede introducir pequeñas diferencias de color propias del formato con pérdidas.
- El editor de títulos Gen7 rechaza importaciones BFLIM que cambien el footer o tamaño; indica importar PNG para una conversión compatible.
- Los reemplazos ALYT de igual tamaño se realizan en la posición original sin reconstruir ni reorganizar la tabla SARC.

Se crea una extracción separada `Reparacion-titulo-USUM-75/PokemonUltraSol-reparado` conservando las imágenes editadas, pero con su formato original. Se verifican tamaños y footers de todas las texturas y la identidad de los demás registros GARC. No se modifica la extracción del usuario, el historial original ni las ROM Progressive anteriores.

Pruebas: Debug sin errores (88 advertencias al recompilar Core y App), 3.877 regresiones, incluyendo los cuatro casos del indicador BLZ, rechazo de expansión de títulos y una textura ETC1A4 rectangular transpuesta con versión diferente. Los hashes de Core y App publicados deben coincidir con los binarios sometidos a las pruebas.

La reconstrucción y prueba de arranque se documentan en los registros adjuntos. La validación de estructura no equivale por sí sola a probar todo el juego. Windows registró cierres previos de Lime3DS con excepción 0xc0000005 en ucrtbase.dll; ese evento no identifica por sí solo la causa del cierre.

Para nuevas ediciones usar el candidato 75 y la extracción reparada, o una extracción limpia. Abrir el proyecto anteriormente editado con una App nueva no revierte automáticamente las texturas que ya se habían convertido a RGBA8.

## Título que no refleja los cambios

Se descifró la sección RomFS de `UltraSol-arranque-corregido.3ds` y se extrajo únicamente `a/0/9/9`. Coincide exactamente con el GARC reparado (SHA-256 3924A9840CE03D3D3A54D2D4193A53016639C794AD4E2C147397C7F312C26065). La textura `Title_Sun_00.bflim` del recurso 21 contiene el logo Ultranova Eris. El usuario confirmó después que sí aparece al iniciar una partida nueva; el logo original que observaba corresponde a la secuencia de acceso a una partida guardada, que usa otros recursos.

Antes de esa aclaración se creó una variante de diagnóstico con las mismas 17 imágenes en los nueve recursos de idioma de Ultra Sol (18–26). Conserva tamaños y footers de las texturas y no cambia las variantes de Ultra Luna. Queda como evidencia de reconstrucción; no hace falta usarla para conservar la edición exclusivamente española del usuario.

La variante `UltraSol-titulo-todos-idiomas.3ds` terminó correctamente con tamaño real 4.294.967.296 bytes y SHA-256 FC8D2BFF9F53818F550E3D92885695E8E60A37CA1BA5A7BDF8B61A24E2FA9B47. Se verificaron el indicador BLZ y el hash del ExHeader cifrado. Su GARC de título descifrado coincide exactamente con el proyecto de nueve idiomas (SHA-256 81F7A44649187D3E227CE6EE4821A7121A7566ABE8094AE7B289B1D3A3079D39).

El arranque y el logo al iniciar una partida nueva fueron confirmados por el usuario. La secuencia de acceso a una partida guardada no está cubierta por este editor. Se actualizan la descripción del recurso USUM, la ayuda del editor y la tarjeta de Sistemas para reflejar ese alcance; también se precisa que la verificación del rebuild se refiere a la integridad del archivo. Tras esos cambios de texto se ejecutan las comprobaciones específicas de integración.

La entrega recomendada es `UltraSol-final-es-completo.3ds`: conserva íntegramente los bytes de la ROM española cuyo arranque confirmó el usuario y añade únicamente relleno FF hasta la capacidad de 4 GiB indicada por NCSD. Su registro `final-spanish.log` incluye el hash. No modifica los otros idiomas. No se afirma validación de una partida completa ni implementación de la otra secuencia visual.
