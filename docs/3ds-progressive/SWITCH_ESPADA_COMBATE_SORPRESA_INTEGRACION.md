# Combate sorpresa de Espada: integración del prototipo

22/10/2026 · rama feature/switch-swsh-prototype · sin commit ni push.

## Resultado y alcance

Integrados adaptador nativo, NSO propio, escritor validado, previsualización WPF, historial de sesión, manifiesto y exportación Ryujinx. Exclusivo de Espada 1.3.2, Build ID A3B75BCD3311385AEED67FBEEB79CBB7BF02F471000000000000000000000000. Escudo y consola se rechazan en esta fase. No se modificó la extracción original ni main.npdm. No se cambiaron motores 3DS.

El modelo visible se conserva. Un spawn básico aprobado recibe otra especie base presente en su copia para combate. Se conserva nivel; forma base 0, género/habilidad mediante sentinelas del constructor y movimientos solicitados al aprendizaje por nivel. Selección estable para el mismo objeto visible mientras permanezca generado; huir y tocarlo otra vez no promete otro sorteo. Pool real: 664 especies base presentes. No se promete persistencia de la secuencia tras reiniciar.

## Implementación y archivos

- `native/switch/surprise/adapter.h`, `adapter.c`, `adapter_tests.c`: aprobación de spawns básicos, revocación de gimmicks, separación modelo/copia y pruebas host. Sin escritura del objeto del mapa; bloqueo atómico con bypass si hay contención.
- `native/switch/surprise/module.c`, `module.S`, `module.ld`: módulo MOD0/DT_INIT propio, descubrimiento de main mediante QueryMemory, wrappers ARM64 y conservación de registros/estado. Sin escrituras RX en ejecución.
- `native/switch/surprise/build_runtime.py`, `test_runtime.py`, `README.md`: construcción reproducible, conversión ELF a NSO con hashes, prueba Unicorn y documentación.
- `3DS.Progressive.App/Assets/Switch/sword-surprise.nso` y `.csproj`: recurso embebido propio. SHA256 base 3C0D8D4C12DCCC3B789B0D4503FE27B9CBDB39D7873A1EC44B1F2EA9CF9E7ED4; construcción repetida produjo el mismo hash. No se distribuye módulo precompilado ajeno.
- `pk3DS.Core/NX/SwordSurpriseMod.cs`: perfil exacto, SHA de texto original, tres entradas verificadas, 72 bytes de trampolines y 24 bytes BSS adicionales. Reapertura y hashes; rodata/data preservados. Texto original: 82A40B8D3C334DBA1B5D9D1F2F99D5BA6A04D0B1953A069C39C3E9097264A37E.
- `3DS.Progressive.App/Services/Switch/SwordShieldProject.Surprise.cs`, `SwordShieldProject.cs`, `SwordShieldProject.Gameplay.cs`: revisión de sesión, rechazo de previsualizaciones ajenas/desactualizadas, protección de subsdk9 existente, historial, reset y exportación combinada. Reset restablece todos los cambios de sesión; no existe botón individual para retirar solo este módulo.
- `3DS.Progressive.App/SwordSurpriseWindow.cs`, `SwordShieldGameplayWindow.cs`, `SwordShieldPrototypeWindow.cs`: botón experimental, semilla accesible, propuesta de solo lectura, aplicación explícita y diario. Exportación restringida a Ryujinx mientras haya módulo nativo.
- `3DS.Progressive.Tests/SwordSurpriseIntegrationChecks.cs`, `Program.cs`: pruebas sintéticas, componentes WPF y probe de extracción real.
- Documentación: este informe, README nativo, aviso histórico en `SWITCH_ESPADA_COMBATE_SORPRESA.md` y actualización de `HOJA_RUTA_SWITCH.md`.

El resto de los archivos pendientes de Switch pertenecen al trabajo previo de esta rama; no se descartaron ni restauraron.

## Resultados ejecutados

- Release de Tests y sus referencias App/Core: 0 errores, 88 advertencias en archivos anteriores; ninguna en los componentes Switch nuevos. Log y binlog en el temporal DR-SWSH-adapter-20261021.
- `--swsh-only`: 211 aprobadas.
- `--swsh-ui-only`: 29 aprobadas, fuera de pantalla.
- `--synthetic-only`: 3973 aprobadas, XY/ORAS/SM/USUM.
- `--visual-redesign-only`: 95 aprobadas, sin errores de binding detectados por el runner.
- `--guidance-only`: 233 aprobadas, fuera de pantalla.
- `--sword-surprise-probe` con extracción real: 17 aprobadas; semilla reproducible, previsualización sin aplicar, tres entradas modificadas, segmentos preservados, historial, rechazo de revisión ajena/desactualizada, exportación y reapertura, origen/NPDM intactos, combinación con personal, rechazo de consola sin salida parcial, reset y aplicación WPF.
- Adaptador C host: 13 aprobadas. Política C: 20 aprobadas en la fase anterior, sin cambios posteriores del núcleo.
- Unicorn ARM64: 9 aprobadas al repetir el módulo reproducido; funciones originales y respuestas SVC simuladas. No equivale a ejecutar el juego ni su cargador RTLD real.
- Verificación Python independiente con LZ4: hashes NSO correctos, rodata/data idénticos, cambios solo en tres instrucciones originales, +72 bytes texto y +24 bytes BSS.
- `git diff --check`: correcto. No se hizo commit ni push.

| Requirement | Evidence |
|---|---|
| Preservar modelo visible | `Adapter_MapObjectAndVisibleModelAreNeverWritten`, `Arm64_VisibleMapObjectRemainsByteIdentical` |
| Preservar retorno original | `Arm64_WrapperPreservesOriginalReturnRegistersSimdAndFlags` |
| Exportación íntegra y origen intacto | `Surprise_Real_ExportReopensNativeResources`, `Surprise_Real_OriginalExecutableAndNpdmUnchanged` |
| Aplicación explícita | `Surprise_Real_UI_PreviewWaitsForApply`, `Surprise_Real_UI_ApplyEnablesNativeSessionOverlay` |

## Mod preparado y protocolo manual

Carpeta nueva: `C:/Users/Joanj/AppData/Roaming/Ryujinx/mods/contents/0100abf008968000/DR-Prueba-Combate-Sorpresa-20261022`.

Contiene solo exefs/main, exefs/subsdk9, manifiesto y LEEME. Los cuatro archivos copiados se verificaron por SHA256. No se modificaron otros mods, activaciones ni guardados. Es la salida nativa sola, no la salida de prueba combinada que cambiaba HP de una entrada.

1. Cerrar totalmente el juego y usar Espada 1.3.2. Activar este mod; desactivar otros mods ExeFS que sustituyan main o añadan módulos/parches, incluido Divine Sword si está activo. Puede combinarse con el mod previo de objetos corregidos si este aporta únicamente RomFS. No usar estado rápido.
2. Tocar varios Pokémon visibles y comprobar que el modelo sigue igual pero la especie del combate es otra. Comprobar nivel y que luchar, seleccionar movimientos y abrir/cambiar Pokémon no bloquean.
3. Capturar y comprobar que la especie capturada coincide con la del combate; revisar forma, nombre, género, habilidad, estadísticas y movimientos coherentes. Guardar/cargar normalmente y volver a revisar.
4. Huir y volver al mismo objeto: debe conservar la selección mientras siga generado. Probar distintos spawns y cambios de zona.
5. Probar entrenadores, encuentros ocultos, eventos y gimmicks: deben conservar su comportamiento. La cobertura de rutas guionizadas no se considera exhaustiva hasta probarlas.
6. Si falla carga, batalla o menú, conservar el log y describir punto, especie visible y especie de combate. Desactivar esta carpeta permite volver al comportamiento anterior sin borrar partidas.

## Riesgos residuales

No se ejecutó Ryujinx ni una prueba interactiva real. Carga MOD0/DT_INIT, generación completa del PK8, captura, estadísticas, aprendizaje, rutas ocultas y eventos siguen pendientes de validación jugable. El adaptador usa offsets y sentinelas inferidos del ejecutable; las pruebas no certifican todas las rutas del motor. El registro de aprobaciones es acotado a 512 entradas y usa hash del spec; un spawn antiguo puede perder elegibilidad y se mantiene el comportamiento original. La secuencia depende del orden de generación y de sesión.

El escritor exige texto original exacto y ausencia de subsdk9 en origen; no combina parches arbitrarios. El NSO main crece por conservar el payload original y añadir texto literal LZ4; es una decisión de integridad del prototipo, pendiente de optimización. Shield y Atmosphere no se anuncian compatibles.

## Corrección de arranque v2 — 22/10/2026

El usuario informó cierre del emulador al activar el primer mod. El log `Ryujinx_1.3.3_2026-10-22_00-32-24.log` registra `ResultLoaderInvalidNso (2009-0005)` en `NsoExecutable.DecompressSection`, antes de ejecutar el juego. El escritor emitía texto LZ4 literal cuyo tamaño comprimido era mayor que el original. El lector LibHac incluido en ese Ryujinx rechaza explícitamente esa condición antes de descomprimir. Las pruebas anteriores aceptaban el bloque LZ4 y no verificaban esta restricción del cargador.

Se corrigió `SwordSurpriseMod.cs`: el texto modificado se guarda sin compresión, con bit de compresión desactivado, tamaño almacenado igual al tamaño del segmento y hash conservado sobre los bytes originales descomprimidos. El lector propio ahora también rechaza segmentos comprimidos mayores que el tamaño decodificado. No cambia el código ARM64, el pool ni la configuración del módulo. No afecta la compresión de los GFPack.

Validación nueva:
- Compilación Release: 0 errores, 88 advertencias anteriores.
- `--swsh-only`: 212 aprobadas, incluida `Surprise_Mod_RejectsCompressedSegmentLargerThanDecodedSize`.
- Probe de extracción real: 18 aprobadas, incluida `Surprise_Real_TextUsesLoaderCompatibleUncompressedStorage`.
- Prueba independiente con el **mismo LibHac.dll de Ryujinx 1.3.3**, extraído únicamente a temporal sin alterar la instalación: el main anterior reproduce InvalidNso en texto; sus otros segmentos y subsdk9 pasan. La salida corregida pasa los seis segmentos con resultado Success. Harness temporal mediante NsoReader.Initialize/GetSegmentSize/ReadSegment; no se lanzó el emulador.
- Copia SHA256 verificada de los cuatro archivos a `DR-Prueba-Combate-Sorpresa-v2-20261022`, dentro de la carpeta autorizada de mods. Mod anterior preservado, incluidas posibles adiciones del usuario.

Para probar: cerrar completamente Ryujinx, desactivar el mod `DR-Prueba-Combate-Sorpresa-20261022` anterior y otros ExeFS, activar exclusivamente v2 entre los ExeFS. Los RomFS independientes pueden mantenerse. No activar las dos variantes a la vez. El cargador real, inicialización RTLD y jugabilidad todavía requieren prueba del usuario; no se declara solucionado todo el arranque hasta confirmarla. Sin cambios en ROM/guardado, ni commit/push.

## Diagnóstico de falta de efecto — v3, 22/10/2026

El usuario confirmó que v2 arranca, pero Yamper sigue siendo Yamper en combate. No se considera funcional la característica. El log 00-46-22 confirma la sustitución y carga de main y subsdk9 de v2; al iniciar el juego la variante anterior figura desactivada. No permite determinar si DT_INIT, descubrimiento de main o elegibilidad del spawn está fallando.

Se añadieron trazas DRSurprise v3 mediante svc OutputDebugString (0x27), cuya autorización se verificó en los descriptores KAC del main.npdm suministrado sin modificarlo. Muestran entrada al inicializador, configuración inválida, regiones RX con tipo/tamaño, coincidencia de main, primera ruta básica/gimmick y primeras cuatro copias para batalla con cambio o rechazo. No se cambiaron offsets ni condiciones de elegibilidad a ciegas. El RTLD original fue inspeccionado en lectura: su tabla dinámica reconoce DT_INIT; no se añadió init_array especulativamente.

Archivos: module.c, module.S, test_runtime.py, recurso NSO propio y RuntimeHash de SwordSurpriseMod.cs. SHA base de este módulo: E252964293C92212F8408AEB413082385985571EAD5F000667DE47EB743C67E9 (sustituye el hash histórico para el recurso actual).

Validación: Release 0 errores/88 advertencias anteriores; 212 comprobaciones Switch; 18 probe de extracción real; 12 ARM64 simuladas. Tres nuevas: Arm64_DiagnosticsDistinguishInitializationAndInstallation, Arm64_DiagnosticsReportActualMemoryTypeAndSize, Arm64_DiagnosticsDistinguishSpawnBattleAndBypass. Lector LibHac del emulador acepta los seis segmentos de ambos NSO. La preservación de registros sigue aprobada con las llamadas de diagnóstico.

Instalado en carpeta nueva DR-Prueba-Combate-Sorpresa-v3-trazas-20261022, cuatro hashes de copia verificados. Probar con solo v3 como mod ExeFS, cierre completo previo, hasta entrar en un encuentro visible; después cerrar normalmente y revisar el último log. Si no aparece initializer entered, investigar inicialización real/RTLD. Si aparece main not found o query stopped, revisar mapa/tipo/alcance. Si instala callbacks y aparece basic pero no battle, revisar ruta de batalla; si battle eligibility rejected, revisar layout y correspondencia de spawn. Estos son próximos pasos condicionados a evidencia, no causas confirmadas.

No se ejecutó el juego por herramientas; no se da por corregido el efecto. Mods previos, activaciones, extracción y guardado intactos. Sin commit/push.

## Destino indirecto de batalla — v4, 22/10/2026

Log real 00-56-35: initializer entered, RX type 3/tamaño 0x1901000, main matched installing callbacks, basic spawn reached y cuatro battle reached but eligibility rejected. La carga, inicialización y rutas de callbacks quedan acreditadas para ese recorrido; la selección no se aplicaba.

La función original D60920 guarda x8 en x20 y en D609A8 ejecuta `ldr x20,[x20]` antes de escribir especie en x20+A0. El wrapper conservaba x8 y lo entregaba directamente al adaptador: verificaba datos del handle en lugar del objeto destino. Se añadió una única desreferencia después de retornar la función original. No se relajaron las condiciones de elegibilidad, no se cambiaron offsets de especies y no se escriben el handle ni el objeto del mapa.

Prueba corregida: el cuerpo original simulado ahora desreferencia x8 igual que el ejecutable real. El handle se coloca aparte, con canarios. Ejecutar la prueba contra el ELF v3 produce FAIL Arm64_ActualNativeAdapterChangesOnlyApprovedVisibleBattle. Contra v4 pasan 13 comprobaciones ARM64, incluida Arm64_BattleDestinationHandleRemainsUnchanged, preservación de registros y modelo. La prueba anterior era insuficiente porque simulaba un destino directo; se reconoce y corrige esa diferencia de ABI.

Cambios: module.S, test_runtime.py, recurso NSO propio y RuntimeHash. Hash base actual F3E572AF8BD8249DD42E5EF4E6CC81D8BD5C0190BD61874749964E9B20FE90D8. Se mantienen las trazas con prefijo DRSurprise v3 como formato de diagnóstico; la carpeta de entrega identifica v4. Las funciones originales siguen siendo fixtures en Unicorn, no ejecución interactiva.

Validación final v4: Release 0 errores/88 advertencias anteriores; 212 Switch, 18 probe reales y seis segmentos aceptados por LibHac. Carpeta nueva DR-Prueba-Combate-Sorpresa-v4-20261022 instalada con cuatro hashes verificados. Mods anteriores y guardados preservados. Pendiente probar el encuentro y captura en el juego; sin commit/push.

## Rechazo persistente en v4 — diagnóstico v5

Log real 01-06-17: v4 activo, inicialización/callbacks correctos, ruta básica y cuatro rechazos de elegibilidad. La corrección de indirección no basta para resolver el encuentro real. No se afirma que la especie se haya randomizado.

Se añade dr_adapter_copy_diagnostic con motivo por llamada, sin estado global compartido y sin alterar el valor de retorno ni las condiciones del API anterior. Motivos: 0 aplicado, 1 entrada/pool inválidos, 2 modelo fuera del rango esperado, 3 modelo/especie distintos, 4 forma del modelo distinta, 5 especie destino distinta, 6 forma destino distinta, 7 nivel destino distinto, 8 contención, 9 sin aprobación coincidente, 10 selección rechazada. Se registran los nueve valores en hexadecimal, las primeras ocho estructuras básicas de 88 bytes y la estructura de batalla si falta correspondencia. Hasta doce copias de batalla por sesión; nada escribe ROM o guardado. Los campos llamados model siguen siendo hipótesis pendientes de contraste con el log, no se elimina su validación a ciegas.

Archivos modificados: adapter.h/c, adapter_tests.c, module.c, recurso NSO y RuntimeHash. Hash base v5: 5644962425DC8BA710F8EE06CE2612362D8203A18F375C4C632E5D93E71B68F5. Pruebas C: 23 aprobadas, incluidas Adapter_DiagnosticReportsMissingApprovalWithoutWrites y Adapter_DiagnosticRejectGate2PreservesOutput hasta Gate7. Pruebas ARM64 del módulo final: 13 aprobadas. Originales simulados, no gameplay. La siguiente prueba real debe identificar el gate concreto y los datos discrepantes antes de cambiar reglas o layouts.

V5 final: Release 0 errores/88 advertencias anteriores; 212 Switch, 18 probe reales y seis segmentos aceptados por LibHac. Instalada carpeta nueva DR-Prueba-Combate-Sorpresa-v5-diagnostico-20261022 con cuatro hashes verificados. Modos de activación, otros mods y guardados intactos. Pendiente diagnóstico real; sin commit/push.

## Campo 8A0 no es especie — v6, 22/10/2026

Log real 01-18-12 registra motivo 3: +8A0 vale 1 y +8A4 vale 0, mientras especie/forma/nivel de spec y destino coinciden. Incluye Yamper (0x343), Pumpkaboo (0x2C6) con formas 1/2 y Diglett (0x32). La comparación +8A0==especie y +8A4==forma era una interpretación incorrecta. El significado exhaustivo de ambos campos no se declara resuelto; +8A0 se trata conservadoramente como marcador de ruta observado, no como índice gráfico o especie.

Corrección: solo marcador observado 1; otros marcadores no se tocan. Especie y forma vienen exclusivamente de +8A8/+8AC, nivel de +8AE, contrastados con el destino real indirecto. Se validan especie 1..898 y nivel 1..100. Se mantiene la aprobación por hash del spec de un spawn básico, revocación gimmick, correspondencia de objeto y todas las restricciones de escritura. No se sustituye la elegibilidad por una randomización global de cualquier spec. El motivo 2 pasa a marcador desconocido, 3 a especie inválida, 4 a nivel inválido; 5..10 conservan significado. Los mensajes de detalle usan route/adjacent en vez de model/form.

Se agregó un fixture con los 22 words exactos del registro de Yamper nivel 14. Con el código anterior falla Adapter_RecordedYamperMarkerIsNotSpeciesId. Con el corregido pasan ese caso, Adapter_RecordedAlternateFormDoesNotUseAdjacentMarkerAsForm y Adapter_UnknownRouteMarkerRemainsUntouched. Se ajustaron los fixtures sintéticos para no inventar igualdad entre marcador y especie.

Archivos: adapter.h/c, adapter_tests.c, module.c, test_runtime.py, recurso NSO y RuntimeHash de SwordSurpriseMod.cs. Hash base actual: 08E7486F06A4349D539FE58E83F8FBF2179EAE9C90E80FCF5DA05ACF242696A7.

Validación final: C host 27 aprobadas; ARM64 13 aprobadas; compilación Release 0 errores/88 advertencias anteriores; runner Switch 212 y probe real 18 aprobadas; los seis segmentos aceptados por LibHac de Ryujinx. Copiados cuatro archivos con SHA verificado a DR-Prueba-Combate-Sorpresa-v6-20261022. Extracción, guardado, activaciones y variantes anteriores intactos. Sin commit/push. La compatibilidad de captura, menús y rutas reales sigue pendiente de una prueba del usuario con v6; no se declara éxito jugable basándose en el fixture.

## Comparación PokeFeur y aprobación tras finalización — v7

El usuario aportó GameBanana 674097 y github.com/JonatanUHC/PokeFeurRandomSWSH. La descripción pública anuncia Vanilla visible + hidden battle y Random visible + hidden battle mediante ExLaunch. La página indica que es un espejo y remite a github.com/jonatanuhc/PokeFeur, que en el árbol consultado contiene principalmente el visor. El repositorio PokeFeurRandomSWSH (GPL-3.0) ofrece script, contratos y módulos subsdk9 estables/Ryujinx; el árbol revisado no contiene el C++ del runtime. Se leyeron descripción, árbol, script y README, sin ejecutar ni instalar sus binarios, NPDM o scripts. No se afirma haber reutilizado una implementación C++ que no se publicó en ese árbol. Referencias: https://gamebanana.com/mods/674097 ; https://github.com/JonatanUHC/PokeFeurRandomSWSH ; assets/exlaunch/README.txt y contract/hook-baseline-epee-1.3.2.json.

El log propio 10-15-26 muestra motivo 9: identidad y nivel ahora coinciden, pero los 88 bytes del spec mutan después de BasicSpawn: cambian naturaleza, género, habilidad, objeto, flags y semilla. Un hash tomado antes de resolverlos nunca coincide con la copia final. No es un fallo de carga.

Se verificó en el ejecutable original que la llamada D31D34 a D329D0 recibe x1=x21+58 y, a continuación, la llamada D31D64 a D311C0 recibe el mismo x24=x21+58. D311C0 completa campos y escribe flag +4B y semilla +50; prólogo original sub sp,sp,#180 (D10603FF). Nuevo cuarto hook a su entrada, con wrapper de salida que preserva el estado original. Después de finalizar se actualiza el hash únicamente para la aprobación más reciente pendiente, válida, con el mismo puntero fuente y la misma identidad inicial de ocho bytes. No se aprueba un registro nuevo por coincidir en especie. Una aprobación revocada por gimmick no puede reaparecer por finalizar.

Los stubs pasan de 72 a 96 bytes, todavía dentro del hueco original de texto; BSS propio añadido de 24 a 32 bytes, cuatro callbacks. Nuevo trampoline 1900F88 y slot 2985018. Se mantienen las otras tres rutas, controles de origen exacto y destino indirecto. El preflight sigue fijando hash de todo el texto original y ahora verifica también el prólogo de finalización. No se modifica la extracción original ni NPDM. Una mutación posterior adicional que no cubra este hook seguirá dando bypass y debe investigarse por log; no se garantiza toda la secuencia del motor sin juego real.

Cambios: adapter.c/h, module.c/S, test_runtime.py, adapter_tests.c, SwordSurpriseMod.cs, SwordSurpriseIntegrationChecks.cs y recurso NSO. Hash base actual 2C368A9B977C0C69AE09367CE5EF2185FF6E231D4D2A802537D21928F8322B24.

Pruebas C: 31 aprobadas; nuevas Adapter_FinalizedMutationRequiresMatchingRefresh, Adapter_FinalizerRejectsUnrelatedSourcePointer, Adapter_FinalizerAcceptsMutatedFieldsOfApprovedSource, Adapter_FinalizerCannotReviveRevokedSpawn. ARM64: 16 aprobadas; nuevas Arm64_UnfinalizedApprovalRejectsChangedSpec, Arm64_FinalizerRefreshesOnlySameApprovedSource, Arm64_FinalizerCannotReviveRevokedGimmick. La prueba muestra rechazo antes de refrescar y aceptación después con iguales campos mutables a los observados. Originales simulados; no gameplay ni certeza de captura.

V7 final: Release 0 errores/88 advertencias anteriores; 212 Switch y 18 probe reales aprobadas, incluida Surprise_Real_OnlyFourInstructionsAndNewStubsChange; seis segmentos aceptados por LibHac. Instalada DR-Prueba-Combate-Sorpresa-v7-20261022 con cuatro SHA de copia verificados. Pendiente prueba real del hook de finalización y captura. Variantes anteriores, activaciones, ROM y guardados preservados; sin commit/push.
