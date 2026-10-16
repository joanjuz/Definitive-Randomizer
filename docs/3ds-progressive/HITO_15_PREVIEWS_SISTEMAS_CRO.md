# Hito 15 — Level Up: vista previa real + Sistemas activos

Rama: `feature/3ds-progressive-ui`. Sin merge a estable.

## Fallos visuales y funcionamiento corregidos

1. **Level Up Moves**: columna «Nivel» pasa a un ancho explícito mínimo de 90 px con encabezado completo en las tres tablas, sin la franja nativa de encabezado de fila.
2. El footer se reemplazó por un `Grid` de dos columnas: mensaje flexible con `TextWrapping` y botón Guardar separado, sin solapamiento.
3. **La vista previa de Randomizar todos / Metrónomo todos se ve dentro de las tablas de Level Up** y al cambiar de Pokémon muestra su learnset propuesto. Se construye a partir de los bytes exactos del GARC congelado que se aplicaría; nunca se vuelve a randomizar al navegar.
4. Al mostrar una vista previa global, los comandos de edición individual y el guardado quedan desactivados. Un aviso identificable dice que los valores mostrados **no han sido escritos**; hay un botón «Descartar vista previa» que restaura los movimientos actuales del juego.
5. Al modificar las opciones después de generar preview, se invalida el resultado y se restauran los valores originales. Aplicar pide confirmación, compara fingerprint original y escribe con historial.

## Nuevo menú Sistemas

### Mecánicas y CRO

- **Player Level Caps USUM** completo en una nueva vista WPF nativa con los **21 checkpoints investigados** (Event Flag / Event Work), offsets, máscaras y caps.
- Presets Standard, Strict/Relaxed por `LevelCapPresets`, cap final, lectura de la tabla instalada cuando exista, restablecer valores investigados, import/export JSON compatible por claves.
- Sync Trainer Caps mediante IDs permitidos respaldados por checkpoints de partidas investigadas; el resto de checkpoints conserva su progreso, con monotonía de niveles.
- Vista previa del instalador `LevelCapPatch.InstallManaged` original. Si faltan archivos o no coinciden firmas CRO0/code.bin o hooks se rechaza; si ambas instalaciones son compatibles se guardan **Battle.cro y code.bin en una transacción conjunta con historial**.
- Aviso de datos pendientes al abandonar la pantalla de edición.
- En el mismo hub se enlazan las opciones reales de Totem, Static Encounters e Items.

### Herramientas ROM

- Inventario exportable en JSON con ruta relativa, tamaño y SHA-256 de archivos RomFS/ExeFS mediante proceso en segundo plano, sin escribir la ROM.
- Backup completo: reutiliza el motor `ProjectBackup` anterior y el historial verificado.
- Accesos a exportaciones concretas de MT, Level Up Moves e Historia y textos.

### Automatización

- Accesos a los **motores que ya están integrados**: randomización personal masiva, entrenadores, encuentros salvajes, estáticos y Level Up todos; historial de resultados.
- **Todavía no existe un Randomize All global integrado**; el hub lo deja explícito y no simula una ejecución de motores faltantes.

### Configuración

- Carpetas predeterminadas de apertura y backup, guardadas **localmente** en AppData, sin cuentas externas.
- Información del proyecto y copia de su ruta al portapapeles. El modo visual oscuro permanece coherente.

## Validación

Regresiones y smoke Windows con datos sintéticos XY, ORAS, SM y USUM: la vista previa global incluye exactamente los bytes GARC propuestos sin escribir; descartar restaura la lista; navegación de Sistemas y Player Caps es real; Player Caps conserva 21 checkpoints y rechaza archivos faltantes. La validez final se establece por el workflow Windows de esta rama.

## Pendientes intencionales

- **Prueba en runtime real de Player Level Caps en Ultra Sun y Ultra Moon**, incluidos Battle.cro, Rare Candy y progresión de flags.
- Comparar Sync Trainer Caps con templates particulares del usuario antes de aplicación en partidas reales.
- Portar mecánicas CRO restantes, tiendas BP avanzadas, egg moves, shiny rate y tabla de tipos.
- Automatización global de plantillas múltiples con orden de ejecución, dependencias, previsualización agregada y rollback.
- Permitir configuración visual avanzada (tipografías/escalas) y personalización de reglas de compatibilidad solo tras diseñar sus controles reales.

**No hacer merge sin revisión manual de preview Level Up y validación del parche Player Caps en ROM extraída real.**
