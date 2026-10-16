# Iteración 58 — títulos Gen6 y replay nativo de estáticos Gen7

Trabajo local en `feature/3ds-progressive-ui`. Sin commits, push ni publicación. Los 12 archivos protegidos de Wild, RecordEditor y notas personales conservan sus SHA-256 anteriores. No se modificó Eris-Ultranova.

## Implementación

- Pantallas de título WPF para XY/ORAS: selección de juego e idioma, vista previa, importación PNG/BCLIM, exportación y guardado con historial. Carga y reconstrucción fuera del hilo de UI; bloquea navegación durante trabajo y avisa de imágenes pendientes. Oculto en Gen7.
- DARC estricto: conserva prefijo, nombres, índices de carpetas y recursos ajenos. Corrige el problema de offsets del editor antiguo al sustituir una textura de tamaño distinto; desplazamientos alineados y relectura de cada archivo antes de guardar. No reutiliza `DARC.InsertFile`.
- GARC de títulos: conserva los registros no editados en su representación comprimida original, verifica los editados tras recompresión y rechaza carpetas/subarchivos no soportados. SHA-256 contra cambios externos antes del commit.
- Decodificación ETC1/ETC1A4 administrada: elimina la dependencia de la DLL que devolvía imágenes nulas. Vectores independientes para colores, selectores, delta diferencial y alpha; también beneficia las herramientas gráficas existentes.
- Global/Batch SM/USUM: el snapshot completo de estáticos se reproduce mediante el servicio WPF, incluyendo suma de niveles, filtros Gifts/Static/Trades, opciones BST y demás valores nativos. Iniciales usa solo los primeros tres regalos. Configuración estricta, alcance Todos y una acción por perfil; no ignora acciones legacy combinadas de intercambios/Totem.
- Recetas completadas desde snapshots nativos llevan `NativeOnly`: añadir módulos legacy sin su configuración original provoca un rechazo previo a la ejecución. Las recetas originales completas mantienen su ruta original cuando no seleccionan el snapshot nativo correspondiente.

## Verificación local

- Debug: 0 errores y 79 advertencias heredadas.
- Suite completa: **2629 regresiones** con datos sintéticos XY, ORAS, SM y USUM, incluidos UI oscuro, captura/restauración de tutores ORAS, replay de estáticos e iniciales, rechazos de configuración y conservación de datos.
- X/Y: 14 archivos DARC y 98 imágenes por GARC, decodificadas realmente. OR/AS: 16 archivos DARC y 174 imágenes por GARC. Lectura y reemplazo RGBA8 sobre copias temporales de los cuatro GARC reales; vecinos DARC y todos los registros GARC ajenos verificados. Originales con SHA-256 intactos.
- Captura sintética del editor de títulos revisada visualmente; no se incluyen imágenes ni binarios de los juegos en la documentación del repositorio.
- Flujo completo de estáticos Gen7: consultar el informe `statics7-fullbatch.log` adjunto para el resultado del ensayo aislado sobre una copia de UltraSol. El proceso original de Batch no se ejecuta sobre la ROM del usuario.
- Comparación adicional independiente: los 750 archivos RomFS/ExeFS de UltraSol original coinciden por SHA-256 con la copia de entrada intacta; informe JSON adjunto.
- `git diff --check` y preservación de 12 archivos protegidos verificados.

Durante el desarrollo se corrigió una conversión de encoding que alteró claves de títulos Global. La suite se repitió después de corregirla; el informe final corresponde a la versión corregida.

La revisión automática inicialmente rechazó la prueba de títulos por interpretar los mensajes de escritura de fixtures como escrituras en los juegos. Se verificó el código y se añadió guardia de destino TEMP con ruta explícita. También exigió copia de entrada independiente para el pipeline de rebuild; se siguió esa alternativa. No quedó una solicitud de aprobación pendiente.

## Pendientes de cierre

Esta iteración no declara completa la migración ni una release estable. Continúan las proyecciones Global/Batch de otros módulos y sus combinaciones avanzadas; espejo de textos localizado de estáticos y perfiles separados Gen6; catálogos completos de scripts/objetos y opciones O-Power; compatibilidad de formatos antiguos de backups/templates; escritura de otros formatos gráficos; matriz conjunta de lotes/rebuilds, empaquetado final y validación en juego. Los ensayos reales aquí descritos cubren acciones concretas, no todas las combinaciones o versiones. SM carece todavía de una referencia descomprimida usada para este ensayo real; su servicio se probó con datos sintéticos.

## Referencias de formato

ETC1: [especificación de Khronos](https://chromium.googlesource.com/external/github.com/KhronosGroup/OpenGL-Registry.git/+/1f8f4efc1d610052492e1099690822c68e4efb20/extensions/OES/OES_compressed_ETC1_RGB8_texture.txt). La organización en tiles y orientaciones se integra con `XLIMOrienter` del Core local. Índices de títulos y disposición DARC contrastados con `TitleScreenEditor6.cs`, `DARC.cs` y archivos de referencia locales.
