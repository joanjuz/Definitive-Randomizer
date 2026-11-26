# Recordador de movimientos de X/Y: investigación e infraestructura protegida

**Estado, 26-11-2026:** identificado el NPC original y su script en copias de Pokémon X y Pokémon Y de forma independiente. Implementada infraestructura estructural y de interfaz comprobable con fixtures sintéticos. **La operación permanece deshabilitada para proyectos reales:** faltan acreditar las dependencias nativas de contexto, textos y menú fuera de Pueblo Fresco. No es una entrega jugable ni una función disponible al usuario.

No se ha generado una ROM candidata, modificado una extracción original ni realizado una prueba en el emulador. No se incorpora contenido comercial como fixture. ORAS y Gen7 quedan excluidos.

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

El campo 94 de la fila de despacho **no se documenta como el banco de textos del recordador**. Se comprobó que Story94 contiene textos del tutor de Cometa Draco. La selección efectiva del contexto y de Story96 requiere seguir las llamadas nativas del script. El desensamblador histórico `Scripts.ParseScript` tampoco debe utilizarse como prueba suficiente de la tabla de casos: su lectura histórica no coincide con el orden documentado de recuento, defecto y pares valor/salto.

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

La sonda también recoge las huellas de `DllField.cro`, `DllWazaOmoidashi.cro`, `DllPokeList.cro` y `DllWazaOboe.cro` como **recursos candidatos** para la investigación nativa. No se afirma que formen una lista completa de dependencias. Los tres últimos son iguales en ambas copias; `DllField.cro` difiere. No se trasladan offsets ni perfiles nativos entre juegos.

## Integración realizada y límites

Se comparte únicamente la infraestructura que aporta una reutilización concreta con «Añadir enfermera»:

- `Gen6MapBackend.ValidateAddService`: casillas, colisiones con NPC/objetos/warps/activadores, orientación, límites de NPC y carga del modelo.
- `Gen6MapBackend.Reminder.cs`: identificación estructural del NPC original de zona 034 y modelo 54. Esta identificación no sustituye una acreditación del ejecutable y sus dependencias.
- `Map3dsNpcMoveService`: propuesta de inserción y modelo mediante `EditorProposal`, con el mecanismo existente de reconstrucción de GARC, comprobación de bytes y `ProjectHistory`.
- `Map3dsEditorView`, `Map3dsMover` y `Map3dsCanvas`: panel de posición compartido con coordenadas manuales, clic, orientación y vista previa diferenciada. En el escenario sintético, el recordador es un rombo azul y aparece junto a la enfermera.
- `Gen6MoveReminderProfile`: barrera de producción que **rechaza todos los proyectos reales** mientras la investigación esté incompleta. El servicio vuelve a comprobarla antes de preparar una escritura.

**El botón no aparece actualmente al abrir un proyecto real de X o Y.** Tampoco aparece en ORAS ni en Gen7. La acreditación explícita de fixtures sintéticos permite probar el recorrido de interfaz, pero no constituye un mecanismo para habilitar proyectos reales.

La propuesta preparada para una futura acreditación modificaría el GARC de zonas y, cuando fuese necesario, el de modelos, en la misma transacción del historial. No escribiría scripts ni textos. Las comprobaciones sintéticas no demuestran todavía el guardado real, deshacer real, reconstrucción de ROM ni funcionamiento en el juego del recordador. La función no se declara compatible con Global Template ni Batch por el mero hecho de utilizar el historial.

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

## Investigación reproducible

Desde la raíz del checkout, utilizar una copia autorizada ya extraída. La sonda no crea archivos ni reconstruye GARC o ROM:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/research/Inspect-XyMoveReminder.ps1 -Project 'RUTA_A_LA_COPIA_X' -Language 2 -StoryBank 96
powershell -NoProfile -ExecutionPolicy Bypass -File tools/research/Inspect-XyMoveReminder.ps1 -Project 'RUTA_A_LA_COPIA_Y' -Language 6 -StoryBank 96
```

`Language` es el índice interno de archivo: 2 inglés, 6 español. La copia debe contener `DecryptedExHeader.bin`, `ExtractedExeFS/.code.bin` y `ExtractedRomFS`. La salida JSON incluye las huellas y los hallazgos, además de indicar la prueba nativa pendiente. `-ShowText` muestra textos únicamente para inspección privada; no adjuntar esa salida, archivos comerciales ni rutas privadas al PR. El lector de texto de la sonda es auxiliar y no sustituye al parser completo del editor.

## Pruebas técnicas

Se ejecutó la sonda independientemente contra X y contra Y en modo de lectura. Coincidieron el registro original, el miembro común, la entrada de modelo y los ocho bancos de textos descritos. No se realizaron escrituras sobre ninguna extracción ni se produjo una ROM de prueba; por tanto, no hubo nada que restaurar en ellas.

El runner propio incorpora `--map3ds-reminder-only` y `--map3ds-reminder-ui-only`, registrados en `RunnerCatalog.cs` y [RUNNER_PRUEBAS.md](RUNNER_PRUEBAS.md). Son comprobaciones sintéticas y WPF fuera de pantalla. Cubren identificación/rechazo de la plantilla y otros scripts, exclusión de juegos, colisiones, límites, modelos, planificación sin escritura, inserción en memoria, conservación de otros registros/partes, reapertura del backend sintético y convivencia estructural con enfermeras. La interfaz comprueba clic, coordenadas, orientación, invalidación de la vista previa, ocultación y guardado delegado; este último no acredita persistencia real.

Validación final ejecutada (todas las invocaciones terminaron con código 0):

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

Las compilaciones conservaron binlogs locales ignorados. Total: 360 comprobaciones aprobadas, de ellas 61 nuevas del recordador. No se ejecutó toda la regresión general del repositorio. Foco físico, activación modal y DPI interactivo no comprobados. Ninguno de esos resultados acredita el servicio jugable, la persistencia real del recordador ni una reconstrucción de ROM.

## Bloqueo y siguiente experimento necesario

La función `0x17B4` del común 42 utiliza auxiliares compartidos. El auxiliar `0xFF4` interviene en la inicialización del contexto y llama a los índices nativos 33/34. La rama del recordador utiliza además las llamadas nativas 47/48. No se han resuelto con certeza sus implementaciones y dependencias de zona, cámara, entidad hablante, banco de textos y selección de menú.

Se buscaron sus identificadores importados en el ejecutable, los CRO de nivel superior y `static.crs`, sin encontrar una correspondencia literal que permitiese identificar esas implementaciones. La coincidencia del NPC original y de las frases no elimina esa incertidumbre. No se ha demostrado que no existan condiciones de historia dentro de las funciones nativas, aunque la bandera de aparición del registro sea cero.

El siguiente experimento debe resolver la selección de contexto y el menú mediante una traza del NPC original: seguir las llamadas 33/34/47/48 y los recursos que cargan, registrar las referencias a entidades y zona, y contrastar el recorrido en X y en Y. Una prueba controlada posterior puede insertar el registro en una **copia aislada** de una zona distinta, con el modelo registrado y una imagen de salida nueva. No se debe cambiar la barrera de producción basándose solo en fixtures sintéticos o en el número de script.

## Prueba jugable posterior y restauración

No hay instrucciones utilizables para pulsar el botón en esta entrega, porque está oculto por la barrera de producción. No se ha preparado una imagen candidata. Tras resolver y documentar la acreditación, realizar primero en X y después, por separado, en Y:

1. Duplicar la extracción y respaldar la partida de prueba. Elegir una zona distinta de Pueblo Fresco.
2. Añadir el recordador mediante clic o coordenadas manuales, elegir dirección y revisar la vista previa, el script 2261 y la incorporación del modelo si falta.
3. Confirmar y guardar por el historial; reabrir el proyecto y comprobar el nuevo NPC y los demás registros.
4. Reconstruir a una ruta nueva y verificar que los archivos de zonas/modelos de la imagen coinciden con los del proyecto editado.
5. En el emulador, entrar en la zona y comprobar aparición y ausencia de congelación.
6. Hablar con el NPC con un Pokémon que tenga un movimiento recuperable y una Escama Corazón; abrir el menú, recordar el movimiento y comprobar el coste original.
7. Comprobar cancelación, ausencia de Escama Corazón, huevo y Pokémon sin movimientos recuperables; guardar y volver a cargar la partida de prueba.
8. Probar una enfermera y un recordador en la misma zona; confirmar ambos servicios de forma independiente.
9. Deshacer desde el historial y verificar la restauración exacta de zonas y modelos. Reconstruir otra salida restaurada si se necesita contrastarla.

La restauración prevista es la del historial transaccional existente, para ambos archivos juntos. No se debe editar ni sobrescribir una ROM original, una extracción original o una partida habitual para ejecutar este protocolo. Hasta completar estas comprobaciones, la compatibilidad jugable de **X está pendiente** y la de **Y está pendiente**.
