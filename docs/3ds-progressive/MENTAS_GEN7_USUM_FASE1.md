# Mentas Gen7 para Ultra Sol / Ultra Luna — fase 1

Estado: implementación en rama; validación jugable del propietario pendiente. No forma parte de una release nueva. El [plan previo](MENTAS_GEN7_USUM_PLAN_20261105.md) se conserva como investigación histórica; esta entrega implementa solo instalación y tienda manual, no sus fases de pools, Global/Batch o distribución general.

**Estas mentas modifican la naturaleza real del Pokémon. No funcionan como las mentas de generaciones posteriores, que mantienen la naturaleza original y modifican sus efectos estadísticos.** Se modifica también lo que el juego considera su naturaleza original, con las consecuencias de esa naturaleza en sus demás sistemas.

## Compatibilidad acreditada

El título se comprueba en la cabecera NCCH (`ncchheader.bin`, `HeaderNCCH0.bin` o `HeaderNCCH.bin`) y se contrasta con un perfil separado de ejecutable. La revisión se fija por su huella exacta, no por la carpeta ni por un número comercial inferido. No se anuncia soporte general de «todas las revisiones 1.x».

| Perfil | Title ID | SHA-256 de code.bin descomprimido |
|---|---|---|
| US-code-d2e9b4d9 | `00040000001B5000` | `d2e9b4d91d7b14bcef0c34d869c1da60ef85d98b1df3c34cbc42e42f54cfba27` |
| UM-code-a6aa17b6 | `00040000001B5100` | `a6aa17b6f409264f6ed15d51735e9638d3f44896c0cec0f05c39940b79fe30e7` |

Bag.cro inicial: SHA-256 `bd97393e3440a04ca6f61c98771c11e595c4ee649f9c4985e793a81e7721155c`, comprobado en ambos títulos, además de las cuatro firmas de hooks. Una revisión, code.bin previamente parcheado, Bag distinto o título sin acreditar bloquea la instalación. No existe opción de forzar ni carga de perfiles externos. No es compatible con Sol/Luna, Gen6, DS ni Switch. Otros parches que alteren code.bin quedan fuera de estos perfiles; no desactives las comprobaciones para combinarlos.

## Instalar y conseguirlas

1. Abre una **copia extraída destinada a edición** y respalda el proyecto y la partida.
2. En **Mecánicas y CRO → Mentas Gen7 — Ultra Sol / Ultra Luna**, comprueba el estado. La propuesta muestra los archivos; aún no escribe en el proyecto.
3. Confirma **Instalar 21 mentas**. Se preparan objetos, bolsillos, nombres/descripciones, mensaje y Bag.cro antes de la única transacción con CRR e historial.
4. Pulsa **Abrir Tiendas y BP**. En el editor existente, selecciona una tienda, añade o cambia una ranura y busca «Menta» (o «Mint» con textos ingleses). No hace falta recordar IDs.
5. Guarda la tienda tras su vista previa. Las tiendas normales usan el precio del **editor de objetos**; precio inicial 20 000, editable en múltiplos de 10. BP conserva sus precios por ranura y límites existentes.
6. Genera la ROM/modificación por el procedimiento existente. Si modificaste otros CRO, reconstruye su integridad CRO/CRR con la herramienta existente antes del rebuild. No se modifica una ROM de origen ni un guardado desde el instalador.

Se habilitan las 21 mentas: Huraña, Firme, Pícara, Audaz, Osada, Agitada, Floja, Plácida, Modesta, Afable, Alocada, Mansa, Serena, Amable, Cauta, Grosera, Miedosa, Activa, Alegre, Ingenua y Seria (los nombres siguen el banco de naturalezas del idioma español del proyecto). La neutral selecciona la naturaleza 12, Seria. El mapa nativo completo y los IDs fijos están en `UsumNatureMintCatalog`; no es una secuencia «posición + 1».

## Guardado, detección y deshacer

La instalación cambia 14 archivos: Bag.cro, tabla de objetos `a/0/1/9`, bolsillos `a/1/0/3`, los diez GARC de Game Text `a/0/3/0` a `a/0/3/9` y `.crr/static.crr`. La tabla de objetos tiene 960 registros y los bolsillos 480 bytes: no se amplía la partida ni se reserva hasta 1024 por suponerlo libre.

Los IDs se aceptan solo si conservan los registros acreditados y los bolsillos esperados. Las huellas de los 21 textos originales reservados se contrastaron en ambos juegos, diez idiomas y cuatro bancos; incluyen los nombres japoneses residuales de objetos antiguos. Un nombre inglés «???» nunca es el único criterio. Los textos ajenos conservan sus filas cifradas, espacios, comandos y metadatos. Solo se agrega el mensaje 159 si el banco de bolsa tiene las 159 filas originales; no se sobrescribe otro parche.

El estado se calcula desde los archivos actuales: **no instalado**, **instalado y reconocido**, **incompatible** o **parcialmente reconocido/modificado externamente**. El marcador del runtime por sí solo no basta: se comprueban cuerpo, digest, hooks, integridad CRO, registros, bolsillos, textos y entrada de Bag en CRR. Reabrir no depende del historial para detectar el parche. Reinstalar lo mismo no crea otra instalación; una propuesta antigua no puede aplicarse de nuevo.

Los cambios se registran en el historial y usan sus puntos de restauración. Para deshacer, restaura primero ediciones posteriores de objetos/textos/CRR que impidan restaurar el conjunto coherente. El editor refresca el catálogo al abrir Objetos/Tiendas. **Deshacer archivos no revierte la naturaleza ni el inventario que ya guardaste dentro de una partida**. Usa una partida de prueba o restaura su respaldo. Se conservan los límites reales de transacciones/recuperación; no se promete protección universal ante apagones o todos los sistemas de archivos.

## Límites de la fase 1

- No se añaden automáticamente mentas a tiendas, Recogida, recompensas, objetos de campo ni equipos. No cambian los pools ni sus semillas.
- Global Template y Batch no instalan este parche en esta fase. No uses una receta de tiendas con IDs de mentas en un proyecto que no las tenga instaladas; no se promete reproducción automática de esta instalación.
- Español e inglés tienen nombres/descripciones propios; otros idiomas reciben nombres/descripciones en inglés. Se conservan los demás textos originales. No se distribuyen iconos nuevos ni gráficos extraídos.
- No hay Ability Patch ni Nature Shifter. Huevos y naturaleza ya coincidente no deben consumir una menta; un éxito debe consumir una. Cancelar conserva el comportamiento nativo previo al handler.
- El runtime usa la ruta y las funciones nativas de los perfiles fijados. Las pruebas ARM con dependencias controladas y la reconstrucción no acreditan por sí solas consumo, mensajes, cifrado del guardado y persistencia dentro del emulador.

## Una prueba manual reproducible

En una copia y una partida respaldada, añade **Menta Firme (#114)**, **Menta Osada (#124)** y **Menta Seria (#840)** a la tienda que corresponda a tu progreso (el stock «Sin pruebas» solo se usa antes de superar pruebas), con precio 10 para facilitar la comprobación. Guarda, reabre los editores y reconstruye. Carga esa salida en el emulador; no reemplaces tu instalación principal.

1. Compra dos de cada una. Anota naturaleza, PS, habilidad, IV/EV, movimientos y objeto equipado del Pokémon de prueba.
2. Usa Firme: debe pasar a Firme, actualizar las estadísticas coherentemente y restar una unidad. Repetir Firme no tiene efecto y no resta otra. Cancela otra selección y comprueba el inventario; un huevo no debe aceptarla.
3. Usa Osada y Seria: cada éxito cambia la naturaleza real correspondiente y consume una. Comprueba que no cura ni revive y no cambia habilidad, IV/EV, movimientos ni objeto equipado; prueba también un Pokémon dañado o debilitado.
4. Guarda la partida, cierra el emulador y vuelve a cargarla. Las naturalezas y cantidades deben persistir; revisa equipo y cajas. Adjunta versión del juego/perfil, idioma y resultado de estos pasos, sin compartir ROMs ni guardados.

La aceptación jugable de fase 1 permanece pendiente hasta esa confirmación. Fuente reproducible, harness ARM y licencia: [runtime](../../native/3ds/nature-mints/README.md), [procedencia](../third-party/USUM_NATURE_MINTS.md).

## Evidencia de esta implementación

- Compilación del proyecto WPF y del runner: sin errores ni advertencias en el cierre.
- 89 checks sintéticos específicos: perfiles rechazados, mapa, registros, bolsillos, preservación de filas de texto, instalación, idempotencia, fallo transaccional inyectado, historial, deshacer, colisiones y escrituras concurrentes. Se conserva la lectura anterior del editor de objetos para Global/Batch; el refresco de nombres se limita a la interfaz de catálogos de mentas.
- 6 checks WPF fuera de pantalla, incluida la navegación real desde la tarjeta de Mecánicas y CRO al instalador. No equivalen a validación dentro del juego.
- 340 checks ARM ejecutan el payload generado/reubicado con dependencias nativas controladas para US y UM; plantilla reproducida desde el ensamblador.
- Regresión sintética general aprobada: 4 136 checks. No se repitió una suite completa ya aprobada; se corrigió una regresión detectada durante el desarrollo antes de obtener esta pasada aprobada.
- Copias privadas completas de Ultra Sol y Ultra Luna: instalación y reapertura, deshacer exacto de 14 archivos, reinstalación, las 21 mentas guardadas en una tienda y precios editables. Fuentes originales intactas. No se distribuyen esas copias ni sus datos.
- Una reconstrucción Full3DS de la copia de Ultra Sol y reextracción: los 750 archivos coinciden byte a byte; el parche sigue reconocido. Esta comprobación no acredita una partida en el emulador ni se extrapola a una reconstrucción probada de Ultra Luna.

Compra/consumo reales, cancelación desde el juego, mensajes y persistencia de naturaleza/inventario después de guardar y cargar: **pendientes de la única prueba manual anterior**. No se declara validación jugable todavía.
