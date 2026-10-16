# Clasificación de Game Text y Story Text

Estado: cobertura funcional implementada y comprobada en las ocho versiones, idioma español. La distribución y su .exe permanecen sin actualizar.

## Funcionamiento

- Catálogo de Game Text por versión y número de banco, revisado contra las copias locales. El catálogo por índice exige la cantidad de archivos esperada para ese grupo.
- Story Text combina funciones revisadas, referencias de zonas, citas de personajes contrastadas y sugerencias explícitas por contenido.
- Gen6: se lee el miembro ZoneData (0x38 bytes) de cada contenedor ZO válido, TextFile en 0x06 y ParentMap en 0x1C. La tabla maestra está al final de los GARC vanilla, no en el miembro cero. Solo se lee la cabecera necesaria; no se desempaquetan scripts ajenos.
- Gen7: registros de zona de 0x54 bytes; campo de texto 0x0C y ParentMap en 0x1C. El vínculo se contrastó con citas largas únicas y nombres ingleses de lugares: ocho bancos SM y siete USUM dieron corroboración independiente de ubicación. Es comprobación estructural y documental, no validación in-game de cada referencia.
- 57 páginas de citas de Bulbapedia consultadas; 6.105 correspondencias con metadatos. Se exigen dos coincidencias con anclas únicas de nueve palabras, o mensajes completos únicos de al menos seis palabras. No se hacen coincidencias difusas.
- Las correspondencias conservan juego, índice, línea, cantidad de líneas, SHA-256 del mensaje inglés, personaje, contexto y fuente. En ejecución se comprueban el archivo inglés local y la estructura del idioma activo. El personaje puede intervenir solo en algunas líneas de un banco mixto.
- Otros 358 bancos se revisaron por función, con huella del archivo inglés completo. Esto incluye servicios, pequeñas misiones, escenas, reacciones breves y actividades; no se inventa un hablante para las reacciones sin contexto suficiente.
- Los catálogos distribuidos contienen índices, hashes y descriptores; no incluyen los textos del juego ni los artículos descargados. El editor no necesita consultar internet para estas etiquetas.
- Vacíos, reservados, marcadores n/a y archivos solo de control se identifican por separado; se cuentan en la cobertura funcional, no como escenas identificadas.

## Interfaz

Filtros por etiquetas presentes en el banco abierto, personajes y búsqueda sin distinción de acentos. El índice de búsqueda incluye el contexto completo aunque la tarjeta muestre un resumen. Los lugares se localizan con los nombres de la propia ROM cuando hay equivalencia.

Las evidencias están en un panel plegable con salto a la línea. El pie muestra un resumen limitado para no desplazar el editor. La personalización manual de etiquetas se retiró por petición del usuario. Se usa exclusivamente la clasificación automática; los antiguos archivos personales de etiquetas se conservan, pero ya no se leen ni modifican. No se cambian mensajes, códigos, orden ni cantidad de líneas al clasificar.

## Resultados

| Grupo | Game Text por versión | Story Text por versión |
|---|---:|---:|
| XY | 151/151 | 424/424 |
| ORAS | 175/175 | 637/637 |
| SM | 122/122 | 776/776 |
| USUM | 127/127 | 1124/1124 |

Las ocho carpetas PokemonX, PokemonY, PokemonOR, PokemonAS, PokemonSol, PokemonLuna, PokemonUltraSol y PokemonUltraLuna pasaron la lectura, cobertura y comprobación de hashes de los bancos españoles: 7.072 archivos. Carga medida de aproximadamente 0,3 a 0,7 segundos por banco en esta máquina.

35 pruebas de clasificación, 56 regresiones de búsqueda/edición y ocho de inicialización de interfaz pasaron. Se comprobó invalidación de referencias inglesas modificadas, límites de zona, aislamiento por juego, códigos y ausencia de duplicados. Debug: cero errores y advertencias preexistentes. Se compiló con UseAppHost=false: no se generó un ejecutable actualizado ni se modificó el paquete instalado. No hubo commits, push ni publicación.

Ver ETIQUETAS_TEXTOS.md para las 53 categorías usadas y los 57 filtros de personaje, y TEXTOS_AUDITORIA_20261015.txt para el informe de las ocho versiones.

## Límites

Cobertura funcional completa significa que todos los bancos revisados tienen una categoría útil; no que cada línea tenga un hablante, mapa o evento concreto confirmado. Las sugerencias por contenido siguen siendo sugerencias. No se declara revisión exhaustiva de todos los idiomas ni compatibilidad por índice con bancos reordenados de hacks. Cuando falla una huella o estructura se omite la correspondencia, en lugar de aplicar una etiqueta que ya no tiene respaldo.

No se controló el escritorio ni el emulador. La revisión visual final por el usuario y las pruebas de sus modificaciones en juego permanecen separadas de esta auditoría del catálogo.

## Reproducción de metadatos

El modo de prueba --export-local-english-for-text-research exporta temporalmente los bancos ingleses de las ROMs propias. Ese archivo NO debe publicarse. Los scripts de tools/text-catalogue generan exclusivamente los dos JSON de metadatos de 3DS.Progressive.App/Data. La caché de investigación y los dumps se eliminaron después de la comprobación; los artículos originales siguen enlazados desde cada correspondencia.

## Fuentes

- https://www.wikidex.net/wiki/Guía_de_Pokémon_X_y_Pokémon_Y
- https://www.wikidex.net/wiki/Guía_de_Pokémon_Rubí_Omega_y_Pokémon_Zafiro_Alfa
- https://www.wikidex.net/wiki/Guía_de_Pokémon_Sol_y_Pokémon_Luna
- https://www.wikidex.net/wiki/Guía_de_Pokémon_Ultrasol_y_Pokémon_Ultraluna
- https://bulbapedia.bulbagarden.net/wiki/Lillie/Quotes (las demás 56 páginas constan en el catálogo)
- Lector original de Gen6: pk3DS.WinForms/Subforms/Gen6/Experimental/OWSEStructs.cs, ZoneData.TextFile y ParentMap.
