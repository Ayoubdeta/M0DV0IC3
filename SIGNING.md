# Firma de código

Windows avisa con **«Windows protegió su PC»** (SmartScreen) al abrir un programa descargado que no tiene firma digital, o que todavía tiene pocas descargas, aunque sea completamente seguro. La firma digital dice quién ha publicado el programa y garantiza que nadie lo ha modificado. La forma de quitar el aviso es firmar las versiones con un certificado de firma de código.

## Opción recomendada: SignPath Foundation (gratis para open source)

[SignPath Foundation](https://signpath.org/) firma gratis proyectos de código abierto. El certificado está a nombre de la fundación y lo usan muchos proyectos, así que ya tiene reputación en SmartScreen.

1. **Solicita el alta** en <https://signpath.org/> (sección *Apply*) con la dirección de este repositorio. Piden, entre otras cosas:
   - una licencia de código abierto aprobada por la OSI (este proyecto es MIT);
   - que las versiones se compilen a partir del código público en un sistema de integración continua (lo hace `.github/workflows/release.yml`);
   - que el proyecto no contenga malware ni software no deseado.
2. Cuando te aprueben, en SignPath:
   - conecta este repositorio de GitHub como *trusted build system*;
   - crea la configuración de artefacto copiando [`.signpath/artifact-configuration.xml`](.signpath/artifact-configuration.xml);
   - crea un usuario de CI y su **API token**.
3. En GitHub, en *Settings → Secrets and variables → Actions*:
   - **Secreto** `SIGNPATH_API_TOKEN`: el token del paso anterior.
   - **Variables** `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` y `SIGNPATH_SIGNING_POLICY_SLUG`: los datos de tu proyecto en SignPath.
4. Publica una versión nueva (`git tag v1.0.1` y `git push origin v1.0.1`). El workflow de releases detecta el secreto y firma `M0DV0IC3.exe` y las DLL propias antes de crear el ZIP.
5. Añade al README la *code signing policy* que te pida SignPath. Suele ser un texto como «Free code signing provided by SignPath.io, certificate by SignPath Foundation», más quién aprueba las versiones.

Comprueba en la documentación de SignPath los nombres exactos de los slugs y si hay pasos nuevos.

## Alternativas de pago

- **Certum «Open Source Code Signing»**: certificado a tu nombre para proyectos de código abierto, unos 70 € al año, con verificación de identidad.
- **Azure Trusted Signing**: unos 10 $ al mes. Comprueba si admiten particulares de tu país.

Con un certificado nuevo, SmartScreen puede seguir avisando unas semanas, hasta que el certificado gane reputación con las descargas.

## Si un antivirus marca la descarga

Es un falso positivo. Pasa a veces con programas nuevos que capturan audio o usan atajos de teclado globales. Para que lo corrijan:

- **Microsoft Defender**: envía el archivo como *Software developer → Incorrectly detected as malware* en <https://www.microsoft.com/wdsi/filesubmission>.
- **Otros antivirus**: mira en [VirusTotal](https://www.virustotal.com/) cuáles lo detectan y usa el formulario de falsos positivos de cada uno.
