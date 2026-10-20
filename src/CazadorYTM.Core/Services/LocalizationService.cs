namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = "es";

    public event Action? LanguageChanged;

    public string CurrentLanguage => _currentLanguage;

    public LocalizationService()
    {
        LoadLanguage("es");
    }

    public void SetLanguage(string langCode) => LoadLanguage(langCode);

    public void LoadLanguage(string langCode)
    {
        _currentLanguage = string.IsNullOrWhiteSpace(langCode) ? "es" : langCode.ToLower().Trim();
        _strings.Clear();

        // 1. Load embedded defaults first
        LoadEmbeddedDefaults(_currentLanguage);

        // 2. Override with custom JSON dictionary if available in lang/
        try
        {
            var baseDir = Helpers.GetBaseDir();
            var langFile = Path.Combine(baseDir, "lang", $"{_currentLanguage}.json");
            if (File.Exists(langFile))
            {
                var json = File.ReadAllText(langFile);
                var dict = JsonHelper.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var (k, v) in dict)
                    {
                        if (!string.IsNullOrEmpty(v))
                            _strings[k] = v;
                    }
                }
            }
        }
        catch { }

        LanguageChanged?.Invoke();
    }

    public string Get(string key, string? defaultValue = null)
    {
        if (_strings.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
            return val;
        return defaultValue ?? key;
    }

    public string this[string key] => Get(key);

    private void SetDefault(string key, string value)
    {
        _strings[key] = value;
    }

    private void LoadEmbeddedDefaults(string lang)
    {
        if (lang == "en")
        {
            // App & Accessibility
            SetDefault("AppName", "Cazador YTM");
            SetDefault("MainWindowHelpText", "Press ALT to access the menu, Control + comma for preferences, or use Control + 1 to 4 to switch tabs.");
            SetDefault("MenuBarHelpText", "Use keyboard arrows to navigate and Escape to exit menu.");
            SetDefault("ResultSelectionHelpText", "Navigate with Up and Down arrows, press Space to select or unselect, and Enter to accept.");
            SetDefault("ResultsListName", "List of found tracks");
            SetDefault("PrefWindowHelpText", "Configure default download, network, maintenance, language and log options.");
            SetDefault("PrefTabsName", "Preferences sections");
            SetDefault("CookiesTabsName", "Cookie setup methods");
            SetDefault("StatusBarName", "Application status bar");
            SetDefault("StatusLabel", "Current operation status");
            SetDefault("MainTabsName", "Main tabs");
            SetDefault("MainTabsHelpText", "Use Ctrl + 1 to Ctrl + 4 to switch tabs, or the Left and Right arrows when focus is on a tab.");
            SetDefault("TabAnnouncement", "Tab {0} selected, {1} of {2}.");
            SetDefault("VersionLabel", "Version");

            // Navigation & Tabs
            SetDefault("TabDownloads", "Downloads");
            SetDefault("TabLibrary", "Library");
            SetDefault("TabPlayer", "Player");
            SetDefault("TabRadar", "Radar YTM");
            SetDefault("PrefStartupTab", "Initial tab on startup:");
            SetDefault("MenuCloseTab", "Close Current Tab");

            // Main Menu
            SetDefault("MenuFile", "_File");
            SetDefault("MenuLoadList", "_Load Song List...");
            SetDefault("MenuOpenFolder", "_Open Downloads Folder");
            SetDefault("MenuPreferences", "_Preferences...");
            SetDefault("MenuExit", "_Exit");
            SetDefault("MenuNavigation", "_Navigation");
            SetDefault("MenuHelp", "_Help");
            SetDefault("MenuShortcuts", "_Keyboard Shortcuts");
            SetDefault("MenuAbout", "_About Cazador YTM");

            // Downloads Tab
            SetDefault("DownloadTitle", "Audio & Video Download");
            SetDefault("SearchPlaceholder", "Search artist, song, playlist or paste URL here...");
            SetDefault("BtnSearch", "Search / Analyze");
            SetDefault("BtnPreview", "Preview");
            SetDefault("BtnStopPreview", "Stop Preview");
            SetDefault("BtnLoadList", "Load List...");
            SetDefault("BtnDownload", "Start Download");
            SetDefault("BtnCancel", "Cancel");
            SetDefault("OptionsHeader", "Advanced options");
            SetDefault("SourceLabel", "Source:");
            SetDefault("FormatLabel", "Format:");
            SetDefault("QualityLabel", "Quality / Bitrate:");
            SetDefault("ThreadsLabel", "Threads:");
            SetDefault("CookiesLabel", "Cookies:");
            SetDefault("DestFolderHeader", "Destination Folder");
            SetDefault("BtnBrowse", "Browse...");
            SetDefault("SponsorBlockOpt", "SponsorBlock (Skip silences & intros)");
            SetDefault("NormalizeOpt", "Volume normalization (FFmpeg)");
            SetDefault("EmbedLyricsOpt", "Embed synchronized lyrics (.lrc)");
            SetDefault("EnumerateOpt", "Enumerate tracks (01 -, 02 -)");

            // Library / History Tab
            SetDefault("LibraryFilterPlaceholder", "Filter library by title, artist or path...");
            SetDefault("BtnRefresh", "Refresh");
            SetDefault("ColTitle", "Title");
            SetDefault("ColFormat", "Format");
            SetDefault("ColStatus", "Status");
            SetDefault("ColDate", "Date");
            SetDefault("BtnPlay", "Play");
            SetDefault("BtnPause", "Pause");
            SetDefault("BtnStop", "Stop");
            SetDefault("OpenFolder", "Open Folder");
            SetDefault("CopyLink", "Copy Link");
            SetDefault("ClearHistory", "Clear History");
            SetDefault("DeleteFromHistory", "Delete from history");

            // Player Tab
            SetDefault("PlayerPanelName", "Media Player");
            SetDefault("TrackPositionLabel", "Playback position:");
            SetDefault("BtnOpenAudioFile", "Open File...");
            SetDefault("BtnPlayPause", "Play / Pause");
            SetDefault("PlayerPlayPauseHelp", "Shortcut: Ctrl + Space or the Media Play/Pause key.");
            SetDefault("PlayerShuffleName", "Shuffle mode");
            SetDefault("PlayerShuffleHelp", "Turns shuffle playback on or off. Shortcut: Ctrl + Shift + H.");
            SetDefault("VolumeLabel", "Vol:");

            // Selection Dialog
            SetDefault("SelectAll", "Select All");
            SetDefault("DeselectAll", "Deselect All");
            SetDefault("ExportTxt", "Export as .txt");
            SetDefault("BtnAccept", "Accept");

            // Preferences Window
            SetDefault("PreferencesTitle", "Preferences & Settings");
            SetDefault("PrefDownloadsTab", "Downloads");
            SetDefault("PrefNetworkTab", "Network & Appearance");
            SetDefault("PrefToolsTab", "Tools");
            SetDefault("PrefLogsTab", "Logs");
            SetDefault("PrefDestFolder", "Default downloads folder:");
            SetDefault("PrefBitrate", "Default Quality / Bitrate:");
            SetDefault("PrefConcurrent", "Simultaneous download threads:");
            SetDefault("PrefSponsorBlock", "SponsorBlock (Skip silences & intros)");
            SetDefault("PrefNormalize", "Volume normalization EBU R128 (FFmpeg)");
            SetDefault("PrefEmbedLyrics", "Embed synchronized lyrics (.lrc)");
            SetDefault("PrefEnumerate", "Enumerate tracks numerically (01 -, 02 -)");
            SetDefault("PrefLanguage", "Application Language:");
            SetDefault("PrefTheme", "Visual Theme:");
            SetDefault("PrefBrowser", "Browser Cookies:");
            SetDefault("PrefProxy", "Proxy Server (Optional):");
            SetDefault("PrefUpdaterHeader", "Component Updater");
            SetDefault("PrefUpdaterDesc", "Check and update yt-dlp, FFmpeg and Deno directly from GitHub.");
            SetDefault("PrefCheckUpdates", "Check for Updates");
            SetDefault("PrefUpdateAll", "Update All");
            SetDefault("PrefVerifierHeader", "Audio Integrity Verifier");
            SetDefault("PrefVerifierDesc", "Scans with FFprobe all files in the downloads folder to detect corrupt audio files and generates a report.");
            SetDefault("PrefVerifyBtn", "Verify Downloads Folder");
            SetDefault("PrefFilterLogs", "Filter by level:");
            SetDefault("PrefCopyLogs", "Copy Logs");
            SetDefault("PrefExportLogs", "Export to File...");
            SetDefault("PrefClearLogs", "Clear");
            SetDefault("Save", "Save");
            SetDefault("Close", "Close");
        }
        else
        {
            // App & Accessibility
            SetDefault("AppName", "Cazador YTM");
            SetDefault("MainWindowHelpText", "Presione ALT para acceder al menú, Control + coma para preferencias o use Control + 1 al 4 para alternar pestañas.");
            SetDefault("MenuBarHelpText", "Use las flechas del teclado para navegar y Escape para salir del menú.");
            SetDefault("ResultSelectionHelpText", "Navegue con las flechas arriba y abajo, pulse Espacio para marcar o desmarcar y Enter para aceptar.");
            SetDefault("ResultsListName", "Lista de pistas encontradas");
            SetDefault("PrefWindowHelpText", "Configure las opciones predeterminadas de descarga, red, mantenimiento, idioma y registros.");
            SetDefault("PrefTabsName", "Secciones de preferencias");
            SetDefault("CookiesTabsName", "Métodos de configuración de cookies");
            SetDefault("StatusBarName", "Barra de estado de la aplicación");
            SetDefault("StatusLabel", "Estado de la operación actual");
            SetDefault("MainTabsName", "Pestañas principales");
            SetDefault("MainTabsHelpText", "Use Ctrl + 1 a Ctrl + 4 para cambiar de pestaña o las flechas izquierda y derecha cuando el foco esté en una pestaña.");
            SetDefault("TabAnnouncement", "Pestaña {0} seleccionada, {1} de {2}.");
            SetDefault("VersionLabel", "Versión");

            // Navigation & Tabs
            SetDefault("TabDownloads", "Descarga");
            SetDefault("TabLibrary", "Biblioteca");
            SetDefault("TabPlayer", "Reproductor");
            SetDefault("TabRadar", "Radar YTM");
            SetDefault("PrefStartupTab", "Pestaña inicial al abrir:");
            SetDefault("MenuCloseTab", "Cerrar Pestaña Actual");

            // Main Menu
            SetDefault("MenuFile", "_Archivo");
            SetDefault("MenuLoadList", "_Cargar Lista de Canciones...");
            SetDefault("MenuOpenFolder", "_Abrir Carpeta de Descargas");
            SetDefault("MenuPreferences", "_Preferencias...");
            SetDefault("MenuExit", "_Salir");
            SetDefault("MenuNavigation", "_Navegación");
            SetDefault("MenuHelp", "A_yuda");
            SetDefault("MenuShortcuts", "_Atajos de Teclado");
            SetDefault("MenuAbout", "_Acerca de Cazador YTM");

            // Downloads Tab
            SetDefault("DownloadTitle", "Descarga de Audio y Video");
            SetDefault("SearchPlaceholder", "Buscar artista, canción, playlist o pegar enlace aquí...");
            SetDefault("BtnSearch", "Buscar / Analizar");
            SetDefault("BtnPreview", "Previsualizar");
            SetDefault("BtnStopPreview", "Detener Previsualización");
            SetDefault("BtnLoadList", "Cargar Lista...");
            SetDefault("BtnDownload", "Iniciar Descarga");
            SetDefault("BtnCancel", "Cancelar");
            SetDefault("OptionsHeader", "Opciones avanzadas");
            SetDefault("SourceLabel", "Fuente:");
            SetDefault("FormatLabel", "Formato:");
            SetDefault("QualityLabel", "Calidad / Bitrate:");
            SetDefault("ThreadsLabel", "Hilos:");
            SetDefault("CookiesLabel", "Cookies:");
            SetDefault("DestFolderHeader", "Carpeta de Destino");
            SetDefault("BtnBrowse", "Examinar...");
            SetDefault("SponsorBlockOpt", "SponsorBlock (Omitir silencios e intros)");
            SetDefault("NormalizeOpt", "Normalización de volumen (FFmpeg)");
            SetDefault("EmbedLyricsOpt", "Incrustar Letras sincronizadas (.lrc)");
            SetDefault("EnumerateOpt", "Enumerar canciones (01 -, 02 -)");

            // Library / History Tab
            SetDefault("LibraryFilterPlaceholder", "Filtrar biblioteca por título, artista o ruta...");
            SetDefault("BtnRefresh", "Actualizar");
            SetDefault("ColTitle", "Título");
            SetDefault("ColFormat", "Formato");
            SetDefault("ColStatus", "Estado");
            SetDefault("ColDate", "Fecha");
            SetDefault("BtnPlay", "Reproducir");
            SetDefault("BtnPause", "Pausar");
            SetDefault("BtnStop", "Detener");
            SetDefault("OpenFolder", "Abrir Carpeta");
            SetDefault("CopyLink", "Copiar Enlace");
            SetDefault("ClearHistory", "Vaciar Historial");
            SetDefault("DeleteFromHistory", "Eliminar del historial");

            // Player Tab
            SetDefault("PlayerPanelName", "Panel del Reproductor");
            SetDefault("TrackPositionLabel", "Posición de reproducción:");
            SetDefault("BtnOpenAudioFile", "Abrir Archivo...");
            SetDefault("BtnPlayPause", "Reproducir / Pausar");
            SetDefault("PlayerPlayPauseHelp", "Atajo: Ctrl + Espacio o la tecla multimedia Reproducir/Pausar.");
            SetDefault("PlayerShuffleName", "Modo aleatorio");
            SetDefault("PlayerShuffleHelp", "Activa o desactiva la reproducción aleatoria. Atajo: Ctrl + Shift + H.");
            SetDefault("VolumeLabel", "Vol:");

            // Selection Dialog
            SetDefault("SelectAll", "Seleccionar Todo");
            SetDefault("DeselectAll", "Deseleccionar Todo");
            SetDefault("ExportTxt", "Exportar como .txt");
            SetDefault("BtnAccept", "Aceptar");

            // Preferences Window
            SetDefault("PreferencesTitle", "Preferencias y Configuración");
            SetDefault("PrefDownloadsTab", "Descargas");
            SetDefault("PrefNetworkTab", "Red & Apariencia");
            SetDefault("PrefToolsTab", "Herramientas");
            SetDefault("PrefLogsTab", "Registros");
            SetDefault("PrefDestFolder", "Carpeta predeterminada de descargas:");
            SetDefault("PrefBitrate", "Calidad / Bitrate por defecto:");
            SetDefault("PrefConcurrent", "Hilos de descarga simultánea:");
            SetDefault("PrefSponsorBlock", "SponsorBlock (Omitir silencios e intros)");
            SetDefault("PrefNormalize", "Normalización de volumen EBU R128 (FFmpeg)");
            SetDefault("PrefEmbedLyrics", "Incrustar Letras sincronizadas (.lrc)");
            SetDefault("PrefEnumerate", "Enumerar pistas numéricamente (01 -, 02 -)");
            SetDefault("PrefLanguage", "Idioma de la aplicación:");
            SetDefault("PrefTheme", "Tema visual:");
            SetDefault("PrefBrowser", "Cookies del navegador:");
            SetDefault("PrefProxy", "Servidor Proxy (Opcional):");
            SetDefault("PrefUpdaterHeader", "Actualizador de Componentes");
            SetDefault("PrefUpdaterDesc", "Compruebe y actualice yt-dlp, FFmpeg y Deno directamente desde GitHub.");
            SetDefault("PrefCheckUpdates", "Buscar Actualizaciones");
            SetDefault("PrefUpdateAll", "Actualizar Todo");
            SetDefault("PrefVerifierHeader", "Verificador de Integridad de Audio");
            SetDefault("PrefVerifierDesc", "Analiza con FFprobe todos los archivos de la carpeta de descargas para detectar audios corruptos y genera un informe.");
            SetDefault("PrefVerifyBtn", "Verificar Carpeta de Descargas");
            SetDefault("PrefFilterLogs", "Filtrar por nivel:");
            SetDefault("PrefCopyLogs", "Copiar Registros");
            SetDefault("PrefExportLogs", "Exportar a Archivo...");
            SetDefault("PrefClearLogs", "Limpiar");
            SetDefault("Save", "Guardar");
            SetDefault("Close", "Cerrar");
        }
    }
}
