# Caramelos Raros personalizados — rutina ARM (Rubí Omega)

Adaptación bajo GPLv3 de un parche de alvarocm21; [procedencia y licencia](../../../docs/third-party/RARE_CANDY_MULTIUSE_OR.md). Documento del módulo: [CARAMELOS_RAROS_PERSONALIZADOS_OR.md](../../../docs/3ds-progressive/CARAMELOS_RAROS_PERSONALIZADOS_OR.md).

`candy.s` es la fuente de los dos bloques de la rutina, con sus direcciones virtuales exactas en `.code`. No es una copia de un archivo del juego. El generador resuelve las etiquetas y ensambla cada instrucción en su dirección; la aplicación no necesita Python para instalar.

```powershell
python -m pip install -r native/3ds/rare-candy-multiuse/requirements.txt
python native/3ds/rare-candy-multiuse/generate.py --check
# Sin --check regenera pk3DS.Core/Modding/Research/RareCandyMultiUsePayload.cs tras editar candy.s.
# Solo en desarrollo, con la referencia privada descomprimida:
python native/3ds/rare-candy-multiuse/generate.py --check --reference <carpeta>/000400000011C400/exefs/code.ips
```

Disposición: bloque A en `0x579690` (despacho del selector `0x579690`, aplicar el siguiente `0x579714`, literal de estado `0x5797D4`); bloque B en `0x579878` (inicio de «Usar» `0x579878`, bucle de aplicación `0x579910`, desvío de aprendizaje `0x579A00`, literal `0x579A20`). El estado ocupa `0x6AEFF0–0x6AEFFB`, en la cola sin uso de la última página de `.bss`.

`r12` lleva siempre la dirección del enlace en `DllBag.cro` + 8; las funciones del CRO se alcanzan relativas a `r12`, así que la dirección de carga del módulo no importa. Las llamadas a `code.bin` son direcciones fijas del perfil acreditado, y el instalador comprueba la huella de cada función llamada.
