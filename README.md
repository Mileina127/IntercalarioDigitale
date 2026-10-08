# IntercalarioDigitale (GerarchIA)

Applicativo per Windows 11 per la gestione e l'analisi degli appunti: un **intercalario digitale** con
contropagina e allegato affiancati, annotazioni a penna, assistente IA e validazioni lungo la linea gerarchica.

> Stato: la logica (`Core`) è coperta da test. L'app WPF (`Desktop`) è una prima versione da provare:
> la lettura dei PDF usa PDFium tramite [Docnet.Core](https://github.com/GowenGit/docnet), l'assistente IA è simulato.

## Come provarlo

Serve Windows 10/11 e il **.NET 8 SDK** (o Visual Studio 2022 con il carico di lavoro "Sviluppo desktop .NET").

```powershell
dotnet run --project src/IntercalarioDigitale.Desktop
```

Oppure apri `IntercalarioDigitale.sln` in Visual Studio e avvia `IntercalarioDigitale.Desktop`.
Su GitHub, la scheda **Actions** compila e prova tutto su Windows e allega l'app compilata (`GerarchIA-win-x64`);
per eseguirla serve il [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

Al primo avvio vengono create 5 pratiche di esempio (con PDF generati) in `%LocalAppData%\GerarchIA`
(si può cambiare cartella con la variabile d'ambiente `GERARCHIA_DATA`). Per ricominciare da capo, chiudi l'app
e cancella quella cartella.

### Cosa si può fare

- **Elenco pratiche**: menu `•••` con Leggi, Scarica, Manda avanti, Restituisci; doppio clic per leggere.
- **Utente simulato** (in alto): cambia ruolo (Capo Sezione, Capo Ufficio, Capo Reparto, Autorità di vertice),
  imposta la licenza con il sostituto parigrado e la delega di inoltro al Vertice per i Capi Ufficio.
- **Regole di flusso**: via gerarchica Sezione → Ufficio → Reparto → Vertice; un Ufficiale in licenza passa l'appunto
  al delegato, altrimenti l'appunto resta «in attesa di presa in carico»; solo i Capi Reparto possono inviare in
  tramite telematico al Capo Sezione di un altro Reparto.
- **Viewer**: contropagina (pagine 0…n) e allegato (1…n) affiancati, con PDFium.
- **Annotazioni**: penna con mouse, stilo o dito (`InkCanvas`), tre colori e tre spessori, annulla e cancella pagina.
  I tratti sono salvati per pagina in `annotations/<documentId>.json`, senza toccare il PDF.
- **Analisi IA**: pannello di chat con risposte simulate (concordanze, approvazioni, annotazioni), ambito selezionabile.
- **Validazione**: timbri Concordato (Capi Sezione e Ufficio), Approvato (Capi Reparto e Vertice) e Aggiuntivo/Visto (tutti).
- **Nuova pratica**: scegli due PDF tuoi oppure lascia vuoto per generare documenti di esempio.

## Struttura dell'intercalario

Quando inizializzi un intercalario viene creata questa struttura:

- `manifest.json`
- `cover.pdf`
- `attachments/*.pdf`
- `annotations/<documentId>.json`

## Progetti

- `src/IntercalarioDigitale.Core`: modelli, repository (`BinderRepository`), regole di flusso (`Workflow`), assistente simulato, PDF di esempio
- `src/IntercalarioDigitale.Cli`: CLI di test operativo
- `src/IntercalarioDigitale.Desktop`: app WPF (elenco pratiche, viewer con PDFium, penna, IA, validazioni)
- `tests/IntercalarioDigitale.Core.Tests`: test xUnit su persistenza e regole di flusso

## Esempi CLI

```bash
IntercalarioDigitale.Cli init ./MioIntercalario ./cover.pdf "Diritto Privato"
IntercalarioDigitale.Cli add-attachment ./MioIntercalario ./capitolo-1.pdf "Capitolo 1"
IntercalarioDigitale.Cli list ./MioIntercalario
IntercalarioDigitale.Cli annotate-demo ./MioIntercalario <attachmentId>
```

## Prossimi passi

1. Sostituire l'assistente simulato con un modello vero e leggere il testo dei PDF.
2. Collegare la banca dati («Leggi banca dati») e l'archivio condiviso al posto della cartella locale.
3. Passare a WinUI 3 per l'aspetto nativo di Windows 11, se serve.
4. Modalità Lettura/Modifica del contenuto e gestione di più allegati per pratica.
