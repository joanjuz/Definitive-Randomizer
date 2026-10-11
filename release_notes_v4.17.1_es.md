# pk3DS Progressive v4.17.1 Stable

- **Rama de origen:** `Eris-Ultranova`
- **Base funcional consolidada:** `0e20df1522cebdff30eef5487445506b77b015b2` (10 de octubre de 2026)
- **Plataforma del binario:** Windows x64
- **Framework:** .NET 10; compilación `win-x64` autocontenida
- **Estado:** versión estable publicada como actualización acumulativa de v4.9.0, con foco principal en Ultra Sun y Ultra Moon.

> **Importante sobre la numeración:** existe un tag histórico `v4.17.0` que apunta al commit `24369d3`, anterior a los últimos arreglos. Para preservar ese tag y publicar el código consolidado se utiliza `v4.17.1`.

## Descarga e instalación

1. Descarga `pk3DS-Progressive-v4.17.1-win-x64.zip` desde los assets de este release.
2. Extrae **todo** el ZIP a una carpeta propia; no ejecutes el programa directamente desde el ZIP.
3. Abre `pk3DS.WinForms.exe` (nombre generado por el proyecto WinForms) y selecciona la carpeta de archivos extraídos de un juego que poseas legalmente.
4. Conserva **por tu cuenta** una extracción original limpia de los archivos del juego, separada de la carpeta de trabajo. **La creación automática de backups persistentes está desactivada.**

El ZIP contiene el ejecutable de Windows y sus dependencias/recursos necesarios, además de la licencia y estas notas. No contiene ROMs ni datos extraídos de juegos.

## Cambios principales desde v4.9.0

### 1. Progresión y randomización de Pokémon salvajes

- Progressive Wild: selección de reglas de BST por tabla real de encuentros y su propio intervalo de niveles, en vez de usar un único nivel máximo para toda el área `Area7`.
- Corrección del caso Ruta 1 / ID 000, con niveles 2–3. Las tablas vacías, internas o placeholder conservan un fallback prudente por área.
- Filtros de especies, generaciones y Pokémon especiales; integración con Wild Template, Global Template y Batch ROM Builder.
- Soporte para `All Pokémon Can Call Allies` en encuentros SOS sin inventar filas inexistentes de `AdditionalSOS` o `Weather`.

### 2. Entrenadores y progresión de historia

- Clasificación de entrenadores `Normal / Important / Boss`.
- Trainer Level Caps, Player Level Caps y escalado `story-aware` de entrenadores regulares.
- Integración de hitos de Totem, aura, BST y límites de nivel.
- Better Movesets: movimientos legales hasta el nivel aplicable y opción de incluir MT compatibles; heurísticas de STAB, cobertura y roles.
- Smart Held Items con calidades configurables por categoría, Strong Stat global y reglas avanzadas de entrenador.
- Max 3 como **máximo** de miembros del equipo, sin forzar tres miembros exactos.
- Random Double Battles con restricción de historia (no activarlas antes del primer Hala) y ajustes de IA para dobles.
- Correcciones para preservar el primer combate de Hau y permitir niveles mínimos apropiados.

### 3. Starter y NPC Trades

- Garantía de que el starter randomizado tenga un movimiento ofensivo, teniendo en cuenta `SpecialMove`, que puede desplazar un ataque del learnset inicial.
- Sincronización de textos de los starters en los bancos correspondientes.
- Corrección de persistencia del request en NPC Trades: las actualizaciones de selección no deben sobrescribir la opción `Accept Any Pokémon`.
- Mantiene el soporte de randomización de ofertas y solicitudes de intercambio con NPC.

### 4. Giro Vil / Brutal Swing — fix V2

- Movimiento ID **693**: configuración de referencia **30 de potencia**, **2–5 golpes**, objetivo `AllFoes` y autoconfusión del usuario **una sola vez al terminar**.
- Fix del selector de ruta de ejecución multigolpe en `Battle.cro`, desplazamiento `0x1FDE4`:
  - Antes: `14 00 00 0A` (`BEQ`).
  - Ahora: `14 00 00 EA` (`B`).
- Conserva el efecto de autoconfusión con `timing 0x04`; no utiliza el experimento descartado de `fixed 3 hits` / `timing 0x43`.
- El patcher detecta instalaciones anteriores de la mecánica y puede agregar solo la corrección spread/multihit cuando corresponde, evitando duplicar registros del movimiento.
- **Validado dentro del juego en Ultra Sun y Ultra Moon**. No modificar ni sustituir este V2 sin evidencia concreta de regresión.

### 5. Tutores de movimiento — Shop.cro / code.bin

- Sincronización de **67** IDs de movimientos enseñados por tutores de playa entre `Shop.cro` y la tabla nativa de `code.bin`.
- Corrección adicional: al guardar el editor de tutores de USUM se comprueba la sincronización con `code.bin` **aunque `Shop.cro` ya tuviera guardados los mismos movimientos randomizados**.
- Soluciona el caso donde el nombre del movimiento mostrado era correcto, pero la descripción seguía asociada al movimiento original.
- Se preservan la compatibilidad por especie y los precios BP; `Free Tutors` sigue siendo una opción independiente.
- **Corrección de descripciones comprobada en Ultra Sun**. Es conveniente validar también Ultra Moon con una ROM sin overrides antiguos.

### 6. Vitaminas 252 EV por 200 Pokédólares

- `Add EV Items` en Expanded Marts ahora ofrece las seis **vitaminas** en lugar de las seis plumas: Más PS, Proteína, Hierro, Calcio, Zinc y Carburante.
- Cada vitamina sigue aumentando **+10 EV** en su estadística correspondiente y ahora funciona por encima de 100 EV, respetando el límite normal de **252 EV por estadística y 510 EV totales**.
- Precio de compra **200 Pokédólares** por vitamina, aplicado a sus registros de objeto.
- Habilitación mediante `ItemFlags2.AddEVAbove100` sobre los registros de los seis objetos en el GARC `item`; **no necesita un parche ExeFS/code.bin** para este comportamiento.
- Validación de seis IDs distintos y de su capacidad de aumentar los EV previstos; comprobaciones previas de datos antes de guardar.
- Migración de las antiguas plumas ya añadidas a tiendas: conversión en vitaminas, sin duplicar las ya existentes, y control del máximo de espacios.
- Conserva Rare Candies, Heart Scale por progresión y Key Stone según las reglas existentes.
- Integración de `marts.add-ev-items` con el marcador `evItems=vitamins` en Global Template y Batch ROM Builder. Los templates antiguos sin el marcador mantienen su comportamiento anterior.
- **Comprobado en Ultra Sun**. La validación de uso en Ultra Moon se mantiene como prueba de regresión recomendada.

### 7. Global Template y Batch ROM Builder

- Reproducción coordinada de randomizaciones, progresiones, entrenadores, objetos, tutores, economía y gameplay QoL.
- Staging temporal separado del proyecto base y opción de mantener proyectos extraídos editables.
- Importante: `Restore pk3DS original-file backups before each ROM` utiliza backups antiguos, si existen. **No crea nuevas copias originales** cuando los backups automáticos están desactivados. Para una compilación verdaderamente limpia, parte de una extracción original limpia.
- El orden de ejecución conserva la aplicación de movimientos y personal antes de entrenadores, para que Better Movesets/Smart Items consuman los datos finales.

### 8. Eliminación de backups automáticos persistentes

- Nuevo componente `pk3DS.WinForms/Misc/AutomaticBackups.cs` con `AutomaticBackups.Enabled => false`.
- Desactivada la copia automática de los archivos originales al abrir una ROM (`GameBackup.BackupFiles`).
- Los patchers de CRO, `code.bin`, Move Relearner, tutorial StoryText y Patch Recipe Engine dejan de generar archivos de backup persistentes por defecto.
- Las copias antiguas siguen siendo legibles por las opciones de restauración **mientras sus archivos existan**; no se eliminan automáticamente.
- Se conserva la preparación en memoria y las validaciones de propiedad de bytes para modificaciones de patchers; **la ausencia de backup persistente significa que no existe recuperación automática del archivo anterior**.
- En el entorno de desarrollo se detectaron aproximadamente **5,13 GB** de backups, y se eliminaron manualmente dos carpetas que sumaban **5,10 GB** tras revisión del usuario. Es una limpieza local, **no** una acción que este release ejecute en equipos de otras personas.
- Para restaurar archivos modificados, utiliza una extracción limpia guardada manualmente.

### 9. Infraestructura y herramientas de integración

- CRO Expander, allocators y herramientas de relocación para patches de mecánicas.
- Patch Recipe Engine (recipes JSON con dependencias, conflictos y validación de regiones propias).
- Fix Economy separado de Expanded Marts: la economía configura precios; los marts definen inventario/disponibilidad.
- Persistent Battle Consumables, Mega Evolution from Start, Fast Egg Hatching y otras funciones QoL compatibles con su configuración.
- Mejoras de interfaz en entrenadores, trades, Items, Move Editor y editores de Gen 7.

## Commits principales de esta consolidación

| Commit | Cambio |
|---|---|
| `96ccf44` | Giro Vil V2: multigolpe en combates dobles |
| `1ba046a` | Progressive Wild por tabla de encuentros |
| `6615a1f` | Tutor Sync entre `Shop.cro` y `code.bin` |
| `d419e7e` | Persistencia de solicitudes en NPC Trades |
| `6aaed4a` | Starter con movimiento ofensivo, considerando `SpecialMove` |
| `2c0c6d1` | Vitaminas a 252 EV, precio 200 y cierre de Move Tutors |
| `949ee11` | Merge de vitaminas/tutores a `Eris-Ultranova` |
| `f37f5aa` | Desactivación de backups automáticos (14 archivos C#) |
| `0e20df1` | Merge de desactivación de backups a `Eris-Ultranova` |

El tag de este release se crea **después** del commit de documentación y apunta a la rama estable, incluyendo todos los cambios anteriores.

## Validación y limitaciones conocidas

- Compilaciones .NET 10 anteriores: **0 errores**, con advertencias de análisis y anotaciones de tipos ya existentes. El publicador de este release vuelve a compilar en configuración **Release**, Windows x64, antes de generar el asset.
- Giro Vil V2: comprobado en Ultra Sun y Ultra Moon.
- Vitaminas y corrección de descripciones de tutores: comprobadas en Ultra Sun; se recomienda verificación de ambos flujos en Ultra Moon.
- NPC Trades: hay una corrección integrada para la persistencia del request, pero sigue recomendándose una regresión de todos los intercambios.
- Una ROM completa creada con Global Template y Batch ROM Builder debería pasar una regresión final sin mods antiguos de Lime3DS activos.
- `Bulk Item Use USUM` (usar varias Rare Candies consecutivamente mediante un selector de cantidad propio) **no está implementado**.
- La mayor parte de las funciones Progressive avanzadas se desarrollaron con foco en USUM; no asumir paridad completa de funciones con X/Y, OR/AS o Sun/Moon.
- Los mods LayeredFS activos en `%APPDATA%\Lime3DS\load\mods\<TitleID>` pueden sobrescribir `code.bin`, `Battle.cro`, `Shop.cro` u otros archivos y provocar pruebas engañosas.

## Seguridad, distribución y licencia

- Aplicación gratuita y de código abierto; no hay ROMs, `.3ds`, `.cia`, archivos completos extraídos de juegos ni contenido propietario en el ZIP.
- Distribución bajo **GNU GPLv3**, conservando la atribución al proyecto original [pk3DS](https://github.com/kwsch/pk3DS).
- Proyecto independiente, no afiliado con Nintendo, Game Freak ni The Pokémon Company.
- Las donaciones son opcionales y no son necesarias para descargar esta versión.

- **Repositorio:** https://github.com/joanjuz/pk3DS-Progressive
- **Releases:** https://github.com/joanjuz/pk3DS-Progressive/releases
