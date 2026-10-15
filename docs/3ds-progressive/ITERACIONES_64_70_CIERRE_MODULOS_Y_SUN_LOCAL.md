# Iteraciones 64–70 — perfiles, intercambios, tiendas SM y gráficos

Trabajo local en `feature/3ds-progressive-ui`, sin commits, push, publicación ni cambios en Eris-Ultranova. **La integración global todavía no se declara completa ni estable.**

## Cambios integrados

- Estáticos Gen6: perfiles independientes para estáticos, regalos e iniciales, con filtros, niveles y atributos separados. Se calculan en una propuesta conjunta, con sincronización del selector de iniciales. Global/Batch conserva todas las opciones. Los snapshots antiguos compartidos reciben únicamente los campos nuevos; los snapshots nuevos incompletos se rechazan.
- Intercambios Gen6: snapshot nativo ejecutable sin RandSettings, semilla reproducible, acciones combinadas ejecutadas una sola vez y compatibilidad con recetas antiguas. La fábrica incorpora intercambios cuando se seleccionan o se recuerda su configuración.
- Tiendas SM: 24 tiendas normales y seis BP, edición manual, randomización, caramelos, plumas/objetos EV con sus umbrales de pruebas, megapiedras gratuitas y filtro de Cápsula Habilidad. Conserva tamaños fijos y regeneración de hashes CRO. Intercambio de opciones Global/Batch y etiquetas Sol/Luna. Expansión y vitaminas 252 EV conservan su comportamiento USUM.
- Gráficos: escritura de los catorce formatos XLIM en BCLIM/BFLIM, encoder administrado ETC1/ETC1A4, lectura correcta de RGB24 y canales de cuatro bits, transparencia RGBA8 y validación de longitudes. Paleta Gen6 de cuatro/ocho bits, hasta 70 entradas incluida transparencia, con tiles cuadrados, cropping rectangular y rechazo de colores/alfa incompatibles. La compresión se ejecuta fuera del hilo WPF. El primer valor RGB5A1 que coincide con el marcador de paleta exige BFLIM/RGBA8 para impedir ambigüedad.
- SM inicia el balance de movimientos con mecánicas CRO y descripciones desactivadas. Conserva la opción para CSV compatibles, con validación antes de guardar; no se afirma que el template de combate completo sea compatible con Sun.

## Verificación local

- Debug final: cero errores, 79 advertencias heredadas.
- Suite completa final: **3734 comprobaciones** XY/ORAS/SM/USUM, incluidos fixtures y recorridos WPF.
- Intercambios: 73 comprobaciones específicas, con paridad de instrucciones en copias privadas de los code.bin reales X/Y/OR/AS y originales intactos.
- Lectura/propuesta de iniciales en X/Y/OR/AS/UltraSol/UltraLuna: los dos archivos principales y el inventario completo conservados. Gen6 ejercita perfiles independientes; no Apply ni rebuild en esas referencias.
- El ejecutor del paquete local cargó el puente nativo y produjo exactamente los tres cambios de intercambios previstos en una copia exclusiva de X. Los 664 hashes de su copia de entrada permanecieron intactos.
- Se encontró y descomprimió el archivo Sun del usuario en una carpeta independiente. La aplicación extrajo 672 archivos y verificó el SHA-256 del contenedor. Archivo comprimido original preservado.
- Sun real: el primer diagnóstico detectó un byte desplazado al copiar la firma de tiendas. Se corrigió contra el editor vanilla, y se añadió una referencia independiente al fixture; no se debilitó la detección.
- Sun real: preflight → proceso Batch → presets SM → Trimmed3DS → extracción, con Shop.cro y objetos idénticos a la propuesta manual. **668 hashes de RomFS/ExeFS e inventario de entrada intactos.** Randomización, BP y filtros reproducen la semilla en bytes aislados.
- Sun real: 24/6 tiendas reconocidas; balance de datos propone 110 cambios y cero archivos extra. Lectura de Pickup, tipos, shiny, entrenadores, campo y economía pasa. 126 huellas e inventario conservados en la auditoría de lectura.
- `git diff --check` se ejecuta al cierre. Evidencias bajo `ITERACIONES_64_70_PRUEBAS`.

## Preservación

Los once archivos protegidos restantes siguen coincidiendo con el baseline inicial. RecordEditorView.xaml.cs mantiene exclusivamente la extensión de captura documentada en la iteración 63; se verifica contra aquel respaldo. No se cambiaron en estas iteraciones servicios Wild, reglas SOS ni selección. Archivos personales y cambios previos conservados.

## Límites y pendientes confirmados

1. El template de combate Gen7 completo rechaza ocho perfiles sobre Battle.cro de Sun real: Nightmare, Water/Mud Sport, Wish, Meditate, Fairy Lock, Lucky Chant, Mist y Brutal Swing. No están portados/verificados para esta fuente SM. El rechazo ocurre antes de guardar; el balance de datos funciona. Esto impide declarar cerrada la integración de esas mecánicas en SM.
2. No se encontró una fuente Moon normal entre los archivos disponibles; UltraMoon es otro juego. Falta cerrar cobertura individual de los ocho títulos y combinaciones de módulos/contenedores de la matriz de aceptación, incluyendo los cambios recientes. Una prueba de tiendas o intercambios no representa todas las opciones.
3. Falta el cierre consolidado de toda la matriz histórica y la validación manual completa de interfaz y juego. Los estados antiguos del CSV son históricos; las filas nuevas no prueban por sí solas paridad global.

Paquete Debug local de revisión: `D:\Progra\DefinitiveRandomizer-validation\20261014-iteration70-local-package`. Requiere .NET Desktop Runtime 10. No es una release estable ni autoriza a dar por probadas todas las funciones en juego. No incluye ROMs.
