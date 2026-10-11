# 3DS Progressive — prototipo funcional 0.1

Esta carpeta contiene la nueva aplicación de escritorio **3DS Progressive**, desarrollada dentro del mismo repositorio, sin cambiar el código de pk3DS.WinForms.

## Qué funciona hoy

- Interfaz WPF independiente, en español y con paleta Dark Graphite.
- Selección de carpeta de juego extraído (RomFS + ExeFS).
- Identificación de X/Y, ORAS, S/M y USUM mediante la lógica existente de pk3DS.Core.
- Lectura de especies y formas desde el GARC personal.
- Búsqueda de especies por nombre o identificador.
- Edición de los seis stats base, los dos tipos y el ratio de captura.
- Vista previa del BST, detección de cambios pendientes y guardado explícito.
- Guardado del GARC con verificación previa y posterior, detección de cambios externos y reemplazo atómico de archivo.

**Todavía no implementado:** edición visual de habilidades, compatibilidades, formas, EV yield, EXP, movimientos, entrenadores, tiendas, templates y otros módulos. Las opciones futuras están deshabilitadas deliberadamente para no aparentar funcionalidad.

## Ejecutar en Windows

Requiere el SDK de .NET 10 instalado:

~~~powershell
git switch feature/3ds-progressive-ui
dotnet run --project .\3DS.Progressive.App\3DS.Progressive.App.csproj -c Release
~~~

No se agregó el proyecto a pk3DS.slnx para que los builds habituales del editor original permanezcan intactos.

## Precauciones

1. Trabaja con una **copia** de tu carpeta de juego extraído, nunca con tu única copia.
2. Studio no extrae ROMs ni modifica archivos al abrir.
3. Los cambios se escriben **solo** después de presionar Guardar y confirmar.
4. Studio no crea backups automáticos.
5. El guardado del primer hito modifica la tabla GARC personal, no genera automáticamente una ROM .3DS.
6. Guarda y cierra otros editores antes de abrir el mismo proyecto; las modificaciones externas se detectan y bloquean el guardado.

## Pruebas pendientes

- Compilar y ejecutar la app en Windows .NET 10 (no hay compilador .NET Windows en el entorno de preparación).
- Abrir copias de Ultra Sol y Ultra Luna; comparar lectura de datos contra pk3DS.
- Modificar temporalmente stats y captura en una copia, guardar y reabrir.
- Comparar tabla GARC de antes y después: solo debe cambiar la entrada prevista y el agregado maestro.
- Validar el comportamiento con formas alternativas y otros títulos compatibles.

No se declara paridad ni release estable de Studio hasta completar estas pruebas.
