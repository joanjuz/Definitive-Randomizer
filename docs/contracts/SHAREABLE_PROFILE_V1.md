# Perfil compartible — formato 1

Un perfil compartible es un archivo JSON local (`*.drprofile.json`) que lleva **configuración** de Definitive Randomizer para reutilizarla en otra copia del mismo juego. No es un instalador ni una ROM: importarlo solo carga secciones en Global Template, que es quien previsualiza, confirma, aplica, registra en el historial y permite deshacer.

Alcance del formato 1: las cuatro familias de 3DS — `XY` y `ORAS` (generación 6), `SM` y `USUM` (generación 7). Un perfil pertenece a un solo juego y solo se usa en ese juego. Los perfiles de DS, 3DS y Switch no se mezclan. Definitive Game Data es otro contrato (datos exportados) y no se usa como receta.

## Documento

| Campo | Obligatorio | Contenido |
| --- | --- | --- |
| `format` | sí | `definitive-randomizer-profile` |
| `version` | sí | `1` |
| `name` | sí | Texto, 1–120 caracteres |
| `author`, `description` | no | Texto que escribe quien exporta (80 y 2000 caracteres). Procedencia declarada, no verificada |
| `createdUtc`, `generator` | no | Fecha y aplicación que lo creó (`name`, `version`) |
| `target` | sí | `platform` = `3ds`, `game` = `XY`, `ORAS`, `SM` o `USUM`, y su `generation` (6 o 7) |
| `sections` | sí | Lista de secciones (máximo 64): `id`, `kind`, `title`, `summary`, `requires` |
| `dependencies` | no | Copia de `Assets` de la receta: archivos externos por nombre y SHA-256. No se incrustan |
| `omitted` | no | Lo que el autor no pudo incluir: `what`, `count`, `reason` |
| `recipe` | sí | Receta Global Template v1, sin el contenido excluido |
| `recipeSha256` | sí | SHA-256 (hex) del texto JSON compacto de `recipe` |

Tamaño máximo: 2 MB.

## Secciones

Se derivan de la receta; la lista del archivo debe coincidir con ellas (mismos `id`).

| `id` | `kind` | Contenido |
| --- | --- | --- |
| `actions/<módulo>` | `actions` | Acciones de la receta con ese prefijo (`trainers.randomize` → `actions/trainers`) |
| `manual/<tipo>` | `manual-operations` | Operaciones manuales de ese tipo |
| `manual/personal-fields` | `manual-values` | Valores exactos de Pokémon |
| `options/nature-mints` | `option` | Incluir las mentas Gen7 entre los objetos disponibles (solo USUM) |

Operaciones manuales admitidas: `record`, `move`, `learnset`, `personal-flags`, `typechart`, `shiny`, `playercaps`, `crointegrity`, `shops6`, `shops7`, `pickup`, `machines7`, `tutors7`.

**Nunca viajan en un perfil:** recursos reemplazados (`resources`), templates creados (`authored-template`), textos del juego y cualquier ruta local. Al exportar se retiran y se informan en `omitted`; si la receta contiene una ruta local, la exportación se rechaza. Un perfil que los contenga se rechaza al importarlo.

## Requisitos (`requires`)

| Requisito | Significado |
| --- | --- |
| `game:xy`, `game:oras`, `game:sm`, `game:usum` | El proyecto abierto es de ese juego. Cada sección lleva el de su perfil |
| `patch:player-level-caps` | El informe de capacidades de Player Level Caps permite instalar (verificada o compatible por adaptación) |
| `patch:nature-mints` | Parche de mentas instalado y reconocido |

Un requisito que la aplicación no conoce bloquea su sección («no reconocida»): no hay forma de forzarla.

## Lectura estricta

Se rechaza el archivo completo si: no es JSON o supera el tamaño; `format` es otro; `version` es mayor que la admitida (se pide actualizar) o no válida; faltan campos obligatorios o tienen otro tipo; `target` es de otra plataforma, juego o generación; la receta no coincide con `recipeSha256`; la receta no es un Global Template válido para ese juego; la lista de secciones no coincide con la receta; o contiene contenido que un perfil no puede llevar.

Se toleran campos desconocidos en la raíz y la ausencia de los opcionales.

## Compatibilidad con el proyecto abierto

Evaluación de solo lectura, por sección:

| Estado | Se puede elegir | Cuándo |
| --- | --- | --- |
| Compatible | sí, marcada | Requisitos acreditados y sin conflicto |
| Conflicto | sí, sin marcar | La acción ya se aplicó en el proyecto; sustituiría una tabla de Player Level Caps distinta; reemplazaría modificaciones manuales existentes |
| No compatible | no | Otro juego, parche ausente o estado no acreditado |
| No reconocida | no | Requisito desconocido |

Elegir secciones produce una receta Global Template con solo ese contenido y la procedencia del perfil en `ProgressiveApp.Profile` (nombre, autor, huella y secciones elegidas). Nada se escribe en el proyecto hasta «Aplicar cambios» de Global Template.

## Evolución

Los cambios compatibles (campos opcionales nuevos, requisitos nuevos, más juegos) se hacen dentro de la versión 1: una aplicación anterior los rechaza con el mensaje de juego no admitido o bloquea la sección por requisito desconocido. Así se añadieron XY, ORAS y SM a los perfiles, que al principio solo admitían USUM. Un cambio incompatible exige `version` 2.
