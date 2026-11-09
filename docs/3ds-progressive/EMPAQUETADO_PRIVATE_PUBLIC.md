# Empaquetado PRIVATE y PUBLIC

Este mecanismo prepara archivos locales y sus metadatos. No crea tags, no sube ZIPs ni publica GitHub Releases. v1.0.0 continúa sin publicar durante esta preparación. No cambia los motores 3DS, DS o Switch.

## PRIVATE, predeterminado

Sin indicar un modo, `tools/Publish-Preview.ps1 -OutputDirectory <carpeta-nueva>` genera una entrega **PRIVATE**, incluso con versión 1.0.0. No exige confirmación de publicación para una compilación local de prueba. La procedencia contiene `distributionMode=PRIVATE`, `publicRelease=false` y `publicReleaseCommit=null`; las notas identifican una entrega privada todavía no publicada.

Las protecciones de SemVer, árbol limpio, versión del proyecto, recursos, dependencias, licencias, contenido permitido, hashes y no sobrescritura siguen vigentes. `VERSION.txt`, notas y procedencia resuelven el commit real al empaquetar, sin SHA autorreferencial en archivos versionados.

## PUBLIC, solo con autorización futura

Después de una autorización expresa del propietario, desde el HEAD real de main limpio:

```powershell
$publicCommit = (git rev-parse HEAD).Trim()
./tools/Publish-Preview.ps1 -OutputDirectory <otra-carpeta-nueva> -Version 1.0.0 -DistributionMode PUBLIC -PublicReleaseCommit $publicCommit
```

Se requieren **ambas opciones**: modo PUBLIC y confirmación del SHA completo de 40 caracteres del HEAD compilado. Un SHA ausente, abreviado, incorrecto o de otro commit se rechaza antes de invocar dotnet. La versión estable no activa PUBLIC por sí sola. En PRIVATE se rechaza una confirmación PUBLIC sobrante para evitar una petición contradictoria.

La procedencia PUBLIC contiene `publicRelease=true`, `distributionMode=PUBLIC` y `publicReleaseCommit` igual al commit compilado. Las notas describen una distribución pública y conservan contenido, límites y evidencias, sin afirmar que ese paquete sea una candidata privada. Esta selección indica el destino de distribución: no acredita que ya se haya creado una release en GitHub ni reemplaza la autorización del propietario.

Las notas de la versión deben incluir los marcadores `@DISTRIBUTION_STATUS@`, `@DISTRIBUTION_METADATA@` y `@DISTRIBUTION_WORKFLOW@`. El empaquetador los resuelve junto a `@BUILD_COMMIT@`. PUBLIC rechaza notas antiguas sin esos marcadores para no conservar avisos privados contradictorios; PRIVATE sigue admitiendo notas históricas, añadiendo su estado privado al principio.

## Validación y publicación posterior

`tools/Test-PublishPreview.ps1` comprueba la función real de selección/renderizado con datos simulados. Extrae únicamente esa función mediante AST: nunca ejecuta el cuerpo de Publish-Preview.ps1, dotnet publish ni el modo PUBLIC real. Las pruebas PUBLIC quedan en memoria, sin generar una distribución pública. El CI específico usa el mismo comando y no repite las suites funcionales de juegos.

La validación del empaquetador no publica nada. Después de integrar estos cambios, una futura distribución autorizada debe construirse de nuevo desde el HEAD real de main, verificar notas/procedencia/hashes, extraerse y probar su ejecutable exacto. Se conservan las limitaciones de compatibilidad y pruebas pendientes descritas en las notas y guías. No se convierte un ZIP privado existente en público cambiando su nombre o editando su manifiesto.

El ZIP privado previamente validado y su informe permanecen intactos. Su `publicRelease=false` describe correctamente aquella entrega; esta preparación no los modifica.
