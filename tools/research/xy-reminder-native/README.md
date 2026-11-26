# Recordador X/Y: evidencia nativa y experimento separado

La aplicación mantiene cerrada la admisión de producción. Estas herramientas no la habilitan.
No se distribuyen ROMs, instrucciones originales, textos ni partidas. Las salidas privadas deben
permanecer en `artifacts/` o en otra carpeta excluida de Git.

## Inspección sin escrituras

`inspect_native_bindings.py PROJECT --resolve-names` resuelve nombres de importación AMX contra
las cadenas de los CRO del propio juego y sus relocaciones internas. La pareja de relocaciones
nombre/función permite llegar al cuerpo nativo, no solo asignar un nombre por parecido.
`--profile` emite huellas y deja explícito que no existe admisión de producción.

Se contrastó la recurrencia del hash con la investigación primaria de
[XAMX](https://github.com/BlastSlimey/XAMX/blob/master/xamx/amx_utils.py): multiplicar por 131,
aplicar XOR con el siguiente byte y conservar 32 bits. No se importa su código ni su catálogo.
Las relocaciones se contrastan con los contratos existentes de `CroRelocationMap` del proyecto.

Los modos `--disassemble VA LENGTH`, `--cro NAME`, `--amx-cells START LENGTH`, `--relocations`,
`--link-slots`, `--word-refs`, `--read-words` y `--xref-string` son diagnósticos privados. Las listas
de instrucciones/textos no deben adjuntarse al PR. Solo el modo de desensamblado necesita Capstone;
la investigación local utilizó 5.0.6. Los tests son estándar `unittest`:

```powershell
python -m unittest discover -s tools/research/xy-reminder-native -p test_inspection.py -v
```

El lector usa las posiciones reales de las tablas AMX (`+0x24` y `+0x28`), no interpreta los flags
`+0x08` como un puntero. Los tests incluyen un public export anterior a los imports para detectar
esa confusión. Ninguno contiene bytes originales del juego.

## Experimento de X, sin cambiar producción

`playable/MoveReminder.Experiment.csproj` es un ejecutable de investigación separado, con el mismo
acceso de ensamblado amigo ya utilizado por otras herramientas del repositorio. Reutiliza
`Gen6ZoneEdit`, las validaciones de colocación/modelo, `GarcTransaction`, `ProjectHistory`,
`RestorePoints`, `GameRomRebuildService` y `GameRomExtractionService`.

Solo acepta el Title ID y las huellas exactas de la copia X investigada. Rechaza Y. Antes de
escribir exige una carpeta de salida nueva cuyo nombre empiece por `PruebasRecordador-`, sin
superposición con el origen ni enlaces. Copia RomFS, ExeFS y cabeceras necesarias, verificando cada
archivo. La plantilla, la entrada del modelo y los ocho bancos Story96 se verifican por SHA-256.
La habilitación del backend estructural existe solo en este ejecutable: el backend creado por
`FromConfig` y la barrera de guardado de la aplicación continúan denegados, y se comprueba expresamente.

Ejemplo desde la raíz del checkout, con rutas de salida nuevas:

```powershell
dotnet build tools/research/xy-reminder-native/playable/MoveReminder.Experiment.csproj -c Release -p:UseAppHost=true '-bl:experiment-{}.binlog'
$experimentTool = 'tools/research/xy-reminder-native/playable/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll'
dotnet $experimentTool prepare 'RUTA_COPIA_X' artifacts/PruebasRecordador-X-nueva 259 463 458
dotnet $experimentTool persistence artifacts/PruebasRecordador-X-nueva
dotnet $experimentTool check artifacts/PruebasRecordador-X-nueva
dotnet $experimentTool build artifacts/PruebasRecordador-X-nueva artifacts/X-recordador-nuevo.3ds
dotnet $experimentTool verify artifacts/PruebasRecordador-X-nueva artifacts/X-recordador-nuevo.3ds artifacts/ReextraidaRecordador-X-nueva
```

`persistence` comienza únicamente desde los GARC originales de la copia. Comprueba previsualización
sin escritura, inserción, modelos, preservación de zonas/entidades/partes, reapertura y deshacer
exacto. Instala enfermera y recordador juntos, los deshace en orden y deja finalmente solo el
recordador. Registra los cambios en `evidence/changed-files.json`, junto con la entrada de historial.
`build` crea una imagen nueva; `verify` la reextrae en otra carpeta nueva y compara todos los archivos.

El origen se relee y sus huellas se comprueban tras cada fase. Si una fase falla, no se deben repetir
operaciones de instalación sobre una copia parcialmente modificada: inspeccionar su historial y
deshacer la última entrada válida o preparar otra carpeta nueva.

Restauración de la última instalación, sin tocar el origen:

```powershell
$lastReminderEntry = Get-Content artifacts/PruebasRecordador-X-nueva/evidence/reminder-entry.json -Raw | ConvertFrom-Json
dotnet $experimentTool undo artifacts/PruebasRecordador-X-nueva $lastReminderEntry.Id
```

La imagen reconstruida es independiente del proyecto. Deshacer restaura el proyecto; no sobrescribe
ni borra imágenes ya generadas. Consultar el protocolo jugable y los resultados reales en
[`MAPAS_GEN6_RECORDADOR.md`](../../../docs/3ds-progressive/MAPAS_GEN6_RECORDADOR.md).
