# Megaevoluciones y habilidades en Ultra Sol / Ultra Luna: diagnóstico

Estado: **causa identificada en el código del juego; corrección instalable en [MEGA_ABILITY_SYNC_USUM.md](MEGA_ABILITY_SYNC_USUM.md), pendiente de prueba en combate real.** Este documento describe el informe de solo lectura.

## Problema reportado

En algunos combates en línea, la habilidad de un Pokémon megaevolucionado cambia de forma inesperada, no se activa o no corresponde a la que se esperaba.

## Lo que sí se sabe (lectura del código y de los datos)

- La randomización de habilidades asigna tres habilidades independientes a **cada** registro de la tabla Personal (`PersonalRandomizer.RandomizeAbilities`). Los registros de las megaevoluciones también son registros de esa tabla.
- En el juego original, cada megaevolución tiene la misma habilidad en sus tres ranuras. Tras randomizar habilidades, una megaevolución puede tener tres distintas.
- Con ranuras distintas, la habilidad que recibe un Pokémon al megaevolucionar depende de la ranura que tenía antes (1, 2 u oculta). Dos ejemplares de la misma especie pueden megaevolucionar con habilidades diferentes.

## Hipótesis sin confirmar

1. **Dependencia de la ranura.** Lo que se percibe como «la habilidad cambió» es la habilidad de la ranura del ejemplar en el registro de la megaevolución. Ocurriría también fuera de línea.
2. **Tablas distintas entre los dos jugadores.** Si cada consola resuelve la habilidad de la megaevolución con su propia tabla Personal, dos juegos randomizados de forma distinta no coinciden. Solo se manifestaría en combates entre dos juegos.
3. **Solo presentación.** El texto o la animación no corresponden a la habilidad que el motor aplica.

Ninguna se ha comprobado en un combate. No se ha analizado el código de combate del juego.

## Herramienta

**Herramientas ROM → Megaevoluciones · Diagnóstico de habilidades → Exportar diagnóstico de megaevoluciones…** (solo Ultra Sol / Ultra Luna).

Lee la tabla Personal guardada y escribe un informe de texto con:

- las formas Mega, Primigenias y Ultra, con sus tres habilidades y las de la especie base;
- cuáles tienen una habilidad distinta según la ranura;
- la huella SHA-256 de la tabla Personal, para comparar con la del otro jugador.

No modifica el proyecto, no reproduce un combate y no confirma la causa.

### Comparar con otro jugador

En la misma tarjeta, **Comparar con otro jugador…** pide dos archivos de «Exportar datos para Progressive Tracker» (uno por jugador) y escribe un informe con:

- si las huellas de los datos exportados coinciden;
- cuántas formas de Pokémon tienen habilidades distintas entre los dos juegos;
- las megaevoluciones cuyas tres habilidades difieren, con las de cada juego.

No necesita ninguna ROM ni el proyecto del otro jugador: solo los dos archivos JSON. Sirve para la hipótesis 2 (pasos 4 y 5 de abajo); no demuestra la causa.

Archivos: `3DS.Progressive.App/Services/MegaAbilityDiagnostics.cs`, `MegaAbilityComparison.cs`, botones en `SystemsView.cs`, suite `--mega-diagnostic-only` (43 comprobaciones con datos sintéticos; la comparación usa exportaciones del exportador real sobre proyectos sintéticos).

## Qué falta para reproducirlo

Con participantes que consientan y sin servicios oficiales:

1. Exportar los datos para Progressive Tracker en los dos juegos y usar «Comparar con otro jugador» para saber si las megaevoluciones coinciden.
2. Elegir una megaevolución marcada como dependiente de la ranura y dos ejemplares con ranuras distintas.
3. Fuera de línea: megaevolucionar cada ejemplar y anotar la habilidad que actúa. Confirma o descarta la hipótesis 1.
4. Entre dos juegos con la **misma** huella: repetir. Si coincide con el paso 3, el problema no es de sincronización.
5. Entre dos juegos con huellas **distintas**: repetir. Si aquí falla y en el paso 4 no, apunta a la hipótesis 2.
6. Si la habilidad que actúa es la correcta pero el mensaje no, apunta a la hipótesis 3.

## Corrección

El análisis del código de combate confirmó la hipótesis 2: al megaevolucionar, cada juego lee la habilidad de su propia tabla, y en un combate entre dos juegos solo uno hace los cálculos. La corrección es Mega Ability Sync, descrita en [MEGA_ABILITY_SYNC_USUM.md](MEGA_ABILITY_SYNC_USUM.md): los dos juegos llevan el parche y cada megaevolución usa la habilidad del juego de su dueño, sin igualar tablas, ranuras ni semillas. Está instalable y probada con pruebas automáticas y archivos reales. La primera prueba en un combate real falló y la rutina se corrigió; la corrección sigue pendiente de prueba en combate, con los pasos de ese documento.

La hipótesis 1 (dependencia de la ranura) sigue siendo cierta y no es un defecto: ocurre también sin conexión. El diagnóstico y el comparador siguen siendo de solo lectura.

Alcance: solo Ultra Sol / Ultra Luna. No se extiende a Sol/Luna ni a la sexta generación.
