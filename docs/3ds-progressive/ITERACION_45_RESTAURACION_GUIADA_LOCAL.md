# Iteración 45 — restauración guiada de respaldos

Fecha: 2026-10-13. Rama `feature/3ds-progressive-ui`.

Se añade «Restaurar backup…» a la cabecera, disponible antes de abrir una ROM. Acepta el ZIP con manifiesto creado por ProjectBackup. Primero verifica todas las entradas, tamaños y SHA-256 y presenta juego, número de archivos y tamaño para confirmar el resultado. El usuario elige una carpeta padre; la restauración genera una carpeta nueva con nombre fechado. No sustituye el proyecto abierto ni sobrescribe destinos existentes.

Se rechazan rutas fuera del proyecto, segmentos ascendentes, nombres reservados Windows, streams alternativos, entradas inesperadas o repetidas, enlaces, colisiones archivo/carpeta, tamaños incoherentes y hashes corruptos. El ZIP se mantiene abierto con bloqueo de escritura durante la operación y se comprueba de nuevo su fingerprint respecto a la vista previa. La salida se prepara en una carpeta temporal hermana, verifica los hashes escritos y se publica mediante un movimiento de carpeta. Error o cancelación antes de publicar limpia el staging. El botón actual no ofrece cancelación interactiva; la API admite CancellationToken. Un cierre de proceso durante la restauración puede dejar staging, sin publicar el destino; no hay recuperación automática de ese staging todavía.

El archivo adicional progressive-history.json se conserva como .definitive-restored-history.json para consulta. No se importa como historial activo, porque puede contener rutas del proyecto anterior. No es una restauración selectiva de archivos sobre una ROM ya modificada ni restauración de directorios bak del pk3DS antiguo: esa equivalencia sigue pendiente.

## Validación local

Compilación Debug y suite de regresiones local con tests de ida y vuelta usando ProjectBackup real, preservación de fuentes, rechazo de destino existente, respaldo cambiado, hash corrupto, entradas fuera de inventario y rutas no válidas. Evidencia de compilación, regresiones y preservación Wild en ITERACION_45_PRUEBAS.

Sin pruebas sobre respaldos personales ni escrituras en ROM originales. Permanecen pendientes Batch Gen6, módulos vanilla restantes y cierre con rebuild, empaquetado y pruebas en juego. Sin commits, push ni publicación.
