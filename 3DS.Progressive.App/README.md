# 3DS Progressive — preview funcional v0.1.1

Nueva aplicación WPF independiente dentro de pk3DS Progressive. No se modifica pk3DS.WinForms ni pk3DS.Core.

## Funcionalidades del primer editor
- Abre RomFS y ExeFS de X/Y, ORAS, S/M y USUM mediante pk3DS.Core.
- Lista y busca especies y formas.
- Edita seis estadísticas base, dos tipos y ratio de captura; muestra BST en tiempo real.
- Guarda el GARC personal con confirmación, comprobación de integridad y detección de cambios externos.
- Tema Dark Graphite completo, ComboBox oscuros, cabecera con nombre, sprite, tipos y estado de edición.
- Controles de otros módulos deshabilitados hasta que se implementen realmente.

**Validación manual reportada por el usuario:** modificó una stat y comprobó el valor cambiado con pk3DS Progressive. Esta evidencia valida ese flujo concreto, no todas las funciones de Studio.

## Sprites y personalización
Se busca por prioridad:
1. Sprite específico de entrada: %LOCALAPPDATA%\3DS Progressive\sprites\custom\entry-001.png
2. Sprite de especie: %LOCALAPPDATA%\3DS Progressive\sprites\custom\species-001.png
3. Nombre numérico: 001.png o 1.png en la misma carpeta custom.
4. Caché: %LOCALAPPDATA%\3DS Progressive\sprites\cache\001.png
5. Descarga opcional desde PokeAPI/sprites en raw.githubusercontent.com.

Las formas alternativas sin PNG propio muestran el sprite de la especie base. Sin conexión ni archivos locales se ve un placeholder. Los PNG no se almacenan en el repositorio ni se incorporan al ejecutable. El artwork puede estar protegido por derechos de terceros.

## Prueba en Windows
Utilizá el artefacto de GitHub Actions para Windows x64 o ejecutá con SDK .NET 10:

    git fetch origin
    git switch feature/3ds-progressive-ui
    git pull --ff-only
    dotnet run --project .\3DS.Progressive.App\3DS.Progressive.App.csproj -c Release

IMPORTANTE: trabajá sobre una copia del juego extraído. Studio no hace backups automáticos y no crea ROMs .3ds directamente.

## Validación pendiente
- Confirmar ausencia de fondo blanco y visibilidad correcta del desplegable de tipos.
- Comprobar sprites online, offline, personalizados y cambio rápido de especie.
- Repetir guardado de stat y verificación con pk3DS Progressive usando la preview 0.1.1.
- Comparar integridad de GARC en copias y probar formas alternativas / otras versiones.
- Migrar las demás opciones del editor Personal y resto de herramientas.
