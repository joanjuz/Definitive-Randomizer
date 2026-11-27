# Persistent Items X: candidato experimental de dos condiciones

Fecha: 27/11/2026. Continuación del PR #145, rama `feature/xy-persistent-items`.
Producción deshabilitada. Y sin candidato. No hay validación jugable del parche.

## Especificación corregida

El objetivo es conservar la distribución de objetos equipados **antes** del combate,
también después de Lanzamiento, Desarme o Truco. No se excluyen estas acciones por
política de procedencia. El estudio anterior de snapshots y seguimiento de eventos
se conserva como investigación histórica; sus invalidaciones no son requisitos de
este experimento. No se añade memoria, hooks, snapshots ni código ejecutable nuevo.

Se revisaron `Gen7PersistentConsumablesPatcher` y `GamePersistentItemsService`:
ambos cambios USUM modifican exclusivamente las condiciones de dos saltos, BNE a
BHI y BEQ a B. No se trasladan sus offsets, registros ni constantes de modalidad.

## Correspondencia nativa demostrada en X/Y

Todos los offsets siguientes son de archivo `DllBattle.cro`, no VA. Para depuración:
`VA = T + (offset - 0x180)`, con `T` medido en la sección cargada.

| Offset | Original | Experimental | Destino conservado | Efecto |
|---|---|---|---|---|
| `0x14854` | `1A000020` BNE | `8A000020` BHI | `0x148DC` | Tras CMP modo,1, omitir la conciliación solamente si el byte es mayor que 1 sin signo. |
| `0x148AC` | `0A000025` BEQ | `EA000025` B | `0x14948` | Restaurar el objeto original sin condicionar por la clasificación consumible. |

En `0x14848` se obtiene el contexto desde `[r9+0x10]`; `0x1484C` lee su byte de
modo, y `0x14850` compara con 1. BNE excluía 0 y todos los valores distintos de 1;
BHI admite 0 y 1, excluyendo 2..255. No es una comparación con signo.

La entrada exterior `0x1469C` ya separa los modos 0/1 y conserva el bypass del
byte `+0x4B == 1`. No se altera. Las trazas originales de X acreditan **0 salvaje**
y **1 Liga**; esto no demuestra el nombre de todos los otros valores ni todos los
subtipos que pueden compartirlos. Maison, PvP y otros modos no están acreditados.

La conciliación obtiene la referencia original desde `meta+0x4C`, selecciona
el Pokémon por índice mediante el helper `0x264`, y obtiene la representación
de combate desde `r8`. En `0x14888` consulta el objeto original, conservado en
`r6`. El parámetro 17 consulta el byte `+0x0E` del registro de objeto mediante
`0x3AC`; CMP en `0x148A4`, NOP en `0x148A8`, y BEQ en `0x148AC` controlaban su
restauración. En `0x14948` se coloca **r6** en r1, la representación destino r10
en r0, y `0x14950` llama al setter nativo `0x6B4`. El bucle vuelve a `0x148D0`.

La modificación conserva esa fuente original y ese setter. No restaura el objeto
de una representación ya consumida. A diferencia del diseño anterior, **sí**
sobrescribe un resultado nativo no nulo para recuperar el objeto inicial: es la
semántica corregida. Las capturas X-07 (activación) y X-08 (Lanzamiento) conservan
158 en la referencia original y 0 después de la pérdida, lo que permite probar
ambos caminos con el mismo cambio. No se parchean las acciones durante el combate.

## Evidencia ejecutable y sus límites

`replay_two_gates.py` ejecuta instrucciones originales de esa conciliación y
de su entrada en Unicorn. Los imports de consulta/setter son contratos simulados;
no se ejecuta una batalla completa ni se demuestra el estado de la Bolsa.

Resultado X: 256 comparaciones de modo; 20 combinaciones de entrada exterior;
14 casos de conciliación (siete casos en modos 0/1), incluidos los valores de
X-07/08, transferencia entre dos miembros, intercambio, dos consumos, objeto no
consumido y equipo vacío. En 0/1 vuelve la distribución original; 2..255 conservan
el resultado original en el replay. No hay código nuevo de transferencia ni
doble contabilización dentro del equipo simulado. Falta comprobar el combate
completo, la Bolsa y la transferencia con el rival en juego.

## Perfiles e integridad

El preparador admite exclusivamente el conjunto **X** observado y el Title ID
`0004000000055D00`, verificado contra NCCH y exheader. Comprueba las ocho
dependencias del perfil existente, firmas únicas, instrucciones originales y
SHA-256 del CRO completo normalizado. Y (`0004000000055E00`) sigue rechazado,
aunque su CRO observado sea igual: sus otras dependencias son diferentes.

* CRO original: `9C07A3342DFBA4ADFC3569271D65FE9FAD88F1E271D5BA650179377E60D0A5A1`.
* CRO experimental: `84E125B4DEB74D523DFC742E3A1D46AE3601B5B24A2F9E4D5CF0EA80A7AB7A42`.

Solo difieren los bytes de condición `0x14857`, `0x148AF` y los campos SHA-256
internos `0x00..0x7F`. Se recalculan los cuatro hashes CRO; se contrastan con
`CroIntegrity.Rebuild`. Tamaño, CRO0, segmentos y relocaciones permanecen iguales.
Se conserva exactamente **DllBattle.cro**, sin segunda copia de otra capitalización.

La CRR se conserva byte a byte. Su registro del CRO ya no acredita el candidato:
**no se afirma una CRR válida ni se fabrica una firma RSA**. El cargador HLE de
Citra 2104 estudiado devuelve éxito en VerifyHash/LoadCRR sin validación real;
esto permite este experimento exclusivamente en emulador. No acredita consola,
cargador real ni cualquier versión de Lime3DS.

## Preparación reversible y reconstrucción

Se usa una extracción nueva de la ROM X original autorizada, no el proyecto con
ediciones anteriores. `experimental_two_gates.py` produce assets en otra carpeta,
sin escribir entradas. Rechaza estados parciales, firmas ausentes/duplicadas,
modificaciones externas, dependencias cruzadas y cambios entre inspección y lectura.
La instalación completa es idempotente; el rollback de dos instrucciones más
hashes exige la huella original exacta.

El CLI `playable/PersistentItems.Experiment.csproj` usa los servicios existentes:
`ProjectHistory.Commit`, `RestorePoints.Undo`, extracción y rebuild. Comprueba
instalación, deshacer exacto, inventario original completo y reaplicación; relee
todos los recursos. No sustituye un archivo vanilla sobre modificaciones ajenas.
El candidato estricto rechaza otras ediciones: **no se acredita todavía convivencia
con tabla de tipos, Escudo Real u otros parches**. El instalador de producción y
su vista WPF siguen pendientes y deshabilitados.

Comandos desde el worktree (PowerShell; destinos nuevos):

```powershell
dotnet build tools/research/xy-persistent-items/playable/PersistentItems.Experiment.csproj -c Release -p:UseAppHost=true '-bl:artifacts/persistent-research/build-experiment-{}.binlog'
$cli = 'tools/research/xy-persistent-items/playable/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll'
$root = 'D:/Progra/Pokemon/Randomizer/PruebasPersistentItems-X-01'
dotnet $cli extract D:/Nintendo/3DS/Juegos/PokemonX/Rom.3ds $root
python tools/research/xy-persistent-items/experimental_two_gates.py --project "$root/Proyecto" --output "$root/evidence/two-gates-assets"
dotnet $cli stage $root "$root/evidence/two-gates-assets"
dotnet $cli build $root "$root/Pokemon-X-PersistentItems-EXPERIMENTAL.3ds"
dotnet $cli verify $root "$root/Pokemon-X-PersistentItems-EXPERIMENTAL.3ds" "$root/Reextraido"
python -m unittest discover -s tools/research/xy-persistent-items -p 'test_*.py' -v
# Solo con recursos originales y Unicorn instalado; no es prueba jugable:
python tools/research/xy-persistent-items/replay_two_gates.py artifacts/pi145-x/stock
```

No ejecutar `extract` o `verify` sobre destinos anteriores: no se sobreescriben.
Los manifests privados `evidence/` registran origen, assets, historial, hashes e
inventarios. ROM, guardados y dependencias comerciales no se incorporan a Git.

## Prueba jugable posterior al candidato

Usar solamente el emulador portable de la carpeta experimental y su guardado
copiado; no abrir por asociación de archivos en el Lime habitual. Cerrar otros
emuladores para evitar confundir ventanas. No usar savestates ni conexión online.

1. Anotar equipo y cantidades de Bolsa antes de entrar. La copia previa a X-08
   tiene Gardevoir con Baya Zidra y Lanzamiento, y 992 bayas en Bolsa. Confirmarlo
   en pantalla; el guardado no se da por válido solo por su nombre.
2. En combate normal de Liga activar la baya. Confirmar que se consume y no vuelve
   a activarse durante **ese** combate. Al terminar, comprobar objeto equipado y
   misma cantidad en Bolsa. Guardar dentro de esta copia, cerrar y reabrir.
3. Con el mismo candidato probar Lanzamiento: efecto sobre el rival normal,
   objeto ausente durante el combate, baya original equipada después, Bolsa sin
   incremento. Luego probar Desarme y Truco/transferencia, anotando ambos portadores
   antes/durante/después; se espera recuperar la distribución original, sin dejar
   una copia adicional del objeto transferido.
4. Repetir consumo con Banda Focus, un salvaje y un entrenador normal. Comprobar
   un objeto no consumido y dos portadores. Los otros consumibles, exclusiones y
   convivencia siguen pendientes; no extrapolar el resultado de una baya.

Si falla una condición, registrar caso, objetos antes/después, Bolsa y momento del
fallo; cerrar sin sobrescribir la copia previa. No habilitar producción por una
prueba positiva aislada. Y requiere su propio candidato, guardado y evidencia.

## Comprobaciones técnicas de esta continuación

132 pruebas Python sintéticas aprobadas (incluidas nueve del preparador y cinco
del arnés de replay sin ejecutar ARM). Se
comprobaron contrafactuales controlados de no-op, aceptación de parcial, omisión
del baseline y admisión de Y: las pruebas detectaron los cuatro cambios. Build
del CLI aprobado. Runner existente: 129 comprobaciones de capacidades y 72 del
recordador XY aprobadas. El CI compila el CLI y descubre las nuevas pruebas; su
resultado del commit nuevo debe consultarse en el PR. Ningún resultado anterior
equivale a ejecución jugable de este candidato.

## Artefacto local entregado

* Imagen: `D:\Progra\Pokemon\Randomizer\PruebasPersistentItems-X-01\Pokemon-X-PersistentItems-EXPERIMENTAL.3ds`.
* SHA-256: `13455A2A3B0AE6457EC68BA96FF4766A9AC6C413895517339DB5762A16BB90C1`.
* Tamaño: 1.801.068.544 bytes. Reextracción: **656 archivos** idénticos al proyecto
  candidato (RomFS y ExeFS), incluyendo nombre físico y CRR intacta.
* Acceso aislado: `D:\Progra\Pokemon\Randomizer\PruebasPersistentItems-X-01\Abrir-Prueba-X.cmd`.
  Abrir ese acceso; también existe una variante `.ps1`. Usa Citra 2104 copiado en `Emulador`, almacenamiento
  portable propio y copia previa a X-08. Depurador y telemetría desactivados;
  no se ha iniciado una batalla nueva ni se ha validado el candidato en juego.
* `evidence/build.json`, `rebuild-verification.json`, `patch-entry.json` y
  `runtime.json` conservan las comprobaciones locales. No están en Git.

Para abandonar el experimento basta cerrar su emulador. No se ha cambiado la
configuración ni el guardado del Lime habitual. Conservar el backup previo a X-08;
no copiar el resultado experimental sobre la partida habitual durante validación.
