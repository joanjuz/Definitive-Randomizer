# Auditoría de permisos de memoria del código ARM (USUM) — 2026-11-01

El fallo de Learn.asm (guardaba un puntero en `.text`) no se notaba en Citra, pero en una consola real es un fallo de acceso. Esta auditoría busca el mismo tipo de error en todo el código que inyecta el randomizer.

## Herramienta

`tools/research/arm_memory_audit/audit_arm_memory.py`: compara una extracción original con una randomizada, localiza el código cambiado en `code.bin` y en cada CRO, y lo comprueba con los permisos reales:
- `.text` RX;
- `.rodata` R;
- `.data` y `.bss` RW.

Funcionamiento:
- **Pasada estática:** sigue las constantes y comprueba a dónde escribe cada store y a dónde salta cada rama.
- **Pasada dinámica:** ejecuta cada payload en Unicorn con esos permisos.
- **Autoprueba:** `--self-test` comprueba con payloads sintéticos que una escritura en `.text` o en `.rodata`, y una llamada a `.data`, se detectan, y que una escritura en `.bss` no se marca.

Modelo de memoria, uso y límites: [README](../../tools/research/arm_memory_audit/README.md).

Nueva opción del runner `--arm-audit-export <extracción> <salida>`: crea una extracción mínima nueva (exheader, `code.bin` y todos los CRO) y aplica el recordador actual a la copia de `code.bin`. Así se audita lo que genera la versión actual sobre una ROM ya randomizada.

## Resultados

Se usaron cuatro ROM randomizadas por testers y las extracciones originales de Ultra Sol y Ultra Luna:
- **Crash:** Ultra Sol.
- **Batch01, TierraPlanta y Zidra:** Ultra Luna. Al principio se compararon con Ultra Sol por error; desde entonces la herramienta rechaza una pareja con distinto Title ID.

| Extracción | Código cambiado | Resultado |
|---|---|---|
| Las cuatro de testers, tal como se generaron | `code.bin` (CAFE.asm, Learn.asm, gancho de intercambios), Battle.cro (mecánicas y level caps), Shop.cro (compra única) | **1 violación:** Learn.asm escribe en `0x5B9D00`, dentro de `.text`. Se detecta en la pasada estática y en la dinámica. |
| Las mismas, re-parcheadas con 0.11 | igual | Sin violaciones. El puntero queda en `.bss` (`0x6D3FFC`). |
| Proyecto de trabajo de Ultra Sol del propietario, re-parcheado con 0.11 | `code.bin`, Battle.cro (21 KB) y Shop.cro (28 KB, con tiendas ampliadas) | Sin violaciones |
| Ultra Luna sin modificar con el recordador de 0.11 | `code.bin` | Sin violaciones |

Coberturas de la pasada estática:
- **Battle.cro:** 735 de 3.503 palabras cambiadas.
- **Shop.cro:** 1.133 de 1.190.
- **`code.bin`:** 84 de 117.

Las palabras no visitadas no son instrucciones incondicionales: son tablas y literales (la tabla de level caps, las de mecánicas y los rangos de la cafetería).

**Observación sin fallo.** La función de level caps de Battle.cro está después del tamaño declarado de `.text`, en el hueco antes de `.rodata`. Ese hueco forma parte de la región de código de la cabecera, que ldr:ro mapea como ejecutable, así que no es un error.

## Límites

- No hay ROM randomizadas de Sol/Luna para comparar. Los ajustes Gen7 nuevos de esos juegos (obediencia, Megas) solo cambian datos.
- La pasada dinámica no reproduce el estado del juego, y la estática solo resuelve direcciones constantes. Un acceso calculado a partir de datos de la partida puede quedar sin comprobar.
- El modelo de permisos de los CRO es el de ldr:ro según la cabecera. No se verificó en consola.
