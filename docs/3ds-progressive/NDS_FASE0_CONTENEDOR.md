# NDS fase 0: contenedor `.nds`, NARC e identidad — 2026-10-30

Base para el soporte de juegos de Nintendo DS (Gen4 y Gen5). Esta fase **no randomiza ni edita datos de juego**: solo lee y reconstruye el contenedor sin pérdidas. Código en `pk3DS.Core/NDS/`.

## Alcance

- `NdsRom`: cabecera (con CRC16), tabla de nombres (FNT) con subdirectorios, FAT y overlays sin nombre (`overlay/NNNN`). Lectura por ruta o por ID.
- `NdsRom.Save`: copia el original byte a byte y aplica solo los archivos reemplazados.
  - Si el archivo cabe en su hueco (hasta la siguiente región ocupada: cabecera, ARM9 + pie, ARM7, tablas de overlays, FNT, FAT, banner, otros archivos y regiones DSi), se escribe en su sitio y el resto del hueco se rellena con `0xFF`.
  - Si crece, se reubica en el espacio libre posterior a los datos, solo si en el original son `0xFF`; se actualizan FAT, fin del área NTR (`0x80`) y CRC de cabecera.
  - Sin espacio, se rechaza el guardado completo antes de escribir. Nunca escribe sobre el origen ni sobre una salida existente; usa un archivo `.partial` y lo mueve al terminar.
  - Los overlays se rechazan hasta que se actualice su tabla (tamaño comprimido y banderas) en una fase posterior.
- `Narc`: lectura y reconstrucción. Conserva la cabecera y la tabla de nombres (BTNF) originales; reconstruye BTAF y GMIF con el byte de relleno detectado en el original.
- `NdsIdentity`: perfiles por código de juego y revisión, y SHA-1 de los volcados originales aportados por el propietario. Un archivo con código admitido pero otro hash se reconoce como **no verificado**. Los romhacks tendrán un perfil propio más adelante.

| Perfil | Código / rev. | Generación | SHA-1 del volcado |
|---|---|---|---|
| Diamante (USA) | `ADAE` / 5 | 4 | `a46233d8b79a69ea87aa295a0efad5237d02841e` |
| Platino (USA) | `CPUE` / 1 | 4 | `0862ec35b24de5c7e2dcb88c9eea0873110d755c` |
| Oro HeartGold (USA) | `IPKE` / 0 | 4 | `4fcded0e2713dc03929845de631d0932ea2b5a37` |
| Negro (USA/EUR) | `IRBO` / 0 | 5 | `26ad0b9967aa279c4a266ee69f52b9b2332399a5` |
| Blanco (España) | `IRAS` / 0 | 5 | `4cc963ca925409ff0f12f3c417d1cbe1094bf934` |
| Blanco 2 (USA/EUR) | `IRDO` / 0 | 5 | `b5d7490be7b415b8f1e672a53e978a9cc667e56a` |

Las cabeceras de los seis tienen CRC válido. Los SHA-1 identifican los volcados del propietario; no se han contrastado con la base No-Intro.

## Juegos DSi-enhanced (Negro, Blanco, Blanco 2)

- El área DSi (`arm9i`, `arm7i`) está **después** del área NTR, y las tablas hash DSi (sector y bloque) están **dentro** del área NTR, a continuación del último archivo. Ese espacio no se usa como libre.
- Espacio libre para reubicar: entre el fin del área NTR y `arm9i` (≈296 KB en Negro, ≈140 KB en Blanco ES, ≈150 KB en Blanco 2). En Gen4, hasta el final del volcado.
- Cualquier cambio invalida los hashes DSi y la firma de la cabecera, que no se pueden regenerar. **El resultado funciona en modo DS y en emuladores, no en modo DSi.** Mismo límite que Universal Pokemon Randomizer ZX.

## Validación

- `--nds-only` (sintético, en el CI): 16 comprobaciones sobre una ROM y un NARC construidos en memoria: nombres anidados, guardado idéntico, escritura en sitio, relleno al encoger, reubicación con actualización de cabecera, rechazos (sin espacio, overlays, sobrescribir origen o salida, CRC incorrecto, NARC truncado).
- `--nds-roundtrip-probe <salida> <rom...>` sobre los seis volcados originales: 39/39.
  - Los 1.454 NARC (838.126 miembros) se reconstruyen byte a byte.
  - Guardar sin cambios reproduce el SHA del original en los seis juegos.
  - Un cambio en sitio y una reubicación conservan todos los demás archivos; en los DSi, el área DSi queda intacta.
  - Los originales no cambian y no quedan archivos en la salida.

## Referencias

Formatos según GBATEK y Universal Pokemon Randomizer ZX (Ajarmar/universal-pokemon-randomizer-zx, commit `7f00eb866ed35c8fe3963f078b6a2e0979dc2b8c`), GPLv3 como este proyecto; licencia en `docs/third-party/UPR-ZX-LICENSE.txt`. La implementación es propia: a diferencia de UPR-ZX, no reconstruye todo el diseño de la ROM, sino que conserva el original y el área DSi.

## Siguiente fase

Gen5 (Negro y Blanco ES) en solo lectura: overlays con BLZ, textos cifrados de Gen5, Pokémon, entrenadores, encuentros, objetos e iniciales.
