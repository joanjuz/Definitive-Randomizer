# Definitive Randomizer 1.0.0-rc.1 — candidata privada

Esta candidata WPF para Windows x64 prepara la primera versión estable. **No es una versión estable publicada.** No hay tag ni release pública de esta candidata.

Base: main con PR #56, #57 y #58 integrados. El editor de eventos DS del PR #55 queda excluido. Commit exacto compilado: `@BUILD_COMMIT@` (se resuelve únicamente al generar el paquete).

Incluye las funciones 3DS, DS Gen4/Gen5 y Espada/Escudo 1.3.2 ya aceptadas, las mentas USUM y la documentación de compatibilidad. Esta preparación solo cambia versión, pie de ventana y empaquetado; no modifica motores. La línea WinForms mantiene su versión propia.

## Instalación y comprobación

Extrae el ZIP completo en una carpeta nueva. Abre `DefinitiveRandomizer.exe`; el runtime .NET x64 está incluido. Conserva LegacyBatch, Tools, Templates, custom_balance_templates, catch_zone_patches, dependencias y licencias juntos. VERSION.txt y BUILD_PROVENANCE.json identifican la compilación. SHA256SUMS.txt verifica el contenido; el hash del ZIP se entrega fuera del archivo. Los ejecutables no tienen firma digital.

## Alcance y límites

Se reutiliza la aceptación técnica previa. No se repiten randomizaciones completas ni se atribuyen nuevas pruebas dentro del juego. Ultra Luna no tiene confirmación jugable de mentas; la ROM específica del tester sigue pendiente. Continúan las incidencias documentadas de pétalos y movimiento Z en USUM. DS romhack sigue en beta y requiere perfiles compatibles. Switch no acredita consola real, Atmosphère, DLC ni protección tardía en dobles con un solo Pokémon utilizable. Deshacer transaccional se limita a 3DS y a los últimos ocho cambios.

El paquete no contiene ROMs, partidas, extracciones ni recetas privadas. Trabaja con copias aisladas y respalda los originales. Consulta la documentación incluida en `docs` para compatibilidad, recuperación y primeros pasos. VALIDATION_REPORT.md distingue las comprobaciones de empaquetado de la prueba de instalación; el informe privado de entrega registra el ZIP exacto y el resultado final.
