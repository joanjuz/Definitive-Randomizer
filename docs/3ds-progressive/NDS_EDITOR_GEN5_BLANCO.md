# Editor DS Gen5: Pokémon Blanco (IRAS/0)

El editor de mapas y eventos de Nintendo DS se abre con **Pokémon Blanco, edición española, revisión 0 (IRAS/0)**. Usa el mismo lector Gen5 de Negro ([NDS_EDITOR_GEN5_NEGRO.md](NDS_EDITOR_GEN5_NEGRO.md)) con un perfil propio, `Gen5MapProfile.White`. No hay un segundo motor: los datos de mapa de Blanco tienen el mismo formato, y además son los mismos bytes.

## Verificación independiente en la copia real

Volcado del propietario: SHA-1 `4cc963ca…`, título `POKEMON W`, DSi-enhanced, 484 archivos (los mismos que Negro).

| Recurso | Ruta | Blanco frente a Negro |
|---|---|---|
| Cabeceras de mapa | `a/0/1/2` | idéntico (CRC32 `0xEC1CB686`) |
| Matrices | `a/0/0/9` | idéntico (`0x65AC968E`) |
| Bloques (terreno, permisos, listas de edificios) | `a/0/0/8` | idéntico (`0x308E1CC6`) |
| Eventos | `a/1/2/5` | idéntico (`0x6B00C87E`); la misma huella `314F7AD2…` |
| Texturas del terreno | `a/0/1/4` | idéntico (`0x0AA75AEC`) |
| Tabla de áreas con variantes de estación | `a/0/1/0` | idéntico |
| Scripts | `a/0/5/7` | **propio** (`0x7638D5C9`, 899 miembros) |
| Textos (nombres de lugar: texto 89, 117 entradas) | `a/0/0/2`, `a/0/0/3` | **propios**, en español: «Pueblo Arcilla», «Ruta 1», «Pueblo Terracota»… |
| Modelos de edificios | `a/2/2/6`, `a/2/2/9` | distintos (no los usa el editor) |

En total, 238 archivos difieren de Negro: sobre todo overlays, textos, scripts y gráficos que no pertenecen a la capa de mapas.

**Censo:** 427 mapas, 2.257 NPC (276 Poké Ball), 886 warps (880 resuelven a un warp existente y 6 son especiales) y 156 registros bloqueados (raíles y warps especiales). 764 de los 772 bloques comprobados enlazan sus texturas con su cabecera, igual que en Negro.

Las reglas de Negro se aplican a Blanco porque los datos son los mismos, no por analogía. Lo propio de Blanco es su identidad (código, nombre y CRC de sus scripts), que se comprueba al abrir.

## Qué está disponible

Lo mismo que en Negro: mover y girar NPC de casilla, orientación de entrenadores (con «mirar fijo»), mover objetos de fondo, cambiar warps a destinos existentes y mover o redimensionar activadores. También checkpoints (`IRAS`/`0`), proyecto, exportación y restauración exacta.

Siguen deshabilitados, con su motivo en la interfaz:
- la visión;
- la dirección de interacción;
- los registros de raíl y los warps especiales;
- las Poké Ball;
- NPC nuevos, scripts y enfermeras.

Un checkpoint de Negro no se acepta en Blanco ni al revés, aunque los eventos sean iguales: cada uno pertenece a su ROM.

## Comprobaciones

- `--nds-gen5-white-only`: 14 comprobaciones sintéticas. Cubren la identidad del perfil, que el resto de valores son los de Negro, las mismas lecturas, escrituras y bloqueos, la negativa con tablas no acreditadas, que los checkpoints no se cruzan, la exportación, la restauración y el detector.
- `--nds-gen5-white-ui-only`: 3 comprobaciones fuera de pantalla.
- Las dos están en el CI.
- `--nds-gen5-white-probe <carpeta nueva> <Blanco> <Negro>`: compara las tablas con una copia de Negro y comprueba el censo, el terreno y los gráficos. Después hace cuatro ediciones distintas de las de Negro:
  - la puerta de la casa vecina en el sentido contrario;
  - un entrenador de la Ruta 1 mirando a la derecha;
  - un NPC de Pueblo Terracota mirando abajo;
  - un objeto oculto de la Ruta 3.

  Rechaza mover una Poké Ball y crear NPC; amplía y restaura un activador. Guarda, reabre, exporta, reabre la ROM exportada, compara el ARM9 y el resto de archivos, y restaura con el mismo SHA-256. Escribe `MANUAL.txt`.
- Galería: 5 mapas de Blanco con 0 triángulos sin textura; Pueblo Arcilla se dibuja idéntico al de Negro.

**Validación jugable pendiente.**

## Limitaciones

Las mismas de Negro: estructuras sin dibujar, estaciones sin aplicar, colisión inferida, raíles, visión y dirección de interacción sin interpretar.
