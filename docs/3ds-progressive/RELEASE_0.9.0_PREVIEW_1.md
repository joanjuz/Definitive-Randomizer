# Definitive Randomizer 0.9.0 Preview 1

Primera preview publicada desde `main`. Integra la línea WPF 3DS (previews 0.8.0) y el soporte de **Pokémon Espada y Escudo** en una sola aplicación. No sustituye la versión estable WinForms v4.17.1.

## Novedades

- **Espada y Escudo 1.3.2**: editores y randomización de Pokémon, movimientos, Level Up, evoluciones, MT/TR y tutores, entrenadores, encuentros, Field Items, tiendas, estáticos, regalos e intercambios, con historial, Global Template propio de Switch y Batch.
- En lugar de reconstruir una ROM, se exporta un **mod en formato Ryujinx** (RomFS/ExeFS). La extracción original, la SD, los mods instalados y las partidas no se modifican.
- Runtime integrado (PokeFeur) con variantes separadas para Espada y Escudo: encuentros, caps de nivel y opciones de EXP. Recursos fijados por SHA-256 y licencia GPL-3.0 conservada.
- **Probabilidad de combate doble (%)** en Entrenadores, reproducible por semilla e incluida en vista previa, historial, Global y Batch. Los combates iniciales obligatorios contra Paul se mantienen individuales.
- Caps y preset DivineSword adaptados a cada juego (Alistair/Mel en Escudo).
- Runner de pruebas: `--help`, `--list` y `-h` listan las opciones; una opción desconocida o con argumentos incorrectos termina con código 2 en lugar de ejecutar la regresión completa.
- Builds reproducibles: se eliminó la marca de tiempo de la versión informativa; el paquete registra el commit en `VERSION.txt`.
- Iconos de sección con el arte Pokémon facilitado.

## Descargar y usar

Extraer completamente `DefinitiveRandomizer-0.9.0-preview.1-win-x64.zip` y abrir **DefinitiveRandomizer.exe**. Windows x64, autocontenido: no requiere instalar .NET. Mantener juntas las DLL y las carpetas LegacyBatch, Tools, Templates, custom_balance_templates y catch_zone_patches.

Conservar los templates personales al actualizar: extraer en una carpeta nueva y copiar después los propios. No se incluyen recetas privadas, ROMs, partidas, extracciones, sprites ni datos locales. Las recetas Global de 3DS y de Switch son formatos distintos y no se convierten entre sí.

Espada/Escudo: abrir una extracción de la versión 1.3.2 (otras versiones y actualizaciones se rechazan), exportar el mod a una carpeta nueva y copiarlo después a Ryujinx. Conservar una copia de las partidas.

## Validación y límites

El propietario probó en juego con Escudo las opciones de esta versión antes de publicarla y Espada tiene confirmaciones jugables previas. No se declara una partida completa con todos los eventos, ni compatibilidad con consola/Atmosphère ni con DLC. El runtime integrado solo se exporta para Ryujinx.

- Animaciones de Pokédex retiradas temporalmente.
- No existe un guard runtime para combates dobles cuando el jugador solo tiene un Pokémon utilizable.

Antes de empaquetar, en Release: 4.059 comprobaciones sintéticas XY/ORAS/SM/USUM (`--synthetic-only`) y 4.457 en la ejecución completa con WPF; 316 de Espada/Escudo, 112 de interfaz Switch y 229 de paridad Switch; todas las suites del CI con los mismos conteos que la línea base. Las comprobaciones son sintéticas o renderizadas fuera de pantalla: no sustituyen la prueba en juego.

## Reportar problemas y reproducir

Indicar versión y commit de VERSION.txt, juego, versión, idioma, pasos, semilla y logs. No adjuntar ROMs ni recursos extraídos. Verificar la descarga con SHA256SUMS.txt.

Publicación reproducible con Windows, SDK .NET 10, Git y dependencias Mobipeg documentadas:

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.9.0-preview.1
```

Detalles de Switch: `HOJA_RUTA_SWITCH.md` y `SWITCH_ESCUDO_VALIDACION_Y_COMBATES_DOBLES.md`. Catálogo del runner: `RUNNER_PRUEBAS.md`.
