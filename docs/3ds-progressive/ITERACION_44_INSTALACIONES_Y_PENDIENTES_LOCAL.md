# Iteración 44 — instalaciones de combate e inventario pendiente

Fecha: 2026-10-13. Rama `feature/3ds-progressive-ui`. Trabajo exclusivamente local.

## Instalaciones incorporadas

Battle Maison y Super Maison en XY/ORAS; Battle Tree y Battle Royal en SM/USUM. Acceso desde Herramientas Pokémon con nombres según el juego. Editor WPF nativo para clases de entrenador, cantidad y referencias de opciones Pokémon, especies, naturaleza, objetos, cuatro movimientos y seis indicadores EV. Forma solamente en Gen7; el campo opaco equivalente Gen6 se conserva. Exportación a texto. Se oculta la randomización: el editor vanilla de estas instalaciones no incluye un randomizador.

Se preservan orden y referencias originales al guardar sin cambios, bits EV altos, bytes de cola, datos de registros no seleccionados y archivos acompañantes. Las referencias nuevas fuera del catálogo se rechazan; referencias desconocidas preexistentes pueden conservarse. Guardado mediante historial transaccional, hashes de ambos GARC y verificación existente. No se aplica rebuild CRO automático.

El catálogo de opciones Pokémon se comparte; abrir la instalación valida estructuras sin construir todos los controles de todos los entrenadores. El editor de un entrenador permite editar todas sus opciones mediante Cantidad y slots. No se añadió copia de tablas de juego a la distribución.

## Evidencia

- Debug: cero errores y 79 advertencias.
- 2128 regresiones sintéticas XY/ORAS/SM/USUM. Edición, resize, no-op, bytes opacos, forma por generación, exportación, rechazo de referencias inválidas y fuentes obsoletas. Navegación WPF normal y alternativa, exportación visible y randomizador oculto.
- Lectura de ambos perfiles en X, Y, OR, AS, Ultra Sol y Ultra Luna: hashes originales conservados; sin Save. Entre 1050 y 1216 entradas según el perfil. No hay referencias reales de Sol/Luna disponibles en esta comprobación.
- Archivos Wild protegidos y personales verificados por SHA-256. Sin commits, push, publicación o cambios en Eris-Ultranova.

Se corrigen estados obsoletos de la matriz para instalaciones, recuperación persistente, scripts de objetos de campo y Global Template. Implementación comprobada localmente no significa funcionamiento validado en juego.

## Inventario de trabajo que sigue abierto

| Bloque | Trabajo necesario |
|---|---|
| Global Template y Batch | Ejecutar Gen6; completar proyecciones de módulos y tres perfiles legacy de estáticos Gen6; lotes reales y configuraciones completas reproducibles. |
| Gen6 | Tutores efectivos en runtime, Sanity/Follow de máquinas/tutores, entrega S/MAX de O-Powers. El editor original TutorEditor6 reconoce que su edición no se refleja en juego. |
| Estáticos | Espejo de textos localizado y validaciones finales de iniciales, regalos e intercambios; perfiles separados Gen6. |
| Objetos de campo y Catch Zones | Catálogos y filtros personalizados completos; sincronización de templates externos de Catch Zones sin distribuir binarios ROM. |
| Herramientas vanilla | Restauración guiada de respaldos; SMDH/iconos/BCLIM; GARC Shuffler; pantalla de título Gen6; randomización protegida de diálogos. |
| UX y rendimiento | Procesos prolongados, cancelación, recuperación y revisión visual integral de módulos. Corte eléctrico/fallo físico no probado. |
| Extracción y rebuild | Validar formatos reales y variantes; integridad y arranque del resultado. Los fixtures y las comprobaciones de lectura no sustituyen esto. |
| Cierre | Compilación/paquete local final, documentación completa y pruebas en juego de cada versión compatible. No publicar sin autorización. |

La aplicación no se declara estable completa. Los archivos ITERACION_44_PRUEBAS conservan logs y evidencia de preservación; no se solicita aún la validación final del usuario.
