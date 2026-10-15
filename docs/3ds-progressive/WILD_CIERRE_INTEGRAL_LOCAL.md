# Progressive Wild — parche integral de cierre (local)

Branch: `feature/3ds-progressive-ui`. No Actions, no commits automaticos.

## Incluido

- Seleccion de especies distinta de la original, incluso en SOS (parche 001).
- Generaciones 1–7, legendarios, eventos, Mega/batalla y formas de Alola opcionales.
- Opcion de diversidad por fila: intenta evitar especies repetidas, con fallback
  cuando el pool es menor que el numero de ranuras disponibles.
- Similar BST con tolerancia configurable y fallback al BST mas cercano opcional.
- BST Progressive, niveles por area y fallback en tablas vacias/internas.
- Proteccion opcional de tablas inaccesibles y tablas marcador Pikipek.
- Niveles sin modificar si el checkbox correspondiente esta desactivado.
- SOS normal, copiar normales, aliados aleatorios y clima respetando modo.
- Templates Wild JSON v1: import/export y flags adicionales opcionales,
  compatibles con versiones originales que ignoran propiedades no reconocidas.
- Dividir/unir rangos Progressive BST desde WPF sin editar JSON manualmente.
- Diagnostico previo de especies, rangos, tablas y flags, sin escritura de ROM.
- Copiar/Pegar por tabla y area, inspector dia/noche con sprites y reporte TXT
  (incorporados en entregas previas).
- Regresiones sinteticas para filtros, fallback, JSON, SOS y rangos.

## Pendiente de validar para cerrar el hito

1. `dotnet build` y `dotnet run` de pruebas en Windows con .NET 10.
2. Revisar todos los filtros y los cuatro modos SOS en Sol/Luna y Ultra Sol/Ultra Luna.
3. Validar en juego las formas de Alola, Minior, Vivillon, especies raras,
   clima, niveles y progresion en tablas internas.
4. Reconstruir ROM y asegurar que los archivos de otras areas son identicos.
5. Conservar compatibilidad de plantillas viejas y no tocar Eris-Ultranova.

El parche se entrega como CANDIDATO DE CIERRE, no como validacion runtime.
