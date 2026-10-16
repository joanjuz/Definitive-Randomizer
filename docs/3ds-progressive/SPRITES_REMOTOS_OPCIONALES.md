# Sprites remotos opcionales

2026-10-15. Cambio local posterior al commit e4626f7; no se realizó push ni publicación.

La aplicación solicita los PNG desde raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{speciesId}.png, igual que el fallback remoto del tracker. Solo carga especies que se muestran, en segundo plano. Conserva imágenes válidas en memoria durante la sesión, comparte peticiones simultáneas de la misma especie y limita la concurrencia a cuatro descargas. No consulta recursos locales ni escribe imágenes en disco.

Sin conexión, con timeout, HTTP 404, datos corruptos o respuestas mayores a 2 MB, devuelve una imagen ausente y no muestra errores al usuario. Tras un fallo de red espera 20 segundos antes de intentar otra descarga. Cancelar una selección no cancela la imagen que otra tarjeta sigue esperando. Las imágenes remotas usan ID de especie nacional, igual que el tracker; no se añadió un catálogo nuevo de formas alternativas.

Se retiró la copia de Sprites del proyecto WPF y del paquete instalado. Se sustituyeron las 1185 referencias a imágenes de Pokemon Sprites del motor WinForms por un bitmap transparente genérico de 64x64, manteniendo los identificadores de recursos para conservar compatibilidad con el motor Batch. Esto no equivale a purgar imágenes del historial Git ni a una revisión legal de los demás recursos del proyecto.

Pruebas: Debug local, 0 errores / 79 advertencias. 13 comprobaciones con HTTP simulado: PNG, memoria, especie vacía, 404, desconexión, cooldown, reconexión, PNG inválido, tamaño y cancelación compartida; además marcador genérico incrustado y ausencia de carpeta Sprites. 45 comprobaciones Master Batch y 96 regresiones Batch aprobadas. No se automatizó el escritorio ni se mostró una ventana de la aplicación. No se realizó una prueba de conectividad real como requisito de funcionamiento.

Aplicación actualizada in situ: D:\Progra\Randomizer\DefinitiveRandomizer\DefinitiveRandomizer.exe. Se conservaron y comprobaron por SHA-256 los 25 archivos de templates y dependencias. App SHA256 644DD5B2A4B374D8BCED12FBBD06D2400F7B9F794F39EF1ECF1180BA48572D3B. Ejecutor Batch SHA256 5715089B3E04DFA0B445CC3ADC286F0E9F4C65F1BAE792217F4DE2C457EB7353. El paquete permanece sin binarios de pruebas ni PDB.
