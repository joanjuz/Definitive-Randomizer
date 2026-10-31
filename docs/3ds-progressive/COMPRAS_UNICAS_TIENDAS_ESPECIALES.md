# Una unidad por objeto en tiendas especiales (USUM)

## Uso

En Tiendas y BP, pulsar **Solo un objeto en tiendas especiales**, revisar la vista previa y aplicar. Es acumulable con los otros botones. También está disponible en el randomizado guiado de USUM.

Cada ID de objeto puede comprarse una vez en cada tienda especial con dinero (índices 8–27). Otra tienda mantiene su propio stock; filas duplicadas del mismo objeto comparten el límite. Las tiendas por progreso 0–7, tiendas BP y otros modos nativos quedan fuera. Cancelar, no tener dinero, tener la bolsa llena o fallar la entrega no consume la compra. La cantidad se limita a una unidad.

La política se registra en el historial como operación reproducible; puede exportarse a Global Template. La acción `marts.single-purchase-specials` funciona sola o junto con randomización. Global/Batch refresca el registro después de aplicar todas las modificaciones manuales, sobre el inventario final. Los templates anteriores con ocho opciones de tiendas siguen siendo válidos.

El registro usa tienda + ID, conserva sus posiciones al reordenar o editar filas y no guarda nombres dependientes del idioma. Retirar un objeto no libera su bit para otro objeto. Quitar la selección pendiente no desinstala una política ya guardada en una ROM.

## Lista blanca

Desde el 31/10/2026, «Lista blanca de compra única…» deja objetos sin límite aunque la política esté activa. Sus filas siguen en el registro, con su bit de la partida, pero marcadas para que la tienda no las encuentre; no cambia el código ARM. Detalles en [GEN7_OBEDIENCIA_MEGAS_ULTRAVIAJE_LISTA_BLANCA.md](GEN7_OBEDIENCIA_MEGAS_ULTRAVIAJE_LISTA_BLANCA.md).

## Mensaje

Se añade una entrada al final del banco 95 de Game Text en los diez idiomas; en español: **Ya compraste este objeto.** No se sustituyen los mensajes originales de dinero, bolsa ni las líneas existentes. El nuevo estado de agotado tiene una ruta propia. Shop.cro, textos y CRR se guardan con el historial mediante la transacción existente, comprobando dependencias antes de escribir.

## Límites y compatibilidad

Implementación exclusiva de ejecutables Shop.cro USUM reconocidos y validados. Un ejecutable desconocido o alterado se rechaza. No amplía soporte a SM/Gen6 ni instala Python en tiempo de ejecución.

Se reutiliza la región candidata de EventWork D00–DFF validada con el prototipo jugable. La cabecera integrada DRS2 protege una identidad diferente y bytes ajenos: en caso incompatible se bloquea la compra sin sobrescribirlos. Migra únicamente el prototipo DRS1/STOC conocido: tienda 10/objeto 101, tienda 11/objeto 101 y tienda 10/objeto 102. Solo acepta sus tres bits y el resto reservado vacío. La consulta no escribe; la primera compra nueva completada traduce los bits a las identidades integradas. Esas claves se conservan aunque ya no figuren en el inventario. Otros encabezados/identidades siguen rechazándose sin sobrescribir datos. Para validar, conservar una copia del guardado; cambiar solo el nombre de la ROM no separa su partida.

Capacidad: 1.984 identidades históricas tienda/objeto por ROM; al excederla se rechaza la operación completa. No se omiten objetos. Los IDs retirados permanecen reservados. La auditoría exhaustiva de todos los usos futuros de la región de guardado sigue pendiente.

## Validación

El usuario confirmó todas las pruebas jugables del prototipo anterior. Esta integración general y el mensaje nuevo requieren una nueva comprobación en juego; las pruebas ARM y WPF fuera de pantalla no certifican una sesión interactiva ni un guardado real.

Pruebas automatizadas: `SinglePurchaseChecks` cubre esquema, compatibilidad previa, todas las ofertas actuales, asignación estable, capacidad, dependencias obsoletas, textos, CRR, historial, replay, Global con backend original y combinación con cambios manuales. `verify_integrated_arm.py` ejecuta instrucciones ARM con sustitutos de inventario/UI y memoria de guardado simulada. Los registros finales y resultados reales se enumeran en el informe entregado al usuario.

No se modifica la aplicación instalada, la ROM fuente ni las partidas del usuario. No se hace commit, push, release ni publicación automáticamente.

## Resultados ejecutados (20 de octubre de 2026)

- Compilación explícita de Tests (incluye App/Core/backend): 0 errores, 88 advertencias en código existente.
- Compilación de pk3DS.slnx (Core y WinForms): 0 errores, 56 advertencias en código existente.
- Runner sintético: 3.762 comprobaciones aprobadas.
- Opciones Global: 132; aplicación Global: 46; tiendas/rebuild UI: 69; guiado: 233; rediseño visual: 95; bloqueo de interacción: 19. Todas aprobadas; UI fuera de pantalla.
- Integración sobre copias: 173 aprobadas, incluidos esquema, 10 idiomas, CRR, historial y preservación de originales.
- Backend original Global: 4 aprobadas, incluidas receta de política sola, inventario manual después del randomizado e historial editable combinado.
- ARM: 460 aprobadas sobre 135 ofertas en 20 tiendas. Importaciones/UI y guardado sustituidos/simulados, no prueba interactiva.
- Total: 4.993 comprobaciones aprobadas. git diff --check sin errores.

Se detectaron y corrigieron durante la integración dos regresiones: lectura de la opción exclusiva USUM en el asistente SM y requisito indebido de Shop.cro en proyectos mínimos que no aplicaban la política. Se repitieron satisfactoriamente las suites afectadas. También se corrigieron los metadatos de un fixture y su ubicación de historial aislado, sin relajar contratos de producción.

Evidencia local: D:/Progra/Pokemon/Randomizer/ValidacionComprasUnicas20261020-final/integrated-arm-results.json y C:/Users/Joanj/AppData/Local/Temp/DR-stock-integration-state/*-verified.log. Los binlogs quedan en ese directorio temporal. No incluyen una afirmación de gameplay real de la integración nueva.

## Corrección tras prueba jugable: guardado del prototipo y cierre del aviso

El usuario informó de compra rechazada antes de comprar y diálogo sin salida. En lectura se confirmó DRS1/STOC con máscara 7 y cola vacía en el guardado usado. La integración anterior lo rechazaba como si ya estuviera comprado. Se implementó la migración acotada descrita arriba; no se modificó el archivo de guardado.

La otra regresión era independiente: la rutina nativa de mensajes compara el ID del contexto contra IDs conocidos para decidir cómo cerrar. El mensaje nuevo 167 se mostraba, pero ninguna rama lo reconocía al terminar. Tras cargar el texto personalizado, el parche ahora usa en el contexto la clasificación de un aviso nativo de bolsa llena, conservando el texto nuevo en pantalla. Se reutilizan la confirmación y el retorno originales a la lista; no se sustituye ningún mensaje de dinero/bolsa.

Pruebas nuevas: la rutina nativa 2B08 avanza tres fases con señal de finalización de UI controlada y vuelve al estado de lista 1/fase 0. El mismo test falla con el parche anterior. Se prueban las ocho máscaras del prototipo, cada compra independiente, migración al entregar, memoria ajena intacta, rechazos de datos desconocidos y bytes leídos del guardado real con Restos (tienda 10/objeto 234). No es una sesión interactiva del emulador: esa confirmación sigue pendiente.

La ROM Tiendas.3ds existente no se actualiza al compilar la aplicación: volver a previsualizar/aplicar la política en el proyecto y generar otra ROM con Rebuild. Se preservan la ROM y el guardado del usuario. No se requiere comenzar una partida nueva para el prototipo DRS1 reconocido.

Resultado de esta corrección: compilación explícita de Tests/App/Core/backend, 0 errores y 88 advertencias existentes; 3.762 regresiones sintéticas, 173 comprobaciones con copias, 4 del backend original y 551 ARM aprobadas (4.490 en total). La prueba de retorno al listado falla con el payload anterior y pasa con el corregido. `git diff --check` sin errores. Evidencia: ValidacionComprasUnicas20261020-runtime-fix/integrated-arm-results.json y logs runtime-*.log en DR-stock-integration-state.

## Validación jugable confirmada

Después de aplicar y reconstruir con la corrección, el usuario confirmó que todo funcionó correctamente: objetos nuevos comprables, conservación de las compras del prototipo y retorno al vendedor al cerrar el aviso. Esta confirmación corresponde a su prueba en UltraSol; no constituye una auditoría exhaustiva de todas las escenas ni de UltraLuna.
