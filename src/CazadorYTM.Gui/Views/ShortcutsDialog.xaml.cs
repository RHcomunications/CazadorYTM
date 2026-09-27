namespace CazadorYTM.Gui.Views;

using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

public record ShortcutItem(string Shortcut, string Description);

public partial class ShortcutsDialog : Window
{
    public ShortcutsDialog()
    {
        InitializeComponent();
        Services.Win11ThemeHelper.ApplyWin11Backdrop(this, isDialog: true);

        var shortcuts = new List<ShortcutItem>
        {
            // Globales
            new("Ctrl + L", "Abrir esta tabla de atajos de teclado"),
            new("Ctrl + 1", "Ir a la pestaña de Descargas"),
            new("Ctrl + 2", "Ir a la pestaña de Biblioteca / Historial"),
            new("Ctrl + 3", "Ir a la pestaña del Reproductor"),
            new("Ctrl + ,", "Abrir ventana de Preferencias y Configuración"),
            new("Ctrl + S", "Abrir la carpeta de descargas en el explorador"),
            new("Alt o F10", "Activar y navegar por la barra de menús"),

            // Reproductor
            new("Espacio", "Reproducir o pausar el audio actual"),
            new("Shift + Izquierda", "Retroceder 5 segundos en la pista"),
            new("Shift + Derecha", "Avanzar 5 segundos en la pista"),
            new("Ctrl + Shift + Izquierda", "Retroceder 30 segundos en la pista"),
            new("Ctrl + Shift + Derecha", "Avanzar 30 segundos en la pista"),
            new("Ctrl + Arriba", "Subir volumen en pasos de 5%"),
            new("Ctrl + Abajo", "Bajar volumen en pasos de 5%"),
            new("Ctrl + Shift + Abajo", "Silenciar o restaurar el volumen (Mute)"),
            new("Ctrl + Shift + Arriba", "Ajustar volumen al 100%"),
            new("Ctrl + Shift + S", "Detener reproducción del audio"),
            new("Inicio (Home)", "Saltar al inicio de la pista (00:00)"),
            new("Fin (End)", "Saltar al final de la pista"),
            new("Ctrl + O (en Reproductor)", "Abrir y cargar un archivo de audio local"),

            // Descargas
            new("Enter (en campo de búsqueda)", "Buscar canción o iniciar descarga directa"),
            new("Ctrl + P (en Resultados)", "Previsualizar o detener streaming de audio"),
            new("Ctrl + O (en Descargas)", "Cargar archivo de lista (.txt / .csv)"),
            new("Espacio (en Resultados)", "Marcar o desmarcar canción seleccionada"),

            // Biblioteca
            new("Enter (en Biblioteca)", "Reproducir pista seleccionada"),
            new("Supr / Delete (en Biblioteca)", "Eliminar pista del historial"),
            new("F5 (en Biblioteca)", "Actualizar lista del historial")
        };

        LstShortcuts.ItemsSource = shortcuts;

        Loaded += (s, e) =>
        {
            LstShortcuts.Focus();
            if (shortcuts.Count > 0)
            {
                LstShortcuts.SelectedIndex = 0;
            }
        };

        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
