using System.Diagnostics;
using System.Security.Cryptography;

namespace IntercalarioDigitale.Desktop.Services;

/// <summary>
/// Trasforma un documento Word in un PDF di anteprima, così si legge con lo stesso viewer degli altri documenti.
/// Usa Microsoft Word se è installato, altrimenti LibreOffice. Il Word originale non viene mai modificato:
/// l'anteprima è una copia in <see cref="AppPaths.CacheFolder"/>, riusata finché il contenuto non cambia.
/// </summary>
public static class WordConverter
{
    private const int WordPdfFormat = 17; // wdExportFormatPDF
    private const int MacrosDisabled = 3; // msoAutomationSecurityForceDisable

    public static async Task<string> ConvertToPdfAsync(string wordPath)
    {
        if (!File.Exists(wordPath))
        {
            throw new FileNotFoundException("Documento Word non trovato", wordPath);
        }

        Directory.CreateDirectory(AppPaths.CacheFolder);
        var cached = Path.Combine(AppPaths.CacheFolder, ContentKey(wordPath) + ".pdf");
        if (File.Exists(cached) && new FileInfo(cached).Length > 0)
        {
            return cached;
        }

        var failures = new List<string>();
        var temporary = Path.Combine(AppPaths.CacheFolder, Guid.NewGuid().ToString("N") + ".tmp.pdf");

        try
        {
            if (WordIsInstalled())
            {
                try
                {
                    await RunOnStaThread(() => ConvertWithWord(wordPath, temporary)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    failures.Add("Microsoft Word: " + ex.Message);
                }
            }

            if (!HasContent(temporary))
            {
                var office = FindLibreOffice();
                if (office is not null)
                {
                    try
                    {
                        await ConvertWithLibreOffice(office, wordPath, temporary).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        failures.Add("LibreOffice: " + ex.Message);
                    }
                }
            }

            if (!HasContent(temporary))
            {
                var reason = failures.Count > 0
                    ? string.Join(" / ", failures)
                    : "non è installato né Microsoft Word né LibreOffice";
                throw new InvalidOperationException(
                    "Non riesco a preparare l'anteprima del documento Word (" + reason + ").");
            }

            File.Move(temporary, cached, overwrite: true);
            return cached;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static string ContentKey(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool HasContent(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // file temporaneo: se resta lì non è un problema
        }
    }

    // ---------- Microsoft Word (automazione COM, senza librerie esterne) ----------

    private static bool WordIsInstalled() => Type.GetTypeFromProgID("Word.Application") is not null;

    private static void ConvertWithWord(string wordPath, string pdfPath)
    {
        var type = Type.GetTypeFromProgID("Word.Application")
            ?? throw new InvalidOperationException("Word non è installato");

        dynamic? app = null;
        dynamic? document = null;
        try
        {
            app = Activator.CreateInstance(type);
            app!.Visible = false;
            app.DisplayAlerts = 0;               // wdAlertsNone: nessuna finestra di dialogo
            app.AutomationSecurity = MacrosDisabled; // le macro non vengono mai eseguite

            // FileName, ConfirmConversions, ReadOnly, AddToRecentFiles
            document = app.Documents.Open(wordPath, false, true, false);
            document.ExportAsFixedFormat(pdfPath, WordPdfFormat);
        }
        finally
        {
            try
            {
                document?.Close(0); // wdDoNotSaveChanges
            }
            catch
            {
                // il documento potrebbe non essersi aperto
            }

            try
            {
                app?.Quit(0);
            }
            catch
            {
                // Word potrebbe essere già chiuso
            }
        }
    }

    private static Task RunOnStaThread(Action work)
    {
        var completion = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                work();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    // ---------- LibreOffice ----------

    private static string? FindLibreOffice()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        foreach (var folder in folders.Where(f => !string.IsNullOrEmpty(f)))
        {
            var candidate = Path.Combine(folder, "LibreOffice", "program", "soffice.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task ConvertWithLibreOffice(string office, string wordPath, string pdfPath)
    {
        var work = Path.Combine(AppPaths.CacheFolder, "lo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var profile = new Uri(Path.Combine(work, "profile")).AbsoluteUri;
            var start = new ProcessStartInfo(office)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--headless");
            start.ArgumentList.Add("--norestore");
            start.ArgumentList.Add("-env:UserInstallation=" + profile);
            start.ArgumentList.Add("--convert-to");
            start.ArgumentList.Add("pdf");
            start.ArgumentList.Add("--outdir");
            start.ArgumentList.Add(work);
            start.ArgumentList.Add(wordPath);

            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("LibreOffice non si è avviato");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // già terminato
                }

                throw new TimeoutException("la conversione ha richiesto troppo tempo");
            }

            var produced = Path.Combine(work, Path.GetFileNameWithoutExtension(wordPath) + ".pdf");
            if (!File.Exists(produced))
            {
                throw new InvalidOperationException("nessun PDF prodotto");
            }

            File.Move(produced, pdfPath, overwrite: true);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // cartella temporanea: se resta lì non è un problema
            }
        }
    }
}
