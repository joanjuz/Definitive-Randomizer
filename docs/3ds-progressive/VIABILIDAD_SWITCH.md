# Viabilidad de soporte Pokémon Switch

Seguimiento: se inició un [prototipo aislado de Espada/Escudo](SWITCH_SWSH_PROTOTIPO.md). Las secciones de viabilidad de este documento describen la investigación previa.

Fecha: 21 de octubre de 2026. Investigación de código y documentación pública; no es una certificación de compatibilidad ni una prueba jugable.

## Conclusión

Es técnicamente viable ampliar Definitive Randomizer manteniendo WPF y el soporte 3DS. La salida de Switch debe ser un mod con archivos modificados, no una ROM reconstruida. No basta cambiar el exportador: se necesitan identificación, formatos, modelos y servicios propios por familia de juegos. Se recomienda un primer prototipo acotado a Espada/Escudo 1.3.2, versión de referencia declarada por pkNX. Esta es una recomendación de ingeniería, no soporte implementado.

## Evidencia externa

- [pkNX README](https://github.com/kwsch/pkNX#readme): editor y randomizer C#/.NET 10, con salida redirigida para LayeredFS. Enumera Let's Go, Espada/Escudo y Leyendas Arceus; para Escarlata/Púrpura y Leyendas Z-A advierte explícitamente que solo permite extraer datos, no editar archivos. Revisar de nuevo las versiones antes de integrar; no asumir soporte de futuras actualizaciones.
- [GameManagerSWSH](https://github.com/kwsch/pkNX/blob/master/pkNX.Game/GameManagerSWSH.cs): lectura y serialización de movimientos, aprendizajes, datos personales y evoluciones; identifica el título mediante main.npdm cuando está disponible. Su alternativa por defecto a Espada NO debe heredarse: la aplicación debe exigir identidad inequívoca.
- [GameFileMapping](https://github.com/kwsch/pkNX/blob/master/pkNX.Game/File/GameFileMapping.cs): mapas y contenedores separados por familia; escrituras de contenedores modificados.
- [GameManagerSV](https://github.com/kwsch/pkNX/blob/master/pkNX.Game/GameManagerSV.cs): Trinity data.trpfs/data.trpfd; inicialización de varios editores comentada. Leer estructuras no demuestra que se puedan reconstruir mods jugables.
- [FileMitm](https://github.com/kwsch/pkNX/blob/master/pkNX.Containers/FileMitm.cs): rutas originales/redirigidas y Enabled son estáticas. Integrar directamente varios proyectos o Batch concurrentes puede cruzar destinos. Necesita aislamiento por proceso o un adaptador con almacenamiento por sesión y validación previa.
- [Atmosphère, implementación RomFS](https://github.com/Atmosphere-NX/Atmosphere/blob/master/stratosphere/ams_mitm/source/fs_mitm/fsmitm_romfs.cpp) y [configuración](https://github.com/Atmosphere-NX/Atmosphere/blob/master/docs/features/configurations.md): infraestructura para redirección de recursos por programa.
- [LumiTool](https://github.com/SaltContainer/LumiTool) y [BDSP Randomizer](https://github.com/AKCore/BDSP-Randomizer): BDSP tiene una vía distinta mediante recursos Unity/AssetBundles; no aparece como familia soportada en el README de pkNX. Estas referencias demuestran herramientas existentes, no una integración lista.

## Matriz preliminar

| Familia | Evidencia disponible | Decisión propuesta |
|---|---|---|
| Let's Go Pikachu/Eevee | Editores en pkNX | Viable; segunda integración, reglas propias de especies y mecánicas |
| Espada/Escudo | Editores y serializadores en pkNX | Primer objetivo; fijar versión y combinación de DLC |
| Leyendas Arceus | Editores en pkNX | Adaptador posterior; encuentros y mecánicas propios |
| Diamante Brillante/Perla Reluciente | Herramientas específicas de AssetBundles | Investigación separada; no reutilizar el adaptador de Espada/Escudo |
| Escarlata/Púrpura | pkNX documenta extracción, no edición | No prometer randomización: investigar escritura, reconstrucción de contenedores e integridad |
| Leyendas Z-A | pkNX documenta extracción, no edición | Investigación posterior independiente; no extrapolar soporte de otras familias |

La lista cubre los RPG Pokémon de interés para este randomizer, no todos los títulos y derivados de Switch.

## Estado del código local

- `pk3DS.Core/Game/GameConfig.cs`: reconoce familias 3DS y usa referencias GARC; no tiene un proveedor Switch.
- `3DS.Progressive.App/Services/GameProjectSession.cs`: está ligada a GameConfig, tablas personales y textos 3DS; no es una abstracción multiplataforma lista para utilizar.
- `3DS.Progressive.App/Services/GameRomRebuildService.cs`: produce RomFS/ExeFS/3DS mediante algoritmos CTR; debe permanecer en la ruta 3DS.
- `3DS.Progressive.App/Services/ProjectHistory.cs`: ofrece registro y commits verificados reutilizables como diseño; las operaciones concretas y replay dependen de los servicios actuales y requieren adaptación. No basta reutilizar el JSON.
- La interfaz WPF, temas, tablas, selectores por ID, vista previa y resumen pueden reutilizarse gradualmente mediante modelos separados de las estructuras binarias.
- El proyecto local y pkNX declaran GPL v3. Antes de incorporar código, revisar archivos concretos, dependencias y atribuciones y conservar los avisos y fuentes correspondientes. No se incorporó código externo en esta investigación.

## Arquitectura propuesta, aún no implementada

1. Un descriptor por proyecto: plataforma, juego, Title ID, versión, DLC, idiomas y huellas de los recursos. Build ID cuando una función modifique ejecutables.
2. Proveedores separados para 3DS y cada familia Switch. Capacidades explícitas por función; no inferir compatibilidad solo por número de generación.
3. Lectura desde una extracción preparada de la versión admitida, más una capa de modificaciones por proyecto. Originales siempre en lectura. Primera versión sin abrir directamente NSP/XCI ni administrar claves.
4. Escrituras en staging, validación de ida y vuelta y commit atómico con historial. Los cambios en archivos contenedores deben reconstruir el contenedor requerido, conservando miembros y datos desconocidos. Un solo cambio interno puede requerir exportar un archivo grande.
5. Exportador Switch independiente: carpeta `atmosphere/contents/<TitleID>/romfs/...` para recursos modificados y un manifiesto externo con juego, versión, semilla, operaciones y hashes. El destino para emulador se define por perfil y debe comprobarse por separado. Sin rebuild de NSP/XCI.
6. Parcheo de ejecutables fuera del primer prototipo. Si se incorpora, formato y Build ID explícitos, rechazando versiones desconocidas. Los parches CRO y las compras únicas de USUM no se trasladan automáticamente.
7. Global Template y Batch se amplían por plataforma y capacidades, preservando recetas 3DS. Un lote de Switch exportaría mods separados con semillas y destinos aislados. Nunca reutilizar IDs y offsets binarios por coincidencia de nombre de una acción.

## Primer prototipo y criterios de aceptación

- Elegir una edición, versión exacta y estado de DLC; verificar un dump preparado con su actualización correspondiente.
- Abrir en lectura e identificar inequívocamente la edición. Rechazar datos incompletos, versiones no soportadas o identidad ambigua.
- Empezar por datos personales y una edición manual pequeña y reversible. Después, randomización con semilla y pool de especies/formas legal para esa edición.
- Sin cambios, no producir recursos modificados. Editando un valor, conservar el resto de datos y reabrir el recurso exportado.
- Exportar un mod, registrar el cambio en historial y verificar que el dump original conserva sus hashes.
- Probar arranque, carga, selección de equipo, selección de movimientos y varios combates y eventos en el juego real. Verificar que retirar el mod restaura el comportamiento original.
- Solo entonces añadir entrenadores, encuentros, aprendizajes, evoluciones y operaciones económicas, cada una con su propia cobertura y prueba jugable.
- No iniciar un refactor masivo del soporte 3DS para hacer este experimento: construir una ruta aislada e ir compartiendo únicamente contratos probados.

## Validación realizada y límites

Se revisaron los archivos locales indicados y fuentes públicas enlazadas. No se cargó ningún juego Switch, no se compiló pkNX, no se integró ninguna dependencia y no se produjo ni probó un mod Switch. La viabilidad técnica no equivale a compatibilidad jugable.

Por separado se recuperaron únicamente los iconos previamente retirados. Compilación Debug de App y Tests: 0 errores, 88 advertencias. Runner: 17 comprobaciones de iconos y 95 visuales aprobadas, fuera de pantalla. No se restauró la función de textos de efectividad ni se modificaron ROMs o guardados. Sin commit ni push.
