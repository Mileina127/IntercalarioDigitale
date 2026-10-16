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
- **Linea gerarchica fissa**: Capo Sezione → Capo Ufficio → Capo Reparto; poi, per ogni livello decisionale da **C4**
  (il meno elevato) fino a quello scelto, Capo Sezione di vertice → Capo Ufficio di vertice → Autorità di vertice.
  Il **livello decisionale** (Reparto, C4, C3, C2, C1) lo sceglie il Capo Sezione che carica la pratica e fissa subito
  tutto il percorso: con C3 l'appunto attraversa Sezione, Ufficio e Reparto, poi la terna C4 e infine la terna C3.
  Non si può saltare nessun passo; il pannello «PERCORSO» del viewer mostra tappe fatte, attuale e future.
- **Altre regole**: un Ufficiale in licenza passa l'appunto al delegato (stesso ruolo e stesso livello), altrimenti
  l'appunto resta «in attesa di presa in carico»; solo i Capi Reparto possono inviare in tramite telematico al
  Capo Sezione di un altro Reparto (l'appunto attraversa la linea di quel Reparto e poi riprende la salita).
  Concordato: Capi Sezione e Ufficio (anche di vertice); Approvato: Capi Reparto e Autorità; Visto: tutti.
- **Firme PNG**: Capi Ufficio, Capi Reparto e le 4 Autorità di vertice (non i Capi Sezione, né il Capo Sezione e il
  Capo Ufficio di vertice) caricano la propria firma da «Firma…» (schermata principale). Premendo Concordo, Approvo
  o Visto la firma compare sulla pagina aperta della contropagina: si trascina dove si vuole, la rotella la
  ingrandisce o la riduce, poi «Conferma». La firma è copiata in `firme/` dell'intercalario, quindi un timbro già
  apposto non cambia anche se la firma viene poi sostituita. Per provare, al primo avvio ognuno riceve una firma di esempio.
- **Viewer**: contropagina e allegato affiancati, con PDFium. La colonna ai lati di ogni riquadro elenca i
  **documenti**: a sinistra la contropagina (0) e i suoi allegati (1…n), a destra gli allegati (1…n) con i loro
  annessi (L1.1, L1.2…). Le **pagine** del PDF aperto si sfogliano con il navigatore sotto la pagina
  (frecce, oppure Pag↑ e Pag↓). Il pulsante «+» sotto la colonna aggiunge un allegato, un annesso o un
  documento della contropagina.
- **Copertina 0**: ogni pratica ha una copertina d'introduzione (PDF o Word) trattata come **allegato 0**, in cima
  alla colonna degli allegati. Non ha contropagina: quando è aperta si vede da sola, al centro. Scegliendo l'allegato
  1 o un altro documento tornano la contropagina e l'allegato affiancati. Si scarica, si sostituisce e conserva
  l'originale come ogni altro documento. Nelle pratiche che non ce l'hanno, il «+» propone di aggiungerla.
- **Annotazioni**: penna con mouse, stilo o dito (`InkCanvas`), tre colori e tre spessori, annulla e cancella pagina.
  I tratti sono salvati per pagina in `annotations/<documentId>.json`, senza toccare il PDF.
- **Analisi IA**: pannello di chat con risposte simulate (concordanze, approvazioni, annotazioni), ambito selezionabile.
- **Validazione**: timbri Concordato (Capi Sezione e Ufficio), Approvato (Capi Reparto e Vertice) e Aggiuntivo/Visto (tutti).
- **Nuova pratica**: scegli due documenti (PDF o Word) tuoi oppure lascia vuoto per generare documenti di esempio.
- **Documenti Word**: si possono inserire come allegati, annessi o documenti della contropagina. Si leggono
  **in sola lettura**: l'app li trasforma in un PDF di anteprima (serve Microsoft Word oppure LibreOffice installato;
  il Word originale non viene mai modificato e le macro non si eseguono). Per modificarli: «Scarica copia»,
  si lavora in Word e poi «Sostituisci…» (solo per chi ha in carico l'appunto).
- **Prima versione sempre conservata**: alla prima sostituzione l'originale viene copiato in `originals/` e resta
  consultabile con «Vedi originale» (sola lettura). Le sostituzioni successive scartano le versioni intermedie e
  tengono solo l'originale e la versione attuale. Ogni sostituzione è registrata nei movimenti.

## Struttura dell'intercalario

Quando inizializzi un intercalario viene creata questa struttura:

- `manifest.json`
- `cover.pdf` (o `cover.docx`): la contropagina
- `intro.pdf` (o `intro.docx`): la copertina 0 (allegato 0)
- `attachments/*.pdf` (o `*.docx`)
- `firme/*.png` (firme apposte con i timbri)
- `originals/<documentId>.*` (prima versione dei documenti sostituiti)
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
