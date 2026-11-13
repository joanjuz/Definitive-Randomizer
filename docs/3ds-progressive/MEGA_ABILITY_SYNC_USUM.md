# Mega Ability Sync · Ultra Sol / Ultra Luna

Estado: **confirmado por el propietario en un combate real entre dos juegos con el parche en ambos (13/11/2026), con la rutina corregida.** La primera prueba de ese día, con la primera rutina, falló y motivó la corrección. La confirmación cubre el caso principal; los casos de la lista «Sin confirmar en juego» siguen sin prueba en combate.

## Primera prueba en combate y corrección

En la primera prueba, con el parche en los dos juegos, el combate se comportó como el juego original: las megaevoluciones usaron la habilidad de la tabla del juego que calculaba y cada pantalla siguió mostrando la suya.

Causa, identificada en el código del juego y reproducida en la emulación de la rutina (la nueva prueba en combate, ya con la corrección, salió bien): en un combate por enlace cada juego ya conoce el equipo del rival antes de empezar. Aunque el equipo se vuelve a enviar por la conexión, el juego no lee ese paquete: reconstruye el equipo rival a partir de su copia local. La primera rutina solo buscaba el dato añadido cuando el equipo se leía directamente del paquete recibido, así que en ese caso no guardaba nada.

Corrección: al preparar el equipo de cada jugador, la rutina busca el dato añadido en el búfer que guarda los paquetes de ese jugador, se lea el equipo de donde se lea. El formato de lo que se envía no cambia.

**Los proyectos que tienen instalada la primera rutina deben actualizarla:** el editor la muestra como «instalado con la rutina anterior» y al guardar con `1` la sustituye en el mismo sitio (acción «Actualizar Mega Ability Sync»). Después hay que generar de nuevo los dos juegos.

## Qué resuelve

Dos jugadores con juegos randomizados de forma distinta tienen tablas de habilidades diferentes. En un combate entre los dos, la habilidad de una megaevolución se resolvía con la tabla del juego que hace los cálculos, no con la del dueño del Pokémon: la megaevolución de un jugador podía actuar con la habilidad del juego del otro, y cada pantalla podía mostrar una distinta.

Con el parche instalado **en los dos juegos**, cada megaevolución usa la habilidad que tiene en el juego de su dueño, en los dos juegos y sea cual sea el que hace los cálculos. Las tablas de cada jugador siguen siendo distintas: no se igualan habilidades, ranuras ni semillas.

## Cómo funciona el juego original

- Al megaevolucionar, el juego cambia la forma del Pokémon y vuelve a leer su habilidad de la tabla Personal **local**, en la ranura (1, 2 u oculta) del ejemplar. Ese dato no viaja por la conexión.
- Al empezar un combate entre dos juegos, cada uno envía su equipo y recibe el del otro. Los dos juegos intercambian además una versión; el que tiene la mayor (o, a igualdad, el primero por orden de participante) hace todos los cálculos del combate y el otro reproduce el resultado.
- Por eso el efecto no es solo de texto: inmunidades, daño y activaciones se calculan con la habilidad que dice la tabla del juego que calcula.

## Qué hace el parche

Solo modifica `Battle.cro` (el mismo archivo en Ultra Sol y Ultra Luna). Lee `static.crs` y `code.bin` para saber dónde están las funciones del juego; no los cambia.

1. **Al enviar el equipo**, añade después de los datos del equipo 12 bytes: una marca, la versión del protocolo, la habilidad que tendrá cada uno de los seis Pokémon al megaevolucionar según la tabla y la ranura de ese juego (0 si no puede megaevolucionar) y una suma de comprobación. Los datos del equipo no cambian.
2. **Al preparar el equipo del otro jugador**, localiza el búfer que guarda los paquetes de ese jugador y comprueba que contiene el paquete del equipo, que mide exactamente lo esperado y que la marca, la versión y la suma son correctas. Si todo coincide, guarda la habilidad con la copia en memoria de cada Pokémon del otro jugador; si el paquete del equipo está pero no lleva un dato válido, guarda un cero. Esa copia solo existe durante el combate. El equipo propio y los equipos que no pertenecen a un jugador de la conexión (un compañero controlado por el juego) no se tocan.
3. **Al megaevolucionar**, después del cambio de forma original, si el Pokémon lleva una habilidad guardada, la usa. Si no, queda la de la tabla local, como en el juego original.

### Compatibilidad entre los dos juegos

| Situación | Resultado |
| --- | --- |
| Los dos con el parche y la misma versión de protocolo | Cada megaevolución usa la habilidad del juego de su dueño en los dos juegos |
| Solo uno con el parche | Los dos juegos se comportan como el original. El juego sin parche recibe 12 bytes de más que no lee |
| Los dos con el parche pero con versiones de protocolo distintas | Ninguno aplica nada: comportamiento original |
| Paquete dañado, de otro tamaño o de otro tipo | Comportamiento original |
| Combates sin conexión, contra entrenadores o salvajes | Sin cambios: no hay equipo recibido por la conexión |

No hay un aviso en pantalla cuando el otro jugador no tiene el parche: el combate sigue con el comportamiento original. Los dos jugadores deben comprobar antes que lo tienen instalado (el editor muestra el estado).

### Alcance

Megaevoluciones, incluida la de Rayquaza. **No** cubre la Regresión Primigenia, el Ultraestallido ni otros cambios de forma durante el combate, que siguen resolviendo la habilidad con la tabla local. Las repeticiones de Vídeo de Combate no llevan el dato añadido y pueden no coincidir con el combate en vivo.

## Instalar y retirar

**Mecánicas → Mega Ability Sync** (también desde Herramientas ROM → Megaevoluciones · Diagnóstico de habilidades). Solo Ultra Sol / Ultra Luna.

- El editor muestra si está instalado. Guardar con `1` instala; con `0` retira. Hay vista previa, queda en el historial y se puede deshacer.
- Si el proyecto tiene la primera rutina publicada, el editor lo indica y guardar con `1` la sustituye por la actual en el mismo espacio; con `0` se retira igual que la actual.
- Retirar restaura las cuatro instrucciones originales y borra la rutina. Si para instalar hubo que ampliar el módulo, este conserva el tamaño nuevo; en otro caso el archivo queda idéntico al de antes.
- No se instala si no se puede acreditar el módulo. Motivos de rechazo: `Battle.cro` distinto del estudiado, rutinas de combate cambiadas por otra modificación, un punto de enganche ocupado por otro parche, una instalación a medias, o `code.bin`/`static.crs` ausentes o con funciones que no tienen la forma esperada. No existe un modo de forzar.
- Convive con Player Level Caps y Persistent Items, que usan el mismo archivo, en cualquier orden.

Niveles del informe de capacidades (`usum.mega-ability-sync`): **verificada** cuando la rutina cabe en el espacio libre; **compatible por adaptación** cuando hay que ampliar el segmento de código; **conflicto** si otro parche ocupa un punto; **no acreditada** en los demás casos.

## Evidencia

| Nivel | Qué se comprobó | Qué no demuestra |
| --- | --- | --- |
| Modelo de dos juegos (`--mega-ability-sync-only`) | Dos juegos con tablas distintas, los dos papeles invertidos, las tres ranuras, la misma megaevolución con habilidades distintas, inmunidades y una activación al entrar, un solo lado con parche, protocolos distintos, paquetes repetidos, cortos y dañados, sin conexión | Es un modelo de las reglas, no el motor del juego |
| Módulos sintéticos (misma suite) | Instalar, releer, reinstalar sin cambios, retirar, conflictos, instalación parcial, módulo sin espacio, direcciones de otro juego, convivencia con los otros dos parches, editor con historial; reconocer la primera rutina, sustituirla en su sitio y retirarla | No contienen código del juego |
| Archivos reales (`--mega-ability-sync-real`, fuera del CI) | Ultra Sol y Ultra Luna originales, un Ultra Luna con Player Level Caps y el módulo con la primera rutina tal como se jugó: direcciones resueltas, instalación releída, reinstalación sin cambios, retirada byte a byte idéntica, Player Level Caps conservado, primera rutina sustituida en su sitio con el mismo resultado que una instalación nueva | No ejecuta el juego |
| Ejecución de la rutina en un emulador de procesador | La rutina ensamblada, dentro del módulo real y junto a las rutinas reales del juego que deciden de dónde se lee cada equipo, con las funciones de `code.bin` sustituidas por modelos. Cubre los dos casos (equipo rival ya conocido, como en la prueba que falló, y equipo leído del paquete); la primera rutina falla en el primero | No ejecuta el motor completo ni la conexión |
| **Combate real entre dos juegos** | Primera rutina: **falló** (comportamiento original). Rutina corregida: **funciona** (propietario, 13/11/2026): combate por enlace entre dos Ultra Luna randomizados de forma distinta, parche en los dos, megaevolución en los dos lados | Los casos de «Sin confirmar en juego» |

La suite automática tiene 165 comprobaciones. Solo la prueba en combate confirma el comportamiento: las pruebas automáticas de la primera rutina también pasaban, porque ninguna reproducía el caso del equipo rival ya conocido.

### Sin confirmar en juego

- Un solo juego con el parche (debe comportarse como el original, sin errores de conexión).
- El combate creado por el otro jugador.
- Combates dobles, múltiples y Battle Royal.
- Ultra Sol contra Ultra Luna.
- Megaevolución por movimiento (Rayquaza).
- Convivencia en combate con Persistent Items.

## Prueba en juego

Con dos proyectos de Ultra Sol / Ultra Luna randomizados con semillas distintas y dos participantes que lo acepten, sin servicios oficiales:

1. Instalar el parche en los dos (o actualizarlo si el editor indica «rutina anterior»), generar de nuevo los dos juegos y comprobar con «Comparar con otro jugador» una megaevolución cuyas habilidades difieran.
2. Combate local entre los dos: megaevolucionar en cada lado y anotar en cada pantalla la habilidad mostrada y un efecto que la distinga (una inmunidad, un cambio de clima o de estadísticas al entrar).
3. Repetir intercambiando quién crea la sala.
4. Repetir con el parche en un solo juego: debe comportarse como sin parche y el combate debe seguir con normalidad.
5. Repetir sin parche en ninguno, como referencia.

Resultado esperado con los dos parches: la misma habilidad y el mismo efecto en las dos pantallas, la del juego del dueño, en los pasos 2 y 3. El paso 2 está confirmado; los demás no se han comunicado.

## Archivos

- `pk3DS.Core/Modding/Research/MegaAbilitySyncPatch.cs`: reglas del protocolo, resolución de direcciones, inspección, instalación y retirada.
- `3DS.Progressive.App/Services/GameMegaAbilitySyncService.cs`: editor, informe de capacidades e historial.
- `3DS.Progressive.Tests/MegaAbilitySyncChecks.cs`: suites `--mega-ability-sync-only` y `--mega-ability-sync-real`.
