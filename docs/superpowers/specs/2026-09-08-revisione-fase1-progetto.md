# FASE 1 — progetto alla cieca (scritto 2026-09-08 prima di aprire i doc della proposta)

## Misure che guidano il progetto (VPS, turns.jsonl + metadati trascrizioni, dedupe per requestId)
- Totale main+subagent 2,040 G token, 9 770 chiamate; cache_read 97,8 %; subagent 13,1 % (ephemeral_5m solo lì). Coincide con i fatti dati.
- Implementer (172 turni print): first-call ctx mediano 249 k (p90 719 k); token nuovi/turno mediana 6 k (media 60 k); token/turno mediana 690 k (media 3,26 M); output/turno mediana 2,8 k.
- Reviewer (109 turni): first-call ctx mediano 241 k; nuovi/turno mediana 5 k; token/turno mediana 448 k (media 1,03 M).
- Entry pendenti per turno: mediana 1, media 1,56 (651 turni eseguiti).
- Canali membri live: 40–166 KB (45–85 entry); FROM app = 6–13 % dei byte; archivio riletto 2 volte in tutto (grep tool_use).
- Boot (first-call ctx della prima chiamata di sessione): implementer 33,9–46,6 k; reviewer 33,9–44,5 k; supervisor 55,9–56,9 k; general (haiku) 29,7–32 k.
  Delta supervisor−implementer 22 k per 62 KB di skill in più → ~2,8–3,2 byte/token [stima]: skill implementer ≈ 8 k, reviewer ≈ 7 k, supervisor ≈ 27–30 k; base (system prompt + tool + MCP) ≈ 26–39 k.
- Doc obbligatori Fincanva: AGENTS.md 15,7 KB + Platform/AGENTS.md 23,2 KB ≈ 10–12 k token per turno che li rilegge.
- Gap fra turni: mediana 1,9 min, p90 50 min, 9 % > 60 min.
- Retry: 130 "attempt 2", 30 "attempt 3" su ~650 turni print + stream (log).
- Config `runners.<ruolo>.resume` è GLOBALE per ruolo (PrintTurnDispatcherModel.cs:659); nessun override per orchestrazione (solo *ModelOverride in session.json).

## Il progetto: "tappe + digest del bridge + foglio opzionale"
1. Resume a tappe (`staged`): il bridge riprende la trascrizione finché il ctx della prima chiamata dell'ultimo turno (usage.iterations[0], già in turns.jsonl) < soglia T (150 k) e finché non si chiude una riga di ledger del membro; altrimenti parte fresco. Continuità dentro il task, reset fra task.
2. Al boot fresco il PROMPT porta un digest deterministico composto dal bridge: brief (entry FROM supervisor più vecchia non risposta, cercata su live+archivio con ChannelHistory), ultima entry propria del membro (il suo report = il suo stato), blocco STATE se presente in quella entry, righe PLAN.md del membro, entry pendenti. Niente "leggi tutto il canale".
3. STATE: 5 chiavi fisse in coda al messaggio finale (goal / branch+commit / verify-cmd / next file:riga / dead-ends), opzionale; il bridge fa parse, tetto ~1,5 k token per troncamento, mai rifiuto; hook Stop che AVVISA (non blocca) se c'è stato lavoro senza STATE (decisione 21).
4. Kill: un solo --resume "di chiusura" con --max-budget-usd, poi tappa fresca dal digest.
5. Dieta skill solo per il supervisor (27–30 k): per i membri il boot è dominato da system prompt+tool (26–39 k), non dalla skill (7–8 k).
6. Misura: token per riga di ledger chiusa; distribuzione first-call ctx; brief ri-emessi e verdetti REWORK; retry/turno.

## Costo stimato per turno mediano implementer [stima]
- Oggi: 249 k × ~3 chiamate + crescita ≈ 690 k (misurato mediana).
- (a) fresh + canale: 45 k boot → +28 k canale → +11 k doc → 3–4 chiamate ≈ 300–380 k (1,8–2,3×).
- (b)/mio: 45 k boot + 4 k digest + 1 k pendenti (+11 k doc se codice) ≈ 50–61 k prima chiamata; ~3 chiamate ≈ 160–200 k (3,5–4,3×); sul turno MEDIO (3,26 M) ≈ 15×.
- Voce dominante dopo: il boot (45 k su 50 k = 90 %), di cui skill 8 k.
