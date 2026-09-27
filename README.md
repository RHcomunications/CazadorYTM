# Cazador YTM 🎵

![Version](https://img.shields.io/badge/version-1.1.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6.svg)
![WCAG](https://img.shields.io/badge/accessibility-WCAG%202.2%20AA-green.svg)
![Fluent Design](https://img.shields.io/badge/UI-WinUI%203%20%2F%20Fluent%20Mica-blueviolet.svg)

> **Cazador YTM** es un gestor de descargas multimedia, reproductor de audio integrado y suite de procesamiento de música de alto rendimiento para Windows, diseñado con accesibilidad universal de primer nivel y la estética moderna de **WinUI 3 / Windows 11 Fluent Design (Mica)**.

Desarrollado por **narayan project's**.

---

## ✨ Características Principales

- **Descargas de Alta Calidad**:
  - Soporte de audio en formatos **MP3 (hasta 320 kbps), FLAC, M4A, WAV, OPUS** y video **MP4**.
  - Búsqueda nativa ultrarrápida sin cuotas de API mediante `YoutubeExplode` y motor de backend `yt-dlp`.
  - Soporte para URLs individuales, listas de reproducción completas y álbumes con creación automática de subcarpetas jerárquicas.
  - Normalización de audio con EBU R128 (`loudnorm`) e integración de `SponsorBlock` para saltar contenido no musical.

- **Reproductor de Audio Accesible Estilo Spotify**:
  - Controles de reproducción fluidos: saltos precisos de 5s y 30s, volumen continuo, silenciamiento rápido y visualización de tiempo restante/transcurrido.
  - Totalmente utilizable mediante atajos de teclado globales.

- **Accesibilidad Universal (A11y / WCAG 2.2 AA / Section 508)**:
  - Compatibilidad total con lectores de pantalla (**NVDA**, **JAWS**, **Narrador** de Windows).
  - Árbol de accesibilidad con `AutomationProperties` en cada control e indicadores visuales de foco de alto contraste.
  - Regiones activas (`LiveSetting="Polite"`) para anunciar progreso y estado de descargas.

- **Diseño Moderno Windows 11 Fluent**:
  - Efecto de material **Mica** nativo vía Desktop Window Manager (DWM).
  - Esquinas redondeadas, modo oscuro inmersivo y tipografía **Segoe UI Variable**.
  - Soporte de alta resolución **PerMonitorV2 High-DPI**.

- **Actualizador Integrado de Aplicación y Componentes**:
  - Verificación automática de nuevas versiones en **GitHub Releases**.
  - Actualización automática silenciosa del ejecutable y dependencias (`yt-dlp`, `FFmpeg`, `Deno`).

---

## ⌨️ Atajos de Teclado Globales

| Atajo | Función |
| :--- | :--- |
| <kbd>Ctrl</kbd> + <kbd>1</kbd> | Ir a la pestaña **Descargas** |
| <kbd>Ctrl</kbd> + <kbd>2</kbd> | Ir a la pestaña **Biblioteca e Historial** |
| <kbd>Ctrl</kbd> + <kbd>3</kbd> | Ir a la pestaña **Reproductor** |
| <kbd>Ctrl</kbd> + <kbd>O</kbd> | Cargar archivo de lista de URLs |
| <kbd>Ctrl</kbd> + <kbd>S</kbd> | Abrir carpeta de descargas en el Explorador |
| <kbd>Ctrl</kbd> + <kbd>,</kbd> | Abrir **Preferencias y Herramientas** |
| <kbd>Ctrl</kbd> + <kbd>L</kbd> | Ver lista completa de atajos de teclado |
| <kbd>Espacio</kbd> | Reproducir / Pausar audio |
| <kbd>Shift</kbd> + <kbd>←</kbd> / <kbd>→</kbd> | Retroceder / Avanzar 5 segundos |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>←</kbd> / <kbd>→</kbd> | Retroceder / Avanzar 30 segundos |
| <kbd>Ctrl</kbd> + <kbd>↑</kbd> / <kbd>↓</kbd> | Subir / Bajar volumen en un 5% |

---

## 🛠️ Compilación desde el Código Fuente

### Requisitos
- **Windows 10 (versión 1809+) o Windows 11**
- **.NET 10.0 SDK**

### Pasos de compilación
```powershell
# Clonar el repositorio
git clone https://github.com/RHcomunications/CazadorYTM.git
cd CazadorYTM

# Restaurar dependencias y ejecutar pruebas
dotnet test CazadorYTM.slnx

# Compilar release auto-contenido
dotnet publish src/CazadorYTM.Gui/CazadorYTM.Gui.csproj -c Release -r win-x64 --self-contained true -o bin/
```

---

## 📦 Componentes de Terceros
- [yt-dlp](https://github.com/yt-dlp/yt-dlp)
- [FFmpeg](https://ffmpeg.org/) (GPL builds by Gyan.dev / BtbN)
- [Deno](https://deno.land/)
- [ModernWpfUI](https://github.com/KDE/modernwpf)
- [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)

---

## 📄 Licencia y Créditos
Desarrollado y mantenido por **narayan project's**.  
Copyright © 2026 narayan project's. Todos los derechos reservados.
