# 3DS Progressive — Hito 03: editor integrado + campos avanzados

## Confirmaciones del usuario
- v0.1.x: cambio de stat de Pokémon, guardado y lectura comprobados con pk3DS Progressive.
- v0.2.0: nuevo diálogo Dark Graphite corregido y **guardado de movimientos validado** con pk3DS Progressive.

## Cambios implementados
- Sustitución de MoveEditorWindow por MoveEditorView (UserControl).
- Movimiento entre Pokémon y Movimientos sin abrir ventanas auxiliares; sidebar permanente, paneles centrales alternados y diálogo de confirmación de cambios no guardados.
- GameProjectSession expande PersonalValues y añade:
  - habilidades primera, segunda y oculta;
  - tres objetos equipados (IDs);
  - EV yield para las seis stats (0-3);
  - experiencia base, amistad, ciclos de eclosión, género, grupo huevo 1 y 2, crecimiento de experiencia y ratio de escape.
- Editor de movimientos añade calidad, multigolpe mínimo/máximo, efecto de estado, probabilidad de efecto, críticos, retroceso, objetivo, secuencia de efecto, flags hexadecimales; en Gen 7 también ID, potencia y efecto Z.
- Preservación de los demás bytes y los checks de integridad previos.
- No modifica pk3DS.Core ni pk3DS.WinForms.

## Limitaciones
- No existe paridad completa todavía: compatibilidades MT/tutores, formas y gráficos en Pokémon; y múltiples efectos, stat stages, flags con checkboxes descriptivos, parches, balance y randomizadores en movimientos.
- Los IDs avanzados se exponen como valores técnicos y se necesitan controles más expresivos.
- No se ha validado la nueva v0.3.0 en ROM real todavía.
- No hay merge a rama estable ni release de Studio.

## Pruebas de aceptación pendientes
- [ ] Abrir el proyecto y alternar Pokémon / Movimientos dentro de la misma ventana.
- [ ] Confirmar descartar cambios al cambiar de módulo y al cerrar.
- [ ] Editar un EV yield / habilidad y verificarlo desde pk3DS Progressive.
- [ ] Cambiar un efecto, multigolpe y un flag en copia de Ultra Sol; verificar byte y lectura.
- [ ] Comprobar Gen 6 y Gen 7, especialmente Gen7 Z y preservar todos los bytes que no se tocaron.
- [ ] Revisar diseño al reducir el ancho de la ventana y con resolución 1366×768.

## Próximos hitos
- Completar Personal con compatibilidades TM/HM/tutor, formularios de formas y campos especiales Gen7.
- Completar Move Editor con los demás slots de stats, efectos, flags descriptivos y randomización Progressive.
- Continuar Entrenadores, Encuentros y Tiendas aplicando la matriz de paridad.
