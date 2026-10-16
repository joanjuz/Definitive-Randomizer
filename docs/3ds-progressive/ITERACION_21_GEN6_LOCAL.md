# Iteración local 21 — Static Gen6 y mecánicas Gen6

Fecha de trabajo: 12 de octubre de 2026 (America/Guatemala).
Repositorio: `D:\Progra\pk3DS-Progressive-Dev`.
Rama: `feature/3ds-progressive-ui`.

## Estado de aceptación

**La paridad funcional completa sigue pendiente. Esta iteración no es una release
ni un aviso de que toda la aplicación está lista para validación final en juego.**
No se realizaron commits, push, publicación ni merge a Eris-Ultranova.

## Preservación

Se revisó `git status` antes de editar. Se guardó el diff inicial y un inventario
SHA-256 de los archivos modificados preexistentes, los tres servicios Wild nuevos,
las notas personales y el documento Wild local fuera del repositorio. Los parches
Wild 001–003 no se modificaron. ErisTemplate, artifacts y el estado de templates
personales no se editaron por esta iteración.

El usuario confirmó la validación en juego de las funcionalidades Wild 001–003.
Esa confirmación no identifica las ocho variantes de ROM por separado; no se
extrapola a todas ellas ni a los módulos nuevos de esta iteración.

## Implementación

### Encuentros estáticos, regalos e iniciales XY/ORAS

Nuevo `GameStaticEncounter6Service`, accesible desde el botón de encuentros
estáticos de la ventana WPF. Usa las tablas de `DllField.cro` de vanilla:

| Tabla | XY | ORAS |
| --- | --- | --- |
| Static | 0xEE46C; 13 registros de 12 bytes | 0xF1B20; 59 registros de 12 bytes |
| Gifts | 0xF805C; 19 registros de 24 bytes | 0xF906C; 37 registros de 36 bytes |

Permite editar especie, forma, nivel, objeto, habilidad, género y shiny lock;
estáticos añaden los dos flags IV3 y shiny; regalos añaden naturaleza e IV
individuales. ORAS incluye movimiento, lugar de encuentro y concursos.
Los bytes desconocidos y los sentinelas de objeto sin cambios se conservan.

La modificación de especie en un inicial prepara también `DllPoke3Select.cro`.
Se usa el inicio de su tabla desde 0xB8, más 0x10 en XY, con paso 0x54;
los índices de regalos vinculados son los originales del StarterEditor6.
El selector ausente o incompatible impide guardar el cambio de especie.
Ambos archivos participan en la transacción e historial existentes.

Randomización por alcance con generaciones 1–6, legendarios/míticos, tolerancia
BST, legendario por legendario, evolución final, formas, habilidades, objetos
y niveles. El pool de objetos es el original portado, mediante una sobrecarga
con GameConfig explícito; no requiere cambiar Main.Config. Las generaciones
desmarcadas se rechazan explícitamente. Los regalos con megapiedras se conservan
por defecto. La selección de especies usa la política existente de preferir
un reemplazo diferente si hay alternativas.

**Límites:** admite las disposiciones vanilla XY/ORAS, con validación de cabecera,
longitudes, especies y niveles. No demuestra compatibilidad con CROs Gen6 cuya
tabla haya sido movida. La edición sigue la política original de datos CRO;
no reconstruye automáticamente los hashes CRO/CRR. Todavía faltan opciones
vanilla específicas de reemplazo de regalos Mega y filtros de iniciales básicos
por generación. El randomizador no se declara equivalente byte a byte a vanilla.

### Type Chart y Shiny Rate

Type Chart usa `DllBattle.cro` en Gen6, con offsets vanilla 0xD12A8 (XY) y
0xDB428 (ORAS). Valida cabecera, tamaño y los 324 valores permitidos
(0, 2, 4, 8), prepara cambios en memoria y guarda con historial/fingerprint.
El flujo Gen7 conserva su firma code.bin.

Shiny Rate ya tenía la firma ARM compartida del editor original. Se habilita
su acceso Gen6 y se añaden regresiones de aplicación, relectura, restauración
exacta y rechazo ante modificaciones externas. Esto no acredita comportamiento
en ROMs reales de X, Y, Omega Ruby o Alpha Sapphire.

## Pruebas

Compilación Debug local del proyecto de regresiones, que compila la aplicación
y Core. La compilación inicial pasó con 0 errores y 22 advertencias preexistentes.
La suite inicial completó 882 comprobaciones. La suite de Static Gen6 completó
914. La evidencia final de esta iteración se conserva en `ITERACION_21_PRUEBAS/`.

Los fixtures son sintéticos XY, ORAS, SM y USUM; no son ocho pruebas en juego.
Las pruebas nuevas verifican conservación exacta de bytes, sentinelas negativos,
sincronización de iniciales, historial conjunto, vistas previas sin escritura,
propuestas repetidas/ajenas, cambios externos, filtros vacíos y datos truncados.
Type Chart/Shiny añaden validaciones de tablas, instrucciones y restauración.

## Seguimiento restante

`PARIDAD_ACCIONES.csv` sigue siendo el inventario. Esta revisión inicial contrastó
en código Static Gen6/Gen7, Starter/Gift Gen6, Type Chart, Shiny Rate, el pool
original, las interfaces de propuesta/guardado y la suite existente. No sustituye
la auditoría exhaustiva menú por menú de todas las herramientas.

1. Completar Static Gen6/Gen7: opciones vanilla restantes, regalos Mega,
   iniciales, campos avanzados y fidelidad del randomizador.
2. Entrenadores avanzados: ayudas de selección de movimientos, auditoría de
   opciones vanilla/Progressive y regresiones de reglas/caps.
3. Herramientas vanilla: Pickup, Maison/Royal/Tree, O-Powers, Title Screen,
   SMDH/BCLIM, GARC Shuffler y restauración guiada.
4. Gen6: MT/MO/tutores/tiendas, Field Items/scripts y funciones específicas.
5. Extracción CXI/3DS y reconstrucción CRO/CRR manual. Rebuild existe y conserva
   las regresiones anteriores; todavía requiere validación ampliada por formato.
6. Global Template bidireccional con los editores WPF y Batch ROM integral.
7. UX, rendimiento, progreso de operaciones prolongadas y recuperación durable
   ante apagón (rollback de excepciones no acredita recuperación ante apagón).
8. Auditoría exhaustiva final, pruebas de las ocho variantes, empaquetado local
   y release. Publicar o hacer push sigue requiriendo autorización.

No se detectó un bloqueo de compilación. El trabajo de implementación restante
es real y no se presenta como terminado; la validación en juego de lo nuevo
sigue pendiente. No se solicita todavía la comprobación final del proyecto.

Resultado final: **936 comprobaciones completadas; Debug 0 errores / 22 advertencias.**
La verificación SHA-256 confirmó la preservación de los 12 archivos preexistentes registrados.
