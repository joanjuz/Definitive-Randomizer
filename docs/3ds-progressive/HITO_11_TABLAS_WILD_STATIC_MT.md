# Hito 11 — Tablas de entrenadores, Wild, Static y MT/Tutores

Este hito responde a la comparación visual y funcional suministrada por el usuario. **No significa paridad total ni validación runtime** y permanece en `feature/3ds-progressive-ui`.

## Entrenadores

- Nueva `TrainerRulesWindow` WPF, tema Dark Graphite y columnas editables.
- Dos vistas especializadas: Level Caps y Trainer Move Rules, desde botones del inspector de Entrenadores.
- Las tablas contienen **todos** los candidatos clasificados por el motor original SMTE/TrainerRand; se muestran grupo Important/Boss, ID, nombre y nivel Ace.
- Filtros independientes Important/Boss sin perder datos ocultos.
- Acciones masivas sobre filas visibles (seleccionar/deseleccionar), validación y Cancelar/Aceptar.
- Persistencia en los templates originales, sin escribir ROM hasta `Generar vista previa → Aplicar`.
- Se conservan los metadatos `CurrentAceLevel` y las reglas de otras filas.
- Cada rango Progressive BST se presenta en una tarjeta independiente, con valores mínimo/máximo, BST y FullRandom; se mantiene la clave original del campo para los templates.

## Encuentros salvajes

- ID de registro al principio de la lista para diferenciar áreas y horarios que comparten nombre.
- Filtros de Gen 1–7, legendarios y evento, BST similar por **slot**, seis rangos Progressive BST editables de Gen7, nivel % / ajuste plano, conservar intervalo.
- Modos de slots: todos, normales, SOS, normales con copia a SOS; meteorología opcional. No crear encuentros desde slots vacíos.
- El randomizador sigue generando propuestas congeladas y escribiendo solo al aplicar.
- **Pendiente:** paridad exacta con `Wild7Randomizer`/`ProgressiveWildRandomizer`: templates Wild JSON, formes Mega, `All Can Call Allies`, sprite-grid día/noche/SOS y mejoras visuales de la tabla.

## Static Encounters Gen 7

- Navegación activa para proyectos Gen7. Editor real del archivo GARC `encounterstatic` (0=Gifts, 1=Encounters, 4=Trades).
- Edición específica de especies, formas, niveles, objetos, shiny lock, naturalezas, movimientos, aura Totem, pedido de intercambio y demás campos admitidos.
- Randomización de especies con alcance, BST y niveles, y conservación de locks; vista previa e historial.
- Preservación de bytes ajenos, con transacción y validación del GARC.
- **Pendiente:** randomizador completo de static vanilla/Progressive (starter, Totem BST, auras, actualizaciones CRO, Trades Accept Any, códigos de nivel Totem y textos), Gen6 y validación runtime.

## MT y tutores Gen 7

- Se habilita `MT y tutores` con editor WPF.
- 100 MT desde `code.bin` por firma real, cambios de movimiento y botón de randomización en memoria.
- Tutor BP desde `Shop.cro` (layout USUM validado), tabla, precio y acción Todos gratis. **Movimientos de tutores permanecen solo de lectura**, ya que cambiarlos requiere sincronizar `code.bin` con los hooks originales.
- Guardado con SHA de origen, transacción, historial y refresco de los IDs usados en el inspector de Pokémon.
- **Pendiente:** MT/Tutor Sanity, Follow Evolutions, descripciones de objetos, sincronización del código de tutores, Gen6 y compatibilidad runtime.

## Validación

- `3DS.Progressive.Tests/ModuleChecks.cs`: tests para Gifts y Trades Gen7, campos ShinyLock, bytes desconocidos, preview no destructivo, MT en code.bin, conservación de offsets, invalidación de caché y bloqueo por edición externa.
- `UiSmoke.cs`: tests de filtros Important/Boss (incluye filas ocultas), theme, capturas e integración WPF.
- Ejecutar CI Windows con build y pruebas XY/ORAS/SM/USUM y comprobar que termine completamente verde antes de publicar una preview.
- No alterar Evoluciones ni Historial fuera de correcciones necesarias.

## Criterios de cierre pendientes

- Probar manualmente Ultra Sun y Ultra Moon con proyectos extraídos reales y comparar binarios.
- Comparar cada opción de randomización vanilla y Progressive contra el original, generar matrices por versión y probar condiciones especiales en juego.
- Verificar controles responsivos, contraste, longitud del texto y sprites.
- Antes de merge: documentar cualquier función no migrada y no hacer afirmaciones de paridad completa.
