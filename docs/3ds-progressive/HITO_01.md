# 3DS Progressive — Hito 01 · App WPF + editor Personal

## Repositorio
- Mismo repositorio que pk3DS Progressive; proyecto nuevo: 3DS.Progressive.App.
- Rama de desarrollo: feature/3ds-progressive-ui.
- Base: release v4.17.1 Stable.
- No modificar archivos de pk3DS.Core, pk3DS.WinForms ni su solución existente.

## Arquitectura
- UI: WPF, dark graphite, código de presentación en MainWindow.
- Integración: Services/GameProjectSession accede directamente a pk3DS.Core.
- Modelo: Models/PersonalValues agrupa únicamente los datos editables de la primera etapa.
- Adaptación futura: migrar lógica de WinForms por módulos sin lanzar ni controlar ventanas antiguas.

## Primera equivalencia con pk3DS vanilla
| Origen | Nuevo elemento | Estado |
| --- | --- | --- |
| Main / Open | Abrir proyecto | Implementado; requiere validación runtime |
| PersonalEditor6/7 CB_Species | Lista y búsqueda de especies | Implementado; requiere pruebas |
| TB_BaseHP / ATK / DEF / SPA / SPD / SPE | Seis campos de stats | Implementado; requiere pruebas |
| CB_Type1 / CB_Type2 | Selección de tipos | Implementado; requiere pruebas |
| TB_CatchRate | Captura | Implementado; requiere pruebas |
| Campo BST | Inspector de BST | Implementado; requiere pruebas |
| Guardado de Personal GARC | Guardado verificable | Implementado; requiere pruebas |
| Otros ~2966 controles del inventario | Migración futura | Pendiente |

## Criterio para cerrar este hito
- Build sin errores.
- Abrir proyecto USUM en Windows y revisar que se detecta correctamente.
- Editar la copia de trabajo y reabrir con pk3DS Progressive, comparando valores.
- Corroborar que la ROM original y la aplicación WinForms no se modificaron.
- Actualizar la matriz de paridad al completar pruebas reales.

## Decisiones
- Batch ROM Builder probado con partida completa de Pokémon Ultra Sol.
- NPC Trades validado previamente.
- Bulk Item Use fuera del alcance.
- No incluir ROMs, ExeFS, RomFS, partidas ni backups en Git.
