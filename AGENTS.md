<!-- BEGIN CODEX-AUTO-SKILLS -->
# Codex — reglas permanentes de pk3DS Progressive

## Selección automática de skills y complementos

En cada solicitud, identifica primero el área afectada y utiliza por iniciativa propia las skills o herramientas especializadas **que realmente estén disponibles**. El usuario no necesita invocarlas con `$`. Elige solo las pertinentes: no cargues todas por costumbre ni instales herramientas sin necesidad.

- **Protección de cambios del randomizer**: para cambios en C#, ROM, datos binarios, templates, Global Template, Batch, historial, rebuild, parches o código con riesgo de regresión, aplicar `pk3ds-progressive-safety` desde `.agents/skills/pk3ds-progressive-safety/SKILL.md`.
- **WPF/XAML**: para `3DS.Progressive.App` (vistas, estilos, bindings, accesibilidad, `Dispatcher`, DPI y navegación), aplicar `wpf`.
- **Auditoría/rediseño visual**: aplicar `redesign-existing-projects` junto a `wpf` cuando se soliciten cambios de distribución, jerarquía visual o UX. Adaptar criterios al entorno nativo Windows; no reemplazar WPF o WinForms por web.
- **C# y refactorización**: utilizar las skills relevantes del complemento `dotnet`, como especialistas en C# y refactorización.
- **WinForms**: para `pk3DS.WinForms`, utilizar las skills de `dotnet-winforms`; no emplearlas como sustituto de WPF.
- **Pruebas**: al crear, revisar o ejecutar comprobaciones, utilizar las skills pertinentes de `dotnet-test`, respetando el runner propio del repositorio.
- **Compilaciones y proyectos**: utilizar especialistas de `dotnet-msbuild` para `.csproj`, `pk3DS.slnx`, referencias, binlogs y errores de compilación.
- **Rendimiento/diagnóstico**: utilizar `dotnet-diag` solo cuando se investiguen cuellos de botella, trazas, memoria o fallos de ejecución.

Si una skill no aparece en la sesión, continúa con la mejor alternativa disponible y comunícalo; no declares que la ejecutaste si no fue así. Para tareas de lectura o documentación sencillas, no invoques herramientas innecesarias.

## Reglas de seguridad y compatibilidad

- Prioridad al estado **local actual**; GitHub y documentos históricos son referencias, no autorización para revertir cambios recientes.
- Antes de editar, inspeccionar `git status` y los diffs relevantes. No ejecutar `git reset`, `git clean`, `git checkout --`, `git restore`, `stash`, rebase o cambios destructivos sobre trabajo ajeno sin permiso explícito.
- Preservar las funcionalidades vanilla y las extensiones Progressive, con la matriz específica para X/Y, OR/AS, Sol/Luna y UltraSol/UltraLuna; no asumir paridad de funciones donde no corresponde.
- No alterar formatos binarios, offsets, índices, firmas, `RomFS`, `ExeFS`, `GARC`, `CRO/CRR`, hashes o datos de otras entradas sin pruebas pertinentes. No modificar las ROM/extracciones originales: usar copias aisladas.
- Para Global Template, Batch e historial, preservar esquemas antiguos, nodos desconocidos, parámetros completos, orden de operaciones, recursos `.assets`, SHA-256, replay manual y semilla cuando aplique. No simplificar formatos.
- Un cambio exclusivamente visual no debe modificar motores, randomización, servicios de ROM ni contratos de templates. Preservar nombres de controles, comandos, eventos y bindings.
- No hacer commits, push, merges, releases ni publicar builds sin solicitud expresa.

## Comprobaciones y entrega

- `3DS.Progressive.Tests` es **un ejecutable con runner propio**, no asumir que `dotnet test` ejecuta sus suites. Revisar `3DS.Progressive.Tests/Program.cs` para elegir flags adecuados.
- Para WPF considerar `--visual-redesign-only`, suites relacionadas con la vista modificada y la validación real de foco, contraste y DPI cuando el entorno lo permita.
- Revisar `docs/3ds-progressive/AUDITORIA_VISUAL_WPF.md` cuando se cambie el rediseño, y los documentos del módulo afectado cuando se editen motores o templates.
- Compilar y ejecutar pruebas proporcionales al cambio. Diferenciar pruebas sintéticas, renderizado fuera de pantalla, integración con copias reales y verificación dentro del juego; ninguna sustituye automáticamente a las demás.
- Al terminar, indicar skills especializadas aplicadas, archivos modificados, pruebas realmente ejecutadas, fallos y limitaciones restantes. Si solo se pidió análisis, no modificar archivos.
<!-- END CODEX-AUTO-SKILLS -->
