# Iteración 36: formas, iniciales básicos y Force Totem Gen7

Se incorporan Allow Mega, Basic Starter y Force Totem al editor nativo y a la importación/exportación de Global Template usando los nombres originales CHK_AllowMega, CHK_BasicStarter y CHK_ForceTotem.

Basic Starter utiliza Legal.BasicStarters_7 exclusivamente para los primeros tres regalos. Force Totem reconoce las doce especies del código original y utiliza Legal.FinalEvolutions_7. Estos perfiles explícitos prevalecen sobre filtros de generación/BST; legendario por legendario conserva prioridad sobre Force Totem.

Las formas reproducen las reglas originales: exclusión Mega opcional, formas de Alola, Vivillon 30 y Minior 0–6. Se utiliza el generador de la vista previa, sin alterar el estado aleatorio global.

Pruebas sintéticas SM/USUM: selección de ambos perfiles con generaciones desactivadas, ida/vuelta Global, exclusión e inclusión Mega y formas especiales. Se ejecuta además la regresión completa Debug. Evidencias en ITERACION_36_PRUEBAS.

No se modifican ROMs reales ni los doce archivos protegidos, ni se realiza commit/push/publicación.

Paridad todavía parcial: falta asegurar ataques de iniciales y completar todos los restablecimientos y movimientos del algoritmo original, perfiles Gen6, módulos Global restantes y Batch de extremo a extremo con ROMs reales. Esta iteración no certifica estabilidad completa ni validación en juego.

Resultado local: Debug 0 errores, 79 advertencias existentes; 1.679 comprobaciones aprobadas. Integridad protegida: 12/12.
