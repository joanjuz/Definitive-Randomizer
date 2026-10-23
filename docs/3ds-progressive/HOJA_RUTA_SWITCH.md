# Hoja de ruta: acercar Espada/Escudo a las herramientas de 3DS

Objetivo acordado: cubrir progresivamente la mayoría de módulos del editor/randomizer 3DS, con exportación de mods. No significa que cualquier formato legible sea ya editable ni que cada parche 3DS tenga equivalente Switch. Mantener versiones y capacidades explícitas por juego.

| Módulo | Estado actual | Siguiente trabajo y criterio de aceptación |
|---|---|---|
| Identificación, datos personales y exportación | Prototipo implementado, carga de un cambio de tipo en Espada confirmada por usuario | Probar Escudo limpio y compatibilidad de versión/actualización; verificar cada salida por juego |
| Tipos y habilidades | Implementados con semilla, alcance, protección y propuesta; mod de prueba en Espada confirmado funcional por el usuario | Ampliar cobertura de habilidades/formas/combates, validar Escudo; nombres localizados y configuración de pool más avanzada |
| Estadísticas, captura, objetos equipados y EV yield | Estadísticas/captura editables manualmente; mezcla de estadísticas conservando BST implementada; resto pendiente | Randomización de estadísticas, opciones BST y conservación de distribuciones; campos adicionales con límites y máscaras sin alterar datos opacos |
| Catálogos y textos | Nombres de especies, objetos y entrenadores leídos del juego; quedan IDs en otros campos | Leer los recursos del juego para nombres de especies/formas, habilidades, movimientos y objetos; validar idioma y caracteres de control |
| Movimientos | Pendiente | Esquemas/serialización de los recursos SWSH, edición y randomización de propiedades, conservación de miembros desconocidos y prueba de combate |
| Aprendizajes por nivel, huevo y evoluciones | Pendiente | Lectores específicos, tablas completas y guardado sin pérdida; verificar formas, rangos y referencias antes de herencia de tipos/habilidades |
| MT/TR y tutores | Pendiente | Separar listas de movimientos, compatibilidad y tutores/DLC; no trasladar límites ni offsets de Gen7 |
| Entrenadores | Primera randomización de equipos implementada; pendiente de prueba jugable | Datos de entrenadores/equipos, formatos binarios y condiciones; después randomización, ajustes de nivel/BST y recorrido de combates |
| Encuentros salvajes, estáticos, regalos e intercambios | Encuentros normales visibles/ocultos implementados; los demás pendientes | Validar contenedores, tablas base/DLC, clima, área, formas y slots; no modificar otros miembros del archivo |
| Objetos, Field Items, tiendas y economía | Field Items visibles/ocultos de categorías seguras implementados; tiendas/economía pendientes | Inventario de fuentes y formatos propios; editor y propuesta antes de randomización. Compras únicas de USUM requieren otra investigación, no copiar el parche CRO |
| Historial, Global Template y Batch | Historial de sesión y manifiesto de mod implementados; recetas/lotes pendientes | Contratos por plataforma, validación de recursos/semilla, replay y outputs aislados; mantener compatibilidad de recetas 3DS |
| Mecánicas, caps, shiny y otros parches | Investigación futura | Solo perfiles de ejecutables comprobados, Build ID y offsets propios; pruebas jugables para cada parche |
| Otros juegos de Switch | Sin soporte editado en esta rama | Adaptadores separados después de consolidar Espada/Escudo; no declarar compatibilidad por compartir plataforma |

## Prioridad confirmada por el usuario

Primero consolidar **Espada**. Escudo se analizará después con la extracción y pruebas que proporcione el usuario. Compartir un lector o tener fixtures de ambas identidades no acredita compatibilidad jugable de Escudo. El usuario confirmó entrenadores randomizados en Espada; no se registraron IDs ni un recorrido exhaustivo.

La corrección de objetos y la investigación de encuentros sorpresa están en [SWITCH_ESPADA_CORRECCIONES.md](SWITCH_ESPADA_CORRECCIONES.md). «Visible distinto de combate» es un requisito nuevo de ejecución, no una función completada por editar tablas de encuentros.

## Orden recomendado

1. Prioridad acordada: consolidar entrenadores, encuentros y Field Items con pruebas jugables; primera implementación documentada en [SWITCH_SWSH_PRIORIDADES.md](SWITCH_SWSH_PRIORIDADES.md).
2. Completar catálogos, configuración de pools/BST y datos personales sin bloquear esos tres módulos.
3. Aprendizajes y evoluciones; después movimientos, MT/TR y tutores con formatos comprobados.
4. Objetos, economía y tiendas.
5. Global Template y lotes de mods una vez estabilizados los contratos.
6. Parches de ejecutable como trabajos independientes.

Cada entrega debe incluir lectura real, ida y vuelta sin cambios, diff limitado a campos elegidos, source hashes conservados, semillas reproducibles cuando aplique, exportación aislada, casos de rechazo y prueba dentro del juego. Compartir controles WPF y conceptos de historial; no forzar servicios 3DS a interpretar formatos Switch ni cambiar masivamente la arquitectura actual para un prototipo.

No se ha determinado aún la viabilidad completa de todos los módulos. Las referencias de investigación son pkNX y sus mapeos/estructuras; los recursos reales y las pruebas de cada escritor son la fuente para declarar capacidades. La tabla es una planificación, no una lista de funciones ya disponibles.

## Actualización: prioridad a entrenadores, encuentros y Field Items

La validación y los límites de la entrega nueva están en [SWITCH_SWSH_PRIORIDADES.md](SWITCH_SWSH_PRIORIDADES.md). Las cifras de la entrega anterior se conservan como registro histórico. Los módulos nuevos no tienen todavía confirmación jugable del usuario y no se ha probado una extracción real de Escudo.

## Combate sorpresa en Espada: desarrollo en curso

Se implementaron núcleo, adaptador, módulo NSO propio e integración WPF de previsualización, historial y exportación Ryujinx para Espada 1.3.2. Existe un mod de diagnóstico exportado; su carga y jugabilidad todavía requieren validación real. No se declara Escudo compatible. Ver [integración y protocolo](SWITCH_ESPADA_COMBATE_SORPRESA_INTEGRACION.md).


## Runtime PokeFeur integrado · 22/10/2026

El backend publicado y validado por el usuario sustituye al prototipo propio en la navegación de Espada. La aplicación ya genera modos/pools de encuentros, level caps, EXP, opciones de comodidad y los registros/scripts opcionales; incluye recetas, historial, exportación e instalación explícita de configuración SD. Escudo y consola continúan pendientes. Solo el combate sorpresa/captura/menús del runtime tiene confirmación jugable previa; las funciones adicionales requieren prueba propia. Ver [informe de integración](POKEFEUR_RUNTIME_INTEGRACION.md). Las secciones anteriores de investigación del módulo propio son históricas.


## Actualización de navegación, semillas y editores pkNX · 22/10/2026

El acceso Switch se integra en la ventana principal, se eliminan semillas fijas y se detecta la configuración SD anterior. Se incorporan diez áreas de edición de datos. El alcance, pruebas y pendientes están en [SWITCH_PKNX_INTEGRACION_Y_SEMILLAS.md](SWITCH_PKNX_INTEGRACION_Y_SEMILLAS.md); las instrucciones anteriores del diálogo de prototipo son históricas.
