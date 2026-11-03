# Definitive Randomizer 0.13.0 Preview 1

Preview publicada desde `main`. Esta versión es un rediseño de la interfaz de 3DS para que las herramientas se encuentren sin buscar:
- navegación por grupos y buscador;
- qué se ha cambiado en cada sección;
- opciones aplicadas visibles al volver a randomizar;
- editores de Entrenadores, Encuentros, Field Items y Movimientos renovados.

No cambia los motores de randomización ni el formato de los templates. No sustituye la versión estable WinForms v4.17.1.

## Encontrar las cosas

- **Barra lateral por grupos plegables:** *Pokémon y movimientos*, *Mundo y combates*, *Textos*, *Parches y ROM* y *Recetas y lotes*. «Opciones e idioma» queda fija abajo.
- **Buscador (Ctrl+K o «Buscar…» en la barra):**
  - Encuentra cualquier sección o herramienta, también las de dentro de los hubs.
  - Dice dónde está cada una, ignora tildes y entiende sinónimos («shiny», «caps», «tiendas»…).
- **Miga de pan** en las herramientas de los hubs, con botón para volver. Por ejemplo: «← Herramientas Pokémon › Egg Moves».
- **Cabecera:** con un proyecto abierto, la acción principal es «Rebuild».
- **Hubs en cuadrícula:** descripción breve, etiqueta «Avanzado» o «Parche de código», y el texto técnico en «Detalles técnicos».
- **Automatización** sale de la barra, porque sus accesos ya estaban en ella; sigue en el buscador.

## Qué se ha cambiado

- **Barra lateral:** cada sección muestra cuántos cambios guardados tiene, y su tooltip lista los últimos.
- **Dentro de cada sección:** «N cambios guardados ▾» muestra qué se hizo, cuándo, si vino del Global Template, cuántos valores cambió y a quién afectó.
- **Inicio:** resume el proyecto: cambios por sección («Ver qué se cambió») y si falta un backup o un Rebuild en esta sesión.
- **Corrección:** los proyectos randomizados con un Global Template ya marcan todas las secciones que tocó la receta. También los historiales antiguos con tildes dañadas.

## Opciones aplicadas al volver a randomizar

Al abrir un proyecto ya randomizado, cada randomizador muestra las opciones que se aplicaron, no las de por defecto. Incluye los rangos del BST progresivo de Entrenadores.

- **De dónde salen:** de la receta del último Global Template aplicado o, si la sección se randomizó después desde su editor, de esa randomización.
- **Aviso en el panel:** «Estas son las opciones de la última randomización aplicada», con su origen y fecha.
- **Qué no se toca:** no se escribe nada en el proyecto ni en el historial.

## Editores

- **Entrenadores:**
  - Cabecera con clase, tipo de combate, nivel y objetos.
  - Tarjetas del equipo con sprite, nivel, objeto y movimientos, que se actualizan mientras se edita.
  - Se edita un Pokémon a la vez.
  - **Clic derecho sobre un Pokémon:** Copiar, Cortar, Pegar o Eliminar del equipo, también con Ctrl+C, Ctrl+X, Ctrl+V y Supr.
  - «+ Añadir Pokémon», con «Pegar como nuevo Pokémon» en su clic derecho.
  - En Gen7, género y habilidad por nombre.
  - Tabla de IV y EV con el total de EV.
  - La lista indica la clase, el equipo y los niveles, y la búsqueda encuentra por clase.
- **Encuentros:**
  - Tabla de ranuras: especie, forma y probabilidad (Gen7) o niveles (Gen6) en una fila.
  - Cada tarjeta muestra la probabilidad de su ranura.
- **Field Items (Sol/Luna y USUM):**
  - Resumen del juego: visibles, ocultos, MT, objetos clave y objetos más repetidos.
  - Los demás objetos del área y las otras ubicaciones del mismo objeto, navegables.
- **Movimientos:**
  - Nombre y descripción del movimiento editables desde su ficha, con vista previa como en el juego.
  - Se guardan en Game Text, con su historial.
- **General:**
  - Inspector por grupos, con la acción principal y su alcance arriba.
  - Indicador de pasos al randomizar (configura › vista previa › aplica).
  - Formularios sin huecos y pestañas que no cambian de fila.
  - Menús oscuros.
  - Toda la interfaz en tuteo.

Detalle en [REDISENO_NAVEGACION_JERARQUIA.md](REDISENO_NAVEGACION_JERARQUIA.md).

## Descargar y usar

Extraer completamente `DefinitiveRandomizer-0.13.0-preview.1-win-x64.zip` y abrir **DefinitiveRandomizer.exe**.

- **Requisitos:** Windows x64. Es autocontenido: no requiere instalar .NET.
- **Archivos:** mantener juntas las DLL y las carpetas LegacyBatch, Tools, Templates, custom_balance_templates y catch_zone_patches.
- **Templates personales:** al actualizar, extraer en una carpeta nueva y copiar después los propios.
- **Qué no se incluye:** recetas privadas, ROMs, partidas, extracciones, sprites ni datos locales.

## Qué probar

- **Con un proyecto ya randomizado con Global Template:**
  - que la barra marque todas las secciones;
  - que «Randomizar» en Entrenadores muestre las opciones y los rangos BST de la receta.
- **Entrenadores:**
  - clic derecho sobre un Pokémon;
  - copiar de un entrenador y pegar en otro;
  - eliminar un Pokémon del medio del equipo y guardar.
- **Movimientos:** cambiar una descripción, guardarla y comprobarla en Game Text y en el juego.
- **Buscador:** Ctrl+K y navegación solo con teclado.
- **Pantalla:** resoluciones y escalados reales de Windows (125 % y 150 %).

## Validación y límites

Antes de empaquetar, en Release y con compilación sin advertencias:
- **CI:** las 21 suites del CI.
- **Datos sintéticos:** 4.101 comprobaciones de XY, ORAS, Sol/Luna y USUM.
- **Interfaz:** 165 comprobaciones de rediseño visual y 408 de interfaz.
- **Auditoría visual:** 568 vistas en cuatro juegos y cuatro tamaños, 0 problemas.

**Límites conocidos:**
- **Comprobación de la interfaz:** todo se ha comprobado fuera de pantalla y con datos sintéticos. Falta el uso real con ratón, teclado y lector de pantalla.
- **Backup y Rebuild en Inicio:** se recuerdan solo durante la sesión.
- **Story Text:** comparte registro con Game Text, así que sus cambios se marcan en Game Text.
- **Field Items de XY/ORAS:** no tienen el resumen nuevo.
- **Espacios de Nintendo DS y Espada/Escudo:** no se revisaron visualmente. Su editor de entrenadores sí pasa a editar un Pokémon a la vez.
- **Arrastrados de 0.12:**
  - el movimiento Z del primer combate de Tilo;
  - el cuelgue en la entrega de los pétalos (USUM), sin causa determinada.
- **Partida completa:** no se declara, ni prueba en consola real.

## Reportar problemas y reproducir

**Al reportar:** indicar versión y commit de VERSION.txt, juego, versión, idioma, pasos, semilla y logs. No adjuntar ROMs ni recursos extraídos. Verificar la descarga con SHA256SUMS.txt.

**Para reproducir la publicación:** hace falta Windows, el SDK de .NET 10, Git y las dependencias Mobipeg documentadas.

```powershell
./tools/Publish-Preview.ps1 -OutputDirectory D:/Releases -Version 0.13.0-preview.1
```

Catálogo del runner: `RUNNER_PRUEBAS.md`.
