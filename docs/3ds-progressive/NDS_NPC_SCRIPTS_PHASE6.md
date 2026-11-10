# Nintendo DS — NPC con comportamiento (scripts) y entrenadores: auditoría y primera entrega

**Platino USA CPUE/1.** Esta fase permite copiar una **Enfermera Joy con su comportamiento** a otro mapa y **compartir** el script de un NPC de mensaje simple dentro del mismo archivo de scripts. Los entrenadores nuevos se diseñan aquí y se implementarán en un PR aparte. **Nada de esto está validado dentro del juego** hasta que el propietario lo confirme en el emulador.

Fuentes: [pret/pokeplatinum](https://github.com/pret/pokeplatinum) (`src/script_manager.c`, `include/constants/field/script_ranges.h`, `asm/macros/scrcmd.inc`, `res/field/scripts/common.s`, `res/field/maps/*/scripts.s`, `src/overlay006/healing_machine_animation/pokecenter.c`, `generated/vars_flags.txt`, `include/global/assert.h`, `src/error_handling.c`) y la copia privada (solo lectura). DSPRE (AGPL‑3.0) se consultó como referencia de comportamiento; no se copió código.

## 1. Cómo funcionan scripts, textos y entrenadores en Platino

### Números de script de un NPC (campo +10 del registro)

| Rango | Qué ejecuta | Banco de textos |
|---|---|---|
| 1…N | entrada N del **archivo de scripts del mapa** (`scr_seq`, índice en la cabecera +4) | el del mapa |
| 2000+ | **scripts comunes** (`scripts_common`, entrada = número − 2000) | común |
| 2500, 2800 | carteles comunes, árboles de bayas | propios |
| **3000–4999** | **combate individual contra el entrenador** `número − 2999` | mensajes de entrenador |
| 5000–6999 | combate doble | mensajes de entrenador |
| 7000, 8000 | objetos visibles, ocultos | propios |
| 9000+ | Centro Pokémon 2F/B1F, guardería, movimientos de campo… | propios |

Formato de un archivo de scripts: tabla de u32 (cada valor relativo al final de su propia entrada) terminada en `0xFD13`, y después el código: opcodes u16 con parámetros de tamaño fijo. Los saltos internos son **relativos**, así que el bloque de código puede desplazarse entero. En CPUE/1, **553 de los 1124** miembros de `scr_seq` no son tablas de entradas (son scripts de nivel o de inicio). El lector los detecta y no los trata como archivos de scripts de NPC.

### Datos reales de la copia privada

| Hecho | Valor |
|---|---|
| NPC con script local | 1485 (18 **enfermeras**, 614 **mensajes simples**, 853 otros) |
| NPC con script común | 1075 (590 con 10001, 118 árboles de bayas…) |
| NPC entrenadores | 407 de combate individual (3000+), 10 dobles |
| `trdata.narc` / `trpoke.narc` | 928 entrenadores |
| Flags de entrenador derrotado | `TRAINER_DEFEATED_FLAGS_START` + ID; 929 flags, **204 de entrenadores DUMMY** sin uso |

### La enfermera

Las 18 enfermeras del juego usan **exactamente** el mismo script local de 12 bytes:

```text
SetVar VAR_0x8007, <ID local de la enfermera>   28 00 07 80 LL LL
CallCommonScript 2002                            14 00 D2 07
End                                              02 00
```

`CommonScript_PokecenterNurse` (común 2002) saluda, pregunta, gira a la enfermera hacia la máquina con `ApplyMovement VAR_0x8007`, reproduce la animación de la máquina y ejecuta **`HealParty`**. Usa textos del **banco común** y ninguno del mapa.

**Dependencias:** script común 2002 (global, se conserva), variable 0x8007 = ID local de la enfermera (**se reasigna** al NPC nuevo) y sin textos ni flags locales. **Riesgo identificado:** la animación busca el modelo de la máquina de curación entre las estructuras cargadas. Si no la encuentra (fuera de un Centro Pokémon), el código solo ejecuta `GF_ASSERT(FALSE)`, que en el juego comercial no detiene nada salvo en comunicación inalámbrica, y **no reproduce la animación**. El script continúa con `HealParty`. Es lo esperado según el código fuente, pero **hay que comprobarlo en el emulador**.

### Entrenadores

Un entrenador es un NPC de **tipo 1** con **visión** y **script 3000 + ID − 1**. El script común de combates obtiene el equipo de `trdata`/`trpoke` y los textos (presentación, derrota) de la tabla de mensajes de entrenador (`trtbl`/`trtblofs` y su banco). El estado de derrota es el flag `TRAINER_DEFEATED_FLAGS_START + ID`. **Añadir un registro binario no basta**: hacen falta datos de entrenador, textos y un flag de derrota propio.

## 2. Viabilidad de copiar una Enfermera Joy funcional

**Viable con el script acreditado.** El editor añade **al final** del archivo de scripts del mapa destino una copia de esos 12 bytes con el **ID local del NPC nuevo**, y el NPC usa ese número. Los números existentes no cambian. Los demás miembros de `scr_seq` y todos los demás archivos quedan idénticos byte a byte. La sonda real lo comprobó (enfermera de Jubileo → Pueblo Hojaverde): exportación, reapertura del checkpoint v3, restauración y SHA‑256 de la ROM restaurada idéntico al de la fuente. Falta tu prueba en el juego (curar en Hojaverde).

## 3. Modalidades

| Modalidad | Cuándo | Qué se escribe |
|---|---|---|
| **Solo apariencia** | siempre | NPC con script 0 y flag 0 (fase 5A, sin cambios) |
| **Con comportamiento** | el script del origen es el de la enfermera **y** se refiere al propio NPC; el destino tiene un archivo de scripts legible | script local nuevo + NPC (una operación atómica) |
| **Compartir script del mapa** | el script es un **mensaje simple** (sin ID local, flag ni variable) **y** el destino usa el **mismo archivo de scripts** | solo el NPC (usa el mismo número) |
| Rechazada, con motivo | combate (3000+), objeto (7000+), común/global, script local no reconocido, script de otro NPC, mensaje de otro mapa (copiar textos no está implementado) | nada |

El formulario «NPC nuevo» muestra el **comportamiento** (preseleccionado: comportamiento > compartir > apariencia), las opciones no disponibles **desactivadas con su motivo** y la lista de dependencias. El inspector indica el comportamiento de cada NPC y si se puede copiar.

## 4. Arquitectura de dependencias

```text
NPC (registro de eventos) ── script (número) ──┬─ local: archivo de scripts del mapa ── código (opcodes)
                                                │      ├─ referencias a ID locales (p. ej. VAR_0x8007)
                                                │      ├─ textos del banco del mapa (Message n)
                                                │      ├─ flags / variables
                                                │      └─ llamadas a scripts comunes
                                                └─ global: común 2000+, combate 3000+ (→ datos del entrenador, textos de entrenador, flag de derrota)
```

- **Reconocimiento exacto, no un parser universal** (`PlatinumScriptFile`): tabla de entradas, patrón «enfermera» y patrón «mensaje simple». Todo lo demás es **desconocido** y se rechaza.
- **Operación atómica:** una adición de NPC puede llevar un **enlace de script** (`AppendScript` con el código, o `ShareScript` con el número). La reproducción **vuelve a comprobar** el patrón, el archivo, el número y el ID antes de escribir; un enlace no acreditado invalida el checkpoint.
- **Checkpoint v3** = v2 + enlaces de script. Una sesión sin scripts sigue escribiendo v1 o v2. Las versiones anteriores rechazan un v3 (no lo aplican a medias). Los v1/v2 se siguen leyendo.
- **Exportación:** eventos y, si cambiaron, `scr_seq`. Si otro módulo DS también escribió `scr_seq`, se **combinan** los miembros distintos y se **rechaza** si ambos tocaron el mismo archivo. La verificación posterior comprueba lo añadido por el editor.
- **Restaurar** una adición con script quita ambos (reproducción sin esa operación). Eliminar un NPC añadido deja su script local sin uso (inofensivo); «Restaurar» es la vía exacta.

## 5. Entrenadores nuevos (siguiente PR, diseño)

1. **Reutilizar ranuras DUMMY** (204 IDs cuyo flag de derrota no usa el juego), nunca ampliar `trdata` hasta acreditar el tamaño de la zona de flags. Antes, comprobar que **ningún** NPC ni script referencia esa ranura (combates 3000/5000 y comandos de combate).
2. Escribir **datos y equipo** con el editor de entrenadores DS existente (clase, nombre, Pokémon, niveles, movimientos, objetos).
3. **Textos** de presentación y derrota: tabla `trtbl`/`trtblofs` y banco de mensajes de entrenador; requiere escritura de texto Gen IV acreditada.
4. NPC de **tipo 1**, visión configurable y **script 3000 + ID − 1** (o 5000 para dobles), en una sola operación atómica de checkpoint (v4 o enlace nuevo) junto con los datos del entrenador.
5. Aceptación en el juego: el entrenador ve, combate, se gana, queda derrotado y **otros entrenadores no cambian**.

## 6. Pruebas

- `--nds-npc-script-only` (38): tabla de scripts, patrones exactos y casi iguales, ampliación sin alterar el código existente, archivos compartidos, opciones y motivos, enlace con el ID reasignado, checkpoint v3, exportación (solo eventos y el archivo de scripts del mapa), combinación con otros módulos, rechazo de enlaces no acreditados, restauración exacta.
- `--nds-npc-script-ui-only` (12): inspector, pegado de la enfermera en otro mapa con comportamiento preseleccionado y dependencias, modos desactivados con motivo, duplicado de un NPC de mensaje (compartido), restauración.
- `--nds-npc-script-probe <carpeta nueva> <ROM privada>`: recorrido real (18 enfermeras reconocidas; enfermera de Jubileo → Hojaverde; exportar; reabrir; restaurar; SHA‑256 idéntico).
- `--nds-live-ui-probe` (local, interactiva): añade el flujo de la enfermera con ratón y teclado reales y **deja una ROM exportada** en la carpeta de salida para el emulador.
- Regresión: `--nds-only`, `--nds-ui-only`, eventos, arrastre, estructurales, terreno, lienzo, layout, `--visual-redesign-only`.

## 7. Prueba en Windows (propietario)

1. Compila la aplicación (Release) y abre una **copia** de la ROM CPUE/1. Nintendo DS → Eventos → modo Mapa.
2. Busca **C01PC0101** (Centro Pokémon de Jubileo), haz clic en la enfermera. El inspector debe decir «enfermera del Centro Pokémon… Copiar con comportamiento: disponible». Pulsa **Copiar** (o Ctrl+C).
3. Busca **T01** (Pueblo Hojaverde), pulsa **Pegar copia** y haz clic en una casilla libre (ni roja ni ocupada).
4. En el formulario, «Comportamiento» debe ser **Con comportamiento (enfermera)**. Revisa las dependencias y pulsa **Previsualizar propuesta**, luego **Guardar cambios…** (checkpoint nuevo) y **Exportar ROM…**.
5. En el emulador, con un equipo herido o debilitado, habla con la enfermera en Hojaverde: debe preguntar y **curar**. Es probable que no haya animación de la máquina. Comprueba otros NPC del pueblo y del Centro de Jubileo.
6. En el editor, **Restaurar último cambio…**, guarda otro checkpoint, exporta y comprueba que desaparece.
7. Opcional: abre un checkpoint antiguo (v1/v2) y verifica que sigue cargando.

Informa del resultado. Hasta entonces la función no se declara validada en juego.

## 8. Siguientes PR

| PR | Contenido |
|---|---|
| Entrenadores 1 | lector de ranuras DUMMY y referencias, datos/equipo con el editor existente, NPC tipo 1 + script de combate en una operación atómica; textos de entrenador |
| Textos | escritura de mensajes Gen IV para copiar NPC de mensaje entre mapas y diálogos de entrenador |
| Scripts 2 | más patrones acreditados (tiendas, NPC que entregan objetos con flag propio) con remapeo de flags |
| #67 | integración de la capa de estructuras |
