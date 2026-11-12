# Perfiles compartibles (juegos 3DS)

Un perfil compartible guarda la configuración de una receta para que otra persona la use en su propia copia del juego. El contrato del archivo está en [docs/contracts/SHAREABLE_PROFILE_V1.md](../contracts/SHAREABLE_PROFILE_V1.md).

## Qué reutiliza

No hay un motor nuevo. El perfil es un sobre alrededor de la receta de Global Template:

- **Exportar** parte de la misma receta que «Guardar template global» (opciones de los editores e historial).
- **Importar** carga las secciones elegidas como receta actual de Global Template.
- **Vista previa, confirmación, aplicación, historial y deshacer** son los de Global Template y del historial del proyecto.

Lo nuevo es la capa de intercambio: identidad y versión del formato, juego de destino, secciones con requisitos, procedencia declarada, lectura estricta, informe de compatibilidad y de conflictos, y la exclusión de todo lo que no se debe compartir.

## Uso

En **Global Template**:

1. **Exportar perfil compartible…** Muestra las secciones que llevará y lo que queda fuera. Pide nombre, autor y descripción, y guarda un `*.drprofile.json`. El proyecto no cambia.
2. **Importar perfil compartible…** Lee el archivo, comprueba el formato y muestra quién lo declara, si sirve para el proyecto abierto y el estado de cada sección. Las compatibles vienen marcadas; los conflictos se ofrecen sin marcar; lo no compatible no se puede marcar y explica por qué.
3. Al aceptar, las secciones marcadas quedan como receta cargada. **Previsualizar cambios** y **Aplicar cambios** funcionan como con cualquier receta.

## Límites del formato 1

- Proyectos de X/Y, Rubí Omega/Zafiro Alfa, Sol/Luna y Ultra Sol/Ultra Luna. Un perfil solo sirve en el juego del que salió: en otro, todas sus secciones aparecen como no compatibles. Player Level Caps y las mentas solo existen en USUM.
- No lleva recursos reemplazados, templates creados ni textos editados: se informa de ellos al exportar.
- Las dependencias externas de la receta (`Assets`) no se incrustan: se comprueban al importar y la vista previa las exige.
- La procedencia es la que declara el archivo; no hay firma.
- No hay biblioteca en línea.

## Archivos

- `3DS.Progressive.App/Services/ShareableProfile.cs`: formato, exportación, lectura estricta, compatibilidad y selección.
- `3DS.Progressive.App/ProfileWindows.cs`: diálogos de exportación e importación.
- `3DS.Progressive.App/GlobalTemplateView.cs`: los dos botones y la carga de las secciones elegidas.
- `3DS.Progressive.Tests/ShareableProfileChecks.cs`: suite `--profiles-only`.

## Validación

`--profiles-only`: 105 comprobaciones con binarios sintéticos, el ejecutor real de Global Template sobre un proyecto sintético y ventanas WPF fuera de pantalla. Cubre exportación, exclusiones y privacidad, guardado y recarga, 30 archivos rechazados, tolerancia a campos desconocidos, compatibilidad, requisito desconocido, otro juego, selección, aplicación de Player Level Caps por la reproducción existente, historial, deshacer, conflictos y los diálogos. El recorrido con el ejecutor (USUM y ORAS) comprueba vista previa sin escritura, aplicación del resultado previsualizado, registro solo de la sección elegida, procedencia del perfil en el historial y deshacer.

No se ha probado con un proyecto real ni en la interfaz en pantalla.
