using System.Windows.Media.Imaging;

namespace IntercalarioDigitale.Desktop.Services;

public static class ImageLoader
{
    /// <summary>Legge un'immagine in memoria, così il file non resta bloccato su disco (si può sostituire o cancellare).</summary>
    public static BitmapImage Load(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
