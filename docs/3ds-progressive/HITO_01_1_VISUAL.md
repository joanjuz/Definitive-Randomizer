# 3DS Progressive — Hito visual v0.1.1

## Problemas abordados
1. Fondo central blanco, ajeno a Dark Graphite.
2. ComboBox de tipos difíciles de leer.
3. Ausencia de sprite del Pokémon.
4. Formulario sin cabecera visual y sin dirty state visible.

## Implementación
- App.xaml: ScrollViewer, ScrollBar, ComboBox, ComboBoxItem y ListBoxItem oscuros.
- MainWindow.xaml: fondo central explícito, sprite, nombre, badges de tipos, BST e indicador Guardado/Cambios sin guardar/Revisar campos.
- MainWindow.xaml.cs: carga asíncrona con cancelación por especie, badges, BST y dirty state.
- Services/SpriteService.cs: sprites PNG personalizados, caché y recurso externo PokeAPI opcional.
- Services/GameProjectSession.cs: resolución de especie base de las entradas de formas alternas (solo lectura).
- SavePersonal sin cambios. Tampoco se alteraron pk3DS.Core ni pk3DS.WinForms.

## Pruebas pendientes en Windows
- [ ] Tema oscuro sin zonas blancas.
- [ ] ComboBox de tipos legibles en todas sus posiciones y estados.
- [ ] Sprite visible con Internet o PNG personalizado.
- [ ] Placeholder offline correcto; cambio de especie sin mostrar sprite equivocado.
- [ ] Actualización en tiempo real de BST, badges y estado de cambios.
- [ ] Guardado de stat y lectura posterior desde pk3DS Progressive con copia de juego.
- [ ] Pruebas con formas alternativas, Gen 6 y Gen 7.

La nueva preview es una versión de desarrollo, no una release estable de Studio.
