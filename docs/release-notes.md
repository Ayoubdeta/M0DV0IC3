## Descargar

1. Descarga **{ZIP}** (aquí abajo, en *Assets*).
2. Clic derecho en el ZIP → **Extraer todo…**, y abre **M0DV0IC3.exe** dentro de la carpeta.
3. Para usar tu voz en Discord y juegos, instala también [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (gratis): la app te guía.

Funciona en Windows 10 (versión 2004 o posterior) y Windows 11 de 64 bits. No hace falta instalar .NET.

## ¿Sale «Windows protegió su PC»?

Es el filtro SmartScreen de Windows. Sale con los programas que no tienen firma digital, como este, o que aún tienen pocas descargas, aunque sean completamente seguros.

- **Para que no salga:** antes de descomprimir, clic derecho en el ZIP → **Propiedades** → marca **Desbloquear** → **Aceptar**.
- **Si ya ha salido:** pulsa **Más información → Ejecutar de todas formas**. Solo pasa la primera vez.

Este ZIP lo ha compilado GitHub directamente desde el código fuente público de este repositorio, sin pasar por ningún ordenador personal. Puedes comprobarlo:

- **Huella SHA-256** de {ZIP}: `{SHA256}`
  En PowerShell: `Get-FileHash .\{ZIP}` debe dar la misma.
- **Atestación de procedencia** (prueba firmada de que sale de este repositorio y de este commit), con [GitHub CLI](https://cli.github.com/):
  `gh attestation verify {ZIP} --repo {REPO}`

La app solo se conecta a internet en el modo karaoke, para buscar la letra en lrclib.net (envía el artista, el título y la duración de la canción). El texto a voz usa las voces de Windows, sin internet, y las grabaciones se quedan en tu PC. No pide permisos de administrador y solo usa tu micrófono y, si lo activas, el sonido de la app que elijas para «Música por el micro» o el karaoke.
