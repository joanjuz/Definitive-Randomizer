# Hito 06 — Inspector y templates (v0.6.0)

## Interfaz

Botones visibles en el inspector: Pokémon (Randomizar / Modificar) y Movimientos (Randomizar / Metrónomo / Balance y templates). Sustituyen temporalmente el centro sin quitar el catálogo ni el inspector. Volver al editor conserva las opciones de cada acción durante la sesión y descarta la propuesta. Generar, aplicar y copiar permanecen en un pie fijo.

Semillas nuevas automáticas en cada vista previa; sin entrada manual. Defaults de randomizar Pokémon según las capturas del 11/10/2026. Exclusión de Shadow Tag/Arena Trap integrada en el randomizador. Los controles inactivos usan fondo #32232D, borde #553542 y texto #A58090. Selectores respetan DisplayMemberPath y su plantilla; los records de catálogo tienen un nombre legible como alternativa. Se retiraron los avisos grandes de funciones pendientes y se corrigió la conservación de IDs Z custom fuera del catálogo en el formulario de Pokémon.

## Paridad de templates

- Schema y aliases de `CustomBalanceTemplates`, transformaciones de `MoveEditor6/7` y defaults originales.
- CSV propio o incluido; validación numérica, rangos, tokens, flags, entradas y alcance.
- Stats reemplazados al aplicar, sin acumular slots en aplicaciones posteriores.
- Flags conocidos y bits no tocados preservados; efectos Z separados del efecto normal.
- Mecánicas originales portadas: Escudo Real, Water/Mud Sport, Deseo, Meditación/Pantalla de Humo, Conjuro, Neblina, Cerrojo Feérico, Pesadilla y Giro Vil.
- Algoritmos ARM originales iguales a f459e40 tras normalizar solo namespace y raíces de IO. Giro Vil conserva el upgrade v2 de multigolpe por objetivo y autoconfusión posmovimiento.
- Ningún fallback busca otro juego: todo el IO de los patchers se redirige al directorio temporal.
- Firmas compatibles y segunda pasada idempotente obligatorias. Un parche fallido cancela toda la propuesta; no se guardan descripciones de mecánicas rechazadas.
- Descripciones españolas en banco de idioma 6. Escapes de salto de línea canónicos de TextFile; se serializa solo MoveFlavor y se conservan los demás archivos internos.
- Template de estadísticas Pokémon Gen7 dentro de Modificar; incluye Slaking según el CSV actual. Campos vacíos conservan valores, stats 1–255, names/IDs del catálogo Personal real.

## Escritura

Vistas previas congeladas por servicio, GARC y dependencias (CSV, CRO y textos). Los archivos son preparados y releídos antes de reemplazar. Los fallos de IO y de verificación revierten desde memoria los reemplazos completados; no hay backups automáticos. No se promete atomicidad ante apagado/corte del proceso entre archivos.

No cambia Core, WinForms ni la rama estable. Las mecánicas conservan las limitaciones por ROM/versión de los algoritmos originales; un Battle.cro incompatible se rechaza. La opción de datos solamente permite balance sin instalar mecánicas ni actualizar sus descripciones.

## Verificación

356 comprobaciones de servicios con datos sintéticos XY, ORAS, SM y USUM. WPF en Windows comprueba defaults de las capturas, RNG automático, opciones recordadas, navegación temporal, controles vino, labels y preservación de IDs Z custom. Se guardan renders de ambas vistas y paneles en el artefacto de comprobaciones UI del build.

Los checks anteriores incluyen paridad byte a byte del randomizador Core, maestros Personal, guardados individuales, scope, copias inmutables, stale y cross-session. Nuevos checks cubren CSV incluido/custom, defaults Gen6, Gen7 Z, stats/flags, idempotencia, CRO por firma, textos españoles, dependencia CSV y rollback tras primer/último reemplazo.

## Prueba manual pendiente

1. Abrir la copia de Ultra Sol usada en las validaciones previas.
2. Pulsar Randomizar Pokémon: revisar checks y generar dos propuestas; deben tener semillas distintas. Volver y reabrir: las opciones se conservan.
3. Revisar generación de MO/tutores/SOS en XY, ORAS, SM y USUM.
4. Abrir Modificar y aplicar el template de estadísticas a Slaking tras revisar el alcance.
5. Abrir Movimientos → Balance y templates. Revisar los campos y los archivos adicionales. Probar primero en una copia compatible, confirmar y reabrir el proyecto.
6. Validar en juego las mecánicas recién portadas, las descripciones y Giro Vil por objetivo.
7. Confirmar botones y selectores a 100% y 125% de escala.

## Pendientes de migración

Entrenadores y randomizadores Progressive/Smart, Encuentros, asignaciones MT/tutores, evoluciones, economía/tiendas, automatización, edición de historia/textos y otras herramientas. La matriz de paridad continúa siendo obligatoria; esta preview aún no sustituye todos los módulos vanilla y Progressive.
