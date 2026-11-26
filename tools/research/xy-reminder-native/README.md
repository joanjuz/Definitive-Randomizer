# Recordador X/Y: evidencia nativa y experimento separado

La aplicación acredita únicamente el perfil X confirmado por el propietario. Y permanece bloqueado. Estas herramientas no cambian esa decisión ni habilitan perfiles.
No se distribuyen ROMs, instrucciones originales, textos ni partidas. Las salidas privadas deben
permanecer en `artifacts/` o en otra carpeta excluida de Git.

## Inspección sin escrituras

`inspect_native_bindings.py PROJECT --resolve-names` resuelve nombres de importación AMX contra
las cadenas de los CRO del propio juego y sus relocaciones internas. La pareja de relocaciones
nombre/función permite llegar al cuerpo nativo, no solo asignar un nombre por parecido.
`--profile` emite huellas y deja explícito que el lector no concede admisión de producción.

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

## Experimentos separados y validación de producción X

`playable/MoveReminder.Experiment.csproj` es un ejecutable de investigación separado, con el mismo
acceso de ensamblado amigo ya utilizado por otras herramientas del repositorio. Reutiliza
`Gen6ZoneEdit`, las validaciones de colocación/modelo, `GarcTransaction`, `ProjectHistory`,
`RestorePoints`, `GameRomRebuildService` y `GameRomExtractionService`.

`prepare` acepta únicamente las huellas de X; `prepare-y` exige las de Y, comprobadas independientemente (incluidos `.code.bin`, `DllField.cro` y `static.crs`). Los títulos no se intercambian. Antes de
escribir exige una carpeta de salida nueva cuyo nombre empiece por `PruebasRecordador-`, sin
superposición con el origen ni enlaces. Copia RomFS, ExeFS y cabeceras necesarias, verificando cada
archivo. La plantilla, la entrada del modelo y los ocho bancos Story96 se verifican por SHA-256.
La marca del backend estructural experimental no autoriza al servicio de escritura de la aplicación.
Se comprueba expresamente que la aplicación admite X y rechaza Y. El modo `production` utiliza la
fábrica y el guardado WPF reales de X, con las huellas y comprobaciones de producción.

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

`persistence` y `production` comienzan únicamente desde los GARC originales de la copia. Comprueba previsualización
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

Para comprobar el servicio de producción de X, usar otra carpeta nueva y sustituir `persistence`
por `production`. Ese modo verifica propuestas sin escritura, cambios externos de las 17 dependencias,
modelo presente/ausente, historial/deshacer, preservación, convivencia con enfermera y el guardado real
WPF fuera de pantalla. La protección transaccional conserva abiertas para lectura las dependencias que
no se escriben, impidiendo que cambien durante el commit. Al terminar deja un recordador guardado desde
la vista real y contrasta los dos GARC con los del candidato X aprobado.

Y se prepara de forma explícita y separada; no ejecutar `production` para Y:

```powershell
dotnet $experimentTool prepare-y 'RUTA_COPIA_Y' artifacts/PruebasRecordador-Y-nueva 259 463 458
dotnet $experimentTool persistence artifacts/PruebasRecordador-Y-nueva
dotnet $experimentTool build artifacts/PruebasRecordador-Y-nueva artifacts/Y-recordador-nuevo.3ds
dotnet $experimentTool verify artifacts/PruebasRecordador-Y-nueva artifacts/Y-recordador-nuevo.3ds artifacts/ReextraidaRecordador-Y-nueva
```

La verificación reabre el proyecto y exige que la admisión coincida con la evidencia: X acreditado,
Y bloqueado. El experimento Y no constituye una acreditación jugable hasta la prueba del propietario.
