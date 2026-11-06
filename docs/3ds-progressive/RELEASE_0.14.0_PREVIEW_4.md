# Definitive Randomizer 0.14.0 Preview 4

Candidato Windows x64 **prerelease**, con H2/H3, Deshacer y DGD v1 integrados en `main` (base funcional `f802b4a555f423d27f55e700653ca50f35fe01e8`). La preparación añade únicamente la versión, el pie de la aplicación y documentación. No es una versión estable ni una publicación autorizada todavía.

## Novedades desde Preview 3

- **Seguridad de guardado H2/H3:** Commit, rollback y recuperación retienen los archivos por handle, verifican sus bytes y evitan sobrescribir ediciones externas durante el reemplazo. Si aparece un conflicto de nombre entre los renombrados, conservan la edición externa y el estado necesario para recuperar. Los guardados individuales también conservan un journal para recuperación al reabrir.
- **Historial y Deshacer:** el botón se actualiza al terminar la verificación y liberarse el bloqueo de interacción. Mantiene las comprobaciones de integridad y no permite deshacer durante una operación activa.
- **Definitive Game Data v1:** en Ultra Sol y Ultra Luna, «Exportar datos para Progressive Tracker» genera un JSON desde los archivos realmente guardados. Incluye especies/formas, estadísticas base, tipos, habilidades, propiedades básicas de movimientos y evoluciones. No exporta opciones pendientes ni reconstruye el resultado desde el historial.
- El JSON se puede previsualizar e importar por partida en **Progressive Tracker 1.3.0**, reemplazar o retirar. La naturaleza y las estadísticas vivas del Pokémon siguen procediendo de RAM. DGD no modifica la ROM, la partida ni la memoria del emulador.
- Se conservan las correcciones H1 distribuidas en Preview 3: los cambios deshechos no vuelven a exportarse como vigentes.

## Instalación y actualización

1. Extrae el ZIP completo en una carpeta nueva de Windows x64. No lo ejecutes desde el ZIP ni sobrescribas una instalación anterior.
2. Ejecuta `DefinitiveRandomizer.exe`. El paquete es autocontenido y no requiere instalar .NET.
3. Conserva juntas `LegacyBatch`, `Tools`, `Templates`, `custom_balance_templates`, licencias y las demás dependencias.
4. Respalda proyectos, templates personales y partidas antes de trabajar. No uses ROMs originales como carpetas de edición.
5. Para DGD, guarda primero tus cambios, exporta desde Herramientas ROM o Ctrl+K y selecciona la partida correcta en Tracker. El JSON no contiene gráficos ni binarios extraídos.

Los hashes de archivos y del ZIP se entregan en `SHA256SUMS.txt`. `VERSION.txt` identifica la versión y el commit definitivo usado para compilar; el informe de procedencia adjunto registra la integración de los metadatos. No se ha creado el tag definitivo ni se ha publicado una release.

## Validación existente y del candidato

- Código funcional aprobado en el CI de PR #47: compilación, suites de integración y regresión, auditoría visual y artefactos Windows. La ejecución local de ese bloque registró 37 checks DGD y 4546 regresiones generales aprobadas.
- El propietario confirmó manualmente la integración DGD entre ambas aplicaciones antes de preparar estos candidatos.
- El informe de distribución adjunto al ZIP recoge integridad, versiones, dependencias y arranque desde una extracción limpia. Esa validación de distribución no certifica todos los juegos, consolas, eventos ni combinaciones de opciones.

## Limitaciones conocidas

- DGD v1 admite exclusivamente Ultra Sol/Ultra Luna. Una extracción no acredita la actualización instalada: se declara revisión desconocida y Tracker pide confirmación. El fingerprint identifica los datos exportados, no certifica qué ROM ejecuta el emulador.
- DGD no incluye objetos, gráficos, efectos avanzados de movimientos, parches de combate ni datos de memoria nuevos para DS/Switch. Los campos no integrados conservan referencias del Tracker.
- La protección transaccional conserva sus límites: no garantiza atomicidad para observadores, cortes de alimentación ni todos los sistemas de archivos/almacenamientos. La evidencia principal corresponde a Windows/NTFS; no impide una edición externa posterior a liberar los handles.
- Sigue sin determinarse la causa del cuelgue USUM en la entrega de pétalos informado sobre una preview anterior, pendiente de reproducción. El movimiento Z del primer combate de Tilo conserva su diagnóstico localizado.
- Los dobles Switch tienen protecciones de combates tempranos y umbral del primer gimnasio, pero no un guard runtime general para un único Pokémon utilizable del jugador. El runtime integrado mantiene el alcance Ryujinx; no se declara soporte completo de DLC, Atmosphère o hardware real.
- Los perfiles DS y sus límites de búfer/código original siguen siendo los documentados. No se amplía el soporte de juegos/revisiones en este candidato.
- **No incluye mentas Gen7.** Su plan técnico todavía no forma parte del código de `main`.

Para reportar problemas, incluye versión y commit, juego/revisión/idioma, pasos, semilla y registros pertinentes. No adjuntes ROMs, archivos extraídos ni partidas personales.
