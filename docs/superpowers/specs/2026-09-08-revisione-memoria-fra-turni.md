# Revisione indipendente — memoria fra turni in AIOrchestrator ("PLAN + STATE")

**Data:** 2026-09-08 · **Revisore:** sessione indipendente (Fable 5.1, scelto da Nathan come modello di sessione) · **Stato:** parere, nessuna modifica fatta.

## Verdetto in una riga

La direzione è giusta e i dati la reggono (fresh batte transcript in token appena la trascrizione supera ~50 k, cioè dentro il primo turno), ma il "passo zero senza codice" **non è a zero rischio**: in `fresh` il runner print non consegna le entry pendenti (`PrintTurnExecutorModel.cs:40`), la config `resume` è globale per ruolo (niente A/B concorrente senza codice), e il foglio `STATE:` finisce nel canale e su Telegram se il bridge non lo toglie — tre cose da sistemare prima, non dopo.

---

## Copie lette e metodo

| cosa | copia | come lo so |
|---|---|---|
| Codice | sorgente del branch `ours/integration`, HEAD `1fafaba`, checkout Mac `/Users/nvene/Visual Studio/AIOrchestrator` | `git rev-parse --short HEAD` → `1fafaba` |
| Kit installato sul VPS | `~/.claude/plugins/cache/aiorch-local/aiorch/1.0.0`, `gitCommitSha` `1fafaba…` (`installed_plugins.json`); `SKILL.md` byte-identici al branch (87 271 / 24 951 / 21 696 / 29 818) | `wc -c` su entrambe le copie |
| Daemon in esecuzione | `/opt/aiorchestrator/aiorchestrator-daemon`, build del 2026-09-08 04:17; **commit non registrato, non verificato da me** (nessun `.git`, nessun VERSION) | `ls -la /opt/aiorchestrator` |
| Config viva | `~/.claude/supervision/config.json` sul VPS: supervisor `stream/transcript`, implementer·reviewer·solo·communicator `print/transcript`, general `print/fresh`, `settings: null` ovunque, `streamSilenceSeconds: 600` | `jq '{runners, printRunner}'` |
| Dati | metadati delle trascrizioni (`usage`, `timestamp`, `model`, `requestId`, nomi tool) in `~/.claude/projects/*/*.jsonl` + `*/*/subagents/*.jsonl`; `turns.jsonl`; `print-session.json`; `orchestrator.log.jsonl`; dimensioni dei canali. **Mai il testo dei messaggi.** | script `usage_agg.py` / `turns_agg.py` (dedupe per `requestId`, ultima riga per chiamata) |

**Trasparenza.** (1) La sezione "LA SOLUZIONE PROPOSTA" era nel prompt: l'ho letta prima di iniziare, non potevo evitarlo. Ho scritto la fase 1 (file `fase1-progetto.md`, ore 22:40 circa) **prima** di aprire i due documenti in `docs/superpowers/specs/2026-09-08-*.md`, ma non posso chiamarla cieca al 100 %. (2) Ho lasciato quattro file di lavoro in `/tmp` sul VPS (`usage_agg.py`, `turns_agg.py`, `usage_agg.json`, `turns_rows.json`): scritture innocue fuori da ogni dato del sistema, ma scritture — le segnalo; non le ho rimosse per non fare un'altra scrittura. (3) Nessuna chiamata a un modello è stata fatta sul VPS.

---

## Misure che ho rifatto (VPS, 6→8 set 2026)

Tutte `[MISURATO]` salvo dove marcato. Confermano i fatti dati nel prompt.

| grandezza | valore | fonte |
|---|---|---|
| Token totali, main + sub-agenti, dedupe per `requestId` | **2 039 982 694**; sub-agenti 266 M = **13,1 %** | `usage_agg.py` + glob `subagents/` |
| Chiamate | 5 890 main + 3 880 sub = **9 770** | idem |
| Quota cache_read / output | **97,8 % / 0,22 %** | idem |
| Cache 5 min | 13,6 M, **solo** sulle sidechain; main: `ephemeral_1h` 34,2 M, 5m = 0 | idem |
| Compattazioni CLI | **2** (marker `summary`/`isCompactSummary`) — entrambe a ~940–965 k | idem |
| Implementer, 172 turni print: ctx prima chiamata mediana / p90 | **249 k / 719 k** | `turns.jsonl → usage.iterations[0]` |
| Implementer: token nuovi (cache_creation+input) per turno, mediana / media | **6 k / 60 k** | idem |
| Implementer: token per turno mediana / media; output mediana | **690 k / 3,26 M; 2,8 k** | idem |
| Reviewer, 109 turni: ctx prima chiamata mediana; token/turno mediana / media | **241 k; 448 k / 1,03 M** | idem |
| Entry pendenti per turno (651 turni eseguiti) | **mediana 1, media 1,56, p90 1, max 204** | `print-session.json → executed_turns` |
| Boot = ctx della prima chiamata della sessione | implementer **33,9–46,6 k**; reviewer 33,9–44,5 k; supervisor **55,9–56,9 k**; general (haiku) 29,7–32 k | trascrizioni, `ctx_first` |
| Canali membri live | 40–166 KB, 45–85 entry; `FROM app` = **6–13 % dei byte** | `wc -c`, `awk` |
| Archivio (`channel.archive.md`) aperto da una sessione | **2** tool-use in tutta la finestra (grep del literal nei blocchi `tool_use`, nessun testo letto) | grep |
| Doc obbligatori Fincanva (skill implementer: "read the repo's mandated docs" quando c'è un task) | `AGENTS.md` 15,7 KB + `Platform/AGENTS.md` 23,2 KB = **38,9 KB ≈ 10–12 k token** [stima 3,5 B/tok] | `wc -c` |
| Gap fra turni consecutivi dello stesso membro | mediana **1,9 min**, p90 50 min, 9 % > 60 min | `turns_rows.json` |
| Retry | 130 righe "attempt 2", 30 "attempt 3" nei log; 34 "killed", 28 "silence" | `grep -c` su `orchestrator*.log.jsonl` |
| Cache del boot fresco (general) | 95 % cache_read, 5 % cache_creation → il boot fresco è quasi sempre un prefisso già in cache | trascrizioni |
| Consegne (righe `- [x]` in PLAN.md) vs turni | fincanva-2: **22 righe / 329 turni** (81 imp + 66 rev + 182 sup) ≈ 15 turni per riga; fincanva-1: 5 / 204 ≈ 41 | `grep -c`, `jq` |
| Repo Fincanva dal 6 set | 140 commit, 23 merge (`git log --since`) | read-only |

Calibrazione byte→token per le skill [stima]: delta boot supervisor−implementer = 22 k per 62 KB di skill in più → **~2,8–3,2 byte/token**. Quindi skill implementer ≈ **8 k**, reviewer ≈ 7 k, supervisor ≈ **27–30 k**; la base (system prompt di Claude Code + definizioni tool + MCP `ianus`) ≈ **26–39 k**. **Conseguenza:** per i membri il boot è dominato da Claude Code, non dalla skill.

---

## FASE 1 — il mio progetto (scritto prima di leggere la proposta)

### Famiglie, e perché

| | famiglia | tengo? | perché |
|---|---|---|---|
| a | fresh per turno + canale e git (zero codice) | **sì, come esperimento e pavimento** | è il regime del general supervisor (39 k/chiamata). Costo per turno mediano implementer [stima]: boot 45 k → +28 k lettura canale (100 KB) → +11 k doc → 3–4 chiamate ≈ **300–380 k** contro 690 k (1,8–2,3×); sul turno medio (3,26 M) molto di più. Debolezza: la lettura del canale è la voce variabile più grossa e non è quello che serve (il 6–13 % è bookkeeping, il resto è storia già chiusa); il brief può stare nell'archivio. |
| b | fresh + foglio di stato scritto dalla sessione | **sì, ma subordinato a un digest del bridge** | il foglio aggiunge ciò che né git né canale hanno (prossimo passo file:riga, vicoli ciechi). Ma è scritto da chi non lo rileggerà: può mentire, invecchiare, gonfiarsi. Va trattato come arricchimento, non come fonte primaria. |
| c | trascrizione tenuta ma potata (`--autocompact <token>`) | **sì, come tappo immediato e come rete**, non come soluzione | il bridge oggi non passa né `--settings` né `--autocompact` (`PrintTurnExecutorModel.cs:39`, `StreamTurnExecutorModel.cs:211`: `settingsFile` = `null`); il flag esiste in 2.1.263 (`--help` sul binario VPS). Un tetto a ~150–200 k taglierebbe da solo il 76 % dei token che oggi stanno in chiamate > 200 k, a costo di un riassunto deciso dal modello (opaco, non verificabile) e di una chiamata piena a ogni scatto. Zero cambi di protocollo, reversibile. Interazione con `--resume` fra processi: **non verificata**. |
| d | `--fork-session` / riassunto da modello piccolo a fine turno | **no** | fork non risparmia nulla (stessa trascrizione). Il riassunto esterno costa una chiamata a contesto pieno (≥ 250 k) per produrre ciò che la sessione stessa scrive in 300–800 token di output nel messaggio finale. Ha senso solo come *riparazione dopo un kill*, leggendo canale+git, non la trascrizione. |
| e | stato JSON con schema | **no; blocco a chiavi fisse in markdown** | `--json-schema` esiste ma il contratto è "il messaggio finale È la entry" (`print-runner.md`): un output JSON romperebbe la scrittura del canale. Lo schema compra la presenza dei campi, non la loro verità; ciò che conta (prossimo passo, vicoli ciechi) è prosa. Cinque chiavi fisse + parser da venti righe bastano. |
| f | CLAUDE.md / memoria di Claude Code nella cartella del membro | **solo per il general** (già così: `general/CLAUDE.md`, 4 KB, cwd = sua cartella) | i membri hanno cwd = repo/worktree (`print-session.json → working_directory`): un CLAUDE.md lì finisce nell'albero git (il pericolo che la decisione 3 ha eliminato) e la memoria automatica per progetto è **condivisa fra imp-1 e imp-2** sullo stesso repo (contaminazione). |
| g | **"tappe a soglia" + digest del bridge** | **era la mia scelta; rivista sotto** | riprendere la trascrizione finché il ctx della prima chiamata dell'ultimo turno < T (sensore già su disco: `usage.iterations[0]` in `turns.jsonl`) e finché non si chiude una riga di ledger; al reset, il bridge compone un digest deterministico. |

### Il progetto (versione fase 1)

1. **Digest del bridge al boot fresco** — il prompt porta: il brief (entry `FROM supervisor` più vecchia senza risposta, cercata su live **e** archivio con `ChannelHistory_Counter`, decisione 13), l'ultima entry propria del membro (il suo report = il suo stato), il blocco `STATE:` se c'era, le righe PLAN.md che lo riguardano, le entry pendenti. La skill smette di dire "leggi il canale da cima a fondo".
2. **`STATE:` opzionale**, cinque chiavi fisse (goal · branch+commit · comando che verifica · prossimo passo file:riga · vicoli ciechi), tetto ~1,5 k token per **troncamento**, mai rifiuto; **tolto dalla entry** prima che vada nel canale e su Telegram; hook Stop che **avvisa**, non blocca (decisione 21).
3. **Kill:** un solo `--resume` di chiusura con `--max-budget-usd`, poi tappa fresca dal digest.
4. **Tappe a soglia T** (150 k) come compromesso continuità/costo.
5. **Dieta della skill solo per il supervisor** (27–30 k); per i membri il boot è 26–39 k di Claude Code + 8 k di skill.
6. **Misura:** token per riga di ledger chiusa; distribuzione del ctx alla prima chiamata; brief ri-emessi e verdetti REWORK; retry per turno.

### Costo per turno [stima con calcolo]

Turno mediano implementer (3 chiamate, 6 k nuovi, 1,56 entry):
- **oggi:** 249 k × 3 + crescita ≈ **690 k** (mediana misurata).
- **(a) fresh + canale:** 45 k → 73 k → 84 k → 90 k ≈ **290–380 k** (1,8–2,4×).
- **digest + STATE:** prima chiamata 45 k boot + 2 k digest + 1 k pendenti = 48 k; se codice +11 k doc; ~3–5 chiamate a 50–75 k ≈ **150–300 k** (2,3–4,6×). Sul turno **medio** (3,26 M, ~10 chiamate): 10 × ~85 k ≈ 850 k → **~3,8×**.
- Dopo il cambio il boot è **~90 % della chiamata** (45 k su 50 k), di cui skill 8 k.

### Autocritica dopo i dati

La "tappa a soglia T" (punto 4) è una copertura sulla continuità che **costa token**: con T = 150 k una chiamata costa fino a 3× una fresca, e fresh batte transcript in token appena la trascrizione supera ~50–60 k, cioè dentro il primo turno (il boot da solo è 34–47 k). La soglia va tenuta solo come **terzo gradino**, se fresh+digest mostra rilavoro sui task di debugging che nessun foglio cattura. Il mio primo istinto sovrappesava la continuità; i dati dicono di misurarla prima di pagarla.

---

## FASE 2 — confronto punto per punto con la proposta

Letti dopo la fase 1: `docs/superpowers/specs/2026-09-08-piano-consumo-token.md` (23 KB, con la §0 "Revisione 2") e `2026-09-08-token-efficiency-design.md` (18,7 KB, C1–C9, rollout 0–6). Entrambi non tracciati in git. Dove parlo di "la proposta" intendo la spec inglese, che dichiara di prevalere.

| punto | proposta | mia fase 1 | chi è meglio, e perché |
|---|---|---|---|
| Regime | fresh sempre (C1.1), poi tappe con `continue` | tappe a soglia T, poi fresh | **proposta**: in token fresh vince subito; la soglia è una copertura da pagare solo se il rilavoro è misurato. |
| Cosa entra nel prompt | C2 "state pack": entry nuove, `state.md`, PLAN.md (sup), roster | digest del bridge: brief + ultima entry propria + STATE + righe PLAN del membro + pendenti | **mia**: il pack della proposta **non contiene il brief** né l'ultima entry del membro; il fallback "ultime tre entry" (C1.2) non garantisce il brief. Con 1,56 entry/turno e 3 canali su 25 già compattati (45 entry tenute, `Channel_Compactor.cs:20`), il brief di un membro longevo può stare nell'archivio, che nessuno rilegge (2 letture in tutto). Il bridge lo trova gratis (decisione 13). |
| Chi scrive lo stato | la sessione (`STATE:`), non validato in v1 | il bridge compone; la sessione arricchisce | **mia**, per robustezza: un digest deterministico non mente e non invecchia; il foglio è il complemento. La proposta riconosce il punto (C1.2 "git holds the state") ma lo mette come fallback invece che come base. |
| Dimensione del foglio | ≤ 8 KB ≈ 2 k | ≤ ~1,5 k troncato | **mia**: 8 KB × ogni turno **nel canale** (il messaggio finale È la entry, `print-runner.md`) gonfia esattamente il file che una sessione fresca rilegge, e arriva sul telefono via mirror. La proposta non dice che il blocco viene tolto dalla entry — **va detto**. |
| Kill | C1.3: un resume di chiusura, poi fresh | identico | pari. Bene che abbia sostituito il "mai rigiocato" del piano italiano. |
| Validazione | nessuna in v1 | parse + troncamento + hook Stop che avvisa | **mia, di poco**: costa nulla e dà la misura "quota di tappe con STATE". Nessun rifiuto in entrambe — giusto. |
| Passo zero "senza codice" | `resume: fresh` su una orchestrazione per due giorni | (a) come esperimento | **entrambe sbagliano la fattibilità**: `runners.<ruolo>.resume` è **globale per ruolo** (`RunnerConfigs_Json.cs:9-16`, letto in `PrintTurnDispatcherModel.cs:659`); `session.json` ha solo `SupervisorModelOverride`/`ImplementerModelOverride` (`IOrchestrationSession.cs:41-42`). L'A/B concorrente della §0 **richiede codice**. E in `fresh` il print **non passa nulla su stdin** (`PrintTurnExecutorModel.cs:40`): niente `[bridge turn]`, niente elenco delle entry nuove; la sessione deve dedurre da sola cosa è nuovo, cosa che la skill del general prevede ("channel is a LOG", `general-supervisor/SKILL.md:128-135`) e quella dell'implementer **no** (`print-runner.md`: "from the second turn on the prompt carries it"). Lo stream, invece, in boot fresco manda comunque il follow-up (`StreamTurnExecutorModel.cs:132`). |
| Supervisor | fresh per ultimo (passo 4), con C4 digest dei risvegli | non trattato bene | **proposta**, ma **C4 va prima di C1-sup**: oggi ~1 M token per risveglio (2,5 chiamate × ~400 k) e metà dei risvegli sono da membri: dimezzare i risvegli è un 2× sul ruolo più caro **senza toccare la memoria**. Il pack del supervisor deve portare anche la **coda dell'owner-channel** (5 entry), non solo le pendenti: le domande dell'owner rimandano a scambi precedenti. |
| Dieta skill (C5) | "boot ≈ metà del conto residuo", nucleo ≤ 15 KB per ogni ruolo | solo supervisor | **mia**: per i membri la skill è ~8 k su 34–47 k di boot; il grosso è system prompt + tool + MCP (`.mcp.json` ianus è presente in Fincanva). Prima **misurare la base** (una `-p` vuota con e senza MCP) e poi decidere se la leva è `--strict-mcp-config`/deferral dei tool o la dieta. Per il supervisor (27–30 k) la dieta vale. |
| `--autocompact` | rete che non deve mai scattare | tappo immediato + rete | **mia**: come **passo 0,5** è il taglio più rapido e reversibile del 76 % che sta sopra 200 k mentre l'esperimento fresh gira; i due scatti osservati a ~940 k dimostrano che funziona. Da misurare: scatti, rilavoro dopo lo scatto. La proposta lo declassa a "cura del sintomo" — vero, ma è il sintomo che ha esaurito il limite settimanale. |
| Contabilità (C7) | dalle trascrizioni, attribuzione per `[bridge turn]` prompt | per session id di tappa | **mia**: in `fresh` print il `[bridge turn]` **non esiste** (stdin nullo); la chiave è il **session id per tappa**, che il dispatcher logga (`PrintTurnDispatcherModel.cs:718`) ma `executed_turns` **non conserva** (`IExecutedTurn.cs`: solo TurnNumber, RequestId, indici, esito, costo). Da aggiungere. |
| Verifica dell'ordine env/scrub | "inherited claim, not re-checked" | verificato | scrub a `PrintTurnRunnerModel.cs:116` e `StreamSessionProcess.cs:118`, `environment` applicato **dopo** (`:120`, `:122`). Confermato. |
| "ephemeral_5m 0" | spec: 5m = 0 | 5m = 13,6 M **sulle sidechain**, 0 sul main | la spec generalizza troppo: i sub-agenti ricostruiscono il prefisso in cache breve. Il piano italiano (§0) l'aveva giusto. |

---

## Risposte

### 1. La proposta è ideale?

No, ma è **giusta nella direzione e nell'80 % dei pezzi**. Migliore della mia dove sceglie fresh subito e misura il rilavoro prima di costruire (passo 1→2), dove separa canale da bookkeeping (C3) e dove aggiunge il drenaggio al riavvio (C8) e il contatore vero (C7). Peggiore in quattro punti concreti: (i) il pack non porta il brief né l'ultima entry del membro — la memoria che serve davvero — e si affida a un foglio scritto dal modello; (ii) il passo "senza codice" non è tale: config globale per ruolo e stdin nullo in fresh print; (iii) il foglio da 8 KB entra nel canale e su Telegram se non viene tolto; (iv) l'ordine mette C4 (meno risvegli del supervisor) dopo il supervisor fresco, quando è il taglio più sicuro sul ruolo più caro.

### 2. Costo per turno della proposta [stima con calcolo]

Implementer, tappa tipo (dopo i passi 2–3):
- boot 34–47 k [misurato] + `state.md` 0,5–2 k + pendenti 1 k (1,56 × ~600) = **prima chiamata 36–50 k**;
- se la tappa tocca codice: +10–12 k doc obbligatori Fincanva **per tappa** (oggi per sessione) + 10–30 k [stima] di riletture dei file caldi;
- chiamate per tappa: oggi mediana ~3 (`num_turns` sovrastima, [approssimato]); attese +1–3 di riorientamento → 4–6;
- **tappa "ack" (nessun codice): 2 × ~45 k ≈ 90 k** contro ~500 k oggi (2 × 249 k) → **~5×**;
- **tappa di codice: ~8 chiamate da 50→110 k ≈ 650 k** contro 3,26 M (media oggi) → **~5×**; con rilavoro forte (riletture doppie, una domanda in più) → 2×.

Supervisor (passo 4): boot 56 k + PLAN.md 2–4 k (8–15 KB) + coda owner 2 k + pendenti 1–3 k (multi-sorgente) ≈ **62–65 k**; 2,5 chiamate → ~170 k per risveglio contro ~1 M → **~6×**; con C4 (metà dei risvegli) → **~12× sul ruolo**.

Turni/tappe per consegna: oggi ~15 turni per riga chiusa su fincanva-2 [stima, righe disomogenee]. Mi aspetto **+20–50 % di tappe** (riorientamento, split `continue`), non +50 % secco. Token per consegna: oggi ≈ 23 M per riga su fincanva-2 [stima: 507 M dei membri+sup / 22 righe]; atteso **6–9 M** → guadagno netto **2,5–4×**, intervallo onesto **1,8× (debugging, rilavoro alto) – 5× (lavoro meccanico ben brieffato)**. Coincide con il 3–5× della spec; il 5,5× è un tetto teorico che ignora pack e riletture.

### 3. Modi di fallire del foglio

| modo | prob. | impatto | mitigazione più semplice |
|---|---|---|---|
| **Stato stantio** (tappa killata, foglio della tappa prima) | media (retry ≈ 20 % dei turni oggi: 130 "attempt 2" su ~650) | medio: riparte da un passo indietro | C1.3 (un resume di chiusura); il pack porta anche `git status/log -3` del worktree, che non invecchia |
| **Stato che mente** (commit dichiarato non esistente, "test verdi" non verdi) | bassa-media | alto: la tappa dopo costruisce sul falso | il bridge verifica le due chiavi verificabili (`git cat-file -e <commit>`; il comando di verifica è **dichiarato**, non eseguito) e marca `[non verificato]` nel pack; il reviewer resta il giudice |
| **Foglio che gonfia** (8 KB a turno nel canale e su Telegram) | **alta** se non tolto dalla entry | medio: rilegge il gonfiore ogni boot; rumore all'owner (decisioni 14–15) | il bridge **stacca** il blocco dalla entry, lo salva in `state.md`, tetto 1,5–2 k per troncamento |
| **Foglio che sostituisce la lettura del codice** | media | alto sui task di design/debug | il foglio porta **puntatori e vicoli ciechi**, mai contenuto di file; regola di skill: "verifica il puntatore prima di usarlo" (costa una `Read`) |
| **Brief perso** (compattato, fuori dalle ultime 3 entry) | media per membri longevi (3/25 canali compattati; 45 entry ≈ 29 turni) | alto: lavora al task sbagliato o chiede | il bridge mette il brief nel pack cercandolo su live+archivio |
| **Due membri che si contraddicono** (imp-1 e imp-2 sullo stesso file) | bassa (worktree disgiunti, decisione 16) | medio | fuori dal foglio: è il `PARALLEL UNITS` del brief; il foglio è per membro e non va condiviso |
| **Kill prima della scrittura** | media | medio | C1.3; fallback = ultima entry propria + git |

### 4. Il bridge deve validare?

**No al rifiuto, sì al parse difensivo.** Un rifiuto del fine-turno costa un altro turno pieno (proprio ciò che si vuole risparmiare) e le hook Stop già mostrano il limite: bloccano l'onesto, non il disattento (decisione 21). Il bridge: parse delle chiavi, tetto per troncamento, stacco dalla entry, controllo del commit (`cat-file`), log di "STATE assente/malformato". **Evidenza per decidere di più:** dopo due giorni di passo 2, se la quota di tappe senza STATE > 20 % **e** correla con i segnali di rilavoro (brief ri-emessi, REWORK), si aggiunge l'hook Stop **che avvisa** (kit, zero C#); se il problema è "STATE presente ma stantio/falso", nessuna validazione lo cura — serve il digest del bridge.

### 5. Alternative scartate troppo in fretta / da scartare

Troppo in fretta: **`--autocompact` come tappo immediato** (vedi tabella); **la variabile d'ambiente `CLAUDE_CODE_AUTO_COMPACT_WINDOW` via `environment`** (applicato dopo lo scrub — verificato); **il digest del bridge come fonte primaria** (la proposta lo relega a fallback); **la misura della base del boot** prima della dieta (potrebbe rivelare che il 60 % del boot è tool/MCP, non skill). Da scartare, e la proposta lo fa: riassunti da modello esterno, fork-session, JSON schema, memoria per cartella per i membri. Io scarterei anche **il budget in `--max-budget-usd`** come *misura*: è in dollari, cambia con il modello (C6) e non è ciò che l'owner ragiona; serve solo come rete dura, e la spec lo dice.

### 6. L'ordine dei passi

Giusto in grande (gratuite → esperimento → foglio → supervisor → budget). Sposterei:
- **prima del passo 1:** i tre pre-requisiti che rendono "zero codice" vero — override `resume` per orchestrazione (o accettare un A/B **sequenziale** normalizzato per consegna), follow-up su stdin anche in fresh (`PrintTurnExecutorModel.cs:40`) o nel prompt posizionale [modo da verificare], stacco di `STATE:` dalla entry;
- **0,5:** `--autocompact` a ~200 k sui ruoli transcript, misurato, mentre l'esperimento gira;
- **C4 (meno risvegli del supervisor) prima di C1-supervisor**: taglio grande, rischio zero sulla memoria;
- **C7 con session id per tappa in `executed_turns`** prima del passo 1, altrimenti l'A/B non è attribuibile per i print fresh;
- **C5 dieta** dopo una misura della base: forse non è la leva.

### 7. Cosa avrei costruito io, carta bianca dentro i vincoli

Un bridge che **consegna la memoria invece di aspettare che la sessione se la cerchi**: sessione fresca a ogni turno; prompt = digest deterministico (brief, ultima entry propria, `git status/log -3` del worktree, righe PLAN del membro, pendenti, STATE se c'è); `STATE:` opzionale a cinque chiavi, staccato dalla entry, troncato, verificato dove verificabile; un resume di chiusura sul kill; risvegli del supervisor a digest; contabilità per session id di tappa. Hook solo per avvisare. Niente soglia T finché il rilavoro non è misurato. Tutto in pacchetti `stage/*` piccoli: (1) override per orchestrazione + stdin in fresh, (2) digest, (3) STATE, (4) C4, (5) C7/C8.

### 8. I criteri distinguono "risparmio" da "meno lavoro fatto"?

**Non abbastanza.** "Token per consegna" con consegna = riga `[x]` è Goodhart-abile (il supervisor può spezzare le righe; già oggi 22 righe su fincanva-2 contro 5 su fincanva-1 con più turni). "Chiamate-strumento per consegna entro +30 %" non separa **riorientamento** (letture in più, attese e innocue) da **rilavoro** (modifiche rifatte). Riscrittura:
- **Unità di consegna:** riga di ledger chiusa con verdetto del reviewer **e** commit mergiato — **e** con la riga ancorata a una `OWNER REQUESTS` (decisione 22), contate a inizio esperimento e congelate; minimo **10 consegne per braccio**.
- **Colonna token:** token per consegna (dedupe, sidechain incluse) ≥ 3× giù; ctx mediano della prima chiamata < 70 k; quota di token in chiamate > 200 k < 5 %.
- **Colonna lavoro, tre segnali separati:** (a) **chiamate di produzione** (`Edit`/`Write`/commit) per consegna entro ±15 %; (b) **chiamate di orientamento** (`Read`/`Grep`/`Bash` di sola lettura) per consegna riportate, senza soglia (ci si aspetta che salgano); (c) **rilavoro**: brief ri-emessi per la stessa riga, verdetti REWORK/REJECT per riga, commit `fix`/`fixup` dopo verdetto — **entro +10 %**.
- **Robustezza:** tentativi per turno ≤ 1,1; kill < 2 % dei token.
- **Qualità:** l'owner legge **10 verdetti per braccio, mescolati e senza etichetta**, e non distingue il braccio meglio del caso. Senza cieco, "non peggiori" è un'impressione.

---

## Tengo / butto / cambio

| voce della proposta | verdetto | confidenza | nota |
|---|---|---|---|
| Principio "stato su disco, conversazione usa-e-getta" | **tengo** | alta | i dati (fresh vince sopra ~50 k) lo confermano |
| C8 drenaggio al riavvio | **tengo, primo** | alta | 6 % gratis |
| C7 contatore dalle trascrizioni | **cambio**: chiave = session id per tappa, non `[bridge turn]` | alta | stdin nullo in fresh print |
| Passo 1 "zero codice" | **cambio**: tre pre-requisiti (override per orch, follow-up in fresh, stacco STATE) | alta | config globale per ruolo verificata |
| C1.2 `STATE:` opzionale, ≤ 8 KB | **cambio**: ≤ 1,5–2 k, staccato dalla entry, due chiavi verificate | alta | canale = ciò che una fresca rilegge |
| C1.3 resume di chiusura | **tengo** | alta | |
| C2 state pack | **cambio**: + brief (live+archivio) + ultima entry propria + git status; per il sup + coda owner | alta | il pack oggi non porta la memoria che serve |
| C3 bookkeeping fuori dal canale | **tengo** | media | vale 6–13 % dei byte, meno di quanto suggerito; giusto comunque |
| C4 digest dei risvegli del supervisor | **tengo, anticipo** | alta | ~2× sul ruolo più caro senza toccare la memoria |
| C5 dieta skill per tutti i ruoli | **cambio**: prima misura la base; dieta solo supervisor finché non provato | media | skill membri ≈ 8 k su 34–47 k |
| C6 modello per task | **tengo** (ortogonale ai token) | media | fuori tema di questa revisione |
| C9 budget per ultimo | **tengo** | media | `--max-budget-usd` solo come rete |
| `--autocompact` "solo rete" | **cambio**: anche tappo immediato (passo 0,5) misurato | media | interazione con `--resume` fra processi non verificata |
| Cap 12 tappe `continue` | **tengo** | media | |
| Stima 3–5× | **tengo** (mio intervallo 1,8–5×) | media | il 5,5× è un tetto |
| Criteri di "fatto" | **cambio** (vedi 8) | alta | |

---

## Cosa NON ho verificato

- Se `claude -p <prompt posizionale>` accetti **anche** un prompt su stdin nello stesso processo (serve per il follow-up in fresh print). Non provato: avrebbe richiesto una chiamata al modello.
- L'interazione di `--autocompact <token>` con `--resume` fra processi distinti e la qualità del riassunto (nessun turno letto: solo `usage`).
- Il commit del **daemon in esecuzione** (`/opt/aiorchestrator`, build 04:17): la spec dice "verified at HEAD by symbol"; io no.
- La ripartizione del boot fra system prompt, definizioni tool, MCP e skill: dedotta per differenze (2,8–3,2 B/tok), non misurata su una `-p` vuota.
- La semantica esatta di `usage.iterations[0]` e di `num_turns` nel risultato della CLI: inferite dalle grandezze (iterations[0] ≈ prima chiamata; `num_turns` sovrastima le chiamate API di ~1,5–2×).
- Se `PreToolUse` possa iniettare `additionalContext` in 2.1.263 (il prompt dice che le stringhe sono nel binario; non ho controllato).
- Come il limite settimanale pesi la rilettura della cache rispetto all'input fresco.
- Il contenuto di qualunque turno: ho letto solo quanto pesava, mai cosa faceva.
- Il 17–24 % di "tentativi senza risultato" e l'83/10/8: presi come [documentato] dal prompt e dalla spec; ho confermato solo totali, quote di cache, sidechain, compattazioni, distribuzioni per turno.
