# Iteración 48: selector de intercambios de Zafiro Alfa

Causa: el perfil ORAS usaba posiciones de Rubí Omega para ambos juegos. En la copia de Zafiro Alfa proporcionada, la instrucción A está en 0x3196C0 y la B en 0x319F30, ocho bytes antes de 0x3196C8/0x319F38. Las solicitudes/ofertas conservan sus posiciones.

Comprobación estática: A conserva el prólogo ARM y las instrucciones que guardan los argumentos en r4/r7/r6; B conserva las escrituras previas, la carga literal y la instrucción posterior. Las cuatro llamadas cercanas ajustan su desplazamiento relativo y conservan el destino 0x285D8 en ambos binarios. Evidencia: iteration48-branches.json.

Implementación: Gen6TradeSelectorProfile en Core identifica un único par de posiciones conocidas mediante instrucciones anteriores/posteriores y opcode original o ya parcheado. Editor WPF y ejecutor original/Batch comparten el resolvedor. Se conserva la comprobación de solicitudes, transacción del editor y bloqueo de instrucciones desconocidas. No se intenta buscar/reemplazar una instrucción aislada ni aceptar arbitrariamente cualquier código.

Pruebas: fixtures OR/AS, idempotencia, rechazo de contexto alterado y pareja mezclada. En copias privadas del code.bin real de X/Y/OR/AS se comprueba preview sin escritura, parche limitado exactamente a solicitudes/selectores esperados, igualdad entre editor y motor Batch y originales intactos. Los juegos originales se leen; el parche se aplica exclusivamente a fixtures temporales. No hay binarios de juegos en la documentación ni distribución.

Esto resuelve el bloqueo de compatibilidad estática de la copia de Zafiro Alfa. El comportamiento del selector y los intercambios en una partida continúa pendiente de validación in-game; no equivale a declarar completa la aplicación. Sin commits/push/release ni cambios a Eris-Ultranova.

Resultado final: Debug 0 errores y 79 advertencias; 2182 comprobaciones completadas; 12 archivos protegidos preservados. git diff --check sin errores (avisos informativos LF/CRLF).
