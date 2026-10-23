# Integración del runtime PokeFeur en Definitive Randomizer

22/10/2026 · rama `feature/switch-swsh-prototype` · sin commit, push ni release.

## Resultado y alcance

Integración funcional en la interfaz de Switch para **Espada 1.3.2 / Ryujinx**. El backend es el módulo publicado de PokeFeur que el usuario ya probó con apariencia de Yamper, combate contra Bulbasaur, captura y menús consistentes. Se retiró el prototipo propio de la navegación principal; sus fuentes y pruebas anteriores se conservan como investigación, sin restaurar ni eliminar trabajo local. No se mezcla ese prototipo con PokeFeur en una misma sesión.

La integración y la generación de recursos están probadas. **No se ejecutó el juego con las nuevas opciones en esta entrega**. Level caps, EXP, scripts y demás funciones se habilitan de forma explícita y requieren validación jugable. La confirmación previa del usuario corresponde a combate sorpresa con el pool reducido, no a todas las opciones ni a las 664 especies. Escudo y Atmosphère no se habilitan para este paquete: necesitan extracción y variante probadas.

## Uso

1. En Inicio, abrir **Espada/Escudo · prototipo Switch…** y seleccionar una extracción con romfs y exefs de Espada 1.3.2.
2. Abrir **Runtime · encuentros, level caps y herramientas…**, disponible directamente en esa ventana y también desde Entrenadores/encuentros/Field Items.
3. El runtime fijado, su licencia y sus tres scripts están integrados como recursos del ensamblado. No hay importación de paquetes, dependencia de la caché de este PC ni descarga al abrir la ventana. Sus hashes se comprueban antes de usarlos.
4. Elegir opciones, generar la vista previa y pulsar **Añadir al mod**. Abrir/importar/previsualizar no escribe en la extracción, SD ni ROM. Las recetas del runtime son JSON separados; no son un Global Template de 3DS.
5. Exportar el mod con formato Ryujinx desde la ventana principal. Contiene exefs/subsdk9 y exefs/main.npdm del paquete; **no sustituye el main del juego**. Se combina con las ediciones de Pokémon, entrenadores, encuentros y Field Items de la sesión.
6. Instalar explícitamente la configuración desde **Instalar configuración SD…** o copiar el archivo exportado `sdcard/config/swsh-mods-exl/config.toml` a la SD de Ryujinx. Una configuración distinta requiere confirmación y copia de seguridad; se rechazan cambios concurrentes. El botón exige aplicar antes las opciones pendientes.
7. Cerrar completamente el juego y usar solo un runtime ExeFS; desactivar las pruebas v1–v7 y la prueba anterior PokeFeur al activar el mod exportado. La configuración SD es compartida por los mods que usan PokeFeur. Los demás mods sobre los mismos recursos pueden entrar en conflicto.

## Funciones integradas

| Área | Opciones | Estado |
|---|---|---|
| Encuentros | Visible Vanilla, Random, Zone, Global, Discovery y Random Discovery; combate Vanilla, Random, Zone y Global; pools independientes, semilla y selección de especies presentes | Comportamiento base probado por el usuario con el runtime; nuevos modos/pools requieren prueba propia |
| Pools | IDs de especies base presentes; vacío usa todas las presentes; 664 en la extracción real; rechazo de duplicados y ausentes | Validación de datos aprobada; no garantiza que cada especie/modo haya sido probada en juego |
| Level caps | `story_gym_league` con nueve caps manuales o derivados de los equipos actuales; `receiver_vs_defeated` según el contrato oficial | Generación aprobada; bloqueo de EXP y progresión pendientes de juego |
| Caps automáticos | Jefes 32, 36, 37, 77, 108, 135, 107, 144 y 149; máximo del equipo; verificación de que no cambian sus niveles antes de exportar | En la extracción real produce 20, 24, 27, 36, 38, 42, 46, 48, 65 |
| EXP | Multiplicador 0,01–100, float TOML invariante; activar/desactivar Repartir Exp. | Configuración aprobada; efectos dentro del juego pendientes |
| Shinies visibles | Activación y bonificación 0–100; mensajes extra desactivados | Pendiente de prueba jugable |
| Área Silvestre | Retirar límite de nivel, opción fully | Separado del cap EXP; pendiente de juego |
| Cámara | Ajustes y cámara libre | Usa contrato/defaults del runtime; controles y jugabilidad pendientes |
| Encuentros adicionales | Underworld y spawns de Bosque Lumirinto 1–20 | Pendientes de juego; no se inventan contratos nuevos |
| Pokémon | Sincronización y seguimiento ampliado | Pendientes de juego |
| Pesca | Mejoras y conservar cadena tras brillante | Pendiente de juego |
| Bicicleta temprana | Opción, script AMX e inscripción de `grant_early_bike` | Exportación/registro aprobados; interacción en juego pendiente |
| Menú PC | Script AMX, entrada nueva y sustitución exclusiva del registro del Centro Pokémon | Se rechaza un Centro Pokémon ya modificado; interacción pendiente |
| Pokédex | Animaciones, script AMX e inscripción de `dex_dialog` | Exportación/registro aprobados; interacción pendiente |
| Estado del runtime | Activación explícita | No se activa por omisión; pendiente de juego |

Todas las funciones comienzan desactivadas; Repartir Exp. conserva su estado vanilla activado. `runtime_pc` y `runtime_status`, que el contrato externo activa por defecto si faltan, se emiten explícitamente apagados. Los descubrimientos no se reinician automáticamente y pueden persistir en SD. No se introdujo un parche nuevo del motor: se generan las claves verificadas del runtime externo.

La semilla se limita al rango entero positivo seguro de TOML, 0–9223372036854775807. Los pools y las tablas de caps se congelan al crear la propuesta. Las configuraciones exportadas se pueden reabrir y comparar por hash.

## Runtime integrado y procedencia

Origen: https://github.com/JonatanUHC/PokeFeurRandomSWSH . Commit fijado: `e69afd5b9d4e5f892237bb7fb30aed10101d9c0a`.

- subsdk9_ryujinx: `AB13EC3B8693EF475E5FD81221095D6862D3C34E179399E6B25F351FD7563331`.
- main.npdm_ryujinx: `8FB43885745A0CB0B54D7B631F769A260ADAE13FCB0574E2C97B598C4900A8D2`.
- Los tres AMX y la licencia también tienen hashes fijados. Cambios de versión requieren revisión, no se aceptan silenciosamente.
- Los seis recursos verificados se incluyen en `3DS.Progressive.App/Assets/Switch/PokeFeur/` con procedencia y hashes en `upstream.json`. MSBuild los integra en el ensamblado para compilación y publicación; no son archivos sueltos que el usuario deba proporcionar. Se conserva la licencia exportada. El NPDM es el perfil de permisos del mod publicado por upstream, no el ejecutable main del juego. El registro de scripts sigue procediendo de la extracción del usuario. La caché histórica local se conserva pero ya no se consulta en el flujo de la aplicación.
- El constructor del registro de scripts se adaptó del generador público bajo GPL-3.0, con atribución y licencia en `docs/third-party/PokeFeur-LICENSE.txt`. Se añadieron comprobaciones estrictas de esquema y preservación. No se afirma disponer del C++ completo del runtime externo.

El registro `script_id_record.bin` se genera desde la extracción del usuario. Se conservan todos los registros ajenos; únicamente el PC puede sustituir la entrada vanilla prevista. Campos desconocidos, IDs duplicados, registros truncados y un PC modificado se rechazan. No se borran extras originales ni se copian scripts si sus funciones no están elegidas.

## Sesión, historial y exportación

Previsualización ligada al proyecto y a la revisión; rechazo de propuestas ajenas/desactualizadas. Historial por parámetro, incluyendo origen de caps (manual/jefes) y retirada del runtime. Reaplicar una propuesta equivalente no duplica historial. Retirar el runtime conserva las ediciones de Pokémon/ROMFS; descartar la sesión limpia todos sus borradores. Los manifiestos contienen opciones, procedencia, hashes y evidencia de los jefes. Las recetas no contienen ROM, ejecutables ni rutas personales.

No se alteran los Global Templates, Batch, historial de 3DS ni los motores Gen6/Gen7. Global Template/Batch propios de Switch, MT/TR, movimientos, evoluciones, tiendas y el resto de editores offline de PokeFeur no se consideran implementados por esta integración del runtime.

## Archivos de esta implementación

Nuevos:
- `pk3DS.Core/NX/PokeFeurOptions.cs`.
- `pk3DS.Core/NX/PokeFeurScriptRegistry.cs`.
- `3DS.Progressive.App/Services/Switch/PokeFeurPackage.cs`.
- `3DS.Progressive.App/Services/Switch/PokeFeurSdInstall.cs`.
- `3DS.Progressive.App/Services/Switch/SwordShieldProject.PokeFeur.cs`.
- `3DS.Progressive.App/SwordPokeFeurWindow.cs`.
- `3DS.Progressive.Tests/PokeFeurChecks.cs`.
- `3DS.Progressive.Tests/PokeFeurRealChecks.cs`.
- Este informe y `docs/third-party/PokeFeur-LICENSE.txt`.

Editados de forma puntual sobre el trabajo pendiente:
- `SwordShieldProject.cs`, `SwordShieldProject.Gameplay.cs`, `SwordShieldProject.Surprise.cs`.
- `SwordShieldPrototypeWindow.cs`, `SwordShieldPrototypeWindow.Gameplay.cs`, `SwordShieldGameplayWindow.cs`.
- `3DS.Progressive.Tests/Program.cs` y documentación de estado Switch.

Los otros archivos que aparecen pendientes en Git ya pertenecían al desarrollo previo de Switch. No se restauraron ni descartaron. La solución original y sus referencias no se modificaron; App/Tests se compilan explícitamente. Una escritura inicial fue bloqueada por la revisión automática por posible sobrescritura: se comprobó que los archivos no existían y se crearon con modo exclusivo, conservando los demás cambios.

## Validación ejecutada

- `dotnet build 3DS.Progressive.Tests/3DS.Progressive.Tests.csproj -c Release --no-restore --no-incremental`: **0 errores, 78 advertencias en código previo; ninguna en los archivos nuevos del runtime**.
- `dotnet build pk3DS.slnx -c Release --no-restore --no-incremental`: **0 errores, 56 advertencias previas**. Conserva backend WinForms.
- Ambas compilaciones capturaron binlogs únicos. No hubo nuevas dependencias NuGet.
- `--pokefeur-only`: **76 comprobaciones** de modos, límites, aislamiento de funciones, recetas, scripts y SD, incluidas en `--swsh-only`.
- `--swsh-only`: **288 aprobadas**.
- `--swsh-ui-only`: **39 aprobadas**, diez nuevas de esta ventana, fuera de pantalla. Cinco paneles, asociaciones accesibles, acciones dentro del área disponible a 600×450 y 1000×790 y sin errores de binding detectados.
- `--pokefeur-probe <extracción-real> --builtin <temporal-nuevo>`: **42 aprobadas**. Incluye nueve jefes, recursos/scripts reales, preservación de todos los registros ajenos, sesión, historial, hashes, exportación combinada, rechazo de consola sin salida parcial y aplicación WPF fuera de pantalla. La SD usada es temporal, no la activa de Ryujinx.
- `--synthetic-only`: **4050 aprobadas**, regresiones XY/ORAS/SM/USUM.
- `--visual-redesign-only`: **95 aprobadas**.
- `--guidance-only`: **233 aprobadas**.
- Python `tomllib` y SHA-256: verificación independiente del TOML real final, 664 especies, nueve caps y hashes de los ocho recursos exportados.
- `git diff --check`: aprobado. No se hicieron commits, push ni release.

Se encontraron y corrigieron errores introducidos durante el desarrollo: ambigüedades WPF/WinForms, conversión de errores JSON a validación, normalización de rutas de copia SD y un error de sintaxis al completar el historial. No se presentan como problemas pendientes.

| Requirement | Evidence |
|---|---|
| Claves del modo ya probado | `Poke_ValidatedSurpriseConfigurationUsesOfficialKeys` |
| Caps desde equipos reales | `Poke_Real_AutoCapsReadNineConfiguredBossTeams`, `Poke_Real_CapMatchesOriginalTeam_149` |
| Aislamiento de opciones | `Poke_FeatureIsolated_EarlyBike`, `Poke_DefaultsDoNotActivateRuntimeOrScripts` |
| Preservar scripts ajenos | `Poke_Real_ScriptRegistryPreservesEveryUnrelatedEntry` |
| Historial y revisiones | `Poke_Real_ApplyRecordsGranularRuntimeHistory`, `Poke_Real_RejectStalePreview`, `Poke_Real_ChangingAutoCapPolicyIsNotMistakenForNoOp` |
| Instalar SD sin perder configuración | `Poke_SdReplaceKeepsExactBackup`, `Poke_SdConcurrentEditNotOverwritten` |
| Conservar ediciones al retirar | `Poke_Real_RemoveRuntimePreservesOtherSessionEdits` |
| Recetas completas | `Poke_RecipeRoundTripAllNonDefaultRuntimeTools` |
| UI accesible y adaptable | `Poke_UISeedHasAssociatedLabel`, `Poke_UIFooterActionsStayInsideLayout_600`, `Poke_UIAllTabsHaveNoBindingErrors` |

## Riesgos residuales y pruebas manuales

Las pruebas automatizadas de configuración, datos y componentes no certifican la jugabilidad. Falta probar: el corte de EXP exactamente en cada cap y transición por medalla; comparación receptor/derrotado; multiplicador y Repartir Exp.; menú PC; entrega de bicicleta; animaciones de Pokédex; shinies; cámara/controles; pesca; seguimiento y sincronización; Lumirinto/underworld; pools completos; descubrimientos, persistencia y eventos. Habilitar funciones por grupos y comparar con un mod de control.

No se realizaron pruebas interactivas de teclado, alto contraste ni escalados reales 100/125/150/200 % de esta ventana. Se reutilizan estilos y componentes de accesibilidad existentes, pero eso no equivale a verificarlos manualmente. Tampoco se certifica compatibilidad con otros mods ExeFS, otros emuladores, hardware real, otra versión o Escudo.

La configuración de SD puede afectar cualquier mod PokeFeur que la lea; se instala solo por acción explícita, con revisión de hashes y copia de seguridad si difiere. Los recursos integrados no se actualizan automáticamente: cambiar su versión exige una actualización revisada de la aplicación. El runtime puede guardar descubrimientos en SD: no se activa un borrado de progreso desde esta integración.


## Actualización: runtime autónomo para el usuario (22/10/2026)

Se elimina la importación manual del paquete. `PokeFeurPackage.BuiltIn()` lee exclusivamente seis recursos del ensamblado y comprueba sus hashes y los tres segmentos NSO. La ventana abre con el runtime listo. La previsualización del servicio también usa esta fuente si no se proporciona un proveedor. La importación/exportación de recetas de opciones se conserva. No se consulta caché, se descarga ni se cambia la configuración SD activa. Los recursos suman 532443 bytes; MSBuild los integra con nombres explícitos tanto en compilación como en publicación. Se mantiene la procedencia upstream: esto elimina la dependencia operativa de paquetes externos, no significa disponer del código C++ completo ni haber desarrollado un runtime independiente.

Archivos de esta actualización: `3DS.Progressive.App.csproj`, `Services/Switch/PokeFeurPackage.cs`, `Services/Switch/SwordShieldProject.PokeFeur.cs`, `SwordPokeFeurWindow.cs`, `Assets/Switch/PokeFeur/` (seis recursos y manifiesto), `PokeFeurChecks.cs`, `PokeFeurRealChecks.cs` y este informe. No se cambió la extracción original ni se desplegó un mod en el emulador.

Validación actual ejecutada:

- Compilación Release de `3DS.Progressive.Tests` y referencias App/Core/WinForms: 0 errores, 88 emisiones de advertencia. Son las mismas 67 advertencias únicas que en el registro anterior de integración; la diferencia de emisiones procede de la repetición durante compilación WPF, sin advertencias únicas nuevas.
- `--swsh-only`: 293 comprobaciones aprobadas.
- `--swsh-ui-only`: 40 comprobaciones aprobadas, fuera de pantalla.
- `--pokefeur-probe <extracción> --builtin <temporal>`: 42 comprobaciones aprobadas; proveedor integrado, exportación real aislada, SD temporal, conservación de fuente y previsualización/aplicación WPF sin inyectar paquete.
- `--visual-redesign-only`: 95 comprobaciones aprobadas.
- `git diff --check`: aprobado.

| Requirement | Evidence |
|---|---|
| Runtime disponible sin importación | `Poke_UIRuntimeReadyWithoutImport`, `Poke_BuiltInRuntimeMatchesValidatedModule` |
| Metadatos y scripts originales verificados | `Poke_BuiltInMetadataMatchesValidatedProfile`, `Poke_BuiltInIncludesAllPinnedOptionalScripts` |
| Licencia y aislamiento de recursos | `Poke_BuiltInPreservesUpstreamLicense`, `Poke_BuiltInResourcesAreDetachedSnapshots` |
| Flujo real con proveedor integrado | `Poke_Real_PreviewIsReproducible`, `Poke_Real_UiPreviewDoesNotApply`, `Poke_Real_UiApplyEnablesExplicitSdInstall` |

Pendiente: prueba interactiva de la ventana y jugabilidad de las opciones adicionales; comprobar una publicación instalada en otro equipo. El funcionamiento sin red se sustenta en la carga directa de recursos sin llamadas de red; no se desconectó físicamente la red para simular otro equipo. No se ejecutó publish en esta actualización. Escudo y Atmosphère siguen fuera del alcance validado.


## Actualización de navegación, semillas y editores pkNX · 22/10/2026

El acceso Switch se integra en la ventana principal, se eliminan semillas fijas y se detecta la configuración SD anterior. Se incorporan diez áreas de edición de datos. El alcance, pruebas y pendientes están en [SWITCH_PKNX_INTEGRACION_Y_SEMILLAS.md](SWITCH_PKNX_INTEGRACION_Y_SEMILLAS.md); las instrucciones anteriores del diálogo de prototipo son históricas.
