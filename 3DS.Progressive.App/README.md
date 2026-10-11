# 3DS Progressive — preview funcional v0.6.0

Aplicación WPF/.NET 10 independiente, en español y Dark Graphite, dentro del repositorio pk3DS Progressive. Trabaja en `feature/3ds-progressive-ui`, conserva WinForms y reutiliza Core sin modificar esos proyectos.

## Editores reales

- **Pokémon:** lista/búsqueda de especies y formas, sprites personalizados y caché, estadísticas, tipos, habilidades, objetos, EV, EXP/crianza, compatibilidad MT/tutores y parámetros de formas y Z de Gen 7.
- **Movimientos:** lista/búsqueda, campos básicos y avanzados, multigolpe, efectos, estados, objetivos, críticos, turnos, curación, tres slots de stats, 32 flags y parámetros/efectos Z.
- Ambos permanecen en la misma ventana, con confirmación de cambios y diálogos oscuros.

El usuario ya validó en Ultra Sol abrir proyecto, guardar stats, compatibilidad MT, movimientos y efectos Z con la preview previa. Los campos/acciones nuevos de v0.6.0 requieren prueba manual.

## Acciones en el inspector

**Randomizar Pokémon** y **Modificar Pokémon** abren un panel temporal en el centro. Movimientos ofrece **Randomizar movimientos**, **Modo Metrónomo** y **Balance y templates**. El catálogo y el inspector siguen visibles; **Volver al editor** restaura el formulario. Los botones de generar/aplicar permanecen fijos mientras se recorren las opciones. Se recuerdan las opciones de cada acción durante la sesión.

Cada vista previa aleatoria genera una semilla nueva automáticamente. No existe un número editable. Las pruebas de servicios todavía permiten reproducir una propuesta con la semilla registrada. Los valores iniciales de Pokémon coinciden con las capturas del usuario: habilidades, exclusión de Shadow Tag/Arena Trap, objetos, MT y tutores activados; estadísticas, barajado, tipos, Wonder Guard, captura y grupos huevo desactivados. Los slots de stats empiezan desmarcados. MO se ofrece solo en Gen 6. La exclusión de habilidades permanece como opción del randomizador.

Los selectores muestran nombres; los controles deshabilitados usan un vino discreto. Se eliminaron los avisos obsoletos de campos pendientes.

**Templates:** se incluyen `moves_gen6.csv`, `moves_gen7.csv` y `pokemon_stats_gen7.csv`. Movimientos permite cargar un CSV propio o usar Progressive. Se conservan los defaults de balance de Gen 6 cuando su CSV incluido está vacío, como en el editor original. Se portaron campos básicos, multigolpe, turnos, estados, stats, flags, parámetros de efecto y efectos Z. Las mecánicas de combate usan los algoritmos originales sobre una copia temporal, incluidos Escudo Real, Hidrochorro/Chapoteo Lodo, Deseo, Meditación/Pantalla de Humo, Conjuro, Neblina, Cerrojo Feérico, Pesadilla y Giro Vil v2. Se verifican las firmas y la segunda aplicación antes de ofrecer cambios. Un CRO incompatible rechaza la propuesta completa. Desmarcar mecánicas permite aplicar solo datos de movimientos y desactiva las descripciones.

Las descripciones se escriben en el banco español (idioma 6), conservando los demás archivos internos. **Modificar Pokémon** de Gen 7 añade el template de estadísticas de Progressive y permite elegir un CSV propio.

Alcance **Todo / Lista filtrada / Selección**, vista previa antes/después, **Copiar tabla guardada** y **Copiar cambios**. Cambiar parámetros, selección o el editor invalida la propuesta.

## Guardado y pruebas

El guardado explícito prepara y relee todos los archivos, detecta cambios externos y verifica los bytes finales. Para balance se guardan juntos movimientos, textos y combate; un fallo revierte los archivos ya reemplazados desde memoria. No es una transacción atómica entre archivos frente a un corte del proceso o del sistema. Personal incluye reconstrucción de tabla maestra; Movimientos respeta GARC directo X/Y y contenedor WD ORAS/Gen 7. No se crean backups automáticos.

`3DS.Progressive.Tests` verifica servicios con fixtures sintéticos para XY/ORAS/SM/USUM y añade pruebas de inicialización WPF en Windows. 356 comprobaciones de servicios pasan con fixtures; Windows añade comprobaciones WPF de navegación, defaults, semillas, selectores y estilos, además de capturas reales. CI compila, ejecuta esas comprobaciones y publica **3DS-Progressive-Preview-win-x64** autocontenido (14 días de retención).

```powershell
dotnet run --project .\3DS.Progressive.Tests\3DS.Progressive.Tests.csproj -c Release
dotnet run --project .\3DS.Progressive.App\3DS.Progressive.App.csproj -c Release
```

## Sprites

Prioridad de PNG: `%LOCALAPPDATA%\3DS Progressive\sprites\custom\entry-001.png`, `species-001.png`, `001.png`/`1.png`, caché, y descarga opcional desde PokeAPI/sprites. La forma alternativa usa la especie base si no hay sprite de entrada. Sin imagen se muestra el fallback. No se incorporan sprites descargados al repositorio.

## Alcance pendiente

La migración no está completa. Entrenadores, Encuentros, MT/tutores como editor de asignaciones, economía/tiendas y automatización siguen pendientes. Otros módulos permanecen deshabilitados. Esta es una **preview**, no sustituye el release estable WinForms ni realiza merge a la rama estable.

Extraer el ZIP **completo** y ejecutar `3DS.Progressive.exe`; conservar la carpeta `custom_balance_templates` junto al ejecutable.

Detalles y comprobaciones manuales: [Hito 06](../docs/3ds-progressive/HITO_06_INSPECTOR_Y_TEMPLATES.md). Matriz: [Paridad de acciones](../docs/3ds-progressive/PARIDAD_ACCIONES.csv).
