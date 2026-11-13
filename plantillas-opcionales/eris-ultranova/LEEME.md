# Balance Eris-Ultranova (opcional)

Este paquete contiene el balance de movimientos y estadísticas creado para el romhack **Eris-Ultranova**. Es una personalización de ese proyecto, no una configuración recomendada para todas las partidas.

**Definitive Randomizer nunca lo aplica por defecto.** Ningún randomizador, opción genérica, Global Template nuevo ni Batch lo carga si no lo eliges tú.

## Cómo usarlo

- **Movimientos → Balance y templates:** pulsa «Cargar CSV…» y elige `moves_gen7.csv` (Ultra Sol/Ultra Luna) o `moves_gen6.csv` (X/Y, Rubí Omega/Zafiro Alfa). Para instalar también las mecánicas de combate, marca «Aplicar también las mecánicas de combate del CSV».
- **Template Studio → Importar…:** abre cualquiera de estos CSV para revisarlo o editarlo antes de aplicarlo. Si el template contiene mecánicas de combate, la aplicación te las enumera y te pide confirmación.
- **Pokémon → Modificar Pokémon:** marca «Aplicar estadísticas desde un CSV que elijas» y carga `pokemon_stats_gen7.csv`.

Las recetas guardadas con versiones anteriores que usaban el antiguo «Balance Progressive incluido» siguen reproduciéndose igual: la aplicación localiza estos archivos por su SHA-256 y lo indica en pantalla.

## Archivos

| Archivo | Juegos | Contenido |
| --- | --- | --- |
| `moves_gen7.csv` | Ultra Sol / Ultra Luna | 57 movimientos. 11 filas instalan además mecánicas de combate. |
| `moves_gen6.csv` | X/Y, Rubí Omega/Zafiro Alfa | 32 movimientos: potencia, precisión, PP y efectos. Escudo Real también instala su mecánica de −1 Ataque si activas las mecánicas. |
| `pokemon_stats_gen7.csv` | Sol/Luna, Ultra Sol/Ultra Luna | Slaking: 130/130/100/90/65/85. |

## Mecánicas de combate de `moves_gen7.csv`

Estas filas cambian cómo funciona el movimiento en combate (`Battle.cro`), no solo sus datos. Solo se instalan si las activas expresamente. Los parches siguen disponibles por separado en cualquier CSV propio mediante la columna `BattlePatch`.

| Movimiento | Parche | Efecto |
| --- | --- | --- |
| Escudo Real (588) | `Gen7KingShieldMinusOne` | 5 PP; baja el Ataque solo 1 nivel. |
| Hidrochorro (346) | `Gen7WaterSportFullBlock` | Inhabilita los ataques de tipo Fuego. |
| Chapoteo Lodo (300) | `Gen7MudSportFullBlock` | Inhabilita los ataques de tipo Eléctrico. |
| Conjuro (381) | `Gen7LuckyChantCritBoost` | Además aumenta la probabilidad de crítico del equipo. |
| Deseo (273) | `Gen7WishPivot` | Cambia de Pokémon al usarlo y mantiene la curación diferida. |
| Pantalla de Humo (108) | `Gen7SmokescreenV82` | Protección sin prioridad y baja la Precisión. |
| Neblina (54) | `Gen7MistOwnerActiveV5762` | Prioridad +3; dura hasta que el usuario sale del campo. |
| Meditación (96) | `Gen7MeditateV73` | Permite elegir objetivo, cura estados y sube el Ataque. |
| Giro Vil (693) | `Gen7BrutalSwingSelfConfusionV1` | 30 de potencia, 2–5 golpes a ambos rivales; al terminar confunde al usuario. |
| Cerrojo Feérico (587) | `Gen7FairyLockAllFairyV11` | Convierte en tipo Hada puro a todos los Pokémon activos. |
| Pesadilla (171) | `Gen7NightmareSleepV76` | Despierto: duerme; dormido: pierde el 25 % de PS por turno. |

## Cambios de datos de `moves_gen7.csv`

Además: precisión de Espora (80 %) y Somnífero (70 %); 5 PP para Protección, Detección, Barrera Espinosa, Búnker y los movimientos de curación propia; Corte de tipo Planta; Vuelo y Buceo con 130 de potencia y retroceso; Destello de tipo Eléctrico a ambos rivales; ajustes de Geocontrol, Día de Pago, Anulación, Aullido, Fuerza, Golpe Roca, Beso Drenaje, Megaagotar, Gigadrenado, Shuriken de Agua, Afilagarras, Avivar, Fortaleza, Confidencia, Camaradería, Piñón Auxiliar, Camuflaje Z, Triataque, Profecía, Polvo Explosivo, Cara Susto, Explosión (Especial), Autodestrucción, Fertilizante, Pulpocañón y Bote. Cada fila describe su cambio en la columna `Notes`.

Los CSV no contienen recursos de terceros ni datos de partidas: solo identificadores de movimiento y valores numéricos.
