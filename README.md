# IntercalarioDigitale MVP (strada PDF)

MVP in C# pensato per Windows 11 che implementa la base dati/applicativa per un **intercalario digitale**:

- documento di copertina in PDF;
- allegati in PDF;
- manifest JSON per alimentare una sidebar (copertina + allegati);
- annotazioni penna salvate come layer separato per pagina.

> Nota: in questo repository l'MVP è concentrato su dominio + persistenza + CLI. La UI WinUI/WPF con PDFium è il passo successivo.

## Struttura dell'intercalario

Quando inizializzi un intercalario viene creata questa struttura:

- `manifest.json`
- `cover.pdf`
- `attachments/*.pdf`
- `annotations/<documentId>.json`

## Progetti

- `src/IntercalarioDigitale.Core`: modelli e repository (`BinderRepository`)
- `src/IntercalarioDigitale.Cli`: CLI di test operativo
- `tests/IntercalarioDigitale.Core.Tests`: test xUnit su persistenza

## Esempi CLI

```bash
IntercalarioDigitale.Cli init ./MioIntercalario ./cover.pdf "Diritto Privato"
IntercalarioDigitale.Cli add-attachment ./MioIntercalario ./capitolo-1.pdf "Capitolo 1"
IntercalarioDigitale.Cli list ./MioIntercalario
IntercalarioDigitale.Cli annotate-demo ./MioIntercalario <attachmentId>
```

## Prossimo step (UI)

1. WinUI 3 (o WPF) con layout 2 colonne:
   - sidebar gerarchica;
   - viewer PDF centrale.
2. Rendering PDF via PDFium.
3. Overlay `InkCanvas` per penna.
4. Salvataggio tratti in `annotations/<documentId>.json`.
5. Modalità Lettura/Edit con toggle che inibisce editing contenuto ma mantiene annotazione se abilitata.
