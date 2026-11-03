# Definitive Randomizer 0.14.0 Preview 1

Preview publicada desde `main`. Esta versión aplica las mejoras de la auditoría de la aplicación:
- un Historial que se entiende;
- deshacer cambios;
- un registro del randomizado;
- editores de Evoluciones, Level Up, Encuentros estáticos y Pokémon más claros.

Corrige además cómo se mostraba la naturaleza de los encuentros estáticos de Sol/Luna y USUM. No cambia los motores de randomización ni el formato de los templates. No sustituye la versión estable WinForms v4.17.1.

## Corrección: naturaleza de los encuentros estáticos (Sol/Luna y USUM)

- **El problema:** el juego guarda la naturaleza de un encuentro estático con 0 = aleatoria y el resto desplazado en uno. El editor mostraba la lista sin ese desplazamiento, así que cada naturaleza aparecía una posición desplazada.
- **Ahora:** se ve «Aleatoria», «Fuerte»… en su valor real. El género (Aleatorio, Macho, Hembra, Sin género) y la habilidad (Cualquiera, 1, 2, oculta) también salen por nombre.
- **Qué no cambia:** los valores guardados. Solo se veían mal.

## Historial

- **Cada cambio dice qué se hizo:** «Randomizar entrenadores», su sección, cuántos valores cambió, a quién afectó y cuándo.
- **Una receta aplicada es un solo bloque**, con sus acciones por sección (antes eran decenas de filas con códigos).
- **Filtros** por sección, por tipo (ediciones manuales, randomizaciones, Global Template) y búsqueda de texto.
- **Detalle:** antes → después de cada valor, y los datos técnicos en un desplegable.

## Deshacer

- **Copia de seguridad automática:** al guardar un cambio, la aplicación guarda los archivos tal como estaban antes. Se conservan los 8 últimos, en `.progressive-restore`, y el backup del proyecto no los incluye.
- **Dónde:**
  - «Deshacer este cambio» (o «Deshacer toda la receta») en el Historial;
  - «Deshacer el último cambio» en el buscador (Ctrl+K).
- **Seguridad:** solo deshace si los archivos siguen exactamente como los dejó ese cambio. Si un cambio posterior los modificó, explica el motivo y no toca nada.
- **Registro:** queda en el historial como una restauración. Las recetas no lo repiten.
- **Límite:** solo los cambios guardados con esta versión o posteriores tienen copia.

## Registro del randomizado

- **Qué incluye:** un archivo de texto con los cambios guardados, los regalos y encuentros estáticos, los equipos de todos los entrenadores y los encuentros salvajes. Contiene spoilers.
- **Dónde:** en Herramientas ROM y en el buscador.
- **Cómo funciona:** solo lee el proyecto.

## Editores

- **Evoluciones:**
  - El parámetro del método es un desplegable de objetos, movimientos, especies o tipos según el método, en lugar de un número entre 0 y 65535.
  - Arriba, las rutas de evolución con sprites.
- **Level Up:** tipo, categoría, potencia y precisión de cada movimiento.
- **Pokémon:**
  - Barras de las seis estadísticas.
  - El ratio de género, en palabras.
- **Desplegables cortos:** sin el número delante.
- **Inicio sin proyecto:** «Primeros pasos» (extraer, abrir, randomizar, generar).
- **Nintendo DS y Espada/Escudo:** Ctrl+K busca en sus secciones.

Detalle en [AUDITORIA_MEJORAS_20261103.md](AUDITORIA_MEJORAS_20261103.md).

## Descargar y usar

Extraer completamente `DefinitiveRandomizer-0.14.0-preview.1-win-x64.zip` y abrir **DefinitiveRandomizer.exe**.

- **Requisitos:** Windows x64. Es autocontenido: no requiere instalar .NET.
- **Archivos:** mantener juntas las DLL y las carpetas LegacyBatch, Tools, Templates, custom_balance_templates y catch_zone_patches.
- **Templates personales:** al actualizar, extraer en una carpeta nueva y copiar después los propios.
- **Qué no se incluye:** recetas privadas, ROMs, partidas, extracciones, sprites ni datos locales.

## Qué probar

- **Deshacer:**
  - guardar un cambio en una sección y deshacerlo desde el Historial;
  - comprobar que no deja deshacer si después se cambió el mismo archivo;
  - aplicar un Global Template en una copia del proyecto y deshacer toda la receta.
- **Registro:** exportarlo y revisar que los entrenadores y encuentros coinciden con el juego.
- **Encuentros estáticos (Sol/Luna y USUM):** que la naturaleza mostrada coincida con la del juego.
- **Evoluciones:** cambiar el método a «Usar objeto» y elegir el objeto en la lista.

## Validación y límites

Antes de empaquetar, en Release y con compilación sin advertencias:
- **CI:** las suites del CI, que ahora incluyen la auditoría visual de 568 vistas.
- **Interfaz:** 187 comprobaciones de rediseño visual y 408 de interfaz.
- **Datos sintéticos:** 4.101 comprobaciones de XY, ORAS, Sol/Luna y USUM.

**Límites conocidos:**
- **Interfaz:** todo se ha comprobado fuera de pantalla y con datos sintéticos. Faltan el deshacer, el registro y el Historial con proyectos reales.
- **Marcas de cambios en DS y Espada/Escudo:** no existen, porque sus cambios pendientes no registran sección.
- **Field Items de XY/ORAS:** no tienen el resumen.
- **Arrastrados de versiones anteriores:**
  - el movimiento Z del primer combate de Tilo;
  - el cuelgue en la entrega de los pétalos (USUM), sin causa determinada.
- **Partida completa:** no se declara, ni prueba en consola real.

## Reportar problemas y reproducir

**Al reportar:** indicar versión y commit de VERSION.txt, juego, versión, idioma, pasos, semilla y logs. No adjuntar ROMs ni recursos extraídos. Verificar la descarga con SHA256SUMS.txt.

**Para reproducir la publicación:** hace falta Windows, el SDK de .NET 10, Git y las dependencias Mobipeg documentadas.

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.14.0-preview.1
```

Catálogo del runner: `RUNNER_PRUEBAS.md`.
