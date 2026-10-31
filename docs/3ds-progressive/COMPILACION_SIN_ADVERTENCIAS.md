# Compilación sin advertencias — 2026-10-31

La compilación limpia (`--no-incremental`) de las pruebas, de la aplicación (`-r win-x64`, como el CI) y de `pk3DS.slnx` pasa de **63 advertencias a 0**: 52 distintas, algunas repetidas porque hay archivos que se compilan en dos proyectos. Ninguna corrección cambia el comportamiento.

| Código | Cuántas | Corrección |
|---|---|---|
| CS8632 | 38 | Se quita el `?` de anotaciones en archivos sin contexto nullable (WinForms y los motores heredados copiados en la app). Ahí no significan nada, así que ni el código generado ni el análisis cambian. |
| CS8602 | 7 | `!` en campos `readonly` de vistas WPF que se asignan más adelante en el constructor y que solo usan manejadores de eventos que se ejecutan después. |
| CS0219 | 2 | Constantes sin uso (`finalDescription`, `gap`). |
| CS0414 | 1 | Campo `MartEditor7UU.freeMegaStonesOnSave`, que solo se asignaba. |
| CS0108 | 2 | `new` en el método privado `TrainerRand.Move`, que oculta `Control.Move` (WinForms y la copia de la app). |
| CS8794 | 2 | Modo Metrónomo (MoveEditor6/7): `is not (117 and 32)` siempre era verdadero, así que el PP siempre se ponía a 0 y luego Metrónomo y Golpe Cabeza recibían el suyo. Se deja la asignación sin la condición: mismo resultado. |

## Para que no vuelvan

`Directory.Build.props` trata como error las advertencias nullable y CS0108, CS0219, CS0414 y CS8794 en todos los proyectos. Solo son diagnósticos del compilador, estables entre versiones: un SDK más nuevo en el CI (`10.0.x`) no puede romper la compilación con advertencias de analizadores nuevos, que siguen siendo advertencias.

## Validación

- Compilación limpia de `3DS.Progressive.Tests`, `3DS.Progressive.App` (`-r win-x64`) y `pk3DS.slnx`: 0 advertencias y 0 errores.
- Regresión completa y pruebas reales DS en verde (ver el PR).
