# Hito 09 — Entrenadores: organización y sprites (primera iteración)

## Objetivo de paridad

El estudio WPF debe exponer todas las opciones vanilla y Progressive sin reducir funcionalidades respecto a los formularios originales. La paridad se comprueba por controles visibles, parámetros persistidos, propuestas, resultado binario y validación en juego. Un motor reutilizado no implica por sí solo paridad de interfaz.

## Cambios realizados

- `TrainerLogicHost.OptionsCore` publica reglas individuales de Level Caps y movimientos para el entrenador seleccionado aunque no haya reglas guardadas en el template. Las entradas originales siguen en su JSON y los límites de validación no se modifican.
- El randomizador agrupa opciones por tarea en cinco pestañas: Pokémon/equipos, niveles y límites, movimientos/reglas, IA/objetos/combates y BST progresivo. Se conserva cada `EditorField.Key` para captura y aplicación.
- Se añade una vista visual del equipo seleccionado con hasta seis tarjetas y acceso directo a los campos de cada Pokémon.
- Los 1.184 sprites PNG vanilla se empaquetan desde los recursos originales en `Sprites/`. Se respetan primero los PNG personalizados y las formas, y después el sprite original local; caché y descarga quedan como último recurso.
- El historial, alcance, generación de vista previa, template de entrenadores y archivos de ROM mantienen los servicios anteriores.

## Verificación automatizada

- Las pruebas UI verifican pestañas, exposición de opciones `Rule.*.CapUse` y `Rule.*.MoveUse`, existencia de sprites locales y tarjetas visuales del equipo.
- Se ejecuta compilación/regresiones Windows mediante `.github/workflows/3ds-progressive-ci.yml`.
- Si CI falla, esta etapa sigue pendiente y no debe marcarse como validada.

## Pendiente de cerrar antes de paridad completa

1. Comparar parámetro por parámetro Gen6/Gen7 con los diálogos originales, incluyendo edición simultánea de reglas para múltiples entrenadores, reglas por historia y defaults.
2. Pulir distribución y densidad de opciones a partir de capturas reales del ejecutable.
3. Verificar manualmente XY, ORAS, SM y USUM (también aplicando templates y verificando resultados).
4. Integrar vistas equivalentes de Encuentros, incluida randomización completa Wild/Progressive Wild, con sprites.
5. No modificar Evoluciones ni el Historial fuera de un arreglo concreto comprobado.

**Estado:** primera iteración de Entrenadores; no implica equivalencia completa ni autoriza merge a `Eris-Ultranova`.
