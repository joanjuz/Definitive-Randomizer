# Hito 17 — Estética, SHA opcional, USUM Relearner y CRO Recipes

Rama `feature/3ds-progressive-ui` · Sin merge a estable.

## 1. Global Template

- Se elimina la cabecera blanca mediante estilo explícito de `DataGridColumnHeader` con fondo `Surface`, texto `TextMain`, separadores `Stroke` y altura 36.
- `RowHeaderWidth=0` permanece (sin franja blanca lateral).
- La serialización global `Version=1` / `ActionCoverageVersion=8` de `DEFINITIVO.json` sigue intacta.

## 2. Advertencias SHA-256 del Batch ROM Builder

Preflight distingue:

- **SHA distinto**: se muestra advertencia con los nombres de los assets y una casilla sin marcar. Construir ROMs queda deshabilitado hasta marcar explícitamente autorización para emplear los archivos actuales.
- **Archivo ausente, generación incorrecta, acción desconocida, destino inseguro o colisión**: **bloqueo no anulable**. La autorización SHA nunca desactiva estas verificaciones.
- El consentimiento se propaga como `AllowAssetMismatch` al motor original Gen7 en el proceso `LegacyBatch`; el motor original vuelve a validar los archivos. Si falta una dependencia, se detiene.
- Cambiar parámetros invalida la autorización anterior. El indicador no implica que los archivos coincidan; sirve para continuar con una versión distinta bajo decisión consciente del usuario.

**Precaución:** aceptar distintos SHA no garantiza que la receta produzca los mismos resultados que el template original; puede variar estadísticas/movimientos/evoluciones.

## 3. CRO Recipes — implementación WPF nativa

- Entrar en **Sistemas → Mecánicas y CRO → CRO Recipes** (solo USUM).
- Importar varios JSON originales y combinar en orden estable.
- Usar `pk3DS.Core.Modding.Research.CroMechanicRecipeApplication.TryApplyJson`: analiza versiones, dependencias, offsets y relocation tables originales sin escribir todavía.
- Vista previa: documentación/ID de recetas, entradas, relocaciones, tamaño antes y después y cambios por confirmar.
- Aplicar tras confirmación: `ProjectHistory.Commit` envuelve **Battle.cro e historial en una transacción** con protección de SHA; nunca se escribe parcialmente una receta.
- Se avisa de vistas previas sin aplicar al salir. Falta validar en runtime con un `Battle.cro` auténtico y recetas reales compatibles.

## 4. USUM Relearner — implementación WPF nativa

- Entrar en **Sistemas → Mecánicas y CRO → USUM Relearner**.
- Reutiliza el `USUMMoveRelearnerPatcher` original en `LegacyBatch/pk3DS.WinForms.exe --relearner-preview`. No sustituye parches ARM ni valores específicos de Ultra Sun/Ultra Moon.
- El proceso primero copia RomFS/ExeFS/ExHeader a un staging temporal fuera de la ROM. Allí valida las firmas y las 10 traducciones del tutorial de Lylia, calcula parches CAFE.asm/Learn.asm y prepara los binarios propuestos.
- Vuelve únicamente con las diferencias verificadas de **code.bin y StoryText**; la copia temporal se limpia sin escribir el proyecto original.
- Vista previa con los SHA antes y después, informe del motor y cuenta de archivos. Tras confirmación: transacción `ProjectHistory.Commit` de ambos archivos más historial, con verificación de fingerprints.
- Una ROM con código comprimido, texto alterado o firmware/layout no reconocido debe fallar sin tocar la fuente. Si ya está parcheada no propone un segundo guardado.

## 5. Pruebas

CI Windows construye editor WPF y motor original Gen7, ejecuta regresiones de juegos XY/ORAS/SM/USUM y smoke WPF de navegación, cabeceras oscuras y nuevas secciones.

**Pendiente imprescindible:** probar USUM Relearner en extracción real Ultra Sun y Ultra Moon, verificando ambos patrones ARM y localización; y ejecutar recetas CRO contra `Battle.cro` real antes de integrar en estable. También probar un batch de 1 ROM con assets SHA distintos autorizados conscientemente.

No se ha realizado merge a `master` ni a ramas estables.
