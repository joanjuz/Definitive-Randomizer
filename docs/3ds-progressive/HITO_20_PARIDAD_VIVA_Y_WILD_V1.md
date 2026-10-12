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

### Seguridad y pruebas

- Regresiones JSON de importación/exportación y relectura de rangos.
- Rechazo de versión o juego equivocados, y de intervalos BST discontinuos.
- Prueba WPF de presencia de las opciones de template y formas Mega.
- Sin cambios al GARC hasta aprobar una propuesta de randomización.
- Las funciones heredadas de encuentros siguen pasando por fingerprint,
  vista previa, historial y relectura del GARC LZ11.

## Pendiente para cerrar Progressive Wild

1. Copy/Paste de tabla y de área con compatibilidad de tamaños y comprobación
   de todos los slots, sin modificar otras zonas.
2. Grillas con sprites y filas día/noche/SOS/clima plenamente comparables
   al editor SMWE original; filtros y previsualización visual completa.
3. Comparación control por control de filtros, reglas y defaults de Wild7Randomizer
   y ProgressiveWildRandomizer; evitar diferencias en tablas vacías e inaccesibles.
4. Importación/exportación de tablas de encuentro y reportes con fidelidad.
5. Pruebas reales de Ultra Sol, Ultra Luna, Sol y Luna (incluidas SOS meteorológicas,
   Minior/Vivillon/Alola y formas).
6. Pruebas de reproducción de varios randomizados con templates v1 y Roundtrip,
   incluidos proyectos que ya tienen CRO o LZ11 parcheados.

## Próximos módulos

Después de cerrar Wild: completar Static Encounter (incluido Gen6), entrenadores,
herramientas vanilla/Gen6, sincronización bidireccional Global Template,
estabilización WPF y validación de los ocho juegos conforme al roadmap principal.

**Regla de aceptación:** no declarar un módulo completo por compilar o por
reutilizar el motor original. No hacer merge a `Eris-Ultranova` sin validación
real y revisión de regresiones.
