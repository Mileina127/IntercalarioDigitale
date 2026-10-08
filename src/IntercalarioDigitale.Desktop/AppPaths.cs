namespace IntercalarioDigitale.Desktop;

public static class AppPaths
{
    /// <summary>Cartella dei dati dell'applicazione: %LocalAppData%\GerarchIA (si può cambiare con la variabile GERARCHIA_DATA).</summary>
    public static string DataFolder
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("GERARCHIA_DATA");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                return custom;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GerarchIA");
        }
    }
}
