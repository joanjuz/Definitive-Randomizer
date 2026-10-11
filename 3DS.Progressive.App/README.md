# 3DS Progressive — preview funcional v0.5.0

Aplicación WPF/.NET 10 independiente, en español y Dark Graphite, dentro del repositorio pk3DS Progressive. Trabaja en `feature/3ds-progressive-ui`, conserva WinForms y reutiliza Core sin modificar esos proyectos.

## Editores reales

- **Pokémon:** lista/búsqueda de especies y formas, sprites personalizados y caché, estadísticas, tipos, habilidades, objetos, EV, EXP/crianza, compatibilidad MT/tutores y parámetros de formas y Z de Gen 7.
- **Movimientos:** lista/búsqueda, campos básicos y avanzados, multigolpe, efectos, estados, objetivos, críticos, turnos, curación, tres slots de stats, 32 flags y parámetros/efectos Z.
- Ambos permanecen en la misma ventana, con confirmación de cambios y diálogos oscuros.

El usuario ya validó en Ultra Sol abrir proyecto, guardar stats, compatibilidad MT, movimientos y efectos Z con la preview previa. Los campos/acciones nuevos de v0.5.0 requieren prueba manual.

## Nuevo: Herramientas masivas

Panel desplegable integrado en ambos editores, con alcance **Todo / Lista filtrada / Selección**, semilla, vista previa detallada antes/después y aplicación confirmada.

Pokémon: randomizador completo de datos Personal usando Core; modificaciones de EV, EXP, compatibilidad, crianza, captura y SOS; exclusión de Shadow Tag/Arena Trap persistente en las acciones masivas de la sesión. Las opciones MO/tutores/SOS se muestran según título.

Movimientos: randomización de tipos/categorías y modo Metrónomo con IDs reales. La edición de PP o tipo/categoría conserva los demás campos, incluidos datos Z.

**Copiar tabla guardada** y **Copiar cambios** permiten revisar todos los datos y propuestas en una hoja o editor de texto. La vista previa no escribe; cambiar parámetros, selección o el editor invalida la propuesta.

Se eliminó el distintivo **Editor integrado · 3DS Progressive** y se ocultó la tarjeta obsoleta HITO 01.

## Guardado y pruebas

El guardado explícito utiliza un GARC temporal, compara todos los registros, detecta cambios externos y verifica el archivo final. Personal incluye reconstrucción de tabla maestra; Movimientos respeta GARC directo X/Y y contenedor WD ORAS/Gen 7. No se crean backups automáticos.

`3DS.Progressive.Tests` verifica servicios con fixtures sintéticos para XY/ORAS/SM/USUM y añade pruebas de inicialización WPF en Windows. CI compila, ejecuta esas comprobaciones y publica **3DS-Progressive-Preview-win-x64** autocontenido (14 días de retención).

```powershell
dotnet run --project .\3DS.Progressive.Tests\3DS.Progressive.Tests.csproj -c Release
dotnet run --project .\3DS.Progressive.App\3DS.Progressive.App.csproj -c Release
```

## Sprites

Prioridad de PNG: `%LOCALAPPDATA%\3DS Progressive\sprites\custom\entry-001.png`, `species-001.png`, `001.png`/`1.png`, caché, y descarga opcional desde PokeAPI/sprites. La forma alternativa usa la especie base si no hay sprite de entrada. Sin imagen se muestra el fallback. No se incorporan sprites descargados al repositorio.

## Alcance pendiente

La migración no está completa. Balance/templates, parches de Battle/CRO y textos, Entrenadores, Encuentros, MT/tutores como editor de asignaciones, economía/tiendas y automatización siguen pendientes. Otros módulos permanecen deshabilitados. Esta es una **preview**, no sustituye el release estable WinForms ni realiza merge a la rama estable.

Detalles y comprobaciones manuales: [Hito 05](../docs/3ds-progressive/HITO_05_ACCIONES_MASIVAS.md). Matriz: [Paridad de acciones](../docs/3ds-progressive/PARIDAD_ACCIONES.csv).
