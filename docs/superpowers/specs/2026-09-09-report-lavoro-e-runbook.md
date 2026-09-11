# Report del 2026-09-09 — che cosa è cambiato, che cosa è vivo, e come si verifica

Compagno di `2026-09-09-velocita-dove-va-il-tempo.md` (l'analisi e le proposte). Questo file dice
che cosa è **stato fatto**, che cosa è **in produzione**, e le cose operative che ho imparato sul
campo e che il prossimo che tocca questa app non deve riscoprire.

Scritto per Nathan e per gli altri agenti che lavorano su questa app.

## 1. In una riga

Sette proposte del piano portate a termine, il traduttore rimosso, **difetti veri trovati da un audit
avversario in tutte e tre le modifiche che cambiano comportamento vivo** e corretti, suite verde e
stabile, tutto in `ours/integration` e **in produzione sul VPS** dalle 17:29 del 2026-09-09.

## 2. Che cosa è cambiato, e la misura che lo giustifica

| Cosa | Effetto misurato | Rami |
|---|---|---|
| Il battito del processo stream riparte all'invio del prompt | 26 delle 29 uccisioni per silenzio erano false; 6% dei risvegli del supervisore passava da 25 s a 111 s | `7a` |
| Un rifiuto per limite d'uso compra un appuntamento, limitato a 6 h | 18 stalli da 167–509 min, due orchestrazioni morte una notte | `7b`, `7i` |
| Un turno ucciso alla scadenza scrive dove è arrivato | 11 kill, ~5,5 h di lavoro rifatto da zero | `7f`, `7j` |
| Un'aggiunta a un canale sveglia il ciclo; attese del proprietario ridotte | 11–12 s prima che il supervisore vedesse un messaggio | `7e`, `7k` |
| Il traduttore non esiste più | 183 fallimenti nel log recente, due processi per scambio | `7c`, `7h` |
| Il tick legge ciò che è cambiato | 198 → 44 aperture di file, 1 → 0 scritture per tick | `7g` |
| La suite gira su tempi iniettati | 107 s → 37 s, nessuna asserzione indebolita | `7d` |
| Le diagnostiche del lock non sono rubabili | 5 corse rosse su 6 → 0 su 10 | `7l` |
| Il finto Claude sopravvive a due processi nella stessa cartella | 7 crash su 18 invocazioni concorrenti → 0 su 30 | `7m` |

**Non fatte, per decisione del proprietario:** implementer su runner stream (P4), regole di
protocollo (P9).

## 3. L'audit, e perché conta più delle modifiche

Quattro revisori indipendenti, in worktree isolati, con l'obbligo di **provare** ogni difetto con una
sonda. Hanno trovato difetti reali in **tre modifiche su tre** fra quelle che toccano il
comportamento vivo. Nessuno era visibile da una suite verde.

- **P1** parcheggiava una sessione 24 ore quando l'ora di reset era appena passata, aggiungendo un
  giorno a ogni rifiuto, senza che il traffico nuovo potesse svegliarla. E il cancello d'ingresso era
  la parola «limit»: un turno **riuscito** con il canale bloccato veniva parcheggiato 14 ore senza
  spendere un tentativo — quindi senza mai fermarsi né avvisare.
- **P2** avanzava i cursori su tutti i canali mentre il rapporto di chiusura ne indirizzava uno solo:
  **il messaggio del proprietario poteva risultare consegnato senza risposta.** E il record scritto
  per primo dichiarava «un turno di chiusura è stato eseguito» su cinque rami su sei.
- **P3** aveva ridotto di dodici volte il margine che impedisce di specchiare un'entry a metà: con
  uno scrittore fermo 400 ms l'entry arrivava troncata e **la coda distrutta**.

Il revisore di P3 ha attaccato cinque possibili corse dentro il watcher e non ne ha rotta nessuna: i
difetti stavano in ciò che l'accelerazione costava altrove, non nel meccanismo.

**Da tenere:** un audit avversario dopo una tornata di modifiche non è cerimonia. Qui ha trovato tre
difetti che sarebbero arrivati al proprietario come «l'app ha mangiato il mio messaggio».

## 4. Prezzi pagati, detti chiaramente

- **L'ultima entry di un canale ci mette ~4 s** ad arrivare su Telegram, come prima del watcher. È il
  prezzo di non troncarla. Il guadagno sul percorso del proprietario resta: il dispatcher legge i
  file direttamente e non passa dal tailer.
- **Due messaggi a più di 2 s di distanza comprano ancora due turni.** Non esiste una forma che
  consegni subito il primo e coalizzi il secondo: la grazia deve essere almeno la distanza.
- **`--max-budget-usd` non limita la spesa** — misurato: cap 0,0001 $, speso 0,11 $. Il tetto è
  controllato dopo il passo.
- ~~**La grazia di spegnimento (36 min) non è onorata sul VPS**~~ — **chiuso, e la diagnosi qui sotto
  era sbagliata.** Avevo misurato 90 s effettivi e dato la colpa all'unità systemd; il taglio vero era
  l'host .NET fermo a 30 s, che nessuno aveva impostato (`stage/4e-host-shutdown-timeout`). Misurato
  il 2026-09-10: attesa effettiva 40 minuti. Un numero letto in un punto non nomina la causa.

## 5. Stato verificato

- **Suite: 2751 verdi, 9 skip, 0 rossi**, in più corse consecutive dopo ogni merge. Il tempo è
  risalito a ~1 m 20 s dai 37 s di P8 perché sono stati aggiunti ~150 test.
- **CI Windows**: `.github/workflows/windows-build.yml`, verde su `a2adb76`. Verifica la app WPF, che
  su macOS/Linux non compila — comprese le quattro modifiche fatte alla cieca il 2026-09-09.
- **Produzione**: `Running build 17:23`, kit check OK, 8 sessioni vive, il re-arm del watcher su
  Linux osservato funzionare (`watch … was no longer in place … and has been armed again`).

## 6. Runbook del VPS — cose che costano un'ora se non le sai

Host `orch@159.195.254.120`. Clone in `~/AIOrchestrator` su `ours/integration`; il servizio gira da
`/opt/aiorchestrator`.

- **`rsync` non è installato.** Usa `cp -a /tmp/<publish>/. /opt/aiorchestrator/`.
- **`/opt` è di root**, `/opt/aiorchestrator` è di `orch`: puoi scrivere **dentro**, non accanto. Niente
  swap di cartelle.
- **`sudo` è ristretto a quattro comandi**: `systemctl start|stop|restart|status aiorchestrator`.
  Qualsiasi altro `sudo` fallisce — e se lo usi per costruire una variabile, il resto dello script
  prosegue senza accorgersene.
- **`/usr/bin/time` non esiste.** Non metterlo davanti al comando che conta.
- **Il kit si installa a parte.** `KitAssets_Installer` non aggiorna il plugin: dopo un deploy che
  tocca `kit/`, reinstalla con `PATH=$HOME/.local/bin:$PATH claude plugin uninstall aiorch && claude
  plugin install aiorch@aiorch-local --scope user -y`, altrimenti le sessioni leggono le skill
  vecchie. Il daemon lo dice all'avvio (`kit is version 1.0.0 — the right NUMBER over the WRONG TEXT`):
  **quel messaggio è affidabile, dagli retta.**
- **Un riavvio interrompe il lavoro vivo**: le sessioni muoiono e rinascono, i turni a metà si perdono.
- Sequenza usata: `git pull` → `dotnet publish -c Release -r linux-x64 --self-contained -o /tmp/…` →
  `sudo systemctl stop` → `cp -a` → `sudo systemctl start` → reinstalla il kit → `sudo systemctl restart`.
  Copia di sicurezza in `~/aiorchestrator.bak-<data>`; rollback = ricopiarla e riavviare.

## 7. Aperto

1. **`TimeoutStopSec` del servizio (90 s) contro la grazia di spegnimento (36 min)** — la grazia oggi è
   una promessa non mantenuta. Modifica alla configurazione del server, non al codice.
2. **Due accessi non protetti in `FakeClaude`** (`Program.cs:151`, `FakeClaudeScenario.cs:114`): stessa
   famiglia del difetto corretto in `7m`, oggi irraggiungibili. Segnalati, non toccati.
3. **`CLAUDE.md` è stato modificato** (decisione 11) su istruzione esplicita del proprietario, benché
   sia territorio upstream: andrà risolto al prossimo merge da `upstream`.
4. ~~**Nessuna misura è stata rifatta sul VPS dopo il deploy.**~~ — **fatte il 2026-09-10**, dopo
   diciannove ore di produzione: **zero** righe «said nothing for» (erano 26 — il difetto P0
   confermato sul campo), 69 turni di chiusura, 5 riarmi del watcher su Linux, 9 errori in tutto e
   tutti di rete. Resta non misurato in produzione il costo per turno: le aperture di file vengono
   ancora dal banco di prova.

## 8. Come si verifica il lavoro qui

- `export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH`; **mai** `dotnet` sul `.slnx` da macOS (la WPF
  non compila): usa `AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`.
- La suite è verde: **un rosso è un segnale, non un costo previsto.** Se ne vedi uno, isolalo con
  `--filter` prima di dare la colpa alla tua modifica.
- Un rapporto di un agente è una pretesa: leggi il diff vero e rifai la suite. Oggi ha pescato un
  ramo con la funzione disattivata da un `false &&`, un test la cui premessa era sbagliata, e un
  commit parziale che rompeva il daemon.
