# Definitive Randomizer 0.10.0 Preview 1

Preview publicada desde `main` que añade **Nintendo DS (Gen4 y Gen5)** a la aplicación, junto a 3DS y Espada/Escudo. No sustituye la versión estable WinForms v4.17.1.

## Novedades

- **Randomizer DS** (Inicio → «Nintendo DS · randomizer Gen4/Gen5 (beta)»). Juegos y volcados admitidos:
  - Gen4: Diamante (USA, rev. 5), Platino (USA, rev. 1) y HeartGold (USA);
  - Gen5: Negro (USA/EUR), Blanco (España) y Blanco 2 (USA/EUR).

  Cada tabla se comprueba con su CRC32 antes de usarla. La ROM original no se modifica: se exporta una ROM nueva con un registro de cambios.
- **Opciones**, con semilla reproducible y vista previa antes de exportar:
  - entrenadores y Pokémon salvajes: aleatorio, BST similar o BST progresivo por nivel;
  - iniciales: aleatorios o personalizados, cambiados en la entrega, la pantalla de elección y los combates del rival que dependen de ellos;
  - objetos del mapa y tiendas: solo barajados, así que no aparecen objetos inválidos, y las MT, MO, objetos clave y tiendas especiales no se mueven.
- **Romhacks (beta)**: con «Admitir romhacks» se aceptan ROM modificadas de esos juegos si conservan la estructura esperada. Las opciones que tocan código del juego (overlays o ARM9) solo se activan si ese código sigue siendo el original; si no, la ventana las desactiva y explica por qué. Probado con Renegade Platinum, Sacred Gold, Volt White 2 y un hack de Blanco ES.
- Contenedor NDS: overlays y ARM9 (lectura y escritura en su sitio, con recompresión BLZ verificada), NARC y textos de Gen4 y Gen5.
- Mantenimiento: la compilación pasa de 63 advertencias a 0, y esas categorías de advertencia del compilador ahora fallan la compilación.

## Descargar y usar

Extraer completamente `DefinitiveRandomizer-0.10.0-preview.1-win-x64.zip` y abrir **DefinitiveRandomizer.exe**. Windows x64, autocontenido: no requiere instalar .NET. Mantener juntas las DLL y las carpetas LegacyBatch, Tools, Templates, custom_balance_templates y catch_zone_patches.

Conservar los templates personales al actualizar: extraer en una carpeta nueva y copiar después los propios. No se incluyen recetas privadas, ROMs, partidas, extracciones, sprites ni datos locales.

Nintendo DS: elegir un volcado `.nds` propio, revisar la vista previa y exportar a un archivo nuevo. Negro, Blanco y Blanco 2 son DSi-enhanced: la ROM exportada funciona en modo DS y en emuladores, no en modo DSi. El randomizer no cambia el formato de las partidas: para seguir una partida en DeSmuME basta con copiar su `.dsv` con el nombre de la ROM nueva. No usar estados rápidos de otra ROM.

## Validación y límites

El propietario probó en DeSmuME ROM randomizadas de todas las fases DS: Blanco ES, Blanco 2, Diamante, Platino y HeartGold con todas las opciones, y los romhacks Renegade Platinum y Volt White 2. Todas funcionaron y estaban bien randomizadas. No se declara una partida completa de ningún juego ni la prueba en consola real.

- Sin perfil: Perla, SoulSilver, Negro 2 y otras regiones o revisiones.
- Gen4: no se modifican Feebas, árboles de miel, Gran Pantano, Jardín Trofeo, árboles de Golpe Cabeza ni el Concurso de Bichos.
- Romhacks: solo de los juegos admitidos; en cada uno se desactivan las opciones cuyo código cambió.

Antes de empaquetar, en Release y con compilación limpia sin advertencias:
- todas las suites del CI con los conteos de la línea base: 4.059 sintéticas XY/ORAS/SM/USUM, 4.457 en la ejecución completa, 316/112/239 de Espada/Escudo, 85 sintéticas NDS y 9 de la ventana DS;
- pruebas con volcados reales: Gen5 16/14/20/20 y Blanco 2 21; Gen4 60; ventana DS 9 en cada juego; romhacks 24.

## Reportar problemas y reproducir

Indicar versión y commit de VERSION.txt, juego, versión, idioma, pasos, semilla y logs; para DS, adjuntar el `.cambios.txt`. No adjuntar ROMs ni recursos extraídos. Verificar la descarga con SHA256SUMS.txt.

Publicación reproducible con Windows, SDK .NET 10, Git y dependencias Mobipeg documentadas:

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.10.0-preview.1
```

Detalles de Nintendo DS: documentos `NDS_FASE*` del índice. Catálogo del runner: `RUNNER_PRUEBAS.md`.
