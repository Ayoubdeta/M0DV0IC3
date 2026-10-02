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
| Sección **«Code signing policy»** en la web del proyecto | ⬜ cuando te aprueben (texto de abajo) |

### Pasos

1. **Activa la autenticación en dos pasos** en tu cuenta de GitHub.
2. **Solicita el alta** en <https://signpath.org/apply> con la dirección de este repositorio (`https://github.com/Ayoubdeta/M0DV0IC3`).
3. Cuando te aprueben, en SignPath:
   - conecta este repositorio de GitHub como *trusted build system*;
   - crea la configuración de artefacto copiando [`.signpath/artifact-configuration.xml`](.signpath/artifact-configuration.xml);
   - crea un usuario de CI y su **API token**.
4. En GitHub, en *Settings → Secrets and variables → Actions*:
   - **Secreto** `SIGNPATH_API_TOKEN`: el token del paso anterior.
   - **Variables** `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` y `SIGNPATH_SIGNING_POLICY_SLUG`: los datos de tu proyecto en SignPath.
5. Añade al README la sección de abajo.
6. Publica una versión nueva (`git tag v1.0.1` y `git push origin v1.0.1`). El workflow de releases detecta el secreto y firma `M0DV0IC3.exe` y las DLL propias antes de crear el ZIP. Tendrás que aprobar cada firma en SignPath.

### Texto para el README (cuando te aprueben)

```markdown
## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- Committers and reviewers: [Ayoub](https://github.com/Ayoubdeta)
- Approvers: [Ayoub](https://github.com/Ayoubdeta)

Privacy policy: This program will not transfer any information to other networked systems unless specifically requested by the user.
```

Comprueba en la documentación de SignPath los nombres exactos de los slugs y si hay pasos nuevos.

## Alternativas de pago

- **Certum «Open Source Code Signing»**: certificado a tu nombre para proyectos de código abierto, unos 70 € al año, con verificación de identidad.
- **Azure Trusted Signing**: unos 10 $ al mes. Comprueba si admiten particulares de tu país.

Con un certificado nuevo, SmartScreen puede seguir avisando unas semanas, hasta que el certificado gane reputación con las descargas.

## Si un antivirus marca la descarga

Es un falso positivo. Pasa a veces con programas nuevos que capturan audio o usan atajos de teclado globales. Para que lo corrijan:

- **Microsoft Defender**: envía el archivo como *Software developer → Incorrectly detected as malware* en <https://www.microsoft.com/wdsi/filesubmission>.
- **Otros antivirus**: mira en [VirusTotal](https://www.virustotal.com/) cuáles lo detectan y usa el formulario de falsos positivos de cada uno.
