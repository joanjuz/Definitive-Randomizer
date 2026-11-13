# Pulido de interfaz para la estable 1.0.0

Correcciones de interfaz WPF previas a la primera versión estable. No cambian motores de randomización, formatos binarios, contratos de Global Template, Batch ni historial. Se comprueban con `--stable-polish-only` (35 comprobaciones fuera de pantalla, en CI) y con la auditoría visual existente.

## 1. MT y tutores (Ultra Sol/Ultra Luna): acciones inmediatas frente a opciones de receta

Comprobado en el código antes de cambiar nada: no había un manejador duplicado. Los cuatro controles hacían cosas distintas, pero se presentaban juntos y con nombres parecidos.

| Control (nombre nuevo) | Antes | Qué hace |
|---|---|---|
| **Randomizar movimientos de tutores** | «Randomizar tutores» | Prepara en el editor una nueva asignación de los 67 movimientos. No cambia precios ni escribe archivos. |
| **Poner todos los precios en 0 BP** | «Todos gratis (0 BP)» | Pone a 0 los precios del editor. No cambia movimientos ni escribe archivos. |
| **Vista previa tutores** | igual | Calcula las diferencias reales de `Shop.cro` y de la tabla nativa de `code.bin`. |
| **Aplicar tutores y verificar** | igual | Se habilita solo con una vista previa con cambios; guarda `Shop.cro` y `code.bin` juntos, con historial y Deshacer. |
| **Randomizar tutores al aplicar una receta** | «Randomizar tutores en Global/Batch» | Casilla: se guarda en la receta y se ejecuta al aplicar Global Template o Batch. No modifica el proyecto ahora. |
| **Hacer gratuitos los tutores al aplicar una receta** | «Tutores gratis en Global/Batch» | Igual, para los precios. |

Los cuatro primeros están en la tarjeta **«Acciones para el proyecto actual»**; las dos casillas, en **«Opciones para Global Template / Batch»**, que solo aparece en Ultra Sol/Ultra Luna. La tarjeta de compatibilidad (Sanity/Follow) no cambia. La barra de estado indica tras cada acción qué se cambió y qué no.

Corrección menor: la vista previa actualiza el estado de los botones por el mismo camino que el resto del editor (antes habilitaba «Aplicar» directamente).

## 2. Modo de trabajo recordado

La elección explícita en Inicio (o «Cambiar a modo …» de Ctrl+K) se guarda en `%LOCALAPPDATA%\3DS-Progressive\work-mode.json` (versión 1; nunca en el proyecto, la ROM ni una receta). Al abrir un proyecto 3DS con una preferencia válida se entra directamente en ese modo; sin preferencia o con un archivo ilegible se muestra la elección como antes. El cambio temporal a modo completo que hace la vista guiada no se recuerda. «Modo … · Cambiar» (barra lateral) sigue llevando a Inicio. DS y Switch abren sus propios espacios de trabajo y no usan esta elección.

## 3. Texto negro sobre fondo oscuro

Causa: la aplicación no definía estilo implícito para `Label`, `RadioButton` ni `GroupBox`, cuyos estilos de Windows usan el color de texto del sistema (negro). Afectaba, por ejemplo, a «Ruta del archivo o carpeta» y «Proyectos recientes» del diálogo **Abrir proyecto** y a etiquetas de las ventanas de Espada/Escudo. Se añadieron estilos globales en `App.xaml` con los recursos del tema (`TextSoft` para etiquetas, `TextMain` para el resto), en lugar de corregir textos uno a uno.

La auditoría visual existente no cubría Inicio (con Inicio visible inspeccionaba la sección de debajo) ni el diálogo de apertura: `--stable-polish-only` mide el contraste WCAG AA de Inicio vacío y con proyecto, de la ventana alrededor y del diálogo.

## 4. Inicio con una sola apertura

Se retiran de Inicio «Espada/Escudo · editor Switch» y «Nintendo DS · Gen4/Gen5 (beta)»: «Abrir proyecto…» ya detecta la plataforma. Los espacios de trabajo DS y Switch siguen disponibles en la búsqueda (Ctrl+K). Recientes y arrastrar y soltar siguen usando el mismo flujo universal, sin cambios.

**Ruta recibida de Windows (nuevo).** Hasta ahora el ejecutable ignoraba los argumentos. Si recibe exactamente una ruta existente («Abrir con…», un archivo o carpeta soltado sobre `DefinitiveRandomizer.exe`, un acceso directo con destino), al mostrarse la ventana la abre con el mismo flujo universal que el diálogo y el arrastre: mismo detector, mismas confirmaciones y mismo registro en recientes. Sin argumento, con una ruta inexistente o con varias rutas, la aplicación arranca como siempre. No se registran asociaciones de archivos en Windows.

## 5. Deshacer permanece en Historial

Tras «Deshacer» (Historial o Ctrl+K) se vuelve a leer el proyecto desde disco como antes —no se conserva ningún editor ni estado de receta—, pero se reabre **Historial** en lugar de Inicio. La confirmación se mantiene en la barra de estado aunque Historial inicie su comprobación de archivos. La restauración de archivos seleccionados reabre su propia herramienta. La transacción de Deshacer y sus verificaciones no cambian.

## Pruebas

- `--stable-polish-only`: 35 comprobaciones (tutores con una tabla sintética de 67 ranuras, preferencia de modo con carpeta de estado aislada y reinicio simulado, Inicio, contraste, ruta de inicio, Deshacer). Fuera de pantalla; no es una prueba interactiva ni en juego.
- Suites del CI y auditoría visual: ver el registro de la entrega.
