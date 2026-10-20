# Integración actual

El prototipo jugable previo fue validado por el usuario. La aplicación incorpora ahora `Gen7SinglePurchaseStock`, registro estable de todas las ofertas especiales, historial/Global/Batch y mensaje propio. Véase `docs/3ds-progressive/COMPRAS_UNICAS_TIENDAS_ESPECIALES.md` para alcance y limitaciones. El mensaje nuevo y la integración completa están pendientes de prueba jugable.

`generate_integrated_arm.py` genera únicamente instrucciones propias para el payload C#; `verify_integrated_arm.py` verifica el CRO generado con Unicorn, sin controlar el escritorio. No se distribuyen assets/ROMs. Los apartados siguientes documentan el prototipo histórico, no el estado final de integración.

---

# Compras únicas por tienda: prototipo ARM USUM

**Estado: EXPERIMENTAL. Arnés ARM y herramienta separada para generar una copia jugable de prueba. Pendiente de validación dentro del juego.**

La herramienta ensambla tres hooks en una copia en memoria de Shop.cro y ejecuta instrucciones ARM con Unicorn. No modifica ROMs, ejecutables ni partidas. Solo emite JSON y el ensamblador generado. El arnés por sí solo no instala nada; el helper `playable/` descrito al final solo prepara una copia experimental separada.

Los flags 100, 101 y 102 son datos de una partida simulada. No están reservados ni aprobados para una partida real. No deben trasladarse a una ROM jugable.

Requiere Python y las versiones de requirements.txt. Se pueden instalar en un directorio aislado mediante pip --target. El script acepta los argumentos obligatorios --shop, --code, --crs y --output-directory; --dependencies permite indicar ese directorio aislado.

Ejemplo: python tools/research/usum_shop_stock/prototype_stock.py --shop D:/MiExtraccion/ExtractedRomFS/Shop.cro --code D:/MiExtraccion/ExtractedExeFS/.code.bin --crs D:/MiExtraccion/ExtractedRomFS/static.crs --dependencies D:/DependenciasDePrueba --output-directory D:/ResultadosDePrueba

Solo acepta los hashes de los tres archivos investigados y comprueba instrucciones originales y espacio vacío antes de preparar la copia en memoria. No reconoce otras versiones, otros parches, SM ni Gen6. Rechaza Python con optimización (-O).

## Cobertura

- Cantidad máxima 1 y nueva comprobación al confirmar.
- Primera compra permitida y siguientes bloqueadas por tienda+objeto.
- Otra tienda y otro objeto mantienen su propio estado.
- Filas duplicadas del mismo objeto en una tienda comparten el límite.
- Cancelación, falta de dinero, límite de objetos y fallo de entrega no consumen stock ni cobran.
- Los objetos no incluidos, las tiendas normales y el modo BP conservan sus reglas comprobadas en el arnés.
- Se serializa y restaura memoria de estado en la prueba.

Se ejecutan la rutina original de disponibilidad de Shop.cro, el tramo de confirmación hasta entrega, el cálculo de coste y las funciones nativas de flags de code.bin. Inventario, diario, algunos componentes de interfaz y división entera usan sustitutos controlados. No es una sesión de Citra/Lime3DS/Azahar ni una prueba de guardado, checksums o UI real. El cobro examinado es el saldo local del contexto de tienda; su persistencia en la partida sigue pendiente.

Antes de integrar: reservar almacenamiento auditado, probar guardado y UI reales, validar una versión concreta y añadir integración de editor/Global/Batch. Véase docs/3ds-progressive/PENDIENTE_COMPRAS_UNICAS_USUM.md.

## Retoma del 20/10/2026

Se conserva el modo anterior (flags exclusivamente sintéticos) y se añade `--experimental-storage`.
Este último ejecuta accesores ARM propios sobre 12 bytes candidatos en `EventWork`: offset serializado
`0xD00`, offset del objeto `0xD04` por su prefijo de vtable. **No es una reserva aprobada.**
La cabecera identifica el experimento y su receta de prueba; una cabecera ajena, parcial, de otra receta
o con bits inválidos bloquea la compra sin modificar stock, inventario ni dinero. El identificador de
receta es una constante del arnés: todavía no representa una semilla ni un hash real de Global Template.
No inicializa memoria durante la consulta; solo registra la primera compra confirmada.

Se ejecutan los accesores virtuales nativos del bloque: longitud `0xE00` y puntero `objeto+4`.
El roundtrip copia exactamente ese bloque entre máquinas Unicorn. **No ejecuta el guardado del juego,
no valida checksums ni acredita que este espacio esté libre de todos los consumidores del juego.**
La rutina inicializadora investigada limpia desde objeto+`0xA5A` durante `0x3AA` bytes; este dato es
una pista, no una prueba de propiedad exclusiva del espacio. Deben auditarse lectores/escritores y
el ciclo de guardado antes de instalarlo.

### Shop.cro ampliado y ROM randomizada

`--allow-known-expanded` admite además SOLO las huellas exactas investigadas de la extracción local
randomizada de UltraSol (no cualquier CRO o ejecutable). El Shop ampliado tiene 45.056 bytes; las
tablas ocupan parte de la antigua zona `0x4800`, por lo que los hooks se desplazan a `0x5700`.
Se comprueba que el nuevo hueco esté vacío y que solo cambien en memoria los tres puntos de entrada
y el área experimental. Se mantienen intactos todos los demás bytes, incluidas tablas y relocaciones.
No se instala ni se rehasha un CRO/CRR y no hay garantía para ampliaciones futuras o UltraLuna.

Huellas adicionales:
- Shop.cro: `8BB08FBCD26B694CADC6ED6858616E57BE7AD51AB61A0181D30B570730A28D7A`.
- code.bin: `D2E9B4D91D7B14BCEF0C34D869C1DA60EF85D98B1DF3C34CBC42E42F54CFBA27`.

Validación final: 35 comprobaciones en el modo anterior, 45 en almacenamiento candidato con archivos
originales y las mismas 45 con Shop.cro/code.bin randomizados. También rechaza tres archivos alterados
y la variante ampliada sin autorización explícita del argumento. Los conteos se solapan.
Resultados locales: `C:/Users/Joanj/AppData/Local/Temp/DR-ShopStock-20261020/validation-summary.json`.

Referencias de contraste (no sustituyen la evidencia binaria local):
- https://github.com/kwsch/PKHeX/blob/master/PKHeX.Core/Saves/Substructures/Gen7/EventWork7.cs
- https://github.com/FlagBrew/PKSM-Scripts/blob/master/dev/docs/gen7/usum.txt

## Copia experimental jugable (20/10/2026)

La petición explícita del usuario de generar una ROM para probar permite avanzar a una copia aislada;
no significa que el almacenamiento candidato quede aprobado para producción ni partidas principales.
`playable/ShopStock.Playable.csproj` reutiliza `Gen7ExpandedMartTable`, `CroPatchSession`, `CroIntegrity`,
`GameRomRebuildService` y `GameRomExtractionService`. No modifica los servicios de producción.
Usa el nombre de ensamblado amigo del runner únicamente en su propio directorio de salida.

Fases: `prepare SOURCE NEW_ROOT`, ejecución del arnés Python con `--allocation-plan PLAN
--experimental-storage --allow-known-expanded --export-tested-image`, `install ROOT TESTED_IMAGE`,
`build ROOT NEW_3DS` y `verify ROOT IMAGE NEW_EXTRACTED`.
Cada fase verifica la huella exacta y trabaja en copias. El expansor asigna 0x700 bytes dentro del
segmento ejecutable declarado; en la copia investigada amplió .text 0x2000 bytes. No reutiliza un
hueco fijo ni pisa inventarios existentes. No modifica el thunk nativo de flags: solo los hooks del
prototipo consultan su propio almacenamiento. Todas las comprobaciones ARM pasan antes de exportar.
`install` recalcula y comprueba los hashes finales, conserva las demás tablas y CROs, y reconstruye CRR.
`verify` extrae el .3ds generado y compara todos los archivos de RomFS y ExeFS con la copia preparada.

Fixture: Hélix101 y Domo102 limitados en Hau'oli/tienda10, Hélix101 independiente en Ruta2/tienda11;
Ámbar103 es control sin límite en ambas. Precios originales, sin alterar el GARC de objetos.
La huella de receta del prototipo sigue siendo experimental y fija, sin integración Global/Batch.
Los binarios propietarios y la ROM resultante permanecen fuera del repositorio.


### Regresión de runtime corregida

El verificador integrado ejecuta ahora también la clasificación nativa y el cierre del diálogo, no solo la llamada que carga texto. `--legacy-save` lee un guardado del prototipo conocido y copia únicamente sus 256 bytes candidatos a memoria simulada; jamás escribe la partida. La compatibilidad DRS1/STOC conserva las tres compras originales y se prueba con las ocho máscaras. La ejecución del juego con la versión corregida sigue siendo una comprobación manual.
