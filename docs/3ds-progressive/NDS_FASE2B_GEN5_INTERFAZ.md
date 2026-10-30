# NDS fase 2b: randomizer DS en la aplicación — 2026-10-30

Interfaz para el randomizer Gen5 de la fase 2a (Pokémon Negro USA/EUR y Blanco ES).

## Acceso y flujo

- Inicio → botón **«Negro/Blanco · randomizer DS (beta)»**, junto al de Espada/Escudo. No cambia ningún botón ni texto existente; si no se pasa la acción, `StartView` no muestra el botón.
- Ventana `NdsRandomizerWindow`, independiente del proyecto 3DS abierto y del espacio de Switch. No se abre con el bloqueo de interacción activo ni con el espacio Switch ocupado.
1. **Elegir ROM:** abre y verifica la ROM fuera del hilo de la interfaz. Muestra el juego, si es un volcado original verificado y el número de entrenadores y áreas. Una ROM cuyas tablas no coinciden con el perfil se rechaza con el motivo.
2. **Opciones:** entrenadores y Pokémon salvajes (sin cambios, aleatorio, BST similar, BST progresivo por nivel; por defecto progresivo y similar) y semilla editable o nueva.
3. **Vista previa:** lista de cambios con nombres de especie. Nada se escribe.
4. **Exportar ROM:** pide la ruta, escribe la ROM nueva, la vuelve a abrir para comprobar que conserva entrenadores y áreas y guarda `<nombre>.cambios.txt` con juego, origen, semilla, modos y cada cambio. Usa el bloqueo de interacción y la barra de progreso de la aplicación.

- **Cualquier cambio de opción descarta la vista previa** y desactiva Exportar: nunca se exporta algo que no se ha visto.
- La ROM original nunca se modifica, y no se sobrescriben una ROM o un registro existentes.
- Avisos visibles: los juegos DSi-enhanced solo funcionan en modo DS y emuladores tras modificarse; los iniciales todavía no se randomizan.

## Diseño

Ventana construida en C# como las demás (sin MVVM), con los recursos de tema (`Bg`, `TextMain`, `TextSoft`), `EditorPresentation.StyleAction` para los roles de botón y nombre accesible en cada control. El cuerpo tiene desplazamiento y el pie con Exportar y Cerrar ocupa su propia fila: en el tamaño mínimo (560×520) siguen visibles. Una primera versión los cortaba en ese tamaño; se detectó en la captura renderizada y se corrigió. La lógica está en `Services/Nds/NdsGen5Session.cs`, separada de la ventana.

## Validación

- `--nds-ui-only` (fuera de pantalla, en el CI): 6 comprobaciones. Botón de inicio solo cuando se proporciona y abre la ventana, estado inicial sin vista previa ni exportación, modos por defecto, nombre accesible en todos los controles, rechazo de una ROM con tablas que no coinciden. Con `PROGRESSIVE_UI_CAPTURE_DIR` guarda capturas a 920 y 560 px de ancho.
- `--nds-ui-probe <rom> <salida>` con el original de Blanco ES: 6/6. Carga verificada, vista previa (≈5.300 cambios), un cambio de opción descarta la vista previa, entrenadores sin cambios solo modifica encuentros, exportación con ROM y registro, original sin cambios.
- Las capturas y pruebas no sustituyen una prueba interactiva con teclado, alto contraste o DPI distintos de 100 %.
