# Cazador YTM 🎵

![Version](https://img.shields.io/github/v/release/RHcomunications/CazadorYTM?label=version&color=blue)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6.svg)
![WCAG](https://img.shields.io/badge/accessibility-WCAG%202.2%20AA-green.svg)
![Fluent Design](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Fluent%20Mica-blueviolet.svg)

> **Cazador YTM** es un gestor de descargas multimedia, reproductor de audio integrado y centro de exploración de **YouTube Music** para Windows. Combina la potencia de `yt-dlp` con una interfaz **WinUI 3 / Fluent Design (Mica)** y accesibilidad de primer nivel.

Desarrollado por **narayan project's**.

---

## ✨ Características Principales

### 📥 Descargas de alta calidad
- Audio en **MP3 (hasta 320 kbps), FLAC, M4A, WAV, OPUS** y video **MP4**.
- Búsqueda rápida con `YoutubeExplode` y descarga con `yt-dlp`; fuentes **YouTube Music**, **YouTube general**, **SoundCloud** y búsqueda universal.
- Soporte para URLs individuales, listas de reproducción y álbumes, con creación automática de subcarpetas.
- Descargas simultáneas configurables (1 a 5), progreso por hilo, reintentos y verificación de integridad.
- **Normalización de volumen EBU R128**, integración con **SponsorBlock** y letras sincronizadas en `.lrc` (con respaldo vía LRCLIB).

### 📡 Radar YTM
- Explora tu **Historial**, tus **Canciones que te gustan** y tus **Listas de reproducción** sin salir de la app.
- Datos obtenidos directamente de YouTube Music mediante su API interna (InnerTube) y respaldo automático a `yt-dlp`.
- Abre cualquier lista para ver sus pistas, reproduce en **streaming** o envía pistas y listas a la cola de descargas con `Ctrl + D`.
- Miniaturas, filtro en vivo, caché por sección y estados de carga accesibles.

### 🎧 Reproductor integrado
- Cola de reproducción con **repetir todo / repetir una** y **modo aleatorio**.
- Reproducción continua, saltos de 5 y 30 segundos, volumen continuo, silencio y volumen máximo.
- Transcodificación transparente de **OPUS / OGG / WebM** para reproducirlos sin códecs externos.
- Exportación de la biblioteca a **M3U8** y reproducción de toda la colección con un clic.

### 📚 Biblioteca e historial
- Registro automático de cada descarga, con buscador en vivo.
- Reproductor, apertura de carpeta, copia de enlace original y **reenvío a descargas** desde el historial.
- Herramienta **Depurar** para detectar entradas cuyos archivos ya no existen.

### 🔐 Gestión de sesión y cookies
- **Extracción automática** desde Chrome, Firefox, Edge, Brave, Opera, Vivaldi o Chromium (con detección de restricciones DPAPI).
- **Asistente manual** con validación de tokens (`SID`, `HSID`, `LOGIN_INFO`, `SAPISID`), compatible con formato Netscape y JSON.
- Enlaces opcionales a la extensión de código abierto **Get cookies.txt LOCALLY** para Chrome/Edge y Firefox.
- **Importación y exportación** de `cookies.txt` / `cookies.json` para respaldo o migración.
- Reparación automática de archivos con BOM UTF-8 y comprobación real de la sesión antes de consultar el Radar.

### ♿ Accesibilidad universal (A11y / WCAG 2.2 AA / Section 508)
- Compatibilidad con **NVDA**, **JAWS** y **Narrador** de Windows.
- Navegación completa por teclado, foco visible de alto contraste y `AutomationProperties` en los controles clave.
- Regiones activas (`LiveSetting`) para anunciar estados, errores y progreso.

### 🪟 Experiencia Windows 11
- Material **Mica** nativo, esquinas redondeadas, modo oscuro y tipografía **Segoe UI Variable**.
- Soporte **PerMonitorV2 High-DPI**.
- Pestañas dinámicas con cierre rápido (`Ctrl + W`) e inicio configurable.
- Instancia única: al reabrir la app se restaura y enfoca la ventana existente.
- Notificaciones nativas y progreso en la barra de tareas.

### 🔄 Actualizador integrado
- Comprobación de nuevas versiones en **GitHub Releases**.
- Actualización automática del ejecutable y de los componentes (`yt-dlp`, `FFmpeg`, `Deno`).

---

## ⌨️ Atajos de Teclado

| Atajo | Función |
| :--- | :--- |
| <kbd>Ctrl</kbd> + <kbd>1</kbd> | Pestaña **Descargas** |
| <kbd>Ctrl</kbd> + <kbd>2</kbd> | Pestaña **Biblioteca** |
| <kbd>Ctrl</kbd> + <kbd>3</kbd> | Pestaña **Reproductor** |
| <kbd>Ctrl</kbd> + <kbd>4</kbd> / <kbd>Ctrl</kbd> + <kbd>R</kbd> | Pestaña **Radar YTM** |
| <kbd>Ctrl</kbd> + <kbd>W</kbd> | Cerrar la pestaña actual |
| <kbd>Ctrl</kbd> + <kbd>O</kbd> | Cargar archivo de lista de URLs |
| <kbd>Ctrl</kbd> + <kbd>S</kbd> | Abrir carpeta de descargas |
| <kbd>Ctrl</kbd> + <kbd>,</kbd> | Preferencias y herramientas |
| <kbd>Ctrl</kbd> + <kbd>K</kbd> | Gestor de sesión y cookies |
| <kbd>Ctrl</kbd> + <kbd>L</kbd> | Lista completa de atajos |
| <kbd>Espacio</kbd> | Reproducir / pausar |
| <kbd>Ctrl</kbd> + <kbd>←</kbd> / <kbd>→</kbd> | Pista anterior / siguiente |
| <kbd>Shift</kbd> + <kbd>←</kbd> / <kbd>→</kbd> | Retroceder / avanzar 5 segundos |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>←</kbd> / <kbd>→</kbd> | Retroceder / avanzar 30 segundos |
| <kbd>Ctrl</kbd> + <kbd>↑</kbd> / <kbd>↓</kbd> | Subir / bajar volumen un 5% |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>↑</kbd> / <kbd>↓</kbd> | Volumen máximo / silenciar |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>R</kbd> / <kbd>H</kbd> | Repetir / modo aleatorio |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>S</kbd> | Detener reproducción |

**En el Radar:** <kbd>Enter</kbd> reproduce, <kbd>→</kbd> abre una lista, <kbd>←</kbd> / <kbd>Retroceso</kbd> vuelve a la lista de listas y <kbd>Ctrl</kbd> + <kbd>D</kbd> envía a descargas.

---

## 🛠️ Compilación desde el Código Fuente

### Requisitos
- **Windows 10 (1809+) o Windows 11**
- **.NET 10.0 SDK**
- PowerShell 5.1 o superior (scripts de release y verificación)

### Compilar y probar
```powershell
git clone https://github.com/RHcomunications/CazadorYTM.git
cd CazadorYTM

dotnet build CazadorYTM.slnx -c Release
dotnet test CazadorYTM.slnx -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Verify-I18n.ps1
```

### Generar el paquete autocontenido
```powershell
dotnet publish src/CazadorYTM.Gui/CazadorYTM.Gui.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:PublishReadyToRun=true `
  -o publish/
```

Copia también `lang/`, `README.html` y `CHANGELOG.html` a `publish/`. El flujo de GitHub Actions descarga además las últimas versiones de `yt-dlp`, `Deno` y la suite `FFmpeg`, y publica el ZIP al crear un tag `v*`.

### Estructura del proyecto
```
src/CazadorYTM.Core     Lógica: descargas, metadatos, Radar, cookies y actualizaciones
src/CazadorYTM.Gui      Interfaz WPF (MVVM), vistas, accesibilidad y temas
tests/                  Pruebas xUnit de lógica y smoke tests de vistas WPF
lang/                   Traducciones es/en (verificadas en CI)
scripts/                Verificación de i18n y automatización de releases
docs/releases/          Notas de cada versión publicada
```

---

## 📦 Componentes de Terceros
- [yt-dlp](https://github.com/yt-dlp/yt-dlp)
- [FFmpeg](https://ffmpeg.org/) (builds GPL de BtbN)
- [Deno](https://deno.land/)
- [ModernWpfUI](https://github.com/Kinnara/ModernWpf)
- [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [LRCLIB](https://lrclib.net/) (letras de respaldo)

---

## 📄 Licencia y Créditos
Desarrollado y mantenido por **narayan project's**.
Copyright © 2026 narayan project's. Todos los derechos reservados.

El código se publica con fines de transparencia y consulta; no se concede licencia para redistribuir ni publicar versiones derivadas.
