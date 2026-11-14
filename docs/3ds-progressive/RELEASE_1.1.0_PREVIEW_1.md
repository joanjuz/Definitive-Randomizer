# Definitive Randomizer v1.1.0-preview.1 — acceso anticipado

@DISTRIBUTION_STATUS@

Versión del paquete: **1.1.0-preview.1**. Commit exacto compilado: `@BUILD_COMMIT@`. `VERSION.txt` y `BUILD_PROVENANCE.json` identifican la misma compilación.

@DISTRIBUTION_METADATA@

Versión de **acceso anticipado**: contiene todo lo de la estable **1.0.0** y, además, las funciones integradas en `main` después de la base de la estable. Esas funciones tienen menos evidencia jugable que las de la estable y pueden cambiar antes de pasar a una versión estable. Si buscas la versión más probada, usa la 1.0.0. Las diferencias entre las dos líneas están en [LINEAS_DE_VERSION.md](LINEAS_DE_VERSION.md).

## Qué añade respecto a la estable 1.0.0

| Función | Juegos | Evidencia |
| --- | --- | --- |
| **Mega Ability Sync** (PR #104, #105, #106) y su tarjeta en Mecánicas y CRO (#107) | Ultra Sol / Ultra Luna, con el parche en los **dos** juegos | Confirmado por el propietario en un combate real por enlace (caso principal, 13/11/2026). Sin confirmar: un solo lado con parche, sala creada por el otro jugador, dobles/múltiples/Battle Royal, Ultra Sol contra Ultra Luna, Rayquaza, convivencia con Persistent Items. |
| **Perfiles compartibles**, formato 1 (PR #95, #98) | X/Y, OR/AS, Sol/Luna, Ultra Sol/Ultra Luna | Pruebas automáticas; sin prueba manual documentada del recorrido exportar/importar. |
| **Informes de capacidades de parches** de solo lectura (PR #96, #98, #99, #100) | Player Level Caps, Persistent Items, Shiny Rate (USUM) | Pruebas automáticas con archivos sintéticos; sin validación con ROM real. |
| **Diagnóstico y comparador de megaevoluciones** de solo lectura (PR #97, #100) | Ultra Sol / Ultra Luna | Pruebas automáticas. |
| **Procedencia de perfiles** en Definitive Game Data (`profileProvenance`, PR #101–#103) | Ultra Sol / Ultra Luna | Pruebas automáticas y de navegador con Progressive Tracker; sin aceptación manual con una copia real. |

También incluye, como la estable, el balance Eris-Ultranova como paquete opcional: ningún randomizador lo aplica por defecto ([PLANTILLAS_OPCIONALES_ERIS.md](PLANTILLAS_OPCIONALES_ERIS.md)).

## Riesgos de esta línea

- Las funciones de la tabla pueden cambiar de formato o comportamiento antes de la siguiente estable. Los perfiles y las recetas que uses aquí pueden no abrirse en la 1.0.0.
- Mega Ability Sync modifica el código del combate: los dos jugadores necesitan una versión compatible del parche. Si solo uno lo tiene, los dos juegos se comportan como el original y no hay aviso.
- Trabaja siempre con copias y respalda tus partidas. Lo demás (limitaciones de DS, Switch y mentas) es igual que en la estable: ver [LIMITACIONES_ESTABLE.md](LIMITACIONES_ESTABLE.md).

## Cómo se comprobó

- **Automática:** las suites del CI (`build-wpf`) sobre el commit compilado, la auditoría visual y las suites de cada función de la tabla.
- **Dentro del juego:** solo lo indicado en la columna de evidencia. Las correcciones de interfaz y el paquete opcional se comprobaron con pruebas automáticas fuera de pantalla, no jugando.

## Instalación

Igual que la estable: extrae el ZIP **completo** en una carpeta nueva de Windows x64 y abre `DefinitiveRandomizer.exe`. Puede convivir con la 1.0.0 en otra carpeta. Comprueba el SHA-256 con `SHA256SUMS.txt`; los ejecutables no tienen firma digital.

## Licencia, código fuente y redistribución

Definitive Randomizer deriva de pk3DS y se distribuye bajo **GPLv3** (`LICENSE.md`; licencias de terceros en `third-party`). El código fuente completo de esta compilación es el commit de `VERSION.txt`, publicado en el repositorio público, y la entrega incluye un archivo con el código fuente de ese commit.

El acceso anticipado se refiere a **cuándo** se entrega esta compilación, no a quién puede usarla: quien la recibe tiene todos los derechos de la GPLv3, incluidos estudiar, modificar y redistribuir el programa y su código fuente. No hay activación, cuentas ni bloqueos. Las funciones pasarán a una versión estable gratuita cuando tengan evidencia suficiente.

## Reportar problemas

Indica que usas **1.1.0-preview.1**, el commit de `VERSION.txt`, el juego, los pasos, la semilla y los mensajes de error. **No adjuntes ROMs, extracciones ni partidas.**

## Identidad de la distribución

@DISTRIBUTION_WORKFLOW@
