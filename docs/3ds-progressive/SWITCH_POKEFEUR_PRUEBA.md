# Prueba aislada de PokeFeur — Espada 1.3.2 / Ryujinx

22/10/2026. Autorizada por el usuario tras comparar el runtime propio y el repositorio de referencia. No modifica la aplicación, no integra un backend nuevo todavía y no hay commit/push.

Se utiliza el runtime PUBLICADO de JonatanUHC, sin modificarlo ni ejecutar su generador PowerShell. Commit fijado: e69afd5b9d4e5f892237bb7fb30aed10101d9c0a. Origen: https://github.com/JonatanUHC/PokeFeurRandomSWSH ; descripción: https://gamebanana.com/mods/674097 . Licencia GPL-3.0 copiada para consulta; los artefactos de terceros permanecen en temporal y carpeta privada de mods, no se añadieron al repositorio de la aplicación ni a una release. No se afirma disponer del C++ completo de este runtime.

## Archivos y configuración instalados

Carpeta: `C:\Users\Joanj\AppData\Roaming\Ryujinx\mods\contents\0100abf008968000\DR-Prueba-PokeFeur-Combate-20261022`.

- exefs/subsdk9: assets/exlaunch/0100ABF008968000/subsdk9_ryujinx del commit indicado.
- exefs/main.npdm: la variante main.npdm_ryujinx publicada para el mismo título. Se comprobó META/ACI0, título correcto y flags de arquitectura 0x07. Amplía capacidades SVC/FS respecto al original para el runtime; solo se sustituye metadata dentro del mod, nunca en la extracción original. No se amplían permisos de Windows ni se modifica el ejecutable de Ryujinx.
- No se incluye exefs/main: usa el código original del juego.
- config-prueba.toml, LEEME, licencia y manifiesto con hashes/procedencia.
- Configuración activa nueva: `C:\Users\Joanj\AppData\Roaming\Ryujinx\sdcard\config\swsh-mods-exl\config.toml`. Se exigió que no existiese para no sobrescribir ajustes previos. Su copia del mod es idéntica byte a byte. Es una configuración compartida por el runtime PokeFeur si se usa en otros mods; los módulos DR propios no la leen.

[randomizer]: active=true, visible_mode="vanilla", battle_mode="random", battle_pool=[1,4,7], seed=20261022, reset_discoveries=false. Todos los demás active=false, incluidos runtime_pc y runtime_status (estos tienen default true en el contrato y se desactivaron explícitamente). EXP Share permanece enabled=true como vanilla, multiplicador 1.0 desactivado. No se copiaron extras AMX, textos, scripts PC ni tablas ROMFS: sus funciones están apagadas y el generador oficial los condiciona a esas funciones.

Pool deliberadamente limitado a Bulbasaur, Charmander y Squirtle para que un Yamper no pueda coincidir por azar con el resultado. Se comprobó que los tres existen en la tabla personal original. Esto valida la mecánica antes de habilitar un pool completo; no es aún una configuración final del producto.

## Validaciones reales

- Lector LibHac del Ryujinx del usuario: tres segmentos del runtime publicado aceptados con Success, incluidos hashes y descompresión. Parsear el módulo como datos no lo ejecuta.
- 68/68 preimágenes del contrato oficial de Espada coinciden con la extracción facilitada.
- TOML analizado con tomllib; modos correctos, solo randomizer activo, funciones no relacionadas desactivadas y pool presente.
- Hashes de archivos instalados cotejados; manifest y configuración SD reabiertos. Hash del runtime: AB13EC3B8693EF475E5FD81221095D6862D3C34E179399E6B25F351FD7563331.
- Guardados, extracción original, mods anteriores y estados de activación intactos. No se ejecutó el emulador ni se comprobó una batalla real por herramientas. No hay cambios C#/XAML que compilar en esta entrega.

## Prueba manual pendiente

Cerrar completamente Ryujinx. Desactivar TODOS los DR-Prueba-Combate-Sorpresa v1-v7 y otros ExeFS. Activar únicamente DR-Prueba-PokeFeur-Combate-20261022 entre los ExeFS; para la primera comprobación es preferible usar solo este mod. No usar estado rápido. Tocar Yamper u otro visible que no sea 1/4/7. Debe seguir viéndose ese Pokémon fuera y aparecer uno de los tres starters en combate. Revisar luchar, movimientos, menú de equipo y capturar; la especie capturada debe ser la del combate. Cerrar normalmente para conservar log.

No se garantiza que el runtime externo excluya siempre la especie visible cuando se habilite un pool completo: esa propiedad y el alcance a entrenadores, eventos y otros encuentros deben verificarse antes de integrarlo. Si no funciona, registrar carga de subsdk9, lectura de config y errores del runtime; no asumir paridad a partir de su descripción.

Para revertir la prueba: desactivar esta carpeta de mod. La configuración de SD queda sin lector cuando el runtime no está activo. No se borró ni sustituyó ninguna variante previa.
