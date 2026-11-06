# Runtime de mentas USUM, fase 1

Adaptación de naturaleza real bajo GPLv3; [procedencia y obligaciones](../../../docs/third-party/USUM_NATURE_MINTS.md). No contiene Ability Patch ni otros handlers.

`mints.s` es la fuente de la plantilla ARM little endian. No es una copia de un CRO del juego. El generador resuelve etiquetas y ensambla cada instrucción en su dirección exacta para evitar que las discontinuidades `.org` alteren operandos numéricos B/BL de Keystone. La aplicación no necesita Python para instalar el parche.

```powershell
python -m pip install -r native/3ds/nature-mints/requirements.txt
python native/3ds/nature-mints/generate.py --check
# Omitir --check solo para regenerar la plantilla C# tras editar mints.s.
dotnet 3DS.Progressive.Tests/bin/Debug/net10.0-windows/3DS.Progressive.Tests.dll --usum-mints-arm-fixtures <carpeta-sintetica>
python native/3ds/nature-mints/verify_arm.py <carpeta-sintetica>
```

Disposición: recover `0x0`, dispatch `0x80`, message `0x100`, lookup `0x140`, apply `0x180`, literales `0x300`, tabla terminada en cero `0x340`, marcador `0x3C0`, digest `0x3D0`. El digest abarca todo Bag.cro salvo sus primeros 0x80 bytes de hashes y el propio campo digest. La detección verifica además los hashes CRO y los cuatro hooks; no adopta una firma aislada de otro mod.

`CroPatchSession` asigna espacio del segmento 0; los saltos locales permanecen relativos y los saltos al código original de Bag se reubican. No hay punteros absolutos internos al CRO ni necesidad de relocaciones nuevas para ellos. Los literales hacia `.code` son direcciones fijas del perfil acreditado. Ultra Luna ajusta únicamente las tres llamadas verificadas en su perfil; no se inspecciona una instrucción para adivinar la versión en tiempo de ejecución.

El uso escribe la naturaleza en el bloque A a través de las funciones nativas de acceso/cierre y solicita el refresco nativo con los selectores 1–5, conservando la ruta de bolsa y el control de consumo del juego. El harness sustituye esas dependencias nativas por callbacks controlados, comprueba pila, mapa, guardas, no efecto y ruta de retorno. No simula una partida real ni certifica el cifrado/checksum real del guardado; eso requiere la prueba del propietario.
