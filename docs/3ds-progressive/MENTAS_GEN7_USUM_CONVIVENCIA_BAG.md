# Mentas USUM: convivencia con otros parches de `Bag.cro`

Hasta ahora las mentas solo se instalaban si `Bag.cro` era idéntico byte a byte al original y, una vez instaladas, exigían que todo el archivo siguiera igual. Cualquier otro parche de `Bag.cro` las volvía incompatibles, en cualquier orden.

Ahora las mentas exigen solo aquello de lo que dependen (`UsumBagAnchors`):

- Las ventanas alrededor de sus cuatro puntos de enganche (`0xC318`, `0xDDC8`, `0xC358`, `0x80DC`), con el propio punto excluido de la huella.
- El comienzo de cada función de `Bag.cro` a la que salta su rutina (`0x80A4`, `0x8450`, `0x8460`, `0xC85C`, `0xC8E4`).
- El símbolo importado detrás de cada stub que llama (`Sound::PlaySE`, `G2DUtil::GetStringExpand`, `G2DUtil::SetRegisterPokeNickName`, `CoreParam::IsEgg`, `PokeTool::ITEM_RCV_RecoverCheck`), leído de la tabla de importaciones con nombre.

Reglas:

| Situación | Antes | Ahora |
|---|---|---|
| `Bag.cro` original | Se puede instalar | Igual |
| Otro parche que no toca las anclas, hashes CRO correctos, puntos de mentas originales | Incompatible | Se puede instalar |
| Mentas instaladas y después otro parche que no toca las anclas | Modificado | Instalado («con otras modificaciones compatibles») |
| Otro parche sobre un ancla o un punto de mentas | Incompatible / Modificado | Igual: se rechaza y se indica el motivo |
| Hashes CRO desfasados | Incompatible / Modificado | Igual |

Las anclas son huellas del `Bag.cro` acreditado (`BD97393E…`, idéntico en Ultra Sol y Ultra Luna) y solo se aplican a ese perfil. Los perfiles sintéticos de las pruebas mantienen la regla estricta. El digest que guarda la instalación no cambia de formato: las instalaciones anteriores se siguen reconociendo igual.

Contrato para nuevos parches de `Bag.cro`: no escribir dentro de `UsumBagAnchors.MintRanges`, ampliar el CRO con `CroPatchSession` y dejar los hashes CRO y la entrada de la CRR coherentes. Así conviven con las mentas sin cambios adicionales.

No cambia el servicio de mentas ni su catálogo; el cambio está en `UsumNatureMintPatch.Inspect` y en el archivo nuevo `UsumBagAnchors`.

## Pruebas

- `--usum-mints-only`, `--usum-mints-ui-only`, `--usum-mint-icons-only`, `--usum-mints-distribution-only`: mismos recuentos y sin fallos (98, 31, 88, 128).
- `--usum-bag-coexistence-real <RomFS USUM original> [<Bag.cro con otro parche>]` (copia real, en memoria, fuera del CI): 22 comprobaciones para US y UM.
  - Un parche ajeno antes de las mentas y otro después.
  - Rechazo de parches sobre las dependencias o sobre un punto de mentas, y de hashes desfasados.
  - Las mentas sobre el `Bag.cro` del prototipo de caramelos de uso múltiple.
- No se ha probado en el juego una ROM con mentas y otro parche de `Bag.cro` a la vez.
