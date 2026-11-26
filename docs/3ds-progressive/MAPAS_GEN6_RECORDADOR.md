# Recordador de movimientos de X/Y: investigación e infraestructura protegida

**Estado, continuación del PR #144, 26-11-2026:** identificados el NPC, su script y los enlaces nativos en X e Y de forma independiente. Resuelta estáticamente la selección de Story96 y el despacho del menú auténtico. Preparado un experimento reversible de X fuera de Pueblo Fresco, con persistencia, deshacer, coexistencia con enfermera y reconstrucción/reextracción verificadas. **La operación permanece deshabilitada en producción:** el recorrido completo en juego sigue pendiente en X y en Y.

Se generó una imagen experimental privada de X; no se modificaron extracciones originales ni partidas. Se intentó el arranque en un Citra portátil aislado, sin llegar a validar el servicio en juego; el propietario realizará la prueba manual. No se incorpora contenido comercial como fixture. ORAS y Gen7 quedan excluidos.

## Plantilla original comprobada

La sonda de solo lectura reconoce los Title ID `0004000000055D00` (X) y `0004000000055E00` (Y). En cada copia se leyó el miembro 34 del GARC de zonas `a/0/1/2`, descomprimiendo LZ11 y comprobando el paquete `ZO`.

| Campo | X | Y |
|---|---|---|
| Zona | 034 | 034 |
| Lugar, índice 76 de la lista de ubicaciones | Pueblo Fresco | Pueblo Fresco |
| Área / matriz | 108 / 157 | 108 / 157 |
| NPC, índice binario / ID | 2 / 2 | 2 / 2 |
| Modelo, u16 `+0x02` | 54 | 54 |
| Movimiento, u16 `+0x04` | 0 | 0 |
| Bandera de aparición, u16 `+0x08` | 0 | 0 |
| Script de conversación, u16 `+0x0A` | **2261** | **2261** |
| Orientación, u16 `+0x0C` | 2 | 2 |
| Casilla, u16 `+0x28` / `+0x2A` | 14 / 3 | 14 / 3 |

El NPC 2 es el tercer registro de NPC, de 0x30 bytes; no es el segundo por numeración humana. La zona tiene tres NPC y ningún objeto anterior a ellos. El NPC de índice 1 tiene modelo 55 y script 2260; no se utiliza como plantilla del recordador.

Las rutas se contrastaron con `GARCReference_XY` y `Gen6MapBackend.ModelListArchive`: zonas `a/0/1/2`, modelos por área `a/1/4/9`. Las listas de modelos no se descomprimen por adivinar su primer byte.

## Script común y evidencia del servicio

El GARC `a/0/3/1` contiene 59 scripts comunes. Su miembro **42** es idéntico en las dos copias y contiene el caso **2261**, junto con los casos 2260–2265 y el caso -1.

En ambos ejecutables examinados se encontró en el desplazamiento de archivo `0x445B64` una fila de despacho de 16 bytes con rango 2260–2299, miembro común 42 y los campos restantes 1, 94 y 0. La dirección virtual correspondiente es `0x545B64`, al sumar la base `0x100000`. No deben confundirse direcciones virtuales y desplazamientos de archivo.

En las celdas AMX descomprimidas del miembro 42:

- El valor 2261 aparece en la celda 958, dentro de la tabla de casos.
- La interpretación de los saltos relativos desde la celda de valor conduce a la rama `0xE50`, que prepara un argumento cero y llama desde `0xE54` a la función `0x17B4`.
- Esa función utiliza los índices de texto 31, 32, 33, 34, 35 y 36. En el banco **Story96** corresponden a la presentación del recordador, su coste en Escamas Corazón, la confirmación, la selección de Pokémon y los rechazos de huevo o de ausencia de movimientos que recordar.
- El mismo banco contiene las frases del menú y del pago. Los ocho miembros Story96 por idioma son idénticos entre las copias de X y de Y examinadas.

Esto acredita la identidad de la rama del servicio original. **No acredita por sí solo su portabilidad a otra zona.** No se ha sustituido por un script de diálogo ni por la rutina de la enfermera.

El campo 94 de la fila de despacho **no es el selector usado por el auxiliar del recordador**. Story94 contiene textos del tutor de Cometa Draco. El auxiliar llama explícitamente a `MsgLoad(1, 4)`, cuya tabla nativa selecciona Story96 (ver la evidencia siguiente). El desensamblador histórico `Scripts.ParseScript` tampoco debe utilizarse como prueba suficiente de la tabla de casos: su lectura histórica no coincide con el orden documentado de recuento, defecto y pares valor/salto.

## Resolución de las funciones nativas

El intento inicial de buscar literalmente los hashes en el ejecutable no podía localizar el registro: los CRO conservan los **nombres** y punteros de función, y el hash se obtiene de esos nombres. La nueva herramienta resuelve esta cadena: import AMX → hash del nombre → cadena del CRO → relocación del puntero al nombre → relocación contigua del cuerpo nativo. No basta una coincidencia textual de nombre.

La recurrencia del hash se contrastó con la investigación primaria de [XAMX](https://github.com/BlastSlimey/XAMX/blob/master/xamx/amx_utils.py) y se verificó contra las cadenas y registros de ambas copias. No se incorpora su código ni su catálogo. El formato de relocaciones se contrasta con `CroRelocationMap` del propio proyecto.

Los desplazamientos siguientes son **de archivo dentro de DllField.cro**, no direcciones virtuales del ejecutable. Se resolvieron por separado; algunos cuerpos difieren cuatro bytes entre títulos.

| Import común 42 | Nombre acreditado | Hash | Cuerpo X | Cuerpo Y | Uso observado |
|---|---|---|---|---|---|
| 6 | `_CallWazaApps` | `A4883690` | `0x90D9C` | `0x90D9C` | Despacha la estructura de aplicación de movimientos |
| 31 | `MsgIsLoaded` | `CB54950C` | `0x5EB90` | `0x5EB94` | Espera la carga del contexto de mensajes |
| 33 | `MsgLoad` | `F845BDFF` | `0x98918` | `0x98918` | Carga el banco por selector explícito |
| 34 | `Assert_` | `EC163E7D` | `0x5DCC4` | `0x5DCC8` | Comprueba una condición; no selecciona el menú ni una zona |
| 35 | `MsgRelease` | `B9964356` | `0x5E8C0` | `0x5E8C4` | Libera el contexto previo |
| 37 | `FlagGet` | `133DAA6A` | `0x5DCEC` | `0x5DCF0` | Consulta el flag 410 para la introducción |
| 45 | `ItemGetNum` | `BF3BFFDD` | `0x58A44` | `0x58A44` | Consulta existencias del objeto 93 |
| 46 | `PokePartyCheckWazaRemind` | `965E2497` | `0x5D504` | `0x5D508` | Comprobación de movimientos recuperables del Pokémon seleccionado |
| 47 | `WordSetTemotiMonsNickName` | `A9BA4F61` | `0x98158` | `0x98158` | Prepara el apodo para los mensajes; no abre el menú |
| 48 | `ItemSub` | `8D5E8E31` | `0x5DDB0` | `0x5DDB4` | Resta el objeto de la bolsa |

### Texto y contexto: auxiliar `0xFF4`

`0x17B4` llama a `0xFF4` con **4**. El auxiliar libera un contexto anterior si existe, llama a `MsgLoad(1, 4)`, espera `MsgIsLoaded(4)` mediante el auxiliar `0xD04`, guarda el resultado en el global AMX `0x130` y llama a `Assert_(resultado != 0, 193)`. El 193 es un argumento de diagnóstico, no un banco de texto. El global AMX `0x12C` conserva el selector 4 para las esperas posteriores.

La tabla de salto de `MsgLoad` empieza en `0x989AC`: la entrada 4, en `0x989BC`, tiene una relocación que apunta a `0x98A1C`. Allí el código carga el valor `0x60` (96). Esa ruta no lee el ID de zona, el área ni el banco local para elegir ese número. Se contrastaron tanto la relocación como el cuerpo en X y en Y. El contexto resultante sigue utilizando los gestores ordinarios del campo; esto no equivale a ejecutar todo su ciclo de vida en el emulador.

### Menú, Pokémon, movimientos y cobro: `0x17B4`

El flujo reconstruido consulta el flag 410 y, si falta, muestra la introducción y lo activa. **410 no es la Escama Corazón ni una condición de acceso a Pueblo Fresco.** El objeto de pago es **93**, contrastado con la tabla de nombres de objetos de la copia inspeccionada.

La rutina comprueba `ItemGetNum(93)`, muestra la pregunta y, al aceptar, solicita `CallPokeSelect`. La selección se devuelve mediante la variable de trabajo `0x800C`; la rama de cancelación trata el valor 6. Las ramas posteriores verifican el Pokémon elegido, huevo y disponibilidad de movimientos a través de las funciones originales. La llamada 47 coloca su apodo en el registro de palabras 0.

El menú real se abre desde el auxiliar `0x234`: prepara una estructura de tres celdas `[0x4D, índice del Pokémon, 0x800F]` y la pasa a `_CallWazaApps` (nativa 6). En `DllField.cro`, `0x90DC4` compara el comando con `0x4D`; la rama `0x90F28` obtiene el Pokémon del equipo, prepara los argumentos, solicita la **aplicación 9** y registra el callback de resultado `0xA0950` mediante su relocación.

En el ejecutable de X se resuelve el import del gestor a `0x3B2A00`. La entrada 9 de su despacho apunta a `0x3B27A4`, que transfiere a `0x351838`. Esa rutina carga explícitamente `rom:/DllWazaOmoidashi.cro` desde `0x351880`. Se contrastó la misma rama de despacho por separado en el ejecutable de Y. El callback convierte el resultado de la aplicación y lo guarda en la variable pedida; la rama AMX de resultado 1 realiza `ItemSub(93, 1)`. No se ha reemplazado por una pantalla o diálogo propios.

El cuerpo de `ItemSub` usa el inventario del estado de juego y el número de objeto/cantidad recibidos, sin un ID fijo de zona. `WordSetTemotiMonsNickName` obtiene el Pokémon por índice del equipo y el registro de palabras recibido. `Assert_` solo comprueba la condición. En estos cuerpos y en la estructura enviada al menú no hay una referencia fija al NPC 2, zona 34 o área 108.

La conversación conserva además los auxiliares originales de modelo, orientación y mensajes del interlocutor actual. El experimento mantiene el modelo 54 original y su entrada íntegra del área. **No se declara auditado todo el grafo transitivo de cámara, animaciones, interfaz y guardado** por haber resuelto estos enlaces: esa es una parte de la validación pendiente en juego.

## Modelo del área

En cada juego, el modelo 54 aparece en 18 listas de área: 14, 25, 45, 73, 93, 94, 99, 108, 109, 113, 114, 128, 129, 131, 133, 139, 154 y 157. Todas sus entradas originales de 24 bytes son idénticas, también entre X e Y. El máximo encontrado es de 28 modelos por lista.

La infraestructura reutiliza `Gen6AreaModels` para localizar una entrada única, reutilizarla si el área ya tiene el modelo y añadirla si falta, conservando las demás entradas. Rechaza listas ilegibles, entradas ambiguas y ampliaciones por encima del máximo observado. No se considera suficiente añadir únicamente el registro de NPC: la prueba histórica de la enfermera en X se congelaba sin registrar su modelo en el área.

## Huellas de la evidencia

SHA-256 de recursos leídos, sin incluir sus bytes en el repositorio:

| Recurso | X | Y |
|---|---|---|
| Registro original de NPC 2 | `C7AB122F70C27E674959DF808E5B595C86AADABE64036B590BB4B993B0E13393` | Igual |
| Zona 034 descomprimida | `1F34861AB0B82199027D0B8B83AFEB7259A42908B803DF9FDF1F76ADA4644312` | Igual |
| Común 42, contenido del miembro | `4068FC8704B1DD9529B2351E0173EF5216A2F4B0225B1282B193498704744D6F` | Igual |
| Común 42, celdas AMX descomprimidas | `D920659E0104D90078C26DC18A683BDCE752A9424535BDF88131780B42616229` | Igual |
| Entrada original del modelo 54 | `1A7FC7BF2E1E93ED1093D166EFC2A823D5459039D28126E2B3139915A25361E4` | Igual |
| `.code.bin` | `0F6D3E1D66CBCD55D46FA453DB4B6AA489E03C3537C6B24026F2A53AF142797A` | `62554AC3D494F6A333D92B156AD239017114ECF95367A97DB57E251456692FE7` |

La huella del modelo se volvió a comprobar con la sonda antes de registrar esta tabla. Estas huellas identifican las copias inspeccionadas; no afirman compatibilidad con otras revisiones ni constituyen un perfil de escritura habilitado.

Story96 se encuentra en el miembro 96 de `a/0/8/0` a `a/0/8/7`, según el índice de idioma utilizado por `GameConfig`. Las huellas son iguales en X e Y:

| Índice de idioma | SHA-256 de Story96 |
|---|---|
| 0 | `BD4D1C509737E10D77EA7E387A3FB774FAB9A076C7F05F89FD5675580B9AA329` |
| 1 | `237B0BB54419DE448A67ABE4C907821181943F22A75AC9A6B1CCC568E849D556` |
| 2 | `C2E03639A6986DF5D67213A1E121051B3FB5EA8A0699A6447F204B923759E815` |
| 3 | `5AF8301CA250BF6724E06FB5B9102B29B7AD82A6D17F6D439100EF897C99CE5D` |
| 4 | `4AE040D6DF42F3426AD7D711127EDF0D10850BC995DB7FADFAF517CFDD8F3F2F` |
| 5 | `C0BA403E14B23B6A623269E299E6855609AD90E42325F808D5ACD2A1A5BC0F21` |
| 6 | `14812B46DFE158FD5159C93643793F194BD699051A2B4E7007F2860EFA5793FA` |
| 7 | `597D3C2DAFB8E785A267AFABA3A480A5364C3F749B972261DE624E9D1FD0A9D3` |

La sonda también recoge las huellas de los CRO. El despacho resuelto acredita `DllWazaOmoidashi.cro`; `DllWazaOboe.cro` sigue siendo un recurso candidato y no se presenta como dependencia del comando `0x4D`. No se afirma que esta sea una lista transitiva completa. No se trasladan offsets ni perfiles nativos entre juegos.

| CRO | SHA-256 X | SHA-256 Y |
|---|---|---|
| `DllField.cro` | `ED043EB6C5D4057D64CEEFDA99B59B0978412B982427551F6C99BF6084BE6489` | `F4D77DD5FE18888A51513E13864F84AF0FDA2D0A3420334CD0E886BC9F080BD3` |
| `DllWazaOmoidashi.cro` | `B76C2045E0D7988AEB1B82F132F9F5C38C7A622FEBDCEDD144C1538A1BB4E5DD` | Igual |
| `DllPokeList.cro` | `D673A3B9A38BF301C8337672EB7E67AECABB83DB5B8D2A989F26CDB754E22D38` | Igual |
| `DllWazaOboe.cro` | `EA0BD5FFBCBA1BFD74369519C021E5D997145E7CBA1D44CC48C91DD48C2C1DCD` | Igual |

## Integración realizada y límites

Se comparte únicamente la infraestructura que aporta una reutilización concreta con «Añadir enfermera»:

- `Gen6MapBackend.ValidateAddService`: casillas, colisiones con NPC/objetos/warps/activadores, orientación, límites de NPC y carga del modelo.
- `Gen6MapBackend.Reminder.cs`: identificación estructural del NPC original de zona 034 y modelo 54. Esta identificación no sustituye una acreditación del ejecutable y sus dependencias.
- `Map3dsNpcMoveService`: propuesta de inserción y modelo mediante `EditorProposal`, con el mecanismo existente de reconstrucción de GARC, comprobación de bytes y `ProjectHistory`.
- `Map3dsEditorView`, `Map3dsMover` y `Map3dsCanvas`: panel de posición compartido con coordenadas manuales, clic, orientación y vista previa diferenciada. En el escenario sintético, el recordador es un rombo azul y aparece junto a la enfermera.
- `Gen6MoveReminderProfile`: barrera de producción que **rechaza todos los proyectos reales** mientras la investigación esté incompleta. El servicio vuelve a comprobarla antes de preparar una escritura.

**El botón no aparece actualmente al abrir un proyecto real de X o Y.** Tampoco aparece en ORAS ni en Gen7. La acreditación explícita de fixtures sintéticos permite probar el recorrido de interfaz, pero no constituye un mecanismo para habilitar proyectos reales.

La propuesta preparada para una futura acreditación modificaría el GARC de zonas y, cuando fuese necesario, el de modelos, en la misma transacción del historial. No escribiría scripts ni textos. Un ejecutable de investigación separado ha ejercitado esos archivos reales en una copia aislada mediante `ProjectHistory`, incluyendo deshacer y reconstrucción; no habilita el servicio de guardado de producción ni acredita el funcionamiento en juego. La función no se declara compatible con Global Template ni Batch por el mero hecho de utilizar el historial.

Archivos de implementación y pruebas:

- `pk3DS.Core/Maps/MapEditorContracts.cs`
- `pk3DS.Core/Maps/Gen6/Gen6MapBackend.cs`
- `pk3DS.Core/Maps/Gen6/Gen6MapBackend.Reminder.cs`
- `3DS.Progressive.App/Services/Gen6MoveReminderProfile.cs`
- `3DS.Progressive.App/Services/Map3dsNpcMoveService.cs`
- `3DS.Progressive.App/Map3dsEditorView.cs`
- `3DS.Progressive.App/Map3dsMover.cs`
- `3DS.Progressive.App/Map3dsCanvas.cs`
- `3DS.Progressive.Tests/Map3dsReminderChecks.cs`
- `3DS.Progressive.Tests/Map3dsReminderUiChecks.cs`
- `3DS.Progressive.Tests/Program.cs` y `RunnerCatalog.cs`
- `tools/research/Inspect-XyMoveReminder.ps1`
- `tools/research/xy-reminder-native/`: resolución nativa, tests sintéticos del lector y ejecutable experimental separado (ver su [README](../../tools/research/xy-reminder-native/README.md)).

## Investigación reproducible

Desde la raíz del checkout, utilizar una copia autorizada ya extraída. La sonda no crea archivos ni reconstruye GARC o ROM:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/research/Inspect-XyMoveReminder.ps1 -Project 'RUTA_A_LA_COPIA_X' -Language 2 -StoryBank 96
powershell -NoProfile -ExecutionPolicy Bypass -File tools/research/Inspect-XyMoveReminder.ps1 -Project 'RUTA_A_LA_COPIA_Y' -Language 6 -StoryBank 96
```

`Language` es el índice interno de archivo: 2 inglés, 6 español. La copia debe contener `DecryptedExHeader.bin`, `ExtractedExeFS/.code.bin` y `ExtractedRomFS`. La salida JSON incluye las huellas y los hallazgos, además de indicar la prueba nativa pendiente. `-ShowText` muestra textos únicamente para inspección privada; no adjuntar esa salida, archivos comerciales ni rutas privadas al PR. El lector de texto de la sonda es auxiliar y no sustituye al parser completo del editor.

## Pruebas técnicas

En la primera entrega se ejecutó la sonda independientemente contra X y contra Y en modo de lectura. Coincidieron el registro original, el miembro común, la entrada de modelo y los ocho bancos de textos descritos. En esa entrega no hubo escrituras ni una imagen experimental. La continuación posterior se detalla debajo, sin atribuirle comprobación jugable.

El runner propio incorpora `--map3ds-reminder-only` y `--map3ds-reminder-ui-only`, registrados en `RunnerCatalog.cs` y [RUNNER_PRUEBAS.md](RUNNER_PRUEBAS.md). Son comprobaciones sintéticas y WPF fuera de pantalla. Cubren identificación/rechazo de la plantilla y otros scripts, exclusión de juegos, colisiones, límites, modelos, planificación sin escritura, inserción en memoria, conservación de otros registros/partes, reapertura del backend sintético y convivencia estructural con enfermeras. La interfaz comprueba clic, coordenadas, orientación, invalidación de la vista previa, ocultación y guardado delegado; este último no acredita persistencia real.

Validación de la primera entrega (todas las invocaciones terminaron con código 0):

| Comprobación | Resultado |
|---|---|
| `dotnet build pk3DS.slnx -c Release --no-restore` | 0 errores y 0 avisos |
| Build Release del proyecto `3DS.Progressive.Tests`, `UseAppHost=true` | App, Core, LegacyBatch y runner: 0 errores; aviso preexistente CS0675 en `NdsMapRenderChecks.Synthetic.cs:54` |
| `--map3ds-reminder-only` | 46 sintéticas aprobadas |
| `--map3ds-reminder-ui-only` | 15 WPF fuera de pantalla aprobadas |
| `--map3ds-nurse-only` / `--map3ds-nurse-ui-only` | 19 / 7 aprobadas |
| `--map3ds-gen6-only` / `--map3ds-gen6-ui-only` | 23 / 16 aprobadas |
| `--map3ds-catch-zones-only` / `--map3ds-catch-zones-ui-only` | 21 / 10 aprobadas |
| `--map3ds-gen7-nurse-only` / `--map3ds-gen7-nurse-ui-only` | 11 / 5 aprobadas; regresión del panel compartido, sin habilitar recordador Gen7 |
| `--visual-redesign-only` | 187 aprobadas |
| `git diff --check` | Sin errores |

Las compilaciones conservaron binlogs locales ignorados. Total de aquella entrega: 360 comprobaciones aprobadas, de ellas 61 nuevas del recordador. No se ejecutó toda la regresión general del repositorio. Foco físico, activación modal y DPI interactivo no comprobados.

## Continuación: pruebas y experimento fuera de Pueblo Fresco

Se volvió a compilar el proyecto de pruebas (incluye App, Core y LegacyBatch) y el ejecutable experimental: 0 errores; el runner mantiene el aviso preexistente CS0675 de `NdsMapRenderChecks.Synthetic.cs:54`. Los binlogs permanecen locales e ignorados.

| Comprobación repetida o nueva | Resultado |
|---|---|
| `--map3ds-reminder-only` | 46 aprobadas |
| `--map3ds-reminder-ui-only` | 15 aprobadas |
| `--map3ds-nurse-only` / `--map3ds-nurse-ui-only` | 19 / 7 aprobadas |
| `test_inspection.py` | 8 tests sintéticos aprobados: tablas AMX, hashes, celdas y rechazo de entradas inválidas |
| Inspección nativa de X / Y | Nombres, relocaciones, selector de Story96 y despacho del menú contrastados por separado |
| CLI experimental con Y / destino existente | Rechazados antes de crear o sobrescribir la salida |
| Persistencia experimental X | Historial, reapertura, conservación de entidades/partes/otras zonas y registro de modelo aprobados |
| Deshacer X | Restauración exacta de los dos GARC; también tras instalar enfermera y recordador juntos |
| Reconstrucción y reextracción X | 656 archivos RomFS/ExeFS idénticos a la copia modificada; NPC y modelo reconocidos; barrera de producción cerrada |
| Origen X | 659 archivos copiados y verificados; origen releído sin cambios tras cada fase |
| Comprobación final `check` | Ocho huellas Story96, plantilla/modelo originales y barrera de producción verificados otra vez sin escrituras |

El workflow Windows incorpora ahora las dos suites del recordador, los ocho tests de Python y la compilación del ejecutable experimental; nunca utiliza datos comerciales en CI. Estas 87 comprobaciones locales del runner y 8 tests de Python corresponden a esta continuación. No se repitieron las otras suites de las 360 comprobaciones históricas. Una repetición de `persistence` sobre una copia ya modificada rechazó la colisión; se deshizo la entrada previa y se repitió satisfactoriamente desde los GARC originales. La herramienta ahora exige expresamente esa base intacta.

### Candidato privado de X

La copia experimental se encuentra en `artifacts/native-research/PruebasRecordador-X-001/Proyecto`, dentro del checkout usado para el PR. Destino: **zona 259, Ruta 2; área 2; casilla X=463, Y=458; orientación 180°**. Se copió el registro original con script 2261 y se añadió la entrada original del modelo 54 al área. El candidato final contiene solo el recordador nuevo; la convivencia con la enfermera se comprobó y deshizo antes de generarlo.

Solo cambiaron estos archivos de juego, registrados con sus huellas anteriores/posteriores en `evidence/changed-files.json`:

| Archivo | SHA-256 anterior | SHA-256 posterior |
|---|---|---|
| `ExtractedRomFS/a/0/1/2` | `DC0B7A0DD2764480EAEBD4C1C057D1FA8E268CCDF8953945713BB091925190D2` | `83222B273A8320B8E37CDBA84F7BA903E96367FB761D907FB353E80CB2FB0A03` |
| `ExtractedRomFS/a/1/4/9` | `BD3924B48AC6A91354A5D6160931B677CD18D40B64B1ACE33DDFF1234BFBF50C` | `03B88CE373ACA3738CECCD94C1C72CFAC34B69F0BD1F0A755BA5A953E962199A` |

Imagen nueva: `artifacts/native-research/X-recordador-ruta2.3ds`, **1 801 064 448 bytes**, SHA-256 `D97161428864C1804C4C741AE13D8DC79ACD2B9224D504236123C04F2D9C5B80`. La reextracción está en `artifacts/native-research/ReextraidaRecordador-X-001`. Esos archivos, los logs y las evidencias con rutas privadas están excluidos de Git. No se modificaron scripts, ejecutables ni textos; tampoco los originales ni partidas.

El [README de las herramientas](../../tools/research/xy-reminder-native/README.md) permite reproducir preparación, persistencia, reconstrucción, comparación y deshacer sobre carpetas nuevas. Solo el ejecutable de investigación admite esta copia X fijada por huellas. Y se rechaza deliberadamente, aunque sus enlaces nativos se hayan identificado.

## Bloqueo restante y siguiente evidencia necesaria

La resolución estática explica la selección de Story96, el menú original, la selección del equipo y la resta de una Escama Corazón. No se ha ejecutado el recorrido completo fuera de Pueblo Fresco ni se ha observado el ciclo de vida de sus gestores de campo, interlocutor, cámara, aplicación y guardado. No se afirma una ausencia universal de dependencias transitivas de zona por no ver un ID fijo en las funciones examinadas.

La siguiente evidencia necesaria es una ejecución controlada del candidato X. Si falla, capturar log, zona/estado/acción exacta, pantalla y resultado; comparar con el NPC original y seguir el inicio/fin de `MsgLoad`, `CallPokeSelect`, `_CallWazaApps`, su callback e `ItemSub` en una sesión de depuración. No se realizaron trazas dinámicas durante esta entrega.

Solo tras confirmar el flujo jugable se podrá acreditar un perfil X y completar su habilitación de producción. Y necesitará su propio experimento y validación, no una extrapolación. El botón permanece oculto en todos los proyectos reales; ORAS y Gen7 siguen fuera del alcance. No se han modificado otros PR ni publicado una versión.

## Prueba jugable pendiente y restauración

El botón sigue oculto por diseño. Utilizar la imagen experimental privada ya preparada, sin modificar la instalación o el guardado habitual:

1. Crear una instancia portátil o un perfil de emulador de prueba con directorio de usuario independiente. Copiar allí una partida autorizada de X, conservando el original y un respaldo; comprobar la ruta de guardado antes de arrancar. Se preparó Citra Nightly 2104 portátil en `artifacts/e144` de la carpeta principal del proyecto, con una copia verificada del guardado de X. El primer arranque no reconoció `main` por faltar el archivo hermano `00000001.metadata`; se restauró solo la copia y se incorporó ese metadato. Un segundo arranque registra carga de `DllTitle`/`DllSequence`, sin acreditar acceso al campo ni al servicio. El guardado original se comprobó intacto. El propietario se hará cargo de la prueba manual; no se continúa la automatización del juego.
2. Verificar el SHA-256 de `X-recordador-ruta2.3ds` contra el indicado arriba y abrir esa imagen en la instancia de prueba.
3. Llegar a Ruta 2 (zona interna 259). Buscar el modelo 54 en la casilla del mapa X=463, Y=458, orientado hacia abajo. Entrar/salir y comprobar aparición, colisiones y ausencia de congelación. Si el acceso visual a esa casilla presenta un problema, registrarlo y preparar otra copia con una casilla validada; no cambiar la extracción original.
4. Hablar llevando una Escama Corazón y un Pokémon con un movimiento recuperable. Confirmar que aparece el selector original de Pokémon y después la lista original de movimientos. Anotar especie, nivel, movimiento disponible y cantidad inicial del objeto 93.
5. Cancelar en la pregunta, en la selección de Pokémon y en la lista de movimientos: comprobar retorno a campo y ausencia de cobro o cambios de movimientos.
6. Completar un recuerdo válido. Comprobar que cambia el movimiento elegido y se consume exactamente una Escama Corazón. Salir, volver a hablar y verificar que sigue funcionando.
7. Probar sin Escama Corazón, con un huevo y con un Pokémon sin movimientos recuperables. Registrar mensajes y confirmar que no hay cobro ni bloqueo. Comparar el comportamiento con el recordador original usando la misma copia de partida.
8. Probar una partida anterior y otra posterior a conocer al recordador original para contrastar el flag de introducción 410. Guardar solo en la instancia de prueba, cerrarla y cargar de nuevo: comprobar movimiento, bolsa, NPC y estabilidad.
9. Registrar versión del emulador, Title ID, SHA de la imagen, estado de partida y resultados de cada caso. La convivencia de ambos servicios tiene evidencia de archivos y deshacer, pero su prueba jugable conjunta requiere un candidato adicional; no está incluida en la imagen final de solo recordador.
10. Repetir la investigación experimental y el protocolo por separado en Y antes de habilitarlo. La herramienta actual rechaza su escritura.

Para restaurar exactamente el proyecto experimental X, ejecutar desde la raíz de este checkout:

```powershell
$experimentTool = 'tools/research/xy-reminder-native/playable/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll'
dotnet $experimentTool undo artifacts/native-research/PruebasRecordador-X-001 6a549e66-1aef-40de-a23d-f70e61b77823
```

El historial restaura juntos zonas y modelos. La imagen reconstruida ya generada no cambia al deshacer el proyecto; una imagen restaurada requeriría otra reconstrucción a una ruta nueva. El origen no requiere restauración porque permaneció intacto. La compatibilidad jugable de **X está pendiente** y la de **Y está pendiente**.
