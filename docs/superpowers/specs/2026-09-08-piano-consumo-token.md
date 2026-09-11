# Piano — consumo token di AI Orchestrator

**Data:** 8 settembre 2026 · **Stato:** proposta, non approvata · **Lingua:** italiano (documento di lavoro per l'owner; la spec che entrerà nel repo sarà in inglese come da convenzione)

**Provenienza:** due audit indipendenti, condotti separatamente e messi a confronto. Audit A (questa sessione) e audit B (seconda sessione, senza accesso alle conclusioni di A). Dove i due divergono è segnalato. Ogni cifra è marcata `[MISURATO]` o `[stima]`.

**Copie lette.** Codice: branch `ours/integration` HEAD `1fafaba` sul Mac; clone sul VPS allo stesso HEAD; plugin installato identico a `kit/`. Dati vivi: VPS `orch@159.195.254.120`, sola lettura — trascrizioni di Claude Code (`~/.claude/projects`), registri del bridge (`turns.jsonl`), `orchestrator.log.jsonl`, `config.json`. Finestra: 6 → 8 settembre 2026.

---

## 0. REVISIONE 2 — correzioni dopo il challenge (8 set, sera)

Un terzo agente ha sottoposto questo piano a challenge adversariale. Quanto segue **prevale** su §2 e §4, che vanno letti alla luce di queste correzioni (verificate da me sul VPS dopo il suo rapporto).

### Errori corretti

| cosa diceva questo piano | valore corretto `[MISURATO]` | causa dell'errore |
|---|---|---|
| 18.480 chiamate API | **9.730** | la CLI scrive una riga per blocco di contenuto (thinking / testo / uso strumento) con lo **stesso `message.id` e lo stesso `usage`**: 18.492 righe, 9.730 id distinti. Sommare per riga conta doppio |
| 3,76 miliardi di token | **2,04 miliardi** (rilettura 1.986 M, scrittura cache 47,9 M, output 4,28 M) | conseguenza del doppio conteggio (×1,85) |
| sessioni principali 10.744 / 3.168 M · sub-agenti 7.736 / 478 M | **5.850 / 1.735 M · 3.880 / 250 M** | idem |
| contesto medio per chiamata 197.000 | **209.000** | i duplicati condividevano il contesto, quindi il rapporto teneva |
| **66 % del consumo in turni non completati** | **19–27 % delle sessioni principali** (17–24 % del totale) | doppio errore mio: dividevo un registro deduplicato per trascrizioni contate due volte, **e** trattavo come lavoro morto un 14 % che è sotto-riporto contabile della CLI (l'`usage` del risultato si azzera alle notifiche di task in background). L'audit B (28 %) era vicino al vero |
| "cache da 5 minuti" per i sub-agenti | **prevale la cache da 1 ora**: `ephemeral_1h` 74,5 M contro `ephemeral_5m` 30,2 M | assunzione mai verificata. Il challenge diceva "5m = 0": anche questo è falso, il 5m esiste ed è il 29 % |

### Cose che il challenge ha aggiunto, verificate

- **Il contesto trascinato è l'83 %** del totale (boot 10 %, crescita dentro il turno 8 %). Per ruolo: supervisor 91 %, implementer 87 %, reviewer 75 %, general 0 %. È la misura che questo piano affermava senza dimostrare.
- **Sui ruoli `print` non esiste sensore live.** Config viva: supervisor = `stream`; implementer, reviewer, solo, communicator = `print`; general = `print` + `fresh`. Il budget "avviso all'80 %" **non può funzionare** sul ruolo che consuma di più: in `print` l'app non vede niente fino al risultato finale.
- **I riavvii del daemon uccidono turni:** 76 righe di stop/start nella finestra, ~112 M persi (6 %). Nessuno dei primi due audit l'ha visto. Rimedio gratuito: drenare i turni prima del riavvio.
- **Il registro è cieco anche sui turni completati** (14 %) e sui sub-agenti (250 M). Il contatore nuovo deve **deduplicare per `message.id`** e sommare le sidechain — l'errore che ho fatto io è la prova che serve.
- **`--max-budget-usd` esiste** nella CLI installata (2.1.263), e chiude il §8: esistono anche `autoCompactWindow`, `CLAUDE_CODE_AUTO_COMPACT_WINDOW` e `CLAUDE_CODE_DISABLE_1M_CONTEXT`. La finestra da 1 M è dichiarata dalla CLI stessa (`contextWindow: 1000000`).
- **Taratura sui token di oggi = tarare sul male.** Va tarata su **chiamate** (p50 2, p80 9, p90 18) o sulla crescita interna al turno.
- **Un tentativo ucciso non produce il foglio finale.** Quindi "non si rigioca mai" in forma assoluta è peggio di oggi (dove il `--resume` conserva il ragionamento parziale): serve la clausola *un tentativo ucciso ha diritto a un turno di chiusura sulla propria trascrizione, poi tappa fresca*.
- **I solleciti non innescano turni:** 0 su 673. Spegnerli non è una voce di risparmio (resta igiene).

### La cosa che il challenge ha sbagliato, e che presentava come più grave

Sosteneva che **questo Mac** consumasse sullo stesso limite settimanale del VPS (1.109 M contro 2.040 M), dichiarando l'ipotesi "inferenza, file account non leggibili". Verificato: **sono due account distinti** — Mac `nathan.vene@lastminute.com` (org di lavoro), VPS `nathanvene@gmail.com` (piano Max personale). Il consumo del Mac (2.073 M in tre giorni, deduplicato) è reale ma **non tocca il limite che è stato esaurito**. Il passo "decidere del Mac" esce dal piano; la coincidenza temporale che aveva osservato resta senza spiegazione e non serve.

### Effetto atteso, rivisto

Non 4–8 volte. Intervallo onesto: **2–5 volte** — fino a 5,5× sul contesto (limite superiore misurato, ignora foglio, `PLAN.md` e voci pendenti), 2–2,5× se il limite settimanale pesa il costo di listino invece dei token (la rilettura è ~68 % del costo, non il 97 % dei token). Quale dei due sia, resta la cosa non misurabile del §8.

### Ordine di rilascio, rivisto — prevale su §5

0. **Le cose gratuite:** drenare i turni prima del riavvio del daemon (~6 %); contatore deduplicato per `message.id` con le sidechain sommate.
1. **L'esperimento a zero codice:** `resume: fresh` per implementer e reviewer su **una** orchestrazione, dalla config. È il regime in cui il general supervisor gira già. Due giorni di misura. Se il rapporto è sotto 2×, la tesi non regge e si ferma tutto qui.
2. **Il foglio (`STATE:`), forma minima** — solo se il passo 1 mostra rilavoro.
3. **Supervisor fresco a ogni risveglio** (91 % di trascinamento, il ruolo con il rapporto migliore).
4. **Modello degli implementer** (sonnet di default, opus a richiesta nel brief): leva parallela, gratuita.
5. **Il budget, per ultimo** — con sensore dalla trascrizione su disco (vale anche per `print`), avviso via hook e `--max-budget-usd` come rete dura. Con sessioni fresche i turni da cento chiamate non esistono più e i timeout dovrebbero sparire da soli: se dopo il passo 3 i kill sono sotto il 2 %, questo passo è una rete e non un rimedio.

**Aggiunta mia al metodo:** l'A/B non si fa "due giorni prima / due giorni dopo" — confronterebbe lavori diversi in periodi diversi. Il sistema tiene aperte più orchestrazioni: si mette `fresh` su una e si lascia `transcript` su un'altra **nello stesso periodo**, e si confrontano token per consegna. Il confounding sparisce.

---

## 1. Il problema in una riga

**Costo = somma del contesto di ogni singola chiamata al modello.** Oggi il contesto di una sessione è la sua conversazione intera, che cresce per tutta la giornata e non si azzera mai. Ogni gesto — leggere un file, lanciare un comando — la rilegge da capo.

Quattro moltiplicatori sopra un'unica causa (la memoria della sessione è la trascrizione):

1. **Il contesto non si azzera.** Cresce fino al tetto della finestra.
2. **I turni sono monoliti.** Il protocollo dice "non fermarti mai"; il bridge taglia a 30 minuti e fa **ricominciare da capo**.
3. **Il contatore dell'app registra solo i turni completati.** È cieco esattamente sulla parte sprecata.
4. **Ciò che entra nel contesto non esce più.** Output di test, file stampati interi, elenchi lunghi: serviti trenta secondi, riletti fino a sera.

---

## 2. Le misure

> **Attenzione:** le cifre assolute di questa sezione sono superate dalla §0 (doppio conteggio corretto: dividere per 1,85). I rapporti e le curve di crescita reggono.

### Totali (audit A, trascrizioni sul VPS, nessun duplicato: 18.480 righe / 18.480 uuid distinti) `[MISURATO]`

| voce | valore | quota |
|---|---|---|
| chiamate API | 18.480 | |
| token riletti (cache read) | 3.646 M | **97,0 %** |
| scrittura cache | 104,7 M | 2,8 % |
| output (lavoro prodotto) | 10,8 M | 0,29 % |
| input fresco | 0,045 M | ~0 |
| **totale** | **3,76 miliardi in ~2 giorni** | |
| contesto medio per chiamata | 197.000 token | |

Ripartizione: sessioni principali 10.744 chiamate / 3.168 M riletti · sub-agenti (sidechain) 7.736 chiamate / 478 M · uno-shot del daemon e traduzioni 13 M (**0,4 %** — voce chiusa, non è un problema).

Per giorno: 7 set 2.239 M su 11.140 chiamate · 8 set 1.396 M su 6.996 chiamate (fino a metà giornata).

### La crescita del contesto `[MISURATO]`

Stessa sessione supervisor, contesto per chiamata: **49.000 → 400.000 → 745.000**, monotono, e la sessione successiva riparte da 750.000 e arriva a 862.000. Nessun azzeramento. Audit B misura un massimo di **965.000** su un implementer, con 2 sole compattazioni automatiche in tutta la finestra.

### Il lavoro buttato — **divergenza aperta fra i due audit**

| | metodo | risultato |
|---|---|---|
| audit B | incrocio temporale fra finestre dei tentativi uccisi e chiamate (±5 s, dichiarato incerto) | **28 %** del totale · 48 % dei token implementer · 28 timeout (media 774 s, max 1.801 s = tetto 30 min) · 26 kill a 600 s di silenzio |
| audit A | aritmetica fra registro e trascrizioni, nessun incrocio di orari: registro dei turni completati 1.069 M contro 3.168 M delle sessioni principali | **66 %** dei token delle sessioni principali sta fuori da qualunque turno completato |

**Da chiudere prima di fissare le priorità e i criteri di "fatto"** (§7): si prende **un** tentativo ucciso, si sommano le chiamate della trascrizione nella sua finestra e si confronta con quanto il registro ha scritto per quella sessione. L'assunzione da confermare è che l'`usage` di un turno completato aggreghi tutte le sue chiamate interne; se sotto-riporta, parte del 66 % è contabilità e non lavoro morto.

### Il controesempio interno `[MISURATO]`

Il **general supervisor** è l'unico ruolo con `resume: fresh`: **30–39.000 token di contesto per chiamata**, contro 375.000 del supervisor e 357.000 dell'implementer. Stesso codice, stessa macchina. **La forma del consumo è una scelta di architettura, non del modello.**

### Effetto sul limite

Audit B: limite settimanale dal 37 % (6 set 20:07) al 100 % (8 set ~09:00) — 63 % del budget in 37 ore. Audit A: 18 turni chiusi con *"You've hit your weekly limit"* su 4 sessioni.

### Verificato sul codice e sulla CLI installata `[MISURATO]`

- `--autocompact <auto|tokens>` **esiste** nella CLI installata; il bridge non lo usa (`--settings` passato `null` in entrambi i call site).
- Lo scrub delle variabili `CLAUDE_CODE_*` esiste, **ma il dizionario `environment` viene applicato dopo lo scrub**: il bridge può già iniettare qualunque variabile senza toccare la protezione contro l'annidamento.
- Costanti: timeout di turno 30 min · silenzio stream 600 s · 3 tentativi con 60 s di attesa.
- **Non verificati:** la chiave `autoCompactWindow` nel file settings e la variabile `CLAUDE_CODE_DISABLE_1M_CONTEXT`. Da verificare prima di dipenderne.
- Già risolto e deployato: i respawn "ORPHANED" (182 il 7 set, tutti falsi positivi) sono zero dopo il 7.

---

## 3. La soluzione: la memoria di una tappa è dell'app, non della trascrizione

**Principio:** *lo stato durevole sta su disco; la conversazione di una sessione è materiale usa-e-getta.*

Oggi non è vero, e per questo un riavvio è un incidente. Se diventa vero, il riavvio è un'operazione di routine — e rotazione, watchdog, riavvio del bridge, cambio di modello a metà lavoro diventano la stessa cosa, gratis. È il guadagno strutturale, indipendente dal risparmio.

### Tre livelli di memoria, tutti fuori dalla trascrizione

- **`PLAN.md`** (esiste): il libro mastro dell'endeavour, del supervisor. Invariato.
- **I canali** (esistono): traccia di controllo e unico meccanismo che "sveglia". Il bridge consegna già solo le voci nuove.
- **Il foglio per membro** (nuovo, ≤ 8 KB ≈ 2.000 token): la memoria di lavoro — obiettivo della tappa, stato (branch, file e righe, comandi che verificano), fatto / da fare, domande aperte, **vicoli ciechi**. Sostituito a ogni tappa, mai accodato. Lo scrive la sessione in un blocco dedicato nel messaggio finale; il bridge lo valida e lo salva.

La trascrizione resta su disco come registro per audit e contabilità, non come memoria.

### Dal "turno" alla "tappa"

Una tappa = un processo con **sessione nuova** (mai riprendere la precedente), il cui prompt è: istruzioni di ruolo + foglio + voci pendenti + `PLAN.md` se supervisor.

**Una tappa è un passo grande quanto un commit:** si chiude su uno stato **stabile e verificabile** — per chi scrive codice: test verdi e commit, e il foglio è il messaggio di commit più i prossimi passi con file e righe esatti; per chi non scrive (reviewer, indagini): il verdetto o i risultati depositati nel canale. Git porta lo stato, il foglio porta l'intenzione, la rilettura diventa mirata invece che integrale.

**Il confine è morbido, non un'accetta.** All'80 % del budget la sessione riceve un avviso ("porta il lavoro a un punto stabile e chiudi"); il divieto scatta solo oltre il 120 %, **e lascia sempre passare le operazioni di chiusura** (commit e scrittura del foglio). È il modello a scegliere il punto di taglio.

**La continuazione non passa dal supervisor.** Il messaggio finale dichiara lo stato: continuo / fatto / in attesa / bloccato. Con "continuo" il bridge apre subito la tappa successiva. Il lavoro corre fino alla fine dell'endeavour come oggi — la garanzia per l'owner è identica — ma il contesto riparte da 10–15.000 invece che da 800.000. Tetto di tappe consecutive per brief (12): se un brief le consuma è la spia del rilavoro, e interviene il **supervisor**, non il bridge.

**Un tentativo interrotto non si rigioca mai.** La tappa dopo parte fresca dal foglio, con una nota del bridge che dice dopo quanto si è interrotta e su quale operazione.

### Il budget

- **In token, non in numero di comandi.** È il volume che costa: sessanta `grep` da cinque righe non costano niente, cinque comandi che stampano file interi mangiano la tappa. Lo stream porta già il consumo di ogni chiamata in tempo reale, quindi la misura c'è.
- **Effetto collaterale desiderato:** l'igiene sui comandi si fa rispettare **da sola** — chi stampa un file intero brucia la propria tappa. Nessuna regola da sperare che venga seguita (e il repo ha già imparato che una regola solo consigliata non tiene).
- **La stazza la dà il supervisor nel brief** (piccolo / normale / grande), non un numero di chiamate: un supervisore brieffa magro e non ha letto il codice, sa stimare la stazza e non le chiamate. L'app traduce la taglia in token usando la distribuzione misurata.
- **Taratura sui dati, non a occhio:** il tetto di default si sceglie in modo che **almeno l'80 % dei turni di oggi resti una tappa sola**. Non si spezzetta il lavoro normale: si tosa la coda — e la coda oggi viene già spezzata dalla scure del timeout, in un punto casuale e senza foglio.
- **Adattivo ma non automatico:** se una classe di compiti chiude sistematicamente a budget pieno, l'app **propone** il numero nuovo con la misura in mano; l'ultima parola è dell'owner. Un budget che si alza da solo disfa il rimedio senza che nessuno l'abbia deciso.

### Contabilità di prima classe

Il registro si costruisce dalle trascrizioni / dallo stream (la fonte che contiene anche i tentativi uccisi e i sub-agenti, oggi invisibili). Un solo lettore, come già impone la decisione 10 del repo. Da lì `/tokens`, `/cost` e gli allarmi di limite. Metrica nuova visibile all'owner: **token per cosa consegnata** — il numero che dice se stiamo migliorando.

Budget per tappa, per membro/giorno, per orchestrazione/giorno; alla soglia si usa il gate di pausa che già esiste, più una domanda su Telegram con il numero in chiaro.

### Politica di finestra

Tetto con `--autocompact` come **rete che non deve mai scattare** (se scatta, è un allarme da registro). **La finestra da un milione non si disabilita:** con tappe da 10–15.000 è solo margine, e spegnerla trasforma una degradazione morbida in un muro contro cui sbatte la tappa che ha legittimamente bisogno di una lettura grossa. Le impostazioni si passano tramite il dizionario `environment`, che è già applicato dopo lo scrub: nessuna protezione da indebolire.

### Dieta e rumore

Le istruzioni del supervisor (87 KB, ricaricate a ogni avvio) si spezzano in un nucleo breve più approfondimenti letti a bisogno; il file del watcher esce dal percorso headless dove non serve. Si spengono i solleciti che partono mentre una tappa è in corso e i controlli "è morto?" che in headless non hanno senso.

### Scartate

- **Solo le manopole** (tetto e timeout più corti): cura il sintomo, la trascrizione resta la memoria e cresce fino alla soglia; la compattazione automatica è un riassunto deciso dal modello, non un contratto. Tenuta come rete, non come soluzione.
- **Modelli più piccoli:** non cambia i token; cambia il peso sul limite. Leva legittima ma ortogonale.
- **Cervello esternalizzato** (supervisor sostituito da logica dell'app e modelli piccoli): massimo risparmio, ma il giudizio del supervisor è la cosa che si paga volentieri. Non ora; questo piano non lo preclude.
- **Firewall con i sub-agenti** (proposta iniziale dell'audit A): **declassata**. Misurati 478 M in 7.736 chiamate, con cache da 5 minuti: ogni sub-agente ricostruisce il proprio prefisso. Togliere materiale dal padre resta giusto, ma va misurato, non assunto.

---

## 4. Effetto atteso

> **Superato dalla §0:** l'intervallo onesto è 2–5×, non 4–8×.

`[stima, costruita sui rapporti misurati]`

- Sparisce il grosso del lavoro buttato (28 % secondo l'audit B, 66 % delle sessioni principali secondo l'audit A — §2).
- Contesto medio per chiamata: da 197.000 a 40–70.000.
- Complessivo: **4–8 volte meno token per la stessa quantità di lavoro consegnato.**
- Costo nuovo da tenere d'occhio: il pavimento del boot, pagato più spesso. Il conto dice che il vantaggio si annullerebbe solo se il ri-orientamento moltiplicasse le chiamate per **circa quattro**; rileggere un foglio da due pagine non costa niente di simile.

---

## 5. Ordine di rilascio

> **Superato dalla §0:** vale l'ordine rivisto (gratuite → esperimento a zero codice → foglio → supervisor → modello → budget).

Pacchetti indipendenti, ciascuno mergeabile da solo, con prova A/B su **una sola** orchestrazione prima di estendere.

1. **Tetto immediato e silenzio** — `--autocompact`, spegnimento dei solleciti durante una tappa e dei controlli orfani in headless. Un giorno, nessun cambio strutturale, misurabile subito.
2. **Il contatore vero** — registro dalle trascrizioni, un solo lettore, metrica "token per consegna". **Non risparmia nulla: è lo strumento della verifica**, e per questo va prima delle tappe (con il contatore attuale, che vede un terzo, l'A/B non è misurabile).
3. **Tappe, foglio, stato dichiarato** — bridge, hook, protocolli. Qui c'è il grosso del risparmio.
4. **Budget in token, pausa, domanda all'owner** — più la taratura sull'80 % e la proposta adattiva.
5. **Dieta delle istruzioni di ruolo e igiene dei comandi.**

Prima di iniziare: **chiudere la divergenza 28 % / 66 %** (§2).

---

## 6. Rischi e difese

### I tre modi di perdere qualità

| rischio | difesa |
|---|---|
| **Comprensione "calda" persa** — il modello rilegge ma non ha le conclusioni che aveva ricavato → rilavoro | Puntatori esatti a file e righe (rilettura mirata) **e** la sezione obbligatoria dei **vicoli ciechi**: non i puntatori, le conclusioni. È la sola parte che né git né il canale possono ricostruire; la sua assenza vale un rifiuto quando lo stato è "continuo". Residuo: non azzerabile. |
| **Taglio nel momento sbagliato** — modifica a metà, test rossi | Il confine morbido (avviso all'80 %, il modello sceglie il punto), la chiusura su stato stabile verificabile (commit e test verdi, o verdetto depositato), e il divieto che lascia passare la chiusura. |
| **Foglio che non cattura lo stato** → la tappa dopo riparte storta | Schema obbligatorio con **fatti verificabili** (commit, branch, comando che dimostra), rifiuto della chiusura senza foglio, e sotto: git, `PLAN.md` e i canali, che sono memoria vera. Se il foglio è scritto male, **lo stato sopravvive comunque nel commit**. |

### Frammentazione

Il pavimento del boot **si paga già a ogni chiamata**, non a ogni turno: spezzare lo sposta su una base più bassa, non lo moltiplica. Il rischio vero non è il consumo ma il **pensiero corto** — un agente che sa di poter essere fermato prende la decisione più piccola. Difese: budget generoso e tarato sui dati (80 % dei turni intatti), continuazione automatica e gratuita, fermarsi non deve mai essere premiato. Spia: tappe per consegna.

### Altri

Più scrittura di cache (oggi 2,8 %: sale, resta marginale). Compattazione in-tappa che taglia istruzioni: tarata per non scattare mai; se scatta è un allarme. Riscrittura del protocollo "non fermarti mai": è una regola nata da incidenti reali (sessioni che si addormentavano) — la garanzia va conservata parola per parola nella nuova forma, non indebolita.

---

## 7. Criteri di "fatto"

Due giorni di misura prima, due dopo, sulla stessa classe di compiti, **stessi script dell'audit**. Si guardano **due colonne**, non una.

**Token:** contesto medio per chiamata da 197.000 a < 70.000 · quota di token in chiamate sopra 200.000 da ~76 % a ~0 · token in tentativi non completati alla cifra stabilita in §2 → < 5 % · registro dell'app allineato alle trascrizioni entro il 10 %.

**Lavoro consegnato:** numero di consegne stabile · output per consegna stabile (**se crolla, il lavoro non si sta facendo**) · tappe per consegna ≤ 1,5 volte i turni di oggi.

**E una cosa che nessuna metrica vede:** la qualità dei verdetti del supervisor. Se dopo il cambio diventano superficiali, ci si ferma. Va guardata con gli occhi.

---

## 8. Cosa NON è verificato

- La divergenza 28 % / 66 % sul lavoro buttato (§2) — **da chiudere prima di partire**.
- Se l'`usage` di un turno completato aggreghi le sue chiamate interne e i sub-agenti (non documentato): è l'assunzione sotto il 66 %.
- La chiave `autoCompactWindow` e la variabile `CLAUDE_CODE_DISABLE_1M_CONTEXT` (il flag `--autocompact`, invece, è verificato).
- Se la finestra da un milione sia il default del piano o un'attivazione: misurato l'effetto, non la causa.
- Se il limite settimanale pesi la rilettura come l'input fresco (non documentato).
- Il contenuto dei messaggi: nessuno dei due audit ha letto cosa facevano i turni più costosi, solo quanto pesavano.
- Il numero atteso di 4–8 volte è una stima costruita su rapporti misurati, non una misura.

---

## 9. Decisioni aperte per l'owner

1. Si procede con l'ordine di §5 (tetto → contatore → tappe → budget → dieta)?
2. Il vincolo di taratura "almeno l'80 % dei turni di oggi resta una tappa sola" va scritto come requisito?
3. Il budget adattivo propone e l'owner approva — confermato?
4. Chi scrive la spec e in quale worktree.

---

*Nessuna parte di questo piano è stata certificata da chi l'ha scritta. L'audit A e l'audit B si sono controllati a vicenda; ciò che dimostra il risultato è il confronto prima/dopo di §7, con il contatore nuovo già in funzione.*
