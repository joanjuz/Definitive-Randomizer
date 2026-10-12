# Hito 14 — Optimización visual y sistemas de encuentros estáticos

Rama: `feature/3ds-progressive-ui` — sin merge a estable.

## Correcciones visuales

- Level Up Moves muestra ahora **tres tablas independientes al estilo MT** (dos columnas por tabla: nivel y movimiento), filas de 34 px y encabezado claro. El nivel tiene ancho 62 px y los encabezados de fila de Windows están deshabilitados. Adaptación responsiva 1/2/3 columnas. Los campos de opciones del randomizador original están **siempre visibles**, en tarjeta oscura distribuida en tres columnas.
- MT mantiene tres tablas editables, elimina la franja blanca de row headers.
- Tutores BP ahora utiliza **dos tablas paralelas**, cada una con número, sede, movimiento y precio editable. El guardado sigue sincronizando únicamente precios con el CRO; los cambios de movimientos de tutores siguen pendientes del parche USUM correspondiente.

## Sistemas Progressive añadidos a Static Encounters Gen7

| Sistema | Alcance y seguridad | Paridad conocida |
|---|---|---|
| Gift BST Baseline | Regalos 4–13 usan Squirtle (BST 314), restantes no iniciales Arcanine (BST 555), excluyendo índices 0–2. Preview + confirmación + historial | Paridad del algoritmo de selección base |
| Totem Level Caps | Tabla WPF in-app por ID original del encuentro: usar, actual, cap (0 conserva nivel). Incluye Ultra Necrozma. Templates JSON compatibles por claves con el original. Vista previa y guardado GARC | Aplicación por tabla de 0–100, aún falta integrar automáticamente tras Randomize All |
| Totem BST | Tabla WPF in-app por ID con intervalos originales de BST, selección por fila y preview/guardado. Mueve la especie solo si hay candidatos. Actualiza movimientos por nivel | Primera implementación: faltan los filtros completos por especie+forma y Mega / evoluciones finales del randomizador original |
| Trades Accept Any Pokémon | Cambia Requests a 0 **y** instala en la misma transacción el hook validado `code.bin` (0x2843F4 / wrapper 0x4B9C00). Rechaza binarios desconocidos y zonas ocupadas, sin escribir | Función USUM compatible con firma original; falta validación en juego |
| Hide Trade Pokémon Names (ES) | Porta el diccionario curado del original a `storytext` español (banco 6, archivo 14). Revisión de codificación ida/vuelta, fingerprint y transacción con historial | Función específica para español USUM; otros idiomas pendientes |
| Bloqueo legendarios en regalos/intercambios | Filtro activado por defecto del randomizador WPF, incluye 772, 789, 790 y 803 además de legendarios y míticos | Sigue pendiente afinar forma y pool de especies equivalentes al original |

## Pendientes de paridad / pasos siguientes

1. **Player Level Caps (USUM)**: 21 checkpoints, flags Event Work reales, presets, sincronización con Trainer Caps, instalador verificado en Battle.cro + code.bin. Es distinto de las reglas de entrenadores.
2. **Totem BST completo**: filtros form-aware, Mega, sustituciones seguras y encadenarlo con Randomize All.
3. **Static Randomize Starters** con pool de especies de dos evoluciones y gift move randomizer original.
4. **Static Totem Aura / Force Fully Evolved** completo, gestionando formas especiales y eventos.
5. **Parches de trade**: validación runtime Ultra Sun/Ultra Moon y localización para idiomas distintos de español.
6. Otros sistemas pendientes: tiendas y BP items, Story Script Editor, shiny rate, tablas de tipos, tutor code-sync, Gen6 pickups y utilidades de CRO.

## Pruebas

Las regresiones usan ROMs sintéticas XY, ORAS, SM y USUM. Incluyen propuestas congeladas de Totem Level Caps por ID, comprobación de originales de BST en regalos y de iniciales inalterados, rechazo de firma desconocida del selector de intercambios, patch junto al GARC, y smoke tests de las dos tablas Totem oscuras integradas. **El verde de CI no certifica las pruebas runtime en una consola/emulador**.

Los cambios son intencionalmente reversibles por copia del proyecto e historial, sin merge hasta validación manual de los módulos binarios.
