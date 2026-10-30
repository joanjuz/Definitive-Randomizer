# DivineSword: caps seleccionados y reglas generales por categoría

Fecha: 22/10/2026. Rama: `feature/switch-swsh-prototype`.

## Cambios

Se extrajeron exclusivamente los 40 IDs con `CapUse=1` y sus `LevelCap` del archivo del usuario `DivineSword.json`. No se incorporaron sus demás opciones, ni se modificó el archivo. SHA256 de referencia: `386cb1946e84f8ebe1caa2ec8cd70de38245ab4173d1c6ccc583be2265165673`.

La tabla de Level Caps de **Espada** contiene únicamente esos combates presentes en la extracción. El botón **DivineSword** activa sus casillas y carga exactamente sus niveles. Esto configura la próxima previsualización: no cambia el proyecto inmediatamente. Aceptar conserva la configuración; Cancelar devuelve los valores anteriores al abrir la tabla. Los niveles por defecto siguen procediendo de la extracción hasta cargar el preset o editarlos. El preset de IDs de Espada no se aplica a Escudo.

Se retiró la tabla Trainer Move Rules de la interfaz Switch. Better Movesets, ataque más fuerte con tolerancia y Smart Items despliegan selectores independientes para **Normales**, **Importantes** y **Líderes de gimnasio**. Potencia mínima dispone también de su alcance por categoría (0 sigue significando sin mínimo). Las categorías usan la clasificación existente de nombres de entrenadores, incluyendo rivales y otros jefes entre los importantes. El permiso de movimientos de estado permanece como opción general.

Cada alcance se resuelve a IDs al previsualizar y se copia como colección inmutable. Null conserva el comportamiento global de recetas anteriores; una colección vacía no aplica esa regla a ningún entrenador. Se validan IDs negativos y duplicados. Los alcances se guardan en historial, Global Template y Batch. El alcance general del randomizador continúa limitando los equipos que se procesan.

## Compatibilidad

Los templates de opciones anteriores siguen siendo importables. Las claves de la antigua tabla Move Rules y los caps que ya no se muestran se validan, pero no se exponen ni aplican desde el nuevo editor Switch. Las recetas Global ya existentes conservan su soporte de reglas individuales en el backend para no romper su reproducción. La interfaz 3DS conserva su tabla y su comportamiento.

No se modificaron motores 3DS, datos originales, distribuciones instaladas ni mods del emulador. No hay commit ni push.

## Archivos de esta entrega

- `3DS.Progressive.App/Services/Switch/DivineSwordCaps.cs`: únicamente IDs y niveles del preset.
- `SwordShieldRecordEditor.Trainers.cs`: tabla filtrada, grupos generales y resolución de categorías.
- `SwordShieldOptionsStore.cs`: compatibilidad de opciones anteriores.
- `SwordShieldProject.Gameplay.cs`: copia defensiva de alcances en las previsualizaciones.
- `pk3DS.Core/NX/SwordShieldBattlePolicy.cs`: aplicación independiente de cada regla por IDs.
- `3DS.Progressive.App/RecordEditorView.xaml.cs` y `.Tables.cs`: botón, navegación y selectores dependientes; preservación de interfaz 3DS.
- `3DS.Progressive.Tests/DivineSwordChecks.cs`, `SwitchTableChecks.cs`, `SwitchVisualChecks.cs`, `Program.cs`: regresiones y comprobación con datos reales.

## Validación

- Solución Release: 0 errores, 56 emisiones de advertencia conocidas. Proyecto de pruebas Release: 0 errores, 88 emisiones conocidas. Ninguna advertencia nueva en comparación normalizada.
- Switch: 181 comprobaciones de paridad, 84 de componentes UI y 312 del prototipo; todas satisfactorias.
- Interfaz compartida: 95 comprobaciones visuales, 398 UI y 233 de navegación guiada; todas satisfactorias.
- Regresión sintética XY/ORAS/SM/USUM: 4055 comprobaciones satisfactorias. Total del runner en esta entrega: 5358.
- Espada real, en lectura: `DivineSword_RealFortySelectedCapsApplyToAllTeamSlotsInMemory` y `DivineSword_RealImportantOnlyItemsSurviveGlobalReplay`, satisfactorias. Caps y objetos aplicados solo en memoria; receta reproducida en otra sesión. No se exportó ni instaló un mod.
- 22 renders WPF a 96 DPI sin errores de binding. Inspección de tabla filtrada/botón y selectores de movimientos/objetos. No equivale a una prueba interactiva ni de DPI 125–200 %.
- Comparación independiente del preset incorporado con el JSON: los 40 IDs y sus 40 niveles coinciden exactamente.
- `git diff --check`: correcto.

Las pruebas detectaron un conflicto local de nombre de variable durante compilación, corregido, y un fixture de potencia mínima que inicialmente contenía un movimiento ya válido, corregido para probar la sustitución de uno inválido. No quedan fallos de esas comprobaciones.

Logs y binlogs: `C:/Users/Joanj/AppData/Local/Temp/DR-DivineSword-20261022`.

## Pendientes manuales

Probar en juego el preset y las combinaciones de categorías, validar Escudo con sus propios IDs/caps, y comprobar teclado/DPI de forma interactiva. La clasificación conserva la dependencia de nombres del catálogo existente. No se afirma jugabilidad por los resultados sintéticos o en memoria.
