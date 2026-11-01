# Auditoría de permisos de memoria del código ARM inyectado (Gen7)

`audit_arm_memory.py` compara una extracción original con una randomizada y comprueba que el código que añaden los parches respeta los permisos que la 3DS da a cada región. Citra y otros emuladores no aplican esos permisos: una escritura en `.text` funciona en el emulador y falla en una consola real.

Solo lee las dos extracciones y escribe un informe JSON. No modifica ROMs, extracciones ni partidas.

## Requisitos

Python 3 y las versiones de `requirements.txt`, instaladas en una carpeta aislada:

```
python -m pip install --target D:/DependenciasArm -r tools/research/arm_memory_audit/requirements.txt
```

## Uso

```
python -I tools/research/arm_memory_audit/audit_arm_memory.py --original <extracción original> --patched <extracción randomizada> --output informe.json --dependencies D:/DependenciasArm
python -I tools/research/arm_memory_audit/audit_arm_memory.py --self-test --dependencies D:/DependenciasArm
```

Cada extracción necesita `exheader.bin` (o `DecryptedExHeader.bin`), `ExeFS/.code.bin` (o `ExtractedExeFS/code.bin`) descomprimido y los `.cro` de `RomFS` (o `ExtractedRomFS`). Las dos deben ser del mismo juego: se comprueba el Title ID.

Para auditar lo que genera la versión actual sobre una extracción, `--arm-audit-export <extracción> <salida>` del runner crea una extracción mínima nueva y aplica el recordador actual a la copia de `code.bin`.

Termina con código 0 si no hay violaciones y 1 si las hay.

## Modelo de memoria

- **code.bin:** `.text` RX, `.rodata` R, `.data` y `.bss` RW. Las direcciones y tamaños salen del exheader, redondeados a páginas.
- **CRO:** la región de código de la cabecera (`.text` y `.rodata`, offsets `0xB0`/`0xB4`) es RX. La cabecera y las tablas son R. `.data` y `.bss` están en el búfer aparte que ldr:ro da al módulo, y son RW. Se aplican las reubicaciones internas.

## Qué analiza

1. **Bytes cambiados.** Se comparan por segmento. Si un segmento crece por dentro (una instrucción insertada desplaza el resto), se alinean las palabras de las dos versiones, para no tomar como nuevo el código que solo se movió. El `.text` de los CRO se compara hasta el siguiente segmento, porque los parches también ponen código en el hueco tras el tamaño declarado.
2. **Pasada estática.** Recorre el código cambiado desde cada destino de salto y cada inicio de tramo, propagando constantes (literales, `MOVW`/`MOVT`, `ADR`, reubicaciones). Informa de cada escritura cuya dirección se conoce y de cada salto cuyo destino se conoce. Después barre las palabras no visitadas que son instrucciones incondicionales, para cubrir funciones a las que solo se llega por puntero. La cobertura del informe cuenta palabras visitadas sobre palabras cambiadas; las no visitadas son tablas y literales.
3. **Pasada dinámica.** Ejecuta cada entrada con Unicorn y los permisos reales. Los registros apuntan a un heap sintético RW. Una llamada fuera del payload devuelve un puntero del heap, y un salto de vuelta al código del juego termina la ejecución. Solo cuenta como violación un fallo dentro de un módulo; los saltos por punteros sintéticos no.

Las notas («code … after the declared .text size») indican código que se ejecuta desde la región de código del CRO pero después del tamaño declarado de `.text`. Es ejecutable, así que no es un fallo.

## Límites

- La pasada dinámica no reproduce el estado real del juego: un camino que depende de datos concretos puede no ejecutarse. La estática solo resuelve direcciones calculadas a partir de constantes.
- Asume código ARM; las entradas Thumb solo se siguen si se llega a ellas con `BLX`.
- No modela el `.data` y el `.bss` de otros módulos que no cambiaron.
