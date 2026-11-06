# Mentas USUM: procedencia y licencia

Referencia fijada: [adhfult/pk3DS-but-mostly-for-USUM, 274d836e88f840c9c326a569198ebff8031969d6](https://github.com/adhfult/pk3DS-but-mostly-for-USUM/tree/274d836e88f840c9c326a569198ebff8031969d6).

Se adaptó exclusivamente la ruta de cambio de naturaleza, las convenciones de sus cuatro hooks y el mapa de 21 mentas de `patch-packages/AbilityPatchAndNatureMints.json`. Se consultaron `PatchPackageInstaller.cs` e `ItemPocketTable.cs` para contrastar los contratos. No se incorporó el instalador general, su payload completo, Ability Patch, Nature Shifter ni otros efectos. El README del fork acredita pk3DS/Kaphotics y la investigación ARM/CRO de ABZB; no se atribuye cada instrucción a una persona concreta.

El fork declara GPL versión 3 en `LICENSE.md`. La adaptación ARM y su plantilla generada se distribuyen bajo GPL-3.0-only, como parte de este proyecto GPLv3; no se presentan como trabajo de sala limpia ni como forma de eludir la licencia. Se conserva el texto completo de la licencia en [UsumNatureMints-LICENSE.txt](UsumNatureMints-LICENSE.txt).

Fuente correspondiente: `native/3ds/nature-mints/mints.s`, su generador, requisitos fijados y el código C# de `UsumNatureMintPatch`, catálogo e instalador. La fuente ensamblador legible genera la plantilla de 1024 bytes; la reubicación y el mapa se completan desde C#. Al distribuir una versión binaria que incorpore esta adaptación, debe facilitarse su fuente correspondiente conforme a GPLv3, conservar estos avisos y la licencia e identificar las modificaciones. Esta entrega es un PR de código, sin release nueva.

Cambios de Definitive Randomizer (5/11/2026): rutina solo de mentas; criterio compartido para disponibilidad y uso; rechazo de huevos y misma naturaleza; perfiles US/UM por huella sin sondeo +8; asignación CRO, reubicación de saltos y registro verificable; conservación de filas de texto ajenas; instalación transaccional/historial y editor de tiendas existente.

No se distribuyen CRO, code.bin, cabeceras, textos originales, gráficos ni ROMs de los juegos. Los perfiles contienen hashes y datos estructurales, no los archivos de origen. Los fixtures automatizados son sintéticos; las copias comerciales de validación son privadas y están fuera de Git.

## Iconos opcionales de fase 1.1

Los gráficos existentes se adquieren localmente desde PokeAPI/sprites, commit `1b3b7c0a538dd2226ae08d0d2f8955125075bbd4`. Su licencia atribuye las imágenes a The Pokémon Company; el CC0 del repositorio no se interpreta como una cesión de esos derechos. No se redistribuyen imágenes oficiales, texturas convertidas ni recursos del juego en este repositorio o en el paquete. La conversión utiliza el codec gráfico existente y el nuevo instalador propio; no se copió código de PokeAPI o Bulbagarden. Fuentes, límites y atribución: [fase 1.1](../3ds-progressive/MENTAS_GEN7_USUM_ICONOS_FASE11.md).
