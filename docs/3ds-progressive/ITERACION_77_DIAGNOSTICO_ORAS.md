# Iteración 77 · Diagnóstico de títulos ORAS y arranque Gen6

## Corrección implementada

El empaquetado ExeFS normaliza `code.bin`, `.code.bin`, `code` y `.code` a `.code.bin` únicamente en la copia temporal. El empaquetador elimina `.bin` y produce la entrada obligatoria `.code`. Anteriormente, una extracción Gen6 con `code.bin` producía `code` y Lime3DS rechazaba la ROM antes de cargarla. Se rechazan entradas duplicadas que resuelvan al mismo nombre. Los nombres y bytes del proyecto fuente se conservan.

El catálogo de imágenes Gen6 identifica el registro GARC, los diseños, las animaciones y las texturas reales. ORAS muestra idiomas completos y distingue el recurso japonés sin sufijo.

## Evidencia de diagnóstico

Se creó una copia independiente de Pokémon Rubí Omega y se marcaron 30 texturas de logos de los 16 recursos de título con un rectángulo magenta TEST 77. No se modificaron las carpetas originales del juego. La primera ROM de diagnóstico evidenció el defecto ExeFS anterior. La segunda ROM, con la entrada y el hash de cabecera corregidos, arranca en una instalación portátil aislada de Lime3DS.

Archivo de diagnóstico válido: `D:\Progra\DefinitiveRandomizer-validation\Diagnostico-ORAS-77\OmegaRuby-diagnostico-logo-77-corregido.3ds`.

SHA-256: `D9E25B19BEB8FEEA4FF524190CC26DC84432F78D8710D59340E3949D3304B125`.

El usuario aportó una captura que confirma ambos marcadores sobre el logo español en la pantalla PULSA START. Esta captura no verifica todavía el logo durante la presentación de Abedul. El usuario controla la copia aislada para avanzar hasta esa escena. No se declara resuelta la localización de un posible recurso adicional.

La revisión del vídeo ORAS `m/ay.moflex` mostró una secuencia de combate, sin el logo moderno completo. El paquete `a/2/6/9` contiene recursos de la introducción de Abedul/PokéNav, pero las texturas inspeccionadas no identificaron el logo solicitado. No se añadieron recursos ajenos como si resolvieran el problema.

## Validación

- Compilación Debug: 0 errores, 79 advertencias existentes.
- Reconstrucción/cache ExeFS: 10 comprobaciones superadas, incluyendo nombre .code, preservación de fuente y rechazo de duplicados.
- Editor de títulos Gen6: 29 comprobaciones superadas.
- Suite completa: 3940 comprobaciones superadas con datos sintéticos de XY, ORAS, SM y USUM.

No hay commit, push ni publicación. La copia candidata 76 no se ha sustituido durante este diagnóstico.


- Auditoría real XY/ORAS: lectura, decodificación y guardado en copias temporales aprobados para Pokémon X, Y, Rubí Omega y Zafiro Alfa; SHA-256 de los originales sin cambios.
