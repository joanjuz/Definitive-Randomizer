# Hito 20 — Matriz de paridad viva y Progressive Wild v1

**Rama:** `feature/3ds-progressive-ui`, sin merge a `Eris-Ultranova`.

## Objetivo del hito

No volver a contar como pendientes editores ya implementados. La fuente de seguimiento
es `PARIDAD_ACCIONES.csv`, ahora ampliada con las herramientas vanilla y Progressive
que faltaban del inventario. Cada acción se clasifica individualmente, con referencia
al código, evidencia y una validación pendiente explícita.

### Semántica de los estados

- **Implementado:** existe una operación WPF que hace trabajo real sobre archivos
  o código y está protegida por controles/preview adecuados. **No significa**
  validación en hardware ni equivalencia exhaustiva.
- **Parcial:** módulo real con funciones todavía ausentes, criterios no demostrados
  o cobertura incompleta para algunos de los ocho juegos.
- **Pendiente:** no existe un flujo WPF equivalente, aunque pk3DS original sí
  contenga el algoritmo.
- **Validado en juego:** solo puede asignarse tras una prueba documentada en
  la ROM concreta. La compilación, un test sintético o abrir una pestaña no bastan.

El inventario antiguo combinaba estados de principios de la migración con estados
actuales. Se revisaron explícitamente Global Template / Batch ROM Builder, Static,
Tutores, Tiendas, Caps, paneles Wild, entrenador y reglas. Se añadieron entradas
para Gen6, herramientas ROM, gráficos, scripts, módulos de batalla y calidad final.
El catálogo será todavía auditado **menú por menú y variante por variante** antes
de concluir que es exhaustivo.

## Progressive Wild — avance de este hito

### Templates originales

Se incorpora `IOptionTemplateProjectEditor` en `GameEncounterService`
con un DTO de `WildRandomizerTemplateFile` v1, sin arrastrar la interfaz WinForms:

- Importar JSON v1 por **Cargar template Wild** desde Encuentros Gen7.
- Aplicar generación, legendarios, eventos, formas Mega, política SOS, slot mode,
  modificación de niveles y rangos Progressive BST (1–100).
- Exportar de nuevo con **Exportar template Wild** sin perder propiedades
  JSON desconocidas de nivel superior.
- Validar versión, juego SM/USUM, 0–3 para slot mode, niveles y rangos BST
  contiguos. Una importación incorrecta no reemplaza la configuración previa.
- Guardar las opciones de randomización al navegar entre pestañas, del mismo modo
  que ya se hace con el template de entrenadores.
- No ofrecer el formato Wild Gen7 en el editor de encuentros Gen6.

**Alcance de compatibilidad:** se reproducen los campos principales del esquema
v1 de SMWE. El motor de randomización WPF no es aún paridad completa con el
`ProgressiveWildRandomizer` original, por lo que esta implementación no cierra
todo el Hito 1.

### Formas

El randomizador Gen7 ya puede asignar formas no exclusivas de batalla, Alola,
Vivillon y Minior. Se añade la opción de permitir formas Mega/batalla y se
utiliza el `Random` de la propuesta para que cada preview conserve sus resultados.
Antes se escribía siempre forma 0 en las nuevas especies.

#### Copiar y pegar tablas de encuentros Gen7

El editor ahora incluye «Copiar tabla actual», «Pegar en tabla seleccionada»
y «Pegar en área · mismas ranuras». La copia conserva las especies, formas,
niveles, tasas y slots SOS/clima del día o noche seleccionados. «Pegar en área»
filtra las tablas del **mapa de destino** por la cantidad de encuentros
normales ocupados, como el B_PasteAll original. «Pegar en tabla» aplica a un
destino individual y solicita confirmación.

La vista previa usa las estructuras existentes, preserva bits desconocidos
y evita copiar datos desde otro proyecto o versión. El cambio se registra en
el historial mediante EditorProposal, sin escribir durante la preparación.

## Seguridad y pruebas

- Regresiones JSON de importación/exportación y relectura de rangos.
- Regresiones de copiar/pegar día/noche, aislando áreas ajenas y proyectos incompatibles.
- Rechazo de versión o juego equivocados, y de intervalos BST discontinuos.
- Prueba WPF de presencia de las opciones de template y formas Mega.
- Sin cambios al GARC hasta aprobar una propuesta de randomización.
- Las funciones heredadas de encuentros siguen pasando por fingerprint,
  vista previa, historial y relectura del GARC LZ11.

## Progressive Wild — inspección completa de filas y fidelidad SOS

El inspector de encuentros WPF ahora presenta un selector explícito para las
**9 filas Gen7** (normal, SOS 1–7, clima) y muestra sprites de las especies y
formas de la fila elegida. En la misma pantalla aparece su tabla de día/noche
emparejada, con navegación a los campos de edición de la otra franja horaria.
La selección inicial sigue siendo «Normales» para preservar hábitos anteriores.

Comparación de código contra `EncounterTable.CopySlotsToSOS`,
`Wild7Randomizer` y `ProgressiveWildRandomizer`:

- **Normal → SOS**: copia diez especies/formas a cada una de las siete filas
  ordinarias, incluso a espacios previamente vacíos; la fila de clima no cambia.
- **Todos pueden pedir aliados**: rellena únicamente los espacios ordinarios SOS
  vacíos cuyo encuentro normal está ocupado, mediante especies nuevas del pool
  permitido. Nunca sobrescribe aliados existentes ni coloca Pokémon nuevos en
  clima por esta opción.
- **Probabilidades**: el editor rechaza guardados con suma diferente de 0%
  o 100%, al igual que SMWE original.
- **Exportar reporte de encuentros**: volcado legible de cada área/tabla,
  niveles, probabilidades, especies, formas y todas las filas normales/SOS/clima
  en TXT UTF-8 sin modificar archivos del proyecto.

Pruebas automatizadas: salida por grupo (10 normales o 6 meteorológicos),
vista día/noche emparejada, copia de slots previamente vacíos, aliados
independientes, preservación de clima, tasa inválida rechazada e informe completo.
**Aún falta** confirmar estas opciones in-game para SM, US, UM y combinaciones
progresivas con tablas inaccesibles.

## Pendiente para cerrar Progressive Wild

1. Validar Copy/Paste de tabla y área, ahora implementado, contra SMWE
   en ROMs reales y con tablas de tamaños/slots heterogéneos.
2. Inspector de 9 filas con sprites y día/noche implementado; ajustar ergonomía
   de edición masiva, filtros y comparaciones visuales con SMWE original.
3. Comparación control por control de filtros, reglas y defaults de Wild7Randomizer
   y ProgressiveWildRandomizer; evitar diferencias en tablas vacías e inaccesibles.
4. Importación/exportación de tablas de encuentro y reportes con fidelidad.
5. Pruebas reales de Ultra Sol, Ultra Luna, Sol y Luna (incluidas SOS meteorológicas,
   Minior/Vivillon/Alola y formas).
6. Pruebas de reproducción de varios randomizados con templates v1 y Roundtrip,
   incluidos proyectos que ya tienen CRO o LZ11 parcheados.

## Progressive Wild — niveles y tablas especiales (fase siguiente)

Al comparar el algoritmo WPF con `ProgressiveWildRandomizer` original se
identificaron dos diferencias de comportamiento que podían producir resultados
inconsistentes incluso con un template Wild válido.

1. **Cálculo del tier de BST**: una tabla real usa su máximo nivel original.
   Si es una tabla interna sin niveles, sin especies regulares o compuesta
   únicamente por Pikipek (ID 731), obtiene el nivel de referencia del área.
   Este fallback considera exclusivamente tablas utilizables y el diccionario
   `Gen7SlotDumper.InaccessibleUnused_SM/USUM` combinado con las zonas
   asociadas al mapa; si no hay ninguna utilizable, recurre al máximo nivel
   de todas las tablas. Los niveles de referencia se congelan antes de
   randomizar el alcance seleccionado, de modo que el escalado de tablas
   anteriores no cambie la progresión de las posteriores.
2. **Activación del escalado**: la opción «Modificar niveles» forma parte
   explícita de los controles WPF. Un template con `modifyLevel=false`
   ya no aumenta ni disminuye niveles aunque conserve un multiplicador
   avanzado diferente de 100 %. Importar/exportar respeta el valor original;
   el valor predeterminado de la interfaz sigue habilitado para conservar
   el comportamiento de las versiones preview previas.

Se añaden regresiones de tier normal, tabla Pikipek, tabla vacía, límites
1–100, desactivación de niveles e importación/exportación JSON sin pérdida
de la casilla. Ninguno de estos cambios se considera validado dentro del
juego hasta las pruebas separadas de Sol/Luna y Ultra Sol/Ultra Luna.

## Próximos módulos

Después de cerrar Wild: completar Static Encounter (incluido Gen6), entrenadores,
herramientas vanilla/Gen6, sincronización bidireccional Global Template,
estabilización WPF y validación de los ocho juegos conforme al roadmap principal.

**Regla de aceptación:** no declarar un módulo completo por compilar o por
reutilizar el motor original. No hacer merge a `Eris-Ultranova` sin validación
real y revisión de regresiones.
