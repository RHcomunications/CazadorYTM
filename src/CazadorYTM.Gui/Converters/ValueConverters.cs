namespace CazadorYTM.Gui.Converters;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v ? v != Visibility.Visible : (object)false;
    }
}

public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        return value.Equals(parameter);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null) return parameter;
        return Binding.DoNothing;
    }
}

public class ThumbnailImageConverter : IValueConverter
{
    private const int MaxCacheEntries = 150;
    private static readonly object _cacheLock = new();
    private static readonly System.Collections.Generic.Dictionary<string, WeakReference<System.Windows.Media.Imaging.BitmapImage>> _cache = new(StringComparer.Ordinal);
    private static readonly System.Collections.Generic.LinkedList<string> _lru = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string url && !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(url, out var weakRef) && weakRef.TryGetTarget(out var cached))
                {
                    return cached;
                }
            }

            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.DecodePixelWidth = 64;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnDemand;
                bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation;
                bitmap.EndInit();

                lock (_cacheLock)
                {
                    if (!_cache.ContainsKey(url))
                    {
                        while (_lru.Count >= MaxCacheEntries && _lru.Last != null)
                        {
                            var oldest = _lru.Last.Value;
                            _lru.RemoveLast();
                            _cache.Remove(oldest);
                        }
                        _lru.AddFirst(url);
                    }
                    _cache[url] = new WeakReference<System.Windows.Media.Imaging.BitmapImage>(bitmap);
                }

                return bitmap;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

