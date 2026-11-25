# Caramelos Raros personalizados (Rubí Omega): procedencia y licencia

Referencia: «PARCHE: CARAMELOS RAROS CON CANTIDAD», de **alvarocm21**, para Pokémon Rubí Omega (`000400000011C400`), distribuido como mod LayeredFS (`exefs/code.ips`, `romfs/DllBag.cro`, `romfs/.crr/static.crr`). Su `LEEME.txt` dice: «Este parche es de libre uso: puedes usarlo, modificarlo y compartirlo libremente, siempre que se den créditos a su autor, alvarocm21».

Qué se tomó: el mecanismo (cuatro enlaces en `DllBag.cro`, una rutina en el relleno de `.text` de `code.bin`, un desvío en el bucle de aprendizaje de movimientos y 12 bytes de estado) y las instrucciones de la parte **alcanzable** de la rutina. Se reconstruyeron como fuente ensamblador legible en [`native/3ds/rare-candy-multiuse/candy.s`](../../native/3ds/rare-candy-multiuse/candy.s). El generador comprueba, en desarrollo, que esa fuente produce los mismos bytes que la referencia en esas direcciones. No se incorporaron los dos bloques no alcanzables que contiene la referencia (`0x579610–0x57968F` y `0x5797D8–0x579877`).

Qué es nuevo: el instalador nativo (`RareCandyMultiUsePatch`), que identifica el título y la revisión por huellas, comprueba cada punto antes de escribir, recalcula los hashes del CRO y la entrada de la CRR, detecta instalaciones parciales, conflictos y el propio parche de referencia instalado externamente, retira el parche restaurando los bytes exactos y se integra con el historial, el deshacer y Global Template.

La adaptación se distribuye bajo GPL-3.0-only como parte de este proyecto, conservando el crédito a alvarocm21.

No se distribuyen `code.bin`, `DllBag.cro`, `static.crr`, cabeceras ni ROM. El instalador contiene huellas SHA-256 y las 52 bytes de instrucciones originales de los puntos que modifica, necesarias para restaurarlos. Las pruebas automatizadas usan archivos sintéticos; las copias comerciales de validación son privadas y están fuera de Git.
