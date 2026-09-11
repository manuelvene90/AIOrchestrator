# Velocità di AI Orchestrator — dove va il tempo, e come riprenderselo

Data: 2026-09-09 · Stato: **ESEGUITO** — le sette proposte sono in `ours/integration`, riviste in modo
avversario e corrette (§7). Analisi originale in §1–6, invariata; le decisioni del proprietario in §3.6 · Autore: sessione Claude (Fable) su richiesta di Nathan

## 0. In una riga

Il tempo perso dalle orchestrazioni non è nel bridge (che gira al 3,7 % di un core) né nel modello
(un turno del supervisore dura 26–33 s mediani): è in **turni che muoiono e non ripartono** — 26 risvegli del supervisore uccisi da un
battito mai azzerato (§3.7), 11 turni implementer uccisi a 30 minuti (≈ 5,5 h) e 8 orchestrazioni
ferme da ore su un limite d'uso che il codice legge ma non usa per ripartire — e in **circa 15–20 s di attese fisse dell'app su ogni scambio con te**. La suite
di test passa il 75 % del tempo ad aspettare orologi veri.

## 1. Cos'è l'app (per chi arriva dopo)

Un bridge (`AIOrchestrator.Daemon`, C#) che fa girare più sessioni Claude Code (un supervisore per
orchestrazione, N implementer, reviewer, un supervisore generale) facendole parlare tramite file di
canale append-only in `~/.claude/supervision/<orch>/…`. Il bridge legge i canali ogni 2 s, avvia un
turno `claude -p` per chi ha traffico nuovo, specchia tutto su Telegram (un topic per orchestrazione)
e riporta nei canali ciò che il proprietario scrive dal telefono. Gira sul VPS `orch@159.195.254.120`
come servizio systemd; su Windows esiste anche la UI WPF.

## 2. Metodo e copie lette

- **Codice**: branch `ours/integration` @ `926cc6b`, checkout principale (non un worktree). Cinque
  ricognizioni parallele (Sonnet) su `Running/`, `Bridge/`, `Watchdog/Status/Planning/`, `kit/`,
  test; **ogni affermazione usata qui l'ho riverificata alla riga citata** (le costanti, il
  compattatore, i 24 `Load_All`, il commento «64 % di 3,65 MB», il log per riga, la politica di
  retry, il traduttore). Ciò che non ho riverificato è marcato `[non verificato]`.
- **Produzione, sola lettura**: VPS, 2026-09-08 23:00–23:30 CEST — `config.json` (senza id),
  `orchestrator.log.jsonl` per orchestrazione, `*.turns.jsonl`, `journalctl`, `ps`, `free`.
  Nessuna scrittura, nessun riavvio. Ho letto solo campi numerici e template dei messaggi di log,
  mai il contenuto dei canali.
- **Locale**: build e suite su questo Mac (.NET SDK 10.0.400, `~/.dotnet`), due run completi.
- **Escluso su tua indicazione**: i quattro documenti del 2026-09-08 su token e memoria fra turni.
  Dove questo studio li tocca lo dico, senza averli letti.

Marcatori: `[misurato]` = numero letto da log, output o costante; `[documentato]` = numero che il
codice o un doc dichiara di aver misurato; `[stima]` = mio calcolo.

## 3. Le misure

### 3.1 Cosa vedi tu dal telefono (fincanva-1/2/3, 7–8 set)

| Grandezza | fincanva-1 | fincanva-2 | fincanva-3 |
|---|---|---|---|
| Tuo messaggio → entry «FROM Owner» nel canale (aggregazione) | med 12 s, p90 14 s | med 11 s, p90 15 s | med 12 s, p90 17 s |
| Tuo messaggio consegnato → risposta del supervisore nel canale | med 40 s, p90 134 s | med 36 s, p90 98 s | med 42 s, p90 213 s |
| Durata turno supervisore (stream) | med 33 s, p90 72 s | med 27 s, p90 51 s | med 26 s, p90 58 s |
| Durata turno implementer (print) | med 101 s, p90 659 s | med 32 s, p90 565 s | med 5 s, p90 763 s |
| Durata turno reviewer (print) | med 7 s, p90 320 s | med 15 s, p90 111 s | med 597 s, p90 1186 s |

Tutti `[misurato]` (`orchestrator.log.jsonl`, `turns.jsonl`; n = 23–46 scambi per orchestrazione).
Manca l'ultimo tratto, canale → Telegram (traduzione + invio): non è loggato con un timestamp
accoppiabile, vedi §3.3.

### 3.2 Le attese fisse dell'app su uno scambio (costanti, tutte `[misurato]` nel codice)

| Passo | Costante | Dove |
|---|---|---|
| Aggregazione del tuo messaggio (dall'**ultimo** segmento) | 6 s | `BridgeEngineModel.cs:163 OWNER_AGGREGATION_SECONDS` |
| Tick del bridge (tutto passa da qui) | 2 s | `BridgeEngineModel.cs:97 MIRROR_TICK_MILLISECONDS` |
| Finestra di coalescenza prima di avviare un turno | 3 s | `RunnerConfigs_Factory.cs:10 DEFAULT_COALESCE_WINDOW` |
| Avvio processo `claude -p` (print, implementer/reviewer) | ~4,5 s | `StreamTurnExecutorModel.cs:18` — «p50 1,27 s stream contro 5,77 s print» `[documentato]` |
| Traduzione IT→EN in ingresso (`claude -p --model haiku`, un processo per messaggio) | spawn, timeout 45 s | `MessageTranslatorModel.cs:17,22` |
| Traduzione EN→IT in uscita (`claude -p --model sonnet`, un processo per messaggio) | spawn, timeout 45 s | `MessageTranslatorModel.cs:20,22` |

Conto su uno scambio tuo → supervisore → te: 6 s aggregazione + ≤2 s tick + [traduzione in
ingresso] + 3 s coalescenza + turno del modello (26–33 s) + ≤2 s tick + [traduzione in uscita] +
invio. Le sole costanti spiegano 6–8 s fino alla consegna nel canale; **misurati sono 11–12 s**: i
~4 s che mancano coincidono con lo spawn di un `claude -p` per la traduzione `[stima]`. Sommando
anche l'uscita, **su ~50 s percepiti circa 15–20 s sono macchina dell'app, non modello** `[stima]`.

Fra agenti (supervisore → implementer → supervisore) ogni salto paga tick + coalescenza (2–5 s) e,
per chi è in print, ~4,5 s di avvio processo: **7–10 s fissi a salto** `[misurato]` + `[documentato]`.

### 3.3 Le perdite grandi: turni che muoiono `[misurato]`

| Evento | fincanva-1 | fincanva-2 | fincanva-3 | Costo |
|---|---|---|---|---|
| Timeout di turno (uccisi a 1800 s) — **corretto 2026-09-09, vedi §3.9** | 6 | 2 | 3 | 11 kill, tutti implementer: ≈ 5,5 h di turni buttati; il rifacimento dopo il kill è durato 82–845 s |
| Stalli «failed 3 times — not retried until new traffic» | 5 | 5 | 8 | durate misurate fino alla prossima riuscita: **167–509 min**; 8 ancora aperti |
| Nudge «unread traffic for 8 min» (globale, ultime 20k righe) | 54 | | | la sveglia arriva con 8 min di granularità |
| Traduzione fallita (exit 1, globale) | 161 (+22 «failed twice») | | | ogni fallimento è uno spawn perso, e sul retry due |

Causa degli stalli aperti: `api_error_status: 429`, `result: "You've hit your weekly limit · resets
5am (Europe/Berlin)"` — sia fincanva-2 che fincanva-3 sono fermi da 07:05Z dell'8 set e alle
20:34Z il riavvio del daemon ha ritentato tre volte a 60 s la stessa entry, poi si è fermato di
nuovo. La politica (`PrintTurnDispatcherModel.cs:404-418`, `DEFAULT_RETRY_BACKOFF` 60 s,
`MAX_ATTEMPTS` 3) non conosce l'ora di reset: il dispatcher non consulta mai la finestra dei limiti
(`grep WindowResetsAtUtc Running/` → vuoto), che il bridge legge solo per gli **avvisi**
(`BridgeEngineModel.cs:2733-2823`). La ripartenza è a mano (`/resume`).

Sui timeout a 1800 s: sono turni **print** (implementer), che emettono un solo JSON alla fine — il
limite di silenzio non li riguarda e il log dei turni non contiene eventi intermedi; che cosa
facessero durante i 30 minuti **non è ricostruibile dai log** (§3.9). La stima iniziale «28 × 30 min
≈ 14 h» era sbagliata: 17 di quei 28 «timeout» erano uccisioni per battito stantio (§3.7).

### 3.4 Il bridge in sé (VPS, 2 orchestrazioni attive, idle)

Load 0,16 · daemon 94 MB RSS, 1 min CPU in 27 min ≈ **3,7 % di un core** · due processi `claude`
stream da 270–285 MB ciascuno · 5,2 GB liberi su 8 GB `[misurato]`. Oggi **non è un collo di
bottiglia**. Però il lavoro per tick cresce con il numero di orchestrazioni e membri, e ne fa molto
di inutile `[misurato nel codice]`:

- `_store.Load_All()` (enumerazione cartella + lettura e parse di ogni `session.json`) è chiamato da
  **24 punti** di `BridgeEngineModel.cs`, molti dentro lo stesso tick; nessuna cache.
- `Channel_Compactor.Compact_Gated` (`Channel_Compactor.cs:76-82`) legge e parsa **per intero ogni
  canale a ogni tick** per poi scoprire che è sotto la soglia di 90 entry.
- `Build_TopicStatusMembers` rilegge live + archivio di ogni membro aperto ogni tick; il codice
  stesso annota «64 % of 3.65 MB per tick» (`BridgeEngineModel.cs:8628`).
- `.bridge-state.json` riscritto a ogni tick anche senza cambiamenti (`BridgeState_Store.cs:117`
  «~30 times a minute»; mtime osservato avanzare in 6 s).
- Nel runner stream ogni riga stdout di `claude` fa `File.AppendAllText` sincrono
  (`TurnLog_Store.cs:162`) sul cammino di lettura.
- UI WPF: ogni evento di attività ricostruisce **tutte** le card (`MainWindow.xaml.cs:74`), senza
  debounce. Non misurabile qui (Windows). `[non verificato]`

### 3.5 Il ciclo di sviluppo (questo Mac) `[misurato]`

- Build incrementale CoreLib+Tests **0,8 s**; daemon **2,5 s**. La build non è un problema.
- Suite: **2583 test, 107 s di orologio, 27 s di CPU** (user 12,6 + sys 14,1) → **75 % del tempo è
  attesa**. Somma delle durate dei singoli test 499 s, i 30 più lenti fanno 329 s; 74 test > 1 s.
- Il singolo più lento: `OwnerAnswerSurvivesFailedSendTests.AFailedSend_…` **49 s** (attende
  `RETRY_BACKOFF_WAIT_MILLISECONDS = 33_000` reali, `:47,141`). Poi un blocco di ~20 test Bridge a
  **8,07–8,20 s** ciascuno: fanno girare il motore vero (`engine.Run_Async`) sul tick vero da 2 s e
  aspettano con `Task.Delay(100)` in `Run_Until_Async` (es. `AttachmentsReachThePhoneTests.cs:232`).
- `IClock` esiste in produzione (`Time/Clock/`) ma **nessun test lo finge**: un solo file lo cita.
  67 chiamate a `Drive_Until` sull'orologio di sistema con tetto 60 s. 13 classi serializzate nella
  collection `channel-lock` perché usano lock su file veri e spawnano bash.
- I «6 rossi noti» non sono più rossi: dal commit `b2dde29` sono `Skipped` su macOS (9 skip nel run).
  La regola in `.claude/rules/git-and-boundaries.md` è da aggiornare. L'intermittente non l'ho
  identificato `[non verificato]`.

## 3.6 Decisioni del proprietario dopo la lettura (2026-09-09, nessun codice avviato)

| # | Decisione |
|---|---|
| P1 | ok |
| P2 | **da rifare**: il timeout di 30 min com'è è sbagliato, un implementer può lavorare ore |
| P3 | a metà: sì ciclo su evento e attesa Telegram; **no** all'azzeramento della coalescenza membro → supervisore (costa risvegli da ~1 M token) |
| P4 | **non si fa** |
| P5 | **spegnere, non costruire**: regola nuova — lingua libera con il proprietario, inglese in file/codice/commit/ledger/canali agli agenti |
| P6 | non si tocca: lo chiude P1 |
| P7, P8 | ok |
| P9 | **non si fa** |

## 3.7 Trovato verificando: il battito del processo stream non viene azzerato a inizio turno (bug, `[misurato]`)

`StreamSessionProcess` misura il silenzio con `LastByteUtc`, aggiornato solo quando arriva un byte
dal processo (`StreamSessionProcess.cs:320,342`) e inizializzato alla creazione (`:44`). Quando il
bridge manda un **nuovo** prompt allo stesso processo persistente, `LastByteUtc` resta quello
dell'**ultimo turno precedente**: la prima iterazione di `Send_AndAwaitResult_Async` (`:215`) vede
`now - LastByteUtc > silenceLimit` e uccide il processo all'istante. La riga di log arriva 5 s dopo
(`CLOSE_GRACE`), da cui i «turn started … killed» a 5 s di distanza.

Conseguenza: **ogni risveglio del supervisore dopo ≥ 10 min di quiete** (600 s di
`streamSilenceSeconds`) viene ucciso, ritentato dopo 60 s su un processo **freddo**, e paga il
riavvio. Sul VPS: 26 delle 29 uccisioni per silenzio hanno questa firma (silenzio = esattamente il
tempo di quiete fra due turni, uccisione ≤ 10 s dall'avvio); 12 in fincanva-2, 12 in fincanva-3, 2 in
fincanva-1. È il **6 % dei 426 risvegli** del supervisore; per quei turni il tempo dal risveglio alla
riuscita è **111 s mediani (min 76 s)** invece dei ~25 s di un turno normale. Il caso più visibile: il
tuo messaggio delle 04:47Z di oggi a fincanva-2 — «said nothing for 29496 s» = il processo era
fermo dal 429 delle 20:36Z; risposta dopo il retry.

Questo spiega anche il punto 1(c) dell'altro agente: il timer non «si ferma durante un 429»; non
dimentica mai il turno precedente. Correzione: azzerare il battito al momento dell'invio del prompt
(una riga), con un test che manda un secondo turno dopo una quiete superiore al limite. Nessun test
oggi copre il secondo turno di un processo stream (`StreamTurnDispatcherTests.cs` copre solo il primo).
**Priorità: prima di P1** — è piccolo, misurato, e colpisce ogni orchestrazione ogni giorno.

## 3.8 Verifica delle indicazioni di un secondo agente (2026-09-09)

Nathan ha ricevuto da un altro agente un parere sui punti 1–8 (numerazione della chat, qui allineata: 6 = nudge, 7 = bridge, 8 = test, 9 = protocollo) e ha chiesto di verificarlo, non di
prenderlo per vero. Esito, con la fonte:

| Punto | Affermazione | Esito |
|---|---|---|
| 1(a) | ripartire solo il turno in attesa, mai una richiesta chiusa (decisione 8) | **confermato** — `CLAUDE.md` decisione 8, e la firma di stallo del dispatcher è per set di entry pendenti (`PrintTurnDispatcherModel.cs:413`) |
| 1(b) | parse tollerante dell'ora di reset, log invece di eccezioni | ragionevole; niente da verificare |
| 1(c) | «il controllo del silenzio è scattato dopo 8 h invece di 10 min» | **fatto vero, spiegazione sbagliata**: non è il timer che si ferma, è il battito mai azzerato (§3.7). La ripartenza automatica da sola non basta: senza §3.7 il primo turno dopo il reset verrebbe ucciso |
| 2(a) | classificare i 28 kill «da turns.jsonl + metadati» | **da correggere**: un turno ucciso **non** produce un record `result` (0 record con durata ≥ 1790 s); si classifica dagli eventi stream con lo stesso `aiorch_request_id`, che nel log ci sono |
| 2(b) | `--resume` della trascrizione uccisa esiste anche in modalità fresh; `--max-budget-usd` come tetto | **confermato**: in fresh l'id è reclamato prima dell'avvio (`PrintTurnDispatcherModel.cs:751-758`); `--max-budget-usd` è nella CLI installata (`claude --help`, riga 123); C1.3 esiste come titolo nella spec token (non letta oltre il titolo) |
| 2(c) | i turni lunghi sono da 50–92 chiamate e 4–12 M token; il round 1 ha reso fresh implementer e reviewer | **confermato nella sostanza**: 31 turni ≥ 10 min, `num_turns` mediana 32 e max 92, input mediano 6,8 M e **max 30 M** (più dei 12 M dichiarati); `config.json.pre-round1-20260908-223404` aveva implementer/reviewer `transcript`, oggi `fresh` |
| 3 | un risveglio del supervisore costa ~1 M token; 247 su ~400 arrivano dai membri | **confermato**: 436 turni, input mediano 913 k token (p90 2,3 M); inneschi su 4 orchestrazioni: owner 134, membri 274, misti 10 |
| 5 | regola «100 % inglese» da sostituire nelle skill di supervisor e general | **confermato** che la regola sta lì (`general-supervisor/SKILL.md:114-115,200`, `supervisor/SKILL.md:111-114`); l'attribuzione alla «decisione 11» è imprecisa (la 11 riguarda il toggle `/italian`) |
| 6 | «0 turni innescati su 673 nudge» | **numeri sbagliati, sostanza giusta**: 76 nudge in tutto, 7 seguiti da un turno del membro entro 90 s. Il nudge sveglia raramente; la conclusione «non abbassare, chiude il punto 1» regge |
| 7 | il pack (stage/4b, live da oggi) legge la storia del canale a ogni turno fresco | **confermato**: `stage/4b-state-pack` è interamente in `ours/integration` (0 commit in più), `StatePackInputs_Reader.cs:53,136` usa `ChannelHistory_Counter.Read_AllEntries`, e il simbolo è nel binario in esecuzione (`/opt/aiorchestrator/AIOrchestratorCoreLib.dll`, build 10:58 di oggi) |
| 8 | orologio finto nei test, 107 → ~30 s | coincide con P8 |

Decisioni di Nathan recepite: **P4 e P9 (protocollo) non si fanno.** P3 a metà: sì al ciclo su
evento e all'attesa Telegram, **no** all'azzeramento della coalescenza per il traffico membro →
supervisore (ogni risveglio in più costa ~1 M token: i numeri sopra lo confermano); azzerare solo il
percorso owner → supervisore.

## 3.9 Classificazione dei turni uccisi a 30 minuti (VPS, solo metadati, 2026-09-09)

42 righe «attempt N timeout» nei log; il tempo fra avvio e uccisione si divide in due gruppi netti:
31 sotto i 60 s (il battito stantio, §3.7) e **11 a ~1800 s** — i veri kill al `DEFAULT_TURN_TIMEOUT`.
Tutti e 11 sono implementer (print): fincanva-1 6, fincanva-2 2, fincanva-3 3. Nessun supervisore
o reviewer. Per ognuno il log dei turni ha un solo record, il marcatore sintetico scritto dal bridge
al kill (`exit_code -1`), perché il runner print produce un unico JSON alla fine: **quale strumento
fosse in corso e quante chiamate avesse fatto non si può sapere**. Tutti gli 11 request id sono poi
riusciti al tentativo successivo in 82–845 s (uno al terzo tentativo). Tre kill (fincanva-2/imp-3/3,
fincanva-3/imp-2/1, fincanva-3/imp-3/1) hanno lo stesso istante di avvio, 00:57:15Z dell'8 set: turni
partiti insieme a un riavvio del daemon e morti insieme 30 minuti dopo.

Conseguenze per P2: il costo misurato è ≈ 5,5 h di turni buttati (non 14) più 30 min di attesa per
ogni kill prima che il lavoro riprenda; il turno di chiusura resta la forma giusta (salva lo stato
di un turno che stava lavorando invece di perderlo), ma il suo valore è più piccolo di quanto scritto
in §3.3. Tabella completa: `killed-turns.md` nello scratchpad della sessione (non nel repo).

## 4. Proposte, in ordine effetto/costo

Numerazione allineata alla lista discussa in chat: P0 battito · P1 limite · P2 timeout · P3 attese ·
P4 stream (non si fa) · P5 traduttore · P6 nudge · P7 bridge · P8 test · P9 protocollo (non si fa).

Ogni proposta ha un «fatto quando» misurabile. Nessuna è iniziata.

### P0 — Azzerare il battito del processo stream a inizio turno (effetto: 6 % dei risvegli del supervisore da 111 s a ~25 s, niente processi freddi · costo: una riga + un test)
Vedi §3.7. Va prima di P1: senza, il primo turno dopo un reset del limite viene ucciso.
**Fatto quando:** un test manda un secondo turno a un processo stream dopo una quiete > limite e il turno
completa; sul VPS zero righe «said nothing for N s» con N uguale alla quiete precedente.

### P1 — Ripartire da soli quando il limite si riapre (effetto: ore per incidente · costo: piccolo)
Quando un turno fallisce con 429 e il testo porta un'ora di reset, il dispatcher programma il
ritentativo a quell'ora (+ jitter) invece di 3 × 60 s e poi il silenzio; la finestra dei limiti che
il bridge già legge per gli avvisi diventa anche la sveglia degli stalli.
**Fatto quando:** un test che dà al dispatcher un risultato 429 «resets 5am» mostra il turno
ripianificato a quell'ora e non prima; sul VPS un'orchestrazione ferma per limite riparte senza
`/resume` (log: nessun «failed 3 times» seguito da ore di silenzio).

### P2 — Turni uccisi a 30 min: «rapporto e chiudi», non «più tempo» (effetto: ≈ 5,5 h/36 h misurate + 30 min di attesa per kill · costo: medio)
**Decisione del proprietario (2026-09-09): com'è ora è sbagliato — un implementer può lavorare
ore.** Forma scelta dopo il confronto con il secondo agente: **non** alzare i 30 minuti (i turni lunghi
sono quelli da 32–92 chiamate e 7–30 M token in ingresso: più tempo = più contesto), ma allo scadere
fare un turno di chiusura — `--resume` della trascrizione appena uccisa (l'id è reclamato prima
dell'avvio anche in modalità fresh) con un prompt «scrivi dove sei e fermati» e un tetto duro con
`--max-budget-usd` — e ripartire freschi col pack al turno dopo. Urgente da quando implementer e
reviewer sono `fresh` (round 1): prima un turno ucciso riprendeva il lavoro via trascrizione, ora lo
perde. Primo passo: classificare i 28 kill dagli eventi stream (`aiorch_request_id`), solo metadati.
**Fatto quando:** ogni «attempt N timeout» è seguito nel log da un turno di chiusura riuscito e da
un'entry di rapporto nel canale; zero lavoro ripetuto da zero.

### P3 — Tagliare le attese fisse, ma solo sul percorso owner → supervisore (effetto: ~11 s → ~3 s a scambio con te `[stima]` · costo: piccolo)
Ciclo 2 s → reazione al cambio file (con il ciclo come rete); aggregazione Telegram 6 → 3 s o zero
per un messaggio singolo e completo. **La coalescenza di 3 s resta** per il traffico membro →
supervisore: fa viaggiare insieme le entry vicine, e ogni risveglio del supervisore in più costa ~1 M
token in ingresso (§3.8). Si azzera solo per il percorso owner → supervisore.
**Fatto quando:** «buffered → delivered» mediano ≤ 4 s nel log; risvegli/ora del supervisore
uguali o minori di prima (misurati con `tools/token-gate/`), non maggiori.

### P4 — [NON SI FA — decisione del proprietario 2026-09-09] Implementer e reviewer su runner stream (effetto: ~4,5 s a turno + niente riavvio di contesto · costo: configurazione + memoria)
È la modalità già usata dal supervisore (`runners.supervisor.runner: stream`). Un processo stream
tiene 270–285 MB: con supervisore + 3 membri fa ~1,1 GB per orchestrazione, sui 5 GB liberi del VPS
— da sorvegliare con il cap `sessionMemoryMax` già presente.
**Fatto quando:** durata mediana dei turni implementer brevi (≤ 10 s oggi = quasi solo avvio) cala
di ≥ 3 s sul VPS.

### P5 — Spegnere il traduttore, non ricostruirlo (effetto: 4–8 s a messaggio `[stima]` e 183 fallimenti in meno · costo: zero costruzione)
**Decisione del proprietario (2026-09-09): la regola cambia.** Lingua libera nella conversazione con
il proprietario; inglese per tutto ciò che finisce in file, codice, commit, ledger e canali verso gli
altri agenti. Con questa regola il traduttore non serve: gli agenti gestiscono le lingue da soli.
Perché esisteva: la skill del supervisore gli **proibisce** di rispondere in italiano
(`kit/skills/supervisor/SKILL.md:109` — «you still answer in English. Never mirror their language»),
quindi l'app, che sta in mezzo e non capisce le lingue, traduceva con un `claude -p` a parte, due volte a
scambio; e il divieto stava lì perché, con il traduttore acceso, un testo già in italiano sarebbe
passato due volte. Un cerchio: si tolgono insieme.
Tre passi, nessuno di codice: (1) `/italian` dal telefono spegne il layer oggi — è acceso per default
(`DEFAULT_TELEGRAM_ITALIAN_LAYER = true`) e il `config.json` del VPS non lo spegne; (2) cancellare
il divieto dalla skill del supervisore, sostituito da un promemoria di una riga («con il proprietario
la sua lingua; file, codice, commit, ledger e canali agli agenti in inglese») — testo, ma le skill le
porta l'app: build dal checkout principale e riavvio perché arrivi al VPS; (3) il codice del
traduttore resta inutilizzato finché qualcuno decide di rimuoverlo (voce PARKED, non un lavoro).
Conseguenza accettata: il traffico fra agenti specchiato su Telegram arriva in inglese.
**Fatto quando:** nel log del VPS zero righe «Translation call» dopo lo spegnimento; il supervisore
risponde a un messaggio italiano in italiano e il suo brief successivo all'implementer è in inglese.

### P6 — Nudge a 8 minuti: non si tocca (lo chiude P1)
Sul VPS 76 nudge in tutto, 7 seguiti da un turno del membro entro 90 s: il nudge sveglia raramente, e
ogni nudge è un'entry «FROM App» in più che una sessione fresca rilegge. Il ritardo che segnala lo
elimina P1 (i turni non restano più fermi su un limite). Direzione: contabilità fuori dal canale, non
più frequente.
**Fatto quando:** nulla da fare qui; dopo P1 i «had unread traffic — nudged» nel log devono calare.

### P7 — Igiene del tick (effetto: robustezza con N orchestrazioni, non velocità percepita oggi · costo: medio)
Uno snapshot di `Load_All` per tick condiviso dai 24 chiamanti; il compattatore decide dal conteggio
entry già noto al tailer, senza rileggere; le status line dai conteggi in cache; `.bridge-state.json`
scritto solo se un offset è cambiato; log dei turni bufferizzato.
**Fatto quando:** file aperti per tick (`strace -c` sul VPS per 60 s) ridotti ≥ 80 %; CPU del daemon
idle ≤ 1 % di un core.

### P8 — Suite a orologio finto (effetto: 107 s → 25–30 s `[stima]` · costo: medio)
Un `FakeClock` che i test Bridge iniettano al posto di `SystemClock`, così il tick e l'aggregazione
avanzano su comando; via il `Task.Delay(33_000)`; `Drive_Until` sul clock finto; la collection
`channel-lock` su cartelle temp isolate per poter tornare parallela.
**Fatto quando:** `dotnet test AIOrchestratorCoreLib.Tests` ≤ 30 s con **2574 passed, 9 skipped**,
zero nuovi rossi.

### P9 — [NON SI FA — decisione del proprietario 2026-09-09] Protocollo
Review incrociata obbligatoria per ogni riga del ledger, una sessione per ogni deliverable, rilettura
dell'intero canale «a ogni confine», divieto di sub-agent per il supervisore: ognuna moltiplica i
salti di §3.2. Sono scelte di governo con un costo in tempo noto; non le tocco senza una tua
decisione. `[inferenza dal testo delle skill in kit/]`

## 5. Cosa NON ho verificato

- La latenza reale della traduzione (nessun timestamp accoppiabile nel log): i 4–8 s sono stima.
- Che cosa facevano i turni uccisi a 1800 s.
- La durata effettiva di un tick sul VPS (non è strumentata).
- La UI WPF (non gira su questo Mac).
- Il campo `duration_api_ms` di `turns.jsonl`: somma più del `duration_ms`, non l'ho usato.
- Il test intermittente citato nella regola del repo.

## 6. Comandi usati (riproducibilità)

Build/test locale: `DOTNET_ROOT=~/.dotnet ~/.dotnet/dotnet build AIOrchestratorCoreLib.Tests/…` e
`dotnet test … --no-build --logger trx`, tempi con `/usr/bin/time -p`.
VPS: `ssh orch@159.195.254.120` + `jq`/`python3` su `~/.claude/supervision/*/orchestrator.log.jsonl`
e `*turns.jsonl` (solo campi `ts`, `message`, `duration_ms`, `num_turns`, `is_error`,
`api_error_status`, `result` del solo record d'errore); `systemctl status`, `ps`, `free`, `journalctl`.


---

# 7. Esito — che cosa è stato fatto, e che cosa gli audit hanno trovato

Scritto a fine giornata, 2026-09-09. Tutto ciò che segue è in `ours/integration` (`9c6b8d4`),
pushato su `origin`, **non sul VPS**: il daemon in produzione gira ancora il binario del mattino.

## 7.1 Le sette proposte, più due che non erano nel piano

| # | Che cosa fa ora | Rami |
|---|---|---|
| P0 | Il battito del processo stream riparte quando gli si manda il prompt: un risveglio dopo una quiete non è più scambiato per silenzio | `7a` |
| P1 | Un rifiuto per limite d'uso compra un appuntamento **limitato a 6 ore**, mai per una parola sola, e lo dice al proprietario con la data | `7b`, `7i` |
| P2 | Un turno ucciso alla scadenza scrive dove è arrivato, e **consuma solo ciò a cui ha risposto** | `7f`, `7j` |
| P3 | Un'aggiunta a un canale sveglia il ciclo; il tuo messaggio finito aspetta 2 s invece di 6 | `7e`, `7k` |
| P5 | Il traduttore **non esiste più**: layer, flag, comando `/italian`, 47 test del suo mondo | `7c`, `7h` |
| P7 | Il tick legge ciò che è cambiato: **198 → 44 aperture di file, 1 → 0 scritture** | `7g` |
| P8 | La suite gira su tempi iniettati: **107 s → 37 s**, nessuna asserzione indebolita | `7d` |
| + | Le diagnostiche del lock non sono più rubabili: l'intermittente storico del repo è chiuso | `7l` |
| + | Il test di P0 giudica il turno giusto, non l'intera corsa | `7l` |

P4 (implementer su runner stream) e P9 (regole di protocollo): **non fatte**, decisione del proprietario.

## 7.2 L'audit avversario, e perché è servito

Quattro revisori indipendenti, in worktree isolati, con l'obbligo di **provare** ogni difetto con una
sonda invece di segnalarne di teorici. Hanno trovato difetti veri in **tutte e tre** le modifiche che
cambiano il comportamento vivo. I peggiori, tutti confermati e tutti corretti:

- **P1** parcheggiava una sessione **24 ore** quando l'ora di reset era appena passata, e ogni
  rifiuto successivo aggiungeva un giorno — con il traffico nuovo che non svegliava più nulla.
  Peggio del comportamento sostituito. E il cancello d'ingresso era la parola «limit»: un turno
  **riuscito**, il cui unico problema era un canale bloccato, veniva parcheggiato 14 ore senza
  spendere un tentativo, quindi senza mai fermarsi né avvisare.
- **P2** poteva **mangiarsi il messaggio del proprietario**: i cursori avanzavano su tutti i canali
  mentre il rapporto di chiusura ne indirizzava uno solo. E il record scritto per primo dichiarava
  «un turno di chiusura è stato eseguito» su cinque rami su sei dove non era vero.
- **P3** aveva ridotto **di dodici volte** il margine che impedisce di specchiare un'entry a metà:
  con uno scrittore che si ferma 400 ms l'entry arrivava troncata e **la coda distrutta**, non
  ritardata. Più due difetti da ~1 M token: un punto finale nel messaggio del proprietario
  raddoppiava il turno del supervisore, e un rapporto arrivato un attimo dopo ne comprava un altro.

Il revisore di P3 ha anche attaccato cinque possibili corse dentro il watcher e **non ne ha rotta
nessuna**: i difetti stavano in ciò che l'accelerazione costava altrove, non nel meccanismo.

## 7.3 I prezzi pagati, detti chiaramente

- **L'ultima entry di un canale torna a impiegare ~4 s** per arrivare sul telefono, come prima del
  watcher. È il prezzo di non troncarla. Il guadagno sul percorso del proprietario resta: il
  dispatcher legge i file direttamente e non passa dal tailer.
- **Due messaggi a più di 2 s di distanza comprano ancora due turni.** Non esiste una forma che
  consegni subito il primo e coalizzi il secondo: la grazia deve essere almeno la distanza. 2 s è un
  giudizio, ed è una costante che si sposta.
- **La UI Windows è modificata alla cieca** (spunta della lingua e sezione delle impostazioni):
  quel progetto non compila su macOS. **Serve una build su Windows prima di dirla verificata.**
- **`--max-budget-usd` non limita la spesa**: misurato il 2026-09-09, con cap 0,0001 $ ha comunque
  speso 0,11 $ — il tetto è controllato dopo il passo, non prima.

## 7.4 Stato della suite

**2751 verdi, 9 skip, 0 rossi**, in cinque corse consecutive sul main finale; 1 m 10 s – 1 m 30 s.
Il tempo è salito dai 37 s di P8 perché sono stati aggiunti ~150 test.

La regola del repo che parla di «6 rossi noti + 1 intermittente» **non è più vera** e va riscritta:
i sei sono skip onesti dal commit `b2dde29`, e l'intermittente è chiuso da `7l`. La formulazione
suggerita: *«la suite è verde: 0 rossi, 9 skip (6 impossibili su macOS, 3 smoke live). Confronta
l'insieme dei nomi, mai il numero.»* Il file appartiene al proprietario del repo e non è stato
toccato.

Da segnalare anche: la **decisione 11** del `CLAUDE.md` descrive `/italian` e
`Create_WithItalianLayer`, che non esistono più.

## 7.5 Che cosa NON è stato verificato

- Il comportamento su **Linux** del watcher quando la cartella osservata sparisce: qui è macOS
  (FSEvents), il VPS è inotify. Il re-arm è stato scritto per quella forma ma è **inferito**.
- La **UI Windows** (vedi 7.3).
- Il **VPS**: niente di tutto questo è stato messo in produzione, e nessuna misura è stata rifatta lì
  dopo le modifiche. I numeri di §3 restano quelli del binario del mattino.
