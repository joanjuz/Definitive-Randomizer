# Iteración 84 — rendimiento Batch y fases de reconstrucción

Fecha: 2026-10-15. Rama feature/3ds-progressive-ui. No commits ni publicación.

## Diagnóstico
35/35 acciones indica que terminó el replay; aún falta construir RomFS, comprimir ExeFS, ensamblar .3ds y calcular el SHA-256 final. El puente creaba GameRomRebuildService para cada ROM, perdiendo la caché ExeFS existente. BLZ también actualizaba una barra nativa por cada token, y el generador IVFC de RomFS por cada bloque de 4 KB. BLZ usaba acceso IList<byte> y un objeto SearchPair por búsqueda.

## Cambios
- Caché ExeFS de una sola entrada compartida por el puente durante el proceso del lote, protegida por el BuildGate existente. La clave incluye nombres ordenados y SHA-256 del contenido de todos los archivos ExeFS. Un cambio invalida la caché. RomFS y hashes de cada salida se generan normalmente.
- BLZ mantiene la búsqueda y codificación; usa byte[] y un struct para el resultado, y limita actualizaciones de progreso a unas 200 por compresión.
- RomFS limita las actualizaciones de barra de su bucle IVFC; conserva todos los bloques y cálculos SHA-256.
- El puente comunica fases reales y progreso BLZ a BatchBridgeProgress. El registro del lote incluye segundos transcurridos al cambiar de fase. Los porcentajes siguen siendo pasos, no ETA.

## Mediciones locales USUM
Mismos archivos ExeFS de una copia propia PokemonUltraSol:
- Versión 83: 125,705 s.
- Solo caché y menos actualizaciones: primera 101,707 s; repetición 0,036 s.
- Versión final (además byte[]/struct): primera 57,070 s; repetición 0,036 s; una sola compresión.
Todos generan SHA256 76318C51519E6DD7664EFFC366DBA17CBF22C4755359426FCE08056E2DE887CD. Estos tiempos corresponden a ExeFS, no a una ROM completa ni a la randomización. Varían con equipo/carga/disco. La primera ROM sigue requiriendo compresión; si cambia ExeFS entre semillas se comprime de nuevo.

## Pruebas
Debug local: 0 errores / 79 advertencias. 11 comprobaciones de caché y reconstrucción; 96 regresiones Batch aprobadas. Se prueba reutilización entre instancias nuevas, invalidación de entradas cambiadas, nombres .code, conservación de originales y rechazo de entradas ambiguas. El progreso de fases es monotónico.

Candidato local: D:\Progra\DefinitiveRandomizer-validation\20261015-iteration84-batch-rapido\DefinitiveRandomizer.exe. Conserva los assets y DEFINITIVO compatible del candidato 83. DLL App SHA256 6E14E618D02DA04C66EABA5ED3E2B173A8427E5174E97D41627E013CA4642E00; Core SHA256 6B83CFA2BA276C3D99AD8CD730C43D62901DD0A85A28AA2741D422A95B5917F6 (también sincronizado en LegacyBatch).

La comparación inicial de una ROM ORAS completa encontró únicamente 44 bytes distintos en 0x1010–0x103B: CTRUtil genera CardInfo.InitialData mediante Random.Shared en cada reconstrucción. No se cambia esa función. La prueba de dos ROMs compara todos los demás bytes con el control D ya validado en juego. El resultado final se añade a continuación.

## Resultado de reconstrucción integral
Dos llamadas consecutivas al puente real NativeBatchRomBridge, con todas las opciones del ASU integral conservadas:
- ROM 1: 75,394 s, sin caché inicial; RomFS 6,4 s aprox., BLZ 53,4 s aprox.
- ROM 2: 39,809 s, reutilización ExeFS confirmada; el ensamblado tuvo mayor tiempo de E/S en esta ejecución.
Ambas comparan todos los bytes con el control ORAS D funcional, exceptuando exactamente el campo aleatorio CardInfo.InitialData de 44 bytes. El SHA-256 completo de cada archivo sí se calculó como siempre. Archivos de prueba bajo D:\Progra\DefinitiveRandomizer-validation\Rendimiento84. No se ejecutó ni controló el emulador.
