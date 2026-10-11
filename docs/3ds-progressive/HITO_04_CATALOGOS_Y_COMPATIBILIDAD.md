# 3DS Progressive — Hito 04: catálogo vanilla y compatibilidad
Fecha: 2026-10-11
Rama: feature/3ds-progressive-ui

## Confirmado por el usuario antes de este hito
- Datos personales / stats: comprobación desde pk3DS Progressive.
- Movimientos básicos: comprobación desde pk3DS Progressive.
- Navegación integrada: comprobada.
- Diálogos Dark Graphite: comprobados.
- Nuevos campos avanzados de v0.3.0: el usuario indica que todo funcionó. El Hito 04 introduce campos adicionales que requieren pruebas nuevas.

## Cambios de Movimientos
Se copian los nombres y el orden de listas del editor WinForms, sin asignar IDs diferentes:
- Quality: clase lógica (solo daño, estados, curación, estadísticas...), no calidad gráfica.
- Movimiento Z: lista de nombres, con IDs, marcadores físico/especial cuando se aplica.
- Efecto Z: catálogo de 31 descripciones, índices 0–30.
- Objetivos: lista catalogada por nombre.
- Estado infligido: lista catalogada con código especial 0xFFFF.
- 3 slots de cambios de stats (tipo, nivel, probabilidad), turnos y recovery.
- Flags: 32 checkboxes y sincronía hexadecimal; variantes Gen 6/7.
- Poké Relax Gen 7 (tipo y porcentaje).

## Cambios de Pokémon
- Checkbox por MT compatible 01–100, se conservan bits 101–128 sin alteración.
- Tutores por tipo: ocho opciones que coinciden con los tutores del vanilla.
- Tutores especiales: bits editables con nombre técnico de grupo/ranura; lista custom requiere remapeo de nombres en implementación posterior.
- Edición de EvoStage, FormCount, FormSprite, color, altura y peso.
- Pokémon Gen 7: cristal Z, movimiento Z base y especial, variante regional.
- Editor original y núcleo intactos: todo implementado solo en 3DS.Progressive.App.

## Aspectos pendientes antes de paridad
- Más editores: Entrenadores, encuentros, objetos/tiendas, evoluciones, TMs/tutores como editores independientes, historia/textos, herramientas ROM y Batch/template.
- Randomizaciones y Balance Moves: todavía sin UI funcional.
- Mejorar el catálogo de MT y tutores especiales con nombres propios de la ROM, no valores por defecto.

## Pruebas
- [ ] Windows CI compila y genera ejecutable.
- [ ] Abrir proyecto USUM, seleccionar Movimiento Z y Efecto Z por nombre, guardar y reabrir con pk3DS original.
- [ ] Verificar Quality por etiqueta y preservación de índice.
- [ ] Cambiar una flag individual, confirmar cambio hexadecimal y GARC.
- [ ] Cambiar compatibilidad de MT/tutor para Bulbasaur y comparar con pk3DS original.
- [ ] Probar Z exclusivo y variante regional; solo con una copia del proyecto.
- [ ] Validar formularios de Gen 6 y Gen 7; probar recuperación ante edición externa de archivos.

No hay merge ni release de Studio. Cada comprobación runtime se marcará tras confirmación del usuario.
