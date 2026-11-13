# Líneas de versión: estable y acceso anticipado

Definitive Randomizer (WPF) se distribuye en dos líneas con numeración distinta. Las dos son software libre bajo GPLv3.

| | Estable | Acceso anticipado |
| --- | --- | --- |
| Para quién | Cualquier usuario; la recomendada | Quien quiera probar novedades antes |
| Rama | `release/1.0` (mantenimiento) | `main` |
| Versiones | `1.0.0`, `1.0.1`, `1.0.2`… | `1.1.0-preview.1`, `1.1.0-preview.2`… |
| Contenido | Funciones maduras, con correcciones de errores y compatibilidad | Todo lo de la estable más las funciones integradas después |
| Cambios | Solo correcciones; ninguna función nueva | Funciones nuevas y experimentales |

## Qué contiene cada una (1.0.0 frente a 1.1.0-preview.1)

La estable 1.0.0 parte de `main` tras el PR #94 e incorpora las correcciones de interfaz del PR #107, la corrección de randomización del PR #97 y el balance Eris-Ultranova como paquete opcional. La línea de acceso anticipado añade:

- Mega Ability Sync (Ultra Sol/Ultra Luna) y su tarjeta en Mecánicas y CRO.
- Perfiles compartibles (formato 1).
- Informes de capacidades de parches (solo lectura).
- Diagnóstico y comparador de megaevoluciones (solo lectura).
- Procedencia de perfiles en Definitive Game Data (`profileProvenance`).

Detalle y evidencia de cada función: [RELEASE_1.1.0_PREVIEW_1.md](RELEASE_1.1.0_PREVIEW_1.md) y [RELEASE_1.0.0.md](RELEASE_1.0.0.md).

## Cómo se mantienen

- **Todo cambio entra primero en `main`** mediante PR con el CI aprobado.
- **Una corrección que también afecte a la estable** se lleva a `release/1.0` con `git cherry-pick -x` en un PR propio contra esa rama, con su CI. Así no se duplica el trabajo: se escribe una vez y se aplica en las dos líneas.
- **Una función nueva no entra en la estable.** Cuando las funciones de acceso anticipado tengan evidencia suficiente, se publica la siguiente estable (`1.1.0`) desde `main` y se abre su rama de mantenimiento (`release/1.1`).
- Cada número de versión corresponde a un único paquete. Una corrección de la estable sube el parche (`1.0.1`); una nueva compilación de acceso anticipado sube el número de preview (`1.1.0-preview.2`).
- Los dos paquetes se generan con `tools/Publish-Preview.ps1` desde un checkout limpio del commit exacto; cada uno incluye `VERSION.txt`, `BUILD_PROVENANCE.json`, `SHA256SUMS.txt` y sus notas.

## Licencia y acceso anticipado

El acceso anticipado se refiere al momento en que se entrega una compilación. No cambia los derechos de quien la recibe: la GPLv3 le permite usar, estudiar, modificar y redistribuir el programa, y recibir el código fuente correspondiente. El código de las dos líneas está en este repositorio público, y cada entrega incluye además el código fuente del commit compilado. No hay activación, cuentas, DRM ni funciones bloqueadas.
