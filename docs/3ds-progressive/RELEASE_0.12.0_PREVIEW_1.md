# Definitive Randomizer 0.12.0 Preview 1

Preview publicada desde `main`. Esta versión trae cuatro bloques:
- el modo Nintendo DS con la interfaz y las opciones de 3DS;
- el escalado de entrenadores según la historia en todos los juegos;
- mejoras del editor pedidas por los testers;
- la corrección de los combates de prueba de Sol/Luna y USUM.

No sustituye la versión estable WinForms v4.17.1.

## Corrección importante: combates de prueba (Sol/Luna y USUM)

Con «Reemplazar especies Totem por evoluciones finales», algunos combates de las pruebas salían como evolución final o legendario sin filtro de BST. Ejemplos reales de testers: Swoobat o Metagross a nivel 11 en la primera prueba, Palkia en la de Lana y Blaziken en la de Kiawe.

- **Causa:** el Totem se reconocía por su especie. Los combates de la prueba que comparten especie con su Totem también recibían ese trato:
  - el Gumshoos o Raticate de las madrigueras;
  - Wishiwashi, Marowak y Salazzle.
- **Ahora:** solo cuenta como Totem el combate que tiene aura de Totem. El resto sigue la randomización normal, con BST similar si está activado.
- **ROM ya randomizadas:** hay que restaurar la especie original de esas entradas y randomizarlas de nuevo. Son las entradas 6, 11, 15, 16, 18, 20, 21 y 250 de «Encuentros estáticos».

## Escalado de entrenadores según la historia

Como ya hacía USUM, cada entrenador normal sube de nivel desde el cap del jefe anterior hasta justo por debajo del siguiente, según su posición en la guía de WikiDex.

- **Juegos:** Diamante/Perla, Platino, HeartGold/SoulSilver, Negro/Blanco, Negro 2/Blanco 2, XY, ORAS, Sol/Luna y Espada.
- **Dónde se activa:** «Escalado según la cronología de la historia (WikiDex)», en «Política global de Level Caps» (en Espada, en «Niveles de entrenadores»). Viene activada por defecto.
- **Qué no cambia:** el postgame. Los romhacks con otros niveles de jefes y los templates antiguos usan el escalado anterior.
- **Corrección en XY/ORAS:** las reglas «Ace dinámico» con «Nivel» desactivado dejaban a los entrenadores en nivel 1.

Detalle en [ESCALADO_CRONOLOGIA_HISTORIA.md](ESCALADO_CRONOLOGIA_HISTORIA.md).

## Mejoras del editor

- **Búsqueda en los desplegables:** en los de 10 opciones o más, se escribe para filtrar por nombre o número, sin importar las tildes. Sirve en todas las secciones de 3DS, DS y Espada/Escudo.
- **Copiar y pegar un Pokémon de entrenador:** botones en cada posición del equipo. Si se pega fuera del equipo, el equipo se amplía.
- **Encuentros estáticos desde 000:** la numeración coincide con pk3DS y con la tabla de Totems.
- **Historial como Global Template:** «Cargar template global…» acepta el JSON de «Exportar historial (JSON)».
- **Archivos con otro SHA-256:** si un archivo del template (por ejemplo, un CSV) cambió desde que se guardó, la vista previa ya no se detiene. Muestra un aviso y usa el archivo actual. Si el archivo falta, la vista previa sigue sin poder hacerse.

Detalle en [MEJORAS_EDITOR_20261102.md](MEJORAS_EDITOR_20261102.md).

## Nintendo DS: paridad con 3DS

- **Entrenadores:** «Tabla de Level Caps» y «Tabla de Trainer Move Rules», como en 3DS.
- **Iniciales:** de tres etapas, y opción de iniciales legendarios.
- **Movimientos huevo:** como formulario.
- **Opciones de randomizado:** las de 3DS, con los mismos textos y valores por defecto, en entrenadores, encuentros, estáticos, Level Up, evoluciones, Pokémon y movimientos.
- **Templates:** de entrenadores y Wild.
- **Vistas:**
  - Level Up y MT/tutores con la disposición de 3DS, con «Expandir cantidad».
  - Pokémon con tarjeta de cabecera.
  - Navegación con iconos.
- **Smart Items por generación:** solo objetos que existen en ese juego.
- **Compatibilidad:** los templates DS anteriores dan el mismo resultado.

Detalle en [NDS_PARIDAD_INTERFAZ_3DS.md](NDS_PARIDAD_INTERFAZ_3DS.md).

## Espada/Escudo

- **Level Up con la vista de 3DS:** listas de hasta 64 movimientos, y solo movimientos que el juego puede usar.
- **Smart Items:** ahora da Cinta Fuerte, Gafas Especiales y Cinta Experto (antes daba Cinta Experto, Refleluz y Toxiesfera), y añade objetos de Gen8.

## Auditorías

- **Memoria del código ARM (USUM):** [AUDITORIA_MEMORIA_ARM.md](AUDITORIA_MEMORIA_ARM.md).
  - En las ROM de los testers, la única violación era la del recordador, ya corregida en 0.11.
  - Con 0.11 no queda ninguna.
- **Auditoría visual ampliada:** los cuatro juegos de 3DS en cuatro tamaños, más una auditoría propia para DS. Se arreglaron:
  - los pulgares de las barras de desplazamiento;
  - el desplazamiento en algunas vistas;
  - las columnas de opciones;
  - el contraste.

## Descargar y usar

Extraer completamente `DefinitiveRandomizer-0.12.0-preview.1-win-x64.zip` y abrir **DefinitiveRandomizer.exe**.

- **Requisitos:** Windows x64. Es autocontenido: no requiere instalar .NET.
- **Archivos:** mantener juntas las DLL y las carpetas LegacyBatch, Tools, Templates, custom_balance_templates y catch_zone_patches.
- **Templates personales:** al actualizar, extraer en una carpeta nueva y copiar después los propios.
- **Qué no se incluye:** recetas privadas, ROMs, partidas, extracciones, sprites ni datos locales.

## Qué probar en el juego

- **Combates de prueba de Sol/Luna y USUM:** con «Reemplazar especies Totem», deben salir con un BST parecido al original y no como legendarios.
- **Progresión de niveles:** los entrenadores normales suben de forma gradual entre jefes, en cada juego.
- **Búsqueda en desplegables:** la lista se queda abierta mientras se escribe y se cierra al elegir.
- **Copiar y pegar** un Pokémon entre entrenadores, y guardar.
- **Historial exportado:** cargarlo como Global Template y previsualizarlo.
- **Modo DS:** las tablas de caps y de reglas, los iniciales y las vistas nuevas.
- **Espada/Escudo:** las listas de Level Up largas y los Smart Items.

## Validación y límites

Antes de empaquetar, en Release y con compilación sin advertencias:
- **CI:** todas las suites del CI.
- **3DS sintéticas:** 4.101 comprobaciones de XY/ORAS/SM/USUM.
- **Interfaz:** 408 comprobaciones de interfaz y 55 de las peticiones de los testers.
- **Copias reales:**
  - `--nds-ui-probe` en los cinco juegos DS: 52 comprobaciones por juego;
  - `--story-probe` en XY, ORAS, Sol/Luna y Espada.

**Límites conocidos:**
- **Pruebas en el juego:** ninguna de las novedades de esta versión las tiene todavía.
- **Movimiento Z del primer combate de Tilo:** no lo usa aunque se le añada a mano. Ese combate tiene la misma cabecera de entrenador que el combate en que sí lo usa, así que no se corrige editando el entrenador.
- **Cronología de Galar:** solo se aplica a Espada; Escudo no tiene la suya.
- **Cuelgue en la entrega de los pétalos (USUM):** sigue sin causa determinada.
- **Partida completa:** no se declara, ni prueba en consola real.

## Reportar problemas y reproducir

**Al reportar:** indicar versión y commit de VERSION.txt, juego, versión, idioma, pasos, semilla y logs. No adjuntar ROMs ni recursos extraídos. Verificar la descarga con SHA256SUMS.txt.

**Para reproducir la publicación:** hace falta Windows, el SDK de .NET 10, Git y las dependencias Mobipeg documentadas.

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.12.0-preview.1
```

Catálogo del runner: `RUNNER_PRUEBAS.md`.
