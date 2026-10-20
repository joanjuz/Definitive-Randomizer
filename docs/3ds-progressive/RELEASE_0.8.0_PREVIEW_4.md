# Definitive Randomizer 0.8.0 Preview 4

Prerelease para testers desde `feature/3ds-progressive-ui`; no integra la rama main ni sustituye la versión estable WinForms.

## Novedades

- Nuevo botón acumulable **Solo un objeto en tiendas especiales** en Tiendas y BP de USUM y en el randomizado guiado. Cada objeto puede comprarse una vez por tienda especial con dinero; otras tiendas conservan su propio stock. No afecta a tiendas por progreso ni BP.
- Integración con historial, Global Template y Batch. La política cubre los inventarios finales, después del randomizado y las modificaciones manuales, sin convertir resultados aleatorios en overrides fijos.
- Mensaje **Ya compraste este objeto.** en el banco de tiendas; conserva los textos originales y devuelve el control a la lista al cerrar el aviso.
- Compatibilidad acotada con el guardado del prototipo conocido DRS1/STOC: conserva sus tres compras y migra el registro al completar una compra nueva. Consultar no modifica la partida.
- Corrección del botón Tiendas que desaparecía al abrir una ROM con el bloqueo temporal de controles. Probado fuera de pantalla con XY, ORAS, SM y USUM, en modo completo y simplificado.

## Descargar y usar

Extraer completamente `DefinitiveRandomizer-0.8.0-preview.4-win-x64.zip` y abrir **DefinitiveRandomizer.exe**. Windows x64, autocontenido: no requiere instalar .NET. Mantener juntas las DLL y las carpetas LegacyBatch, Tools, Templates, custom_balance_templates y catch_zone_patches. El ejecutor de Batch necesita sus dependencias y no se publica como un ejecutable único.

Paquete compacto: sin símbolos, logs de desarrollo ni carpetas de traducción del runtime .NET. La aplicación mantiene sus etiquetas en español y la edición de los idiomas de las ROM. Los mensajes de diagnóstico del runtime pueden usar el idioma neutral. Se conservan todos los templates públicos y las dependencias necesarias de vídeo, incluidas sus licencias y el código fuente exigido por su distribución.

Conservar los templates personales al actualizar: extraer en una carpeta nueva y copiar después los propios. No se incluyen recetas privadas, ROMs, partidas, extracciones, sprites ni datos locales. No se declara soporte nativo Linux para WPF/WinForms. Las imágenes Pokémon requieren conexión y pueden no aparecer sin ella.

## Validación y límites

La corrección de compras y del cierre del diálogo fue confirmada en juego por el usuario en UltraSol, usando el guardado del prototipo anterior. No se declara una comprobación jugable exhaustiva de UltraLuna ni de todas las escenas.

Antes de empaquetar: 3.762 comprobaciones sintéticas XY/ORAS/SM/USUM; 173 de integración sobre copias; 4 con el backend original; 551 ARM, incluidas todas las máscaras del prototipo y bytes del guardado real en memoria simulada. La prueba nueva del retorno a la tienda falla con el parche anterior y pasa con la corrección. Las suites contienen casos compartidos; las cifras son comprobaciones ejecutadas, no escenarios independientes. La interfaz se comprueba fuera de pantalla; el arnés ARM sustituye señales de UI e inventario y no representa una sesión interactiva.

Solo se parchean ejecutables Shop.cro USUM reconocidos. Capacidad de 1.984 identidades históricas de tienda/objeto; se conservan claves retiradas y las tres del prototipo. Si se excede, se rechaza la operación completa. Guardados con identidades desconocidas o bytes ajenos se rechazan sin sobrescribirlos. La auditoría exhaustiva de todos los usos de la región candidata de guardado sigue pendiente. Conservar una copia de las partidas; otra ROM con el mismo Title ID comparte el guardado.

Para actualizar una ROM ya generada: volver a previsualizar/aplicar la política en el proyecto y hacer Rebuild. Actualizar solo esta aplicación no modifica ROMs existentes. Una política ya guardada no se desinstala al quitar su selección pendiente.

## Reportar problemas y reproducir

Indicar versión y commit de VERSION.txt, juego, idioma, pasos, semilla y logs. No adjuntar ROMs ni recursos extraídos. Verificar la descarga con SHA256SUMS.txt.

Publicación reproducible con Windows, SDK .NET 10, Git y dependencias Mobipeg documentadas:

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.8.0-preview.4
```

Detalles: `COMPRAS_UNICAS_TIENDAS_ESPECIALES.md`. Icono facilitado por el propietario: Icons8, https://icons8.com/.
