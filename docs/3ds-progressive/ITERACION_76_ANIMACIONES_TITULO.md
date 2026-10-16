# Iteración 76 · Imágenes y animaciones de título

Se amplía el editor de título de Definitive Randomizer para XY, ORAS, Sol/Luna y USUM. Las imágenes 2D y los vídeos se editan en dos pestañas del mismo módulo. Los cambios Progressive existentes y la corrección de reconstrucción de la iteración 75 se conservan.

## Qué recurso corresponde a cada secuencia

- El editor existente conserva PNG/BCLIM/BFLIM y sus contenedores de imágenes, incluyendo los logos y efectos 2D del inicio de partida nueva de USUM.
- La pantalla animada de Solgaleo/Lunala al acceder a una partida guardada utiliza `RomFS/m/title_<idioma>.moflex`. El logo está grabado en los fotogramas. Modificar una textura 2D no modifica ese vídeo.
- Gen6 expone también los clips MOFLEX de `RomFS/m`. XY contiene `a`, `ax`, `ay`, `bx` y `by`; ORAS contiene `ay`. Se muestran los nombres reales y se ofrece vista previa para distinguir los clips, sin atribuir a cada nombre una función no verificada.

## Uso

1. Abrir **Pantallas de título** y elegir **Imágenes y logos 2D** para los PNG habituales, o **Animaciones y vídeos MOFLEX** para las secuencias de vídeo.
2. En vídeos, elegir archivo/idioma y pista. En Gen7 la pista de 400 × 240 corresponde a la pantalla superior; la de 320 × 240, a la inferior. **Ver fotograma** permite consultar un segundo concreto.
3. Exportar la pista a MP4, editarla en un editor de vídeo y volver a importarla. Mantener dimensiones, FPS y cantidad exacta de fotogramas. También se puede exportar/importar un MOFLEX completo compatible.
4. Revisar la vista previa del borrador y pulsar **Aplicar y verificar**. Después reconstruir la ROM con el flujo habitual.

No se aplica automáticamente el mismo PNG al vídeo: su logo forma parte de la animación. Esta versión incorpora el flujo de edición de vídeo; no elimina automáticamente un logo de fotogramas animados.

Al importar un vídeo editado se sustituye solo la pista elegida. Se conserva el contenido de las demás pistas de vídeo y el audio de origen; el audio se decodifica y recodifica al mismo códec, frecuencia y canales porque el empaquetador MOFLEX requiere PCM como entrada. No se importa el audio del MP4. La recodificación de imagen/audio no es una copia sin pérdida. Varias importaciones sobre distintas pistas conservan las ediciones ya preparadas.

## Protección de datos y herramientas

El archivo del juego solo cambia al aplicar, mediante la transacción del historial. Un cambio externo del original impide aplicar un borrador obsoleto. Los fallos y cancelaciones no sustituyen el borrador previamente válido. La exportación exige un archivo nuevo fuera del proyecto. La navegación queda bloqueada mientras se procesa; se solicita descartar al salir con cambios pendientes.

Se comprueban el formato MOFLEX, las pistas audiovisuales, resolución, FPS, disposición 2D/3D, fotogramas, códecs, frecuencia y canales, y se decodifica por completo el resultado. El descriptor de búsqueda generado por el empaquetador no se confunde con una pantalla adicional. La importación tiene un límite de 512 MiB y los procesos un tiempo máximo de 30 minutos. Se muestra el tiempo de vídeo procesado y se permite cancelar.

Motor local: Mobipeg v2.3 (FFmpeg y fork x264 de quatric), ejecutado como proceso independiente mediante argumentos estructurados, sin shell. Se incluyen licencias, atribuciones, fuentes del lanzamiento y script de preparación con SHA-256 fijado. Los ejecutables y archivos comprimidos están excluidos de Git. No se descarga software al abrir el editor.

## Validación local completada

- Compilación Debug: 0 errores; 79 advertencias existentes en el conjunto de proyectos.
- Regresión completa: **3.934 comprobaciones** con datos sintéticos XY, ORAS, SM y USUM, incluyendo 37 comprobaciones específicas de vídeos y 20 nuevas de interfaz.
- Auditoría de solo lectura: **48 archivos MOFLEX**, correspondientes a los ocho juegos extraídos del usuario; hashes de origen conservados.
- Exportación MP4 y codificación MOFLEX completas con los vídeos reales de Pokémon Y, Rubí Omega, Sol y Ultra Sol. Verificación de perfiles y decodificación íntegra del resultado.
- Aplicación transaccional en copias temporales de los cuatro grupos; cancelación que conserva el borrador y rechazo de fuente modificada; originales intactos.
- USUM conserva sus dos pistas de 2.654 fotogramas. Sol conserva las pistas de 3.265 y 3.264 fotogramas respectivamente.
- Las pruebas no dependen de GitHub Actions y no modifican Eris-Ultranova. Sin commits, push ni publicación de release.

Comandos reproducibles del ejecutable de pruebas:

```powershell
dotnet 3DS.Progressive.Tests.dll --title-movies-only
dotnet 3DS.Progressive.Tests.dll --utility-ui-only
dotnet 3DS.Progressive.Tests.dll --read-title-movies D:\Nintendo\3DS\Juegos\PokemonX D:\Nintendo\3DS\Juegos\PokemonUltraSol
dotnet 3DS.Progressive.Tests.dll --check-title-movie-roundtrip D:\Nintendo\3DS\Juegos\PokemonY D:\Progra\DefinitiveRandomizer-validation\prueba-video-nueva
```

El último comando genera archivos nuevos y aplica únicamente sobre una copia sintética temporal del vídeo original. Puede tardar varios minutos. Para repetirlo, utilizar una carpeta de salida nueva.

## Pendiente de validación en juego

Las comprobaciones de contenedor/códec no sustituyen ejecutar la ROM. Falta comprobar el vídeo recodificado en Lime3DS y, si se desea compatibilidad con consola, en hardware real. Revisar arranque, pantalla superior e inferior, audio y sincronización, bucle y transición al pulsar un botón. Con un vídeo editado, revisar que el cambio aparezca en la secuencia e idioma elegidos, tanto con partida nueva como guardada. La compilación local se entrega como candidato para estas pruebas, no como validación definitiva de todas las funciones de la aplicación.
