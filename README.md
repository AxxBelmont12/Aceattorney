# Axx Belmont · Avatar Studio

Aplicación de Windows para crear videos con personajes animados de Apollo Justice, diálogos progresivos y voz propia, o usar los personajes en OBS.

## Instalar

Descarga **[Instalar-Axx-Belmont-Avatar-Studio.exe](https://github.com/AxxBelmont12/Aceattorney/releases/latest/download/Instalar-Axx-Belmont-Avatar-Studio.exe)** desde la última versión. Solo necesitas ese archivo. Requiere Windows de 64 bits; incluye el entorno .NET.

El instalador crea accesos directos y una entrada de desinstalación. Una copia portátil anterior debe migrarse usando el instalador una vez. Tus proyectos, biblioteca, ajustes y modelo de transcripción se mantienen en `%LOCALAPPDATA%\AxxBelmont\AvatarStudio`, separados de la instalación.

## Actualizar

Al abrir la aplicación se buscan versiones estables de GitHub Releases. Si hay una nueva, aparece un aviso con **Actualizar y reiniciar** o **Ahora no**. Al aceptar, la descarga, verificación, instalación y reinicio se realizan en una sola operación. El aviso espera si estás en el editor y no se repite durante esa sesión. También puedes buscar manualmente en **Actualizaciones** o desactivar los avisos al inicio.

Una nueva versión publicada genera un aviso; un commit por sí solo no cambia las instalaciones existentes.

## Funciones

- 27 personajes y 169 poses: habla, reposo y gestos, con atajos personalizables.
- Ventana de personaje para capturar en OBS, espejo, posición y velocidad.
- Editor de video con línea de tiempo, fragmentos, escenas, biblioteca y recuperación de versiones. Elimina con ×, Supr, clic derecho o el botón de la línea de tiempo; recupera con Ctrl+Z.
- Diálogo con escritura progresiva, sonidos y animaciones asignadas a palabras.
- Modo juicio, escenarios, estrados, fondos propios y elementos decorativos.
- Audio propio, recortes, forma de onda y sincronización de boca.
- Transcripción local con whisper.cpp: un diálogo completo o fragmentos sincronizados; permite revisar palabras y tiempos antes de aplicar.
- Exportación MOV/WebM con transparencia y MP4 con fondo; exportación por clips y escenas.

El modelo multilingüe de voz se descarga una vez (148 MB). La transcripción funciona en el equipo, sin enviar audios a un servidor ni utilizar una API de pago. La consulta de actualizaciones utiliza GitHub.

## Compilar

Necesitas Windows, .NET SDK 8 o posterior y PowerShell 7.

```powershell
./tools/fetch_avatar_assets.ps1
dotnet build avatar-app/AvatarStudio.csproj -c Release
```

Los recursos pesados están en la publicación fija `assets-v1`; `avatar-assets.json` fija y verifica el SHA256 del paquete y de cada archivo. No se guardan ejecutables grandes ni recursos binarios del juego en el historial Git. El paquete contiene únicamente los recursos utilizados por Avatar Studio; este repositorio no contiene los instaladores de traducción de la trilogía.

Para generar el instalador y comprobar personajes, diálogo, video y actualizaciones:

```powershell
./tools/release_avatar_studio.ps1
```

El resultado aparece en `dist/avatar-studio-VERSION-installer/`. El script prepara la versión fijada de Velopack automáticamente. Los archivos `.nupkg` y `releases.win.json` son necesarios para las actualizaciones, aunque el usuario solo necesita descargar el instalador.

## Publicar una versión nueva

1. Actualiza `<Version>` en `avatar-app/AvatarStudio.csproj` (por ejemplo, `0.14.1`).
2. Añade las novedades en `releases/v0.14.1.md` y actualiza la guía si corresponde.
3. Guarda y sube los cambios. Después crea y sube la etiqueta:

```powershell
git tag v0.14.1
git push origin main
git push origin v0.14.1
```

GitHub Actions compila, ejecuta las verificaciones y publica el instalador, el paquete de actualización, el índice y las sumas SHA256. Solo se publica si las verificaciones terminan correctamente. La etiqueta debe coincidir con la versión del proyecto. Las publicaciones de recursos `assets-*` son independientes y no se ofrecen como actualizaciones.

Si cambian los gráficos, sonidos o motores incluidos, publica un nuevo paquete de recursos con etiqueta propia y actualiza `avatar-assets.json` con su URL y sumas; no reemplaces el paquete fijo anterior.

## Créditos

Aplicación: Axx Belmont. Personajes, gráficos, sonidos y escenarios originales: Capcom y sus respectivos titulares. Herramientas de terceros: FFmpeg, whisper.cpp y Velopack. Sus licencias y referencias se distribuyen con la aplicación; véase `CREDITOS.txt`. Proyecto independiente, sin afiliación oficial.
