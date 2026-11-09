# Prueba jugable de entrenadores — Platino CPUE/1, PR #56

## Preparación

Usar la compilación local de `feature/nds-trainer-parity` (base `baa0dc5`), no `v0.15.0-preview.2`: la preview de mentas no incluye este PR. Ejecutable compilado: `3DS.Progressive.App/bin/Release/net10.0-windows/3DS.Progressive.exe`, dentro de la copia de trabajo `3DS.Progressive.Tests/bin/nds-trainer-parity`.

Trabajar con una copia de Pokémon Platino USA revisión 1 (`CPUE/1`) y un respaldo separado de la partida. No utilizar Renegade Platinum para esta comprobación del perfil original. Abrir Nintendo DS y cargar esa copia. Las ediciones se acumulan en memoria; Guardar cambios no reconstruye la ROM. Exportar ROM… escribe una ROM nueva y su registro.

Preparar una partida anterior al primer combate con Tristan y conservarla sin sobrescribir. Si ya está derrotado, empezar una partida de prueba aparte; una revancha puede utilizar otro registro. No cargar un estado rápido creado dentro del combate o desde una ROM distinta.

## Entrenador y valores concretos

Elegir **#1, Youngster Tristan**, primer entrenador de Ruta 202 tras la demostración de captura. La ubicación y el Starly de nivel 5 están contrastados en [Serebii, Ruta 202](https://www.serebii.net/pokearth/sinnoh/4th/route202.shtml). En la copia privada CPUE/1 se comprobó directamente que #1 tiene un solo Pokémon: Starly (#396), forma 0, nivel 5, flags 0 (sin movimientos ni objetos explícitos), combate normal.

Antes de editar, anotar también los controles, comprobados en esa misma copia:

| Registro de control | Equipo inicial |
|---|---|
| #2, Youngster Logan | Burmy (#412), forma 0, nivel 5; movimientos explícitos |
| #3, Lass Natalie | Bidoof (#399), forma 0, nivel 5; movimientos por nivel |

No editar esos dos entrenadores ni aplicar opciones de randomización global.

1. Entrar en **Entrenadores**, buscar Tristan y confirmar el ID #1 y su Starly de nivel 5. No elegir una variante de revancha con el mismo nombre.
2. En **Datos del entrenador**, mantener un solo Pokémon y combate normal. Activar **Movimientos personalizados · todo el equipo** y **Objetos equipados explícitos · todo el equipo**.
3. Seleccionar su primera ranura y cambiar estos campos:

| Campo | Valor de prueba |
|---|---|
| Especie | Magikarp (#129) |
| Forma | 0 |
| Nivel | 8 |
| Objeto equipado | Oran Berry / Baya Aranja (#155) |
| Movimiento 1 | Tackle / Placaje (#33) |
| Movimiento 2 | Growl / Gruñido (#45) |
| Movimiento 3 | Tail Whip / Látigo (#39) |
| Movimiento 4 | Splash / Salpicadura (#150) |

Los nombres se toman del idioma de la ROM; los números identifican los valores. Son movimientos explícitos para probar la edición: no se pretende reproducir el aprendizaje natural de Magikarp. Mantener los demás campos, clase, IA, género, habilidad y dificultad sin cambios.

4. Pulsar **Guardar cambios**, revisar que la propuesta solo afecte a #1 y confirmar. Consultar las tarjetas y comparación origen/proyecto: Starly 5 en origen, Magikarp 8 con objeto y cuatro movimientos en el proyecto.
5. Usar **Exportar ROM…** y elegir una carpeta nueva, por ejemplo `Platino-entrenador-prueba.nds`. No sobrescribir el origen ni reutilizar `edited.nds` del probe automatizado, que modifica dos entrenadores y hace operaciones adicionales.
6. Abrir la ROM exportada como otro proyecto DS (si solicita admitir tablas modificadas, habilitar **Admitir romhacks (tablas modificadas; beta)**). Releer #1: comprobar especie, forma, nivel, objeto, cuatro movimientos y formatos explícitos. Releer #2/#3: deben conservar lo anotado. Cerrar sin más cambios.

## Combate y criterios de aceptación

7. Abrir la ROM nueva en el emulador, con una copia de la partida anterior al encuentro. Ir a Ruta 202 y combatir contra Tristan. Debe enviar un **Magikarp de nivel 8**, no Starly 5. Evitar debilitarlo de un golpe: al bajar su vida suficientemente debe consumir su Baya Aranja y recuperar PS. Registrar la activación con captura o vídeo.
8. Observar y anotar sus movimientos. Para comprobar los cuatro sin depender de que la IA elija todos, si se dispone de un Ditto con Transformación en la partida de prueba, transformarlo en el Magikarp rival y abrir el menú de movimientos: debe mostrar Placaje, Gruñido, Látigo y Salpicadura. La comprobación de objeto es separada; Transformación no demuestra el objeto. Sin ese recurso, registrar los movimientos que llegue a utilizar: no dar los cuatro por verificados únicamente porque aparezcan en el editor.
9. Combatir también contra Logan y Natalie, aún sin derrotar en esa partida: deben conservar Burmy 5 y Bidoof 5 respectivamente. Registrar nombres, especie y nivel. La comparación automatizada de todos los registros ajenos complementa estos dos controles jugables.

Devolver: versión/rama de la app, identidad CPUE/1, emulador, resultado de reapertura, capturas del rival/nivel y baya, comprobación de los cuatro movimientos y controles #2/#3. Si un movimiento no se pudo observar/comprobar, marcarlo pendiente. Un combate fallido o bloqueado se describe con el paso exacto y sin continuar modificando otras tablas.

## Estado

El propietario confirmó el 09/11/2026 una prueba jugable satisfactoria del PR #56 en Pokémon Platino: modificó un Pokémon, le asignó cuatro movimientos y un objeto y comprobó el resultado en combate. Se conserva esta guía como procedimiento de reproducción; la confirmación recibida no detalla por separado cada paso ni los controles #2/#3. Los probes reales acreditan adicionalmente la reapertura y preservación de los registros ajenos. No se extrapola esta confirmación a ejecución en juego de Gen5, formas arbitrarias ni a una partida completa. No se fusionó el PR ni se generó una preview nueva.
