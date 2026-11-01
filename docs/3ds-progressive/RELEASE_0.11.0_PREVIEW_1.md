# Definitive Randomizer 0.11.0 Preview 1

Preview publicada desde `main`. Trae nuevas opciones para Sol/Luna y Ultra Sol/Ultra Luna, correcciones del recordador de movimientos de USUM y una revisión visual de todas las tablas. No sustituye la versión estable WinForms v4.17.1.

## Novedades Gen7

- **Los Pokémon intercambiados siempre obedecen** (SM y USUM). Mecánicas y CRO → «Obediencia y formas Mega · Gen7». Pensado para los level caps altos: los Pokémon de los intercambios del juego ya no desobedecen antes de la Liga.
- **Quitar formas Mega obtenibles** (SM y USUM). Mismo lugar. Las Megas, Primigenias y Ultra-Necrozma pasan a su forma base en salvajes, estáticos, regalos, intercambios y evoluciones. Los combates Totem y los entrenadores no cambian.
- **Pista de GAME FREAK en el Ultraviaje** (USUM). Story Text → «Pista de GAME FREAK en el Ultraviaje». Añade una página al diálogo de la Unidad Ultra, en español e inglés.
- **Lista blanca de la compra única** (USUM). Tiendas y BP → «Lista blanca de compra única…». Los objetos marcados se venden sin límite en las tiendas especiales aunque esté activo «Solo un objeto en tiendas especiales».

Las cuatro se guardan en el historial, pasan a Global Template y funcionan en Batch. Detalle en [GEN7_OBEDIENCIA_MEGAS_ULTRAVIAJE_LISTA_BLANCA.md](GEN7_OBEDIENCIA_MEGAS_ULTRAVIAJE_LISTA_BLANCA.md).

## Correcciones del recordador (USUM)

- **La última cafetería no abría el recordador.** El parche dejaba fuera un caso; ahora cubre las 15 cafeterías.
- **Escritura en memoria de código.** El parche guardaba un dato en una zona de solo lectura. Los emuladores no lo notan, pero en una consola real fallaría. Ahora usa una zona de lectura y escritura.
- **ROM ya parcheadas:** al volver a aplicar el recordador se corrigen los dos puntos, cambiando solo esos bytes.
- La vista USUM Relearner funciona también con la copia de trabajo, donde el ejecutable se llama `.code.bin`.

Análisis completo en [ANALISIS_CRASH_PRUEBAS_PETALOS.md](ANALISIS_CRASH_PRUEBAS_PETALOS.md).

## Revisión visual de las tablas

- Texto centrado en vertical en todas las celdas.
- Al editar una celda, el editor usa el tema oscuro: el valor ya no se ve en negro. La fila seleccionada también usa los colores del tema.
- Encuentros estáticos divididos en secciones con un selector: Regalos, Estáticos e Intercambios en Gen7; Iniciales, Regalos y Estáticos en Gen6.
- Level Up y MT/tutores: una sola tabla vertical, con las opciones en tarjetas a la derecha.
- Los botones del Inspector y la lista de registros parten el texto en dos líneas en vez de cortarlo.

Detalle en [TABLAS_CENTRADO_EDICION_SECCIONES.md](TABLAS_CENTRADO_EDICION_SECCIONES.md).

## Descargar y usar

Extraer completamente `DefinitiveRandomizer-0.11.0-preview.1-win-x64.zip` y abrir **DefinitiveRandomizer.exe**. Windows x64, autocontenido: no requiere instalar .NET. Mantener juntas las DLL y las carpetas LegacyBatch, Tools, Templates, custom_balance_templates y catch_zone_patches.

Conservar los templates personales al actualizar: extraer en una carpeta nueva y copiar después los propios. No se incluyen recetas privadas, ROMs, partidas, extracciones, sprites ni datos locales.

## Qué probar en el juego

- Un Pokémon de intercambio de nivel alto obedece antes de la Liga.
- No aparecen Megas en salvajes, regalos ni intercambios.
- La página nueva se ve bien en el diálogo de la Unidad Ultra.
- Un objeto de la lista blanca se puede comprar varias veces; uno que no está en ella, una sola.
- La última cafetería abre el recordador.
- Las tablas: editar valores, cambiar de sección en los estáticos, las opciones de Level Up y MT/tutores.

## Validación y límites

Antes de empaquetar, en Release y con compilación sin advertencias:
- todas las suites del CI;
- 4.081 comprobaciones sintéticas XY/ORAS/SM/USUM y 408 de interfaz;
- `--gen7-adjust-only` (22) y `--single-purchase-only` (6);
- la nueva auditoría visual `--visual-audit`, sin problemas.

Durante el desarrollo pasaron además las pruebas con copias reales:
- `--gen7-adjust-real` (42);
- `--relearner-real` (11);
- `--single-purchase-copies` (173).

Límites conocidos:
- **Cuelgue en la entrega de los pétalos (USUM).** Un tester lo vio en Citra, en la entrega del pétalo de Kiawe. La causa sigue sin determinar: ningún cambio de la ROM actúa de forma demostrable en esa escena. Si vuelve a pasar, conservar una partida de antes de la escena.
- Las opciones nuevas de Gen7 todavía no tienen confirmación en el juego.
- No se declara partida completa ni prueba en consola real.

## Reportar problemas y reproducir

Indicar versión y commit de VERSION.txt, juego, versión, idioma, pasos, semilla y logs. No adjuntar ROMs ni recursos extraídos. Verificar la descarga con SHA256SUMS.txt.

Publicación reproducible con Windows, SDK .NET 10, Git y dependencias Mobipeg documentadas:

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.11.0-preview.1
```

Catálogo del runner: `RUNNER_PRUEBAS.md`.
