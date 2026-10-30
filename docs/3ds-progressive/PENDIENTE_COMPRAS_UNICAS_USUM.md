# PENDIENTE: compras únicas por objeto y tienda especial

Fecha: 16/10/2026. El usuario pidió continuar con el prototipo y dejar la función pendiente para atender otro error.

## Estado de entrega

Prototipo ARM funcional en emulación: tools/research/usum_shop_stock/prototype_stock.py. No está integrado en la aplicación, editor, templates ni Batch. No está desplegado ni habilitado. No se modificaron ROMs, partidas ni distribución instalada. Sin commit ni push de esta tarea.

Guías aplicadas: pk3ds-progressive-safety y dotnet-test:code-testing. La inspección temporal C# se compiló con binlog según dotnet-msbuild:binlog-generation; no hay cambios C#/XAML de producción que compilar.

## Implementación experimental

1. Contexto identificado: modo en [contexto+0x20]->byte0, ID de tienda en [contexto+0x20]->uint32(+4), fila seleccionada +0x82, cantidad +0x84, máximo seleccionable +0x86 y tabla de filas +0x6C.
2. Wrapper de disponibilidad en Shop.cro+0x276C: consulta el estado por tienda+objeto, devuelve el estado nativo de comprado y limita la cantidad, conservando las validaciones de dinero y capacidad.
3. Comprobación al confirmar en Shop.cro+0x15E4, antes de entrega y cobro. Impide repetir una confirmación o alterar la cantidad.
4. Marcado después de entrega en Shop.cro+0x164C. Si añadir el objeto devuelve fallo, cancela antes del cálculo de coste y no consume stock.
5. Lookup por tienda+objeto, independiente de la posición de fila y de la posesión actual. Tres ofertas simuladas cubren dos tiendas y dos objetos.
6. Todos los cambios binarios quedan únicamente en un bytearray. No se exporta una ROM ni un CRO instalable. Se verifican hashes e instrucciones originales.

## Validación ejecutada

Ejecución final: 31 comprobaciones ARM correctas.

| Requisito | Evidencia reproducible |
| --- | --- |
| Primera compra y cobro | first_purchase_succeeds; one_item_and_exact_price |
| Segunda compra bloqueada | second_purchase_reports_sold_out; confirmation_rechecks_sold_out |
| Cantidad limitada | quantity_selector_maximum_one; quantity_clamped_2; quantity_clamped_99; quantity_clamped_32767 |
| Independencia entre tiendas y objetos | same_item_other_shop_has_independent_stock; different_item_same_shop_has_independent_stock |
| Filas duplicadas | duplicate_row_in_same_shop_shares_limit |
| Gastar/vender no repone stock | using_or_selling_item_does_not_restore_stock |
| Restauración de memoria simulada | flag_state_survives_synthetic_save_reload (NO es un guardado real) |
| Fallos sin consumo ni cobro | insufficient_money_preserves_stock_items_and_money; full_bag_preserves_stock_items_and_money; delivery_failure_preserves_stock_items_and_money |
| Cancelación | cancel_does_not_consume_stock |
| Reglas ajenas preservadas en el arnés | unlisted_item_keeps_original_transaction; normal_progression_shop_unaffected; bp_mode_not_subject_to_regular_shop_stock |
| Escritura acotada | only_assigned_synthetic_flag_byte_changes |
| Fuente intacta | original_shop_file_unchanged |

También pasaron rejects_unknown_shop, rejects_unknown_code y rejects_unknown_crs. Rechazaron las entradas incorrectas sin generar un resultado exitoso. Total: 34 comprobaciones correctas.

Resultados locales: C:/Users/Joanj/AppData/Local/Temp/DR-ShopStock-PoC-final/prototype-results.json y prototype-hooks.asm. Las dependencias están aisladas; no se añadieron a la aplicación ni al Python del sistema.

## Bloqueo de almacenamiento

Los flags 100-102 pertenecen SOLO a memoria sintética. No están libres ni aprobados para partidas reales.

Se inspeccionaron 441 entradas EI; no aparecieron los candidatos catalogados como no utilizados 0x09E4 y 0x0A2F. Ese resultado solo cubre recogibles visibles: no certifica ausencia de uso en scripts, objetos ocultos u otros módulos. No se reservaron esos flags.

Las funciones nativas investigadas comparan los flags persistentes con 0x1340, mientras que PKHeX expone 4.960 flags (0x1360). Esta diferencia exige validar la versión: no permite asumir que los últimos bits estén libres. Las funciones se resolvieron desde static.crs y se contrastaron con code.bin.

## Para retomar

1. Auditar y reservar estado persistente sin colisiones con historia u otros parches; definir propiedad, inicialización y partidas existentes.
2. Sustituir los IDs simulados por ese contrato y verificar serialización, integridad y guardado/carga reales.
3. Probar UI de compra, agotado, salida/cancelación y cobro persistente. El arnés omite pasos visuales y sustituye inventario/diario.
4. Crear un instalador gestionado con firmas, relocaciones, rehash CRO/CRR y rechazo de versiones desconocidas. Revalidar tras ampliaciones de Shop.cro y parches de tutores.
5. Integrar editor, historial y Global/Batch conservando recetas anteriores. Mantener desactivado hasta validar en juego.
6. Probar copias aisladas con un fósil en dos tiendas, partidas nueva/existente, guardar/reiniciar, vender/restaurar, fallos de compra y semillas distintas. Definir vendedores que comparten inventario y cambios de semilla con una partida empezada.

No anunciar como función disponible ni incluir en un release mientras estos puntos estén pendientes.

## Retoma 20/10/2026: persistencia candidata y compatibilidad con tablas ampliadas

Se recuperaron únicamente Shop.cro, static.crs y .code desde las imágenes limpias ya existentes,
en una carpeta temporal de diagnóstico. La extracción actual de UltraSol está randomizada; no
se modificó. Se verificaron y probaron tanto los archivos originales como las huellas concretas
de Shop.cro/code.bin randomizados actuales.

El prototipo incorpora `--experimental-storage` y `--allow-known-expanded`; los detalles y límites
están en `tools/research/usum_shop_stock/README.md`. La ampliación de tablas ya ocupa parte del hueco
antiguo de hooks: reutilizar esos offsets habría destruido inventarios. En la variante investigada
se reubican en memoria y una comprobación byte a byte garantiza que el resto del CRO no cambia.

Resultados realmente ejecutados: modo anterior 35 PASS; candidato sobre originales 45 PASS;
candidato sobre archivos randomizados 45 PASS. Cuatro rechazos adicionales verificados: CRO ampliado
sin opt-in y Shop/code/CRS alterados. Son ejecuciones Unicorn con inventario/interfaz sustituidos;
no son partidas, compras interactivas ni guardados reales.

La candidata de almacenamiento usa un área dentro del bloque serializado, cabecera identificada,
identidad experimental de receta y bitmap independiente. No consume flags de historia. La comparación
de la cabecera bloquea datos ajenos y no los reinicializa. El roundtrip usa el límite publicado por
los accesores virtuales nativos (objeto+4, longitud 0xE00); no ejecuta el sistema completo de guardado.
El área 0xD00 sigue **sin reserva aprobada**: quedan por auditar todos sus consumidores, otros parches,
serialización/checksums y comportamiento con partidas existentes. No generar todavía una ROM instalable.

Siguiente trabajo: completar esa auditoría de almacenamiento, convertir los offsets de hooks en una
asignación basada en la estructura real del CRO (no una lista de hashes locales), validar rehash/CRR,
y entonces generar una copia experimental para probar compras y guardado/reinicio dentro del juego.
No se integró en la aplicación ni en Global/Batch y no se realizó commit, push ni release.

## Copia jugable experimental solicitada y generada (20/10/2026)

Por solicitud explícita del usuario se creó una copia experimental, sin considerar aprobada para
producción la reserva candidata ni instalarla en su ROM o partida principal.

- Carpeta: `D:/Progra/Pokemon/Randomizer/PrototipoComprasUnicas20261020`.
- ROM: `USUM-compras-unicas-prototipo.3ds` (3.690.545.152 bytes).
- SHA-256: `6E97136ECBA84EC66DDE55D5A80C53B742E25882B8C128E8CB0A44333AFB989D`.
- Helper de investigación: `tools/research/usum_shop_stock/playable/ShopStock.Playable.csproj` y `Program.cs`.
- Usa el expansor EXISTENTE: `CroPatchSession.TryAllocateRelocatableCode`. Amplió .text 0x2000 bytes;
  grant de 0x700 bytes dentro del segmento declarado; sin usar un hueco fijo.
- Ofertas de prueba: Hélix101 y Domo102 únicos en tienda10 (Hau'oli objetos X), Hélix101 independiente
  en tienda11 (Ruta2 varios), Ámbar103 sin límite en ambas. Mantiene precios, otras tiendas, BP y tutores.
- El thunk original de flags permanece intacto; solo los nuevos hooks usan los accesores candidatos.
- Rehash final con `CroIntegrity` existente y reconstrucción CRR sin cambiar los demás CROs.
- Fuente verificada intacta mediante hashes de todos sus archivos RomFS/ExeFS y ExHeader.

Validaciones finales reales:
- Helper y runner compilados: 0 errores; 88 advertencias ya existentes en Core/WinForms/App.
  Se corrigió durante el desarrollo una referencia interna no accesible del helper; no quedan errores.
- 45 checks ARM del payload exacto asignado antes de exportar.
- Runner `--synthetic-only`: 3.750 comprobaciones, salida 0 en la ejecución final capturada.
  Una ejecución previa quedó sin resultado final verificable y NO se contabiliza como aprobada.
- Primer rebuild interrumpido por la herramienta; se repitió. El rebuild final terminó con salida 0.
- Reextracción final: 750 archivos RomFS/ExeFS idénticos al staging, salida 0.
- `evidence/verified-image.json` y `SHA256SUMS.txt` identifican la ROM entregada.
- `LEEME-PRUEBA.md` contiene las tiendas y los pasos manuales, incluyendo perfil separado:
  cambiar el nombre de ROM no separa el guardado porque mantiene el mismo Title ID.

**Pendiente:** compra interactiva, UI de agotado, guardado/carga/checksums en juego, independencia real
entre vendedores, eventos de historia y auditoría completa del espacio candidato. Solo esa prueba
permitirá valorar el comportamiento de runtime; las verificaciones anteriores no lo certifican.
Sin integración de producción, commit, push o release.


## Actualización de integración — 20 de octubre de 2026

El usuario confirmó que todas las pruebas jugables del prototipo entregado pasaron. El texto agotado mostraba Dinero. Se añadió integración general de todas las ofertas especiales e investigación/implementación de una ruta de mensaje propia. La integración nueva y su aviso aún requieren validación jugable; véase COMPRAS_UNICAS_TIENDAS_ESPECIALES.md. No confundir la validación del prototipo con la nueva versión.
