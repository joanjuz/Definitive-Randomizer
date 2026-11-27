# Persistent Items X/Y: instalación nativa e integración

Continuación del PR #145, 27/11/2026. Rama `feature/xy-persistent-items`.
El propietario confirmó el comportamiento deseado en los candidatos **X e Y**.
Se integra el mismo cambio nativo en el instalador del proyecto, sin publicar
una release, modificar la Preview instalada, fusionar el PR ni ampliar a ORAS.
El CI existente puede adjuntar su ejecutable de pruebas como artefacto.

## Evidencia jugable y alcance

X: el propietario confirmó consumo de Baya Zidra, recuperación equipada después
del combate y cantidad en Bolsa conservada. Posteriormente confirmó Desarme y
Lanzamiento y aceptó todos los comportamientos observados. Y: tras recibir su
candidato independiente, confirmó que las pruebas también funcionan perfectamente.
Son confirmaciones manuales del propietario; no son capturas automáticas nuevas.
No se inventa un desglose adicional por objeto, guardado/recarga o modalidad.

Las imágenes y SHA-256 aprobados se conservan en
[el informe de candidatos](PERSISTENT_ITEMS_GEN6_XY_TWO_GATES.md). La integración
usa exactamente las mismas dos instrucciones y cuatro hashes internos CRO.
No añade snapshots, memoria ni reglas de procedencia. El objeto original vuelve
también tras perderlo o transferirlo, de acuerdo con la especificación corregida.

Alcance acreditado: emulador HLE y combates normales. Instalaciones, PvP y servicios
en línea quedan fuera de alcance. No se afirma compatibilidad en consola: la
CRR se conserva intacta y no se fabrica ni se acredita una firma criptográfica.

## Instalador y perfiles

`GameGen6PersistentItemsService` implementa `IProjectEditor`, `EditorProposal` y
`ProjectHistory`. Está separado de `GamePersistentItemsService` USUM, que no se
modifica. `Gen6PersistentItemsPatcher` cambia:

| Offset de archivo | Original | Instalado | Destino |
|---|---|---|---|
| `0x14854` | `1A000020` BNE | `8A000020` BHI | `0x148DC` |
| `0x148AC` | `0A000025` BEQ | `EA000025` B | `0x14948` |

Se mantienen los perfiles X `0004000000055D00` e Y `0004000000055E00` por separado.
Se verifican NCCH y exheader reales, sus alias si existen, ejecutable descomprimido,
DllField, CRS, CRR y GARC de objetos contra las huellas de cada título. No se acepta
un ejecutable cruzado ni se deduce Y a partir de la aceptación de X.

Para permitir los dos editores de combate acreditados, el CRO completo tiene
una huella estructural normalizada:
`240594E9F384A96822F5142A034B3E60B24F25ED83ED124BA826C174DADD9F2B`.
La normalización excluye exclusivamente:

* `0x00..0x7F`: los cuatro hashes, que se verifican por separado.
* Las dos instrucciones anteriores, admitiendo únicamente sus dos estados exactos.
* `0xD12A8..0xD13EB`: 324 celdas de tipos, limitadas a 0, 2, 4 u 8.
* `0xBAA84`: parámetro FE/FF de Escudo Real, con firma completa única en `0xBAA80`.

Todos los demás bytes deben coincidir con el CRO acreditado. Las dos firmas ARM
completas deben ser únicas y conservar sus posiciones. Cabecera, tamaño, segmentos,
relocaciones y contenido no afectado siguen cubiertos por la huella. Se rechazan
instalaciones parciales, CRO inválido/truncado, firmas ausentes/duplicadas y cualquier
modificación externa fuera de los rangos admitidos. Un patrón instalado acredita
sus bytes; no se atribuye falsamente una instalación previa a este historial.

## Propuestas, escritura y reversión

La vista previa no escribe. Congela bytes originales, resultado, dependencias,
Title ID, huellas y cambios ARM. Apply exige el mismo propietario de propuesta,
recalcula la operación y compara archivos, cambios y metadatos completos. Rechaza
propuestas de otra sesión, obsoletas o alteradas. Mantiene abiertas las dependencias
en modo de lectura sin compartir escritura durante toda la transacción; el archivo
destino queda protegido por `FileTransaction`. Se relee el resultado exacto.

Instalar y retirar son idempotentes y los no-op no crean entradas. La retirada
revierte únicamente las dos instrucciones y reconstruye los hashes sobre los bytes
actuales: conserva tabla de tipos y Escudo Real. Nunca repone un CRO vanilla entero
sobre otras ediciones. `RestorePoints.Undo` restaura la operación exacta en orden
del historial, respetando las modificaciones posteriores y sus huellas.

Las operaciones también se registran como edición manual reproducible en los
contratos existentes. Los esquemas de templates no cambian. La sección Historial
mantiene su política existente de los ocho puntos de restauración más recientes.

## WPF

Se usa la sección existente **Mecánicas y CRO / Persistent Items**. En X/Y abre
`Gen6PersistentItemsView`; en USUM conserva su editor existente. La disponibilidad
por familia permite ver un diagnóstico en X/Y no acreditado, pero la vista impide
instalar o retirar si falla la comprobación binaria.

Muestra juego, estado, compatibilidad, alcance, archivo, instrucciones y SHA-256;
exige vista previa y confirmación antes de aplicar. Tras guardar refresca el estado
y remite a Historial para deshacer. Los textos usan recursos de tema, las acciones
se ajustan al ancho y el contenido es desplazable. MainWindow, SectionCatalog,
SystemsView, ProjectFeatureAvailability y GuidanceCatalog comparten la integración.
No se añade un segundo editor ni se cambian servicios USUM, recordador o mentas.

## Convivencia e integridad

Se verificaron ambas secuencias con los servicios reales de la aplicación:

1. Persistent Items → tabla de tipos → Escudo Real.
2. Tabla de tipos → Escudo Real → Persistent Items.

En copias nuevas `PruebasPersistentItems-X-Integration-01` e
`PruebasPersistentItems-Y-Integration-01`, se comprobaron los tres cambios,
reapertura, reinstalación sin nuevas escrituras, retirada de Persistent Items
conservando los otros dos y deshacer en orden hasta recuperar todos los archivos
RomFS/ExeFS iniciales. Los manifests privados `evidence/service-integration.json`
registran cada Title ID y el resultado. No sustituyen pruebas de combate combinado.

Además se reconstruyó y reextrajo cada combinación de los tres cambios: **656
archivos idénticos por título**, incluyendo sus nombres físicos, con todos los
hashes comparados. El proyecto de integración no cambió durante el rebuild.
Ambas imágenes miden 1.801.068.544 bytes; sus manifests privados están en
`evidence/combined-roundtrip.json`:

| Combinación experimental | SHA-256 de imagen |
|---|---|
| X | `26C1AA36C74497EEA0160BE73A85770A34FCD572B505B775F3D4F58C597F83CC` |
| Y | `3ACA7AC5C28FAFEE1E7F9BE040126DD7BF488B6B609C4A8E7958B61F66E82741` |

Cada archivo se llama `PersistentItems-Types-Shield-EXPERIMENTAL.3ds` dentro de su
carpeta X/Y de integración. Estas imágenes combinadas **no** tienen confirmación
jugable; la aceptación del propietario corresponde a los candidatos de dos
condiciones originales. No se sustituyen las imágenes ni los guardados ya probados.

Dos ajustes de compatibilidad mantienen los hashes correctos en **XY**:
`GameTypeChartService` reconstruye hashes si Persistent Items está presente aunque
la opción manual de reparación esté desmarcada; `GameMoveTemplateService` reconstruye
los hashes después de aplicar el parche nativo de Escudo Real. Antes de instalar
Persistent Items, una edición de tipos requiere su opción de reparar CRO o una
reconstrucción previa de hashes. No se cambia el comportamiento ORAS ni Gen7.
Las fixtures de Escudo Real y CSV se actualizaron a CRO válidos; conservan assertions
de delta exacto y rechazo de firmas duplicadas.

No se acredita cualquier otro parche CRO ni cualquier revisión modificada. Cambios
en las dependencias fijadas, incluido code o GARC de objetos, siguen rechazados;
la vista explica el recurso que no coincide. La CRR se conserva en todos los casos.

## Reproducción técnica

El runner es un ejecutable propio; no usar `dotnet test` para estas suites.

```powershell
dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release -p:UseAppHost=true '-bl:artifacts/persistent-research/integration-tests-{}.binlog'
$runner = '3DS.Progressive.Tests/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll'
dotnet $runner --gen6-persistent-items-only
dotnet $runner --gen6-persistent-items-ui-only
dotnet $runner --patch-capability-only
dotnet $runner --map3ds-reminder-only
dotnet $runner --map3ds-reminder-ui-only
dotnet $runner --synthetic-only
```

Las pruebas de recursos originales usan únicamente copias nuevas explícitas con
prefijo X/Y y marcador de investigación; nunca proyectos habituales:

`service-check` requiere una copia nueva del candidato, sus cabeceras y el marcador
`evidence/source.json`. Sus destinos de rebuild/reextracción también deben ser
nuevos: rechaza una repetición sobre salidas anteriores antes de escribir.

```powershell
dotnet build tools/research/xy-persistent-items/playable/PersistentItems.Experiment.csproj -c Release -p:UseAppHost=true '-bl:artifacts/persistent-research/integration-cli-{}.binlog'
$cli = 'tools/research/xy-persistent-items/playable/bin/Release/net10.0-windows/3DS.Progressive.Tests.dll'
dotnet $cli service-check D:/Progra/Pokemon/Randomizer/PruebasPersistentItems-X-Integration-01
dotnet $cli service-check D:/Progra/Pokemon/Randomizer/PruebasPersistentItems-Y-Integration-01
# Render fuera de pantalla, carpeta nueva:
dotnet $cli render-view D:/Progra/Pokemon/Randomizer/PruebasPersistentItems-X-Integration-01 D:/Progra/Pokemon/Randomizer/PruebasPersistentItems-X-Integration-01/evidence/ui
```

Las reextracciones anteriores acreditaron 656 archivos por candidato. Esta fase
comprobó igualdad de todos los recursos tras deshacer las combinaciones reales.
No se incorporan binarios comerciales, guardados, capturas privadas ni ROM a Git.

## Matriz de pruebas

Resultado local del código final:

| Comprobación | Resultado |
|---|---|
| Compilación App/runner/CLI | Aprobada; 0 errores, una advertencia CS0675 preexistente en pruebas NDS |
| `--gen6-persistent-items-only` | 122 comprobaciones aprobadas |
| `--gen6-persistent-items-ui-only` | 20 comprobaciones aprobadas |
| `--patch-capability-only` (incluye USUM e interacción) | 129 aprobadas |
| Recordador XY: backend / WPF | 72 / 15 aprobadas |
| `--visual-redesign-only` | 187 aprobadas, sin errores de binding |
| `--synthetic-only` | 4229 aprobadas, XY/ORAS/SM/USUM |
| Herramientas de investigación Python | 135 aprobadas |
| Recursos reales aislados X/Y | Ambos órdenes, retirada e historial aprobados por título; rebuild/reextracción combinados de 656 archivos exactos por título |
| Capturas WPF fuera de pantalla | X/Y, 520 px a 96 DPI y 1100 px a 144 DPI; inspección visual aprobada |

Se encontraron y corrigieron dos fixtures antiguas con pseudo-CRO sin cabecera y
una fixture de reapertura sin tabla MT. Ahora usan formatos sintéticos válidos;
no se relajaron las validaciones de producción. La comprobación de capturas
detectó y corrigió el contraste del texto mediante los recursos del tema.

| Requirement | Evidence |
|---|---|
| Dos condiciones, integridad e idempotencia | `Gen6PersistentItems_OnlyTwoConditionBytesAndValidHashesChange`, `Gen6PersistentItems_BothDirectionsIdempotent` |
| Perfiles independientes y rechazo cruzado | `Gen6PersistentItems_IndependentSyntheticTitleProfilesRecognized`, `Gen6PersistentItems_CrossedTitleProfilesRejected` |
| Rechazo de parciales y firmas desconocidas | `Gen6PersistentItems_PartialInstallNotRepaired_14854`, `Gen6PersistentItems_Reject_DuplicateGate`, `Gen6PersistentItems_Reject_UnknownBody` |
| Propuesta congelada y carreras | `Gen6PersistentItems_ModifiedFrozenBytesRejected_X`, `Gen6PersistentItems_ModifiedProposalMetadataRejected_X`, `Gen6PersistentItems_DependencyRaceRejected_ExtractedExeFS_.code.bin` |
| Reapertura, historial y retirada exacta | `Gen6PersistentItems_ReopenedSessionRecognizesInstalledBytes_X`, `Gen6PersistentItems_HistoryUndoRestoresExactOriginal_X`, `Gen6PersistentItems_ExplicitRemovalPreservesAllOtherDependencies_X` |
| Otros parches en ambos órdenes | `Gen6PersistentItems_BothOrdersKeepCompanionAndValidCro_TypeChart_ItemsFirst`, `Gen6PersistentItems_BothOrdersKeepCompanionAndValidCro_KingShield_CompanionFirst` |
| UI incompatible y confirmación | `Gen6PersistentItemsUi_UnknownSyntheticProfileExplainedAndDisabled_X`, `Gen6PersistentItemsUi_PreviewRequiredBeforeAnyWrite_Y`, `Gen6PersistentItemsUi_ConfirmedApplyWritesAndRefreshesStatus_Y` |
| Contraste de tema | `Gen6PersistentItemsUi_UsesThemeContrastResources_X`, `Gen6PersistentItemsUi_UsesThemeContrastResources_Y` |

Los resultados de ejecución y CI se actualizan en el PR. Las pruebas fuera de
pantalla no acreditan foco interactivo, lector de pantalla o todos los DPI reales;
la aceptación jugable de candidatos no equivale a probar esas propiedades de WPF.
