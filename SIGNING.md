# Firma de código

Windows avisa con **«Windows protegió su PC»** (SmartScreen) al abrir un programa descargado que no tiene firma digital, o que todavía tiene pocas descargas, aunque sea completamente seguro. La firma digital dice quién ha publicado el programa y garantiza que nadie lo ha modificado. La forma de quitar el aviso es firmar las versiones con un certificado de firma de código.

## Opción recomendada: SignPath Foundation (gratis para open source)

[SignPath Foundation](https://signpath.org/) firma gratis proyectos de código abierto. El certificado está a nombre de la fundación y lo usan muchos proyectos, así que ya tiene reputación en SmartScreen.

### Condiciones ([términos](https://signpath.org/terms))

| Condición | En este proyecto |
|---|---|
| Licencia de código abierto aprobada por la OSI, sin doble licencia comercial, en todos los componentes | ✅ MIT, y todas las dependencias son open source ([THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)) |
| Sin malware, sin programas no deseados y sin herramientas de hacking | ✅ |
| Sin código propietario (las bibliotecas del sistema se permiten) | ✅ |
| Mantenido activamente | ✅ |
| Ya publicado en la forma que se quiere firmar | ✅ el ZIP de [Releases](https://github.com/Ayoubdeta/M0DV0IC3/releases) |
| La página de descarga describe lo que hace | ✅ README y notas de cada versión |
| **Autenticación en dos pasos** en GitHub y en SignPath para todo el equipo | ⬜ actívala en GitHub (*Settings → Password and authentication*) |
| Roles definidos: *committers*, *reviewers* y *approvers* | ⬜ en la solicitud: Ayoub en los tres |
| Sección **«Code signing policy»** en la página principal y en las de descarga | ✅ [README](README.md#code-signing-policy) y notas de cada versión |
| Cada versión se aprueba a mano antes de firmarla | ⬜ lo haces tú en SignPath (el workflow espera hasta una hora) |

### Pasos

1. **Activa la autenticación en dos pasos** en tu cuenta de GitHub (<https://github.com/settings/security>).
2. **Solicita el alta** en <https://signpath.org/apply> con la dirección de este repositorio (`https://github.com/Ayoubdeta/M0DV0IC3`). La política de firma ya está en el [README](README.md#code-signing-policy).
3. Cuando te aprueben, en SignPath:
   - añade a tu organización el *Trusted Build System* predefinido **GitHub.com** y enlázalo con el proyecto;
   - opcional: instala la *SignPath GitHub App* en este repositorio (permite políticas más estrictas);
   - crea la configuración de artefacto copiando [`.signpath/artifact-configuration.xml`](.signpath/artifact-configuration.xml);
   - crea una política de firma (*signing policy*) que exija tu aprobación manual;
   - crea un usuario de CI con permiso para enviar solicitudes de firma, y su **API token**.
4. En GitHub, en *Settings → Secrets and variables → Actions*:
   - **Secreto** `SIGNPATH_API_TOKEN`: el token del paso anterior.
   - **Variables** `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` y `SIGNPATH_SIGNING_POLICY_SLUG`: los datos de tu proyecto en SignPath.
5. Publica una versión nueva (`git tag v1.0.1` y `git push origin v1.0.1`). El workflow de releases detecta el secreto y envía `M0DV0IC3.exe` y las DLL propias a SignPath. **Aprueba la solicitud en SignPath** (tienes una hora) y el ZIP saldrá firmado.

Cuando las versiones salgan firmadas, quita del README la línea de *Estado: firma solicitada*.

## Alternativas de pago

- **Certum «Open Source Code Signing»**: certificado a tu nombre para proyectos de código abierto, unos 70 € al año, con verificación de identidad.
- **Azure Trusted Signing**: unos 10 $ al mes. Comprueba si admiten particulares de tu país.

Con un certificado nuevo, SmartScreen puede seguir avisando unas semanas, hasta que el certificado gane reputación con las descargas.

## Si un antivirus marca la descarga

Es un falso positivo. Pasa a veces con programas nuevos que capturan audio o usan atajos de teclado globales. Para que lo corrijan:

- **Microsoft Defender**: envía el archivo como *Software developer → Incorrectly detected as malware* en <https://www.microsoft.com/wdsi/filesubmission>.
- **Otros antivirus**: mira en [VirusTotal](https://www.virustotal.com/) cuáles lo detectan y usa el formulario de falsos positivos de cada uno.
