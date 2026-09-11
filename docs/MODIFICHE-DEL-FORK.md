# Modifiche del fork — che cosa è cambiato, e perché

Questo file racconta a chi non ha seguito giorno per giorno **che cosa abbiamo cambiato nel fork
e per quale ragione**. Racconta idee e decisioni, non codice: chi vuole il dettaglio tecnico ha i
messaggi di commit e le spec in `docs/superpowers/specs/`.

Base: `ours/integration` sopra `master`. Alla sera del 2026-09-10 sono **189 commit** in rami
`stage/*` fusi, ognuno tenuto integrabile su `master` per conto proprio. Tutto quello che c'è qui fino
alla sezione 5 è girato in produzione sul server dal 2026-09-09; il blocco Telegram (rami `8a`–`8e`,
`9a`, `9b`, `11`, `12`, `13`) è in produzione dalle 20:56 del 2026-09-10; i rami `15`–`20` (sera del
10/9) sono fusi e **aspettano un solo riavvio** (sezione 7). Suite: **3307 test, 9 saltati**
(3298 verdi sul ramo fuso, `c5deae8`); in parallelo sotto carico ha ancora circa un rosso a corsa
nella famiglia dei test a tempo reale (misurato il 10/9; nelle quattro corse della sera zero) — è il
primo debito aperto della sezione 7.

---

## Come si scrive questo file

Chi tocca il fork da qui in avanti aggiunge la sua voce qui dentro, con queste regole.

**Una voce per cambiamento concettuale, non per commit.** Se tre commit servono la stessa idea,
sono una voce sola. Se un commit contiene due idee, sono due voci.

**Il lettore non legge il codice.** Niente nomi di classi, di metodi o di file sorgente, niente
diff, niente numeri di riga. I nomi che si vedono da fuori — un marcatore come `ATTACH:`, un
comando come `/resume` — si possono nominare, perché sono l'interfaccia, non l'implementazione.

**Prima il principio, il difetto come contorno.** La domanda a cui la voce risponde è «qual è la
regola che adesso vale», non «quale bug abbiamo chiuso». L'incidente serve come prova che la regola
serviva, e va messo dopo, in una riga o due. Una voce che è solo il racconto di un bug è scritta
male.

**Le misure solo se misurate.** «26 uccisioni su 29» va bene perché è stato contato; «molto più
veloce» no. Se un numero è una stima, si dice che è una stima.

**Anche il prezzo pagato.** Se un cambiamento ha peggiorato qualcosa d'altro, si scrive nella voce.
Un lettore che scopre il prezzo da solo, dopo, smette di fidarsi del resto del file.

**La forma di una voce:**

> ### Titolo — la regola, in parole piane
>
> **Com'era.** Il comportamento di prima, in una o due frasi.
> **Cos'è adesso.** Il comportamento nuovo.
> **Perché.** Il principio, e poi l'incidente o la misura che lo hanno reso evidente.
> **Cosa cambia per chi lo usa.** Solo se cambia davvero qualcosa sotto gli occhi.
> **Dove.** I rami `stage/*`, per chi poi vuole andare a vedere.

**Dove metterla.** In fondo alla sezione tematica giusta. Se nessuna sezione va bene, se ne apre
una nuova; l'ordine dentro una sezione non ha significato. La sezione 7 è l'unica che parla di
lavoro non ancora fatto: chi ci scrive dentro mette la data, e chi finisce una di quelle cose la
sposta su, in una voce vera.

**Se un cambiamento successivo ne annulla uno precedente**, non si cancella la voce vecchia: si
riscrive dicendo che cosa è stato ritirato e perché. La storia di una decisione cambiata vale più
della decisione.

**La lingua di questo file è l'italiano**, perché è scritto per una persona. Tutto il resto —
codice, file, commit, voci di canale fra agenti — resta in inglese.

---

## 1. Come il sistema parla con il proprietario

### Una domanda è completa, o non è una domanda

**Com'era.** Un agente chiedeva come gli veniva. Se mancava la riga della domanda, l'app la
ricavava dall'ultima frase del testo e, se non ci riusciva, ci metteva un «decidi tu» prefabbricato.

**Cos'è adesso.** Una domanda ha una forma obbligatoria: la domanda, almeno due opzioni, una
raccomandazione, il livello di rischio e la riga di piano a cui si riferisce. Se è incompleta il
testo arriva lo stesso al proprietario — un errore di forma non deve mai costargli un messaggio —
ma non gli crescono i bottoni sotto, e l'agente riceve **un solo** messaggio che elenca tutto
quello che manca, non uno per volta.

**Perché.** Una domanda con le opzioni e una raccomandazione è una decisione presa in trenta
secondi; la stessa domanda senza è una decisione rimandata di una settimana. Misurato su un
argomento il 2026-09-07: quaranta minuti di domande fatte una alla volta, parecchie senza opzioni,
nessuna con una raccomandazione. E i due ripieghi (domanda dedotta, «decidi tu») erano proprio il
motivo per cui la forma non veniva mai corretta: coprivano il difetto.

**Cosa cambia per chi lo usa.** Raccomandazione e riga di piano stanno accanto alla domanda, dove
si possono leggere e decidere dalla schermata di blocco del telefono.

**Dove.** `stage/3-owner-questions-and-attachments`.

### «Parliamone» — un bottone che non spende niente → ritirato il 2026-09-10

**Com'era (dal 2026-09-08 al 2026-09-10).** Ogni domanda offriva «💬 Parliamone»; la domanda restava
viva con i suoi bottoni e, finché si parlava, quello che si scriveva non chiudeva niente.

**Perché è stato ritirato.** Sul telefono un tocco che non cambia il messaggio è un tocco che non si
vede: l'unico riscontro era l'avviso di un secondo di Telegram. Misurato sul canale di produzione il
2026-09-09: dodici tocchi in un pomeriggio, **quattro nello stesso minuto**, finché il supervisore ha
scritto «ti ho già detto tutto, tocca un'opzione». Il bottone funzionava; era muto. E la domanda lasciata
aperta «in discussione» aveva un secondo effetto: il testo che il tocco mandava al supervisore passava per
una risposta scritta e, se c'era un'altra domanda aperta, la chiudeva a nome del proprietario. La regola
che ne è uscita è nella voce «Ogni tocco cambia il messaggio toccato».

**Dove.** `stage/3-owner-questions-and-attachments` (introdotto), `stage/8a-a-tap-closes-its-message`
(ritirato).

### Una risposta appartiene alla sua domanda

**Com'era.** Qualsiasi messaggio del proprietario chiudeva tutto ciò che era aperto, e le sue parole
venivano archiviate come risposta sotto ogni domanda che chiudeva. Il proprietario ha visto la sua
stessa domanda — «a che punto siamo?» — registrata come risposta a una domanda su un merge, due
volte; e una riga breve chiudere quattro domande insieme, tre delle quali nessuno aveva risposto.

**Cos'è adesso.** L'abbinamento si fa solo quando è sicuro. Se non c'è niente di aperto, se il
messaggio è a sua volta una domanda, o se ce ne sono due o più aperte, non si abbina niente: le
domande restano aperte, con i bottoni vivi e i solleciti attivi.

**Perché.** Il sistema ci credeva già, per i tocchi: un bottone porta con sé un codice non
indovinabile, apposta perché un bottone vecchio non possa rispondere a una domanda per cui non era
stato offerto. Una riga scritta a mano quel codice non ce l'ha, quindi dove l'attribuzione non si
può dedurre l'unica cosa onesta è non attribuire. La direzione dell'errore è scelta: nel caso
peggiore il proprietario tocca un bottone, un gesto; l'errore opposto è una decisione messa a
verbale che nessuno ha preso.

**Dove.** `stage/2a-an-answer-belongs-to-its-question`.

### Il lucchetto ad alto rischio legge la domanda, non il racconto

**Com'era.** Le domande pericolose (mettere in produzione, cancellare, forzare) si sbloccano con un
codice a quattro cifre. Il riconoscimento cercava le parole dentro tutto il testo, pezzo di parola
compreso: in un pomeriggio ha messo il lucchetto a quattro domande di puro prodotto perché nel
racconto attorno comparivano frasi come «il motore in produzione va in crash su queste chiavi».
Non si stava mandando in produzione niente.

**Cos'è adesso.** Guarda la domanda e le etichette delle opzioni — cioè quello che il proprietario
sta effettivamente decidendo — e riconosce parole intere: «lo pubblico adesso?» scatta, «l'avevo già
pubblicato» no.

**Perché.** Un falso positivo non è gratis: è un codice davanti a una decisione che non ne aveva
bisogno, e un lucchetto che scatta su quello che un agente ha nominato per caso è un lucchetto che
si impara a digitare senza leggere. Allora, il giorno che serve, non protegge più niente.

**Dove.** `stage/3-owner-questions-and-attachments`.

### Un file è una consegna, e non può sparire in silenzio

**Com'era.** Gli agenti potevano allegare solo immagini. Il 2026-09-08 quattro mockup HTML che il
proprietario aveva chiesto sono partiti come immagini, Telegram li ha rifiutati uno per uno, l'app
ha scritto un avviso in un registro che non legge nessuno e non l'ha detto a nessuno: né al
proprietario né all'agente. Il supervisore gli ha poi detto in buona fede di averli mandati, e ha
sostenuto quella premessa falsa per tre messaggi.

**Cos'è adesso.** Esiste `ATTACH:` per mandare un documento, con una regola su da dove può uscire
un file — la cartella del progetto, quella della supervisione, quella dei mockup — e i limiti di
dimensione di Telegram. Immagini e documenti passano da un unico cancello che **scrive sempre il
rifiuto nel canale dell'agente, con dentro il rimedio**: quale marcatore usare, quale limite è stato
superato, quali cartelle sono ammesse. E un file arriva al proprietario anche se non è una risposta
a una domanda, perché una consegna è fatta per essere guardata.

**Perché.** Un fallimento silenzioso è la forma peggiore che un guasto può prendere qui, perché la
sessione continua a ragionare partendo da lì. Un errore detto è un errore che costa un minuto; un
errore taciuto diventa una discussione fra due persone che hanno in testa due mondi diversi.

**Dove.** `stage/3-owner-questions-and-attachments`, `stage/3a-a-file-never-vanishes-in-silence`.

### Un'entry lunga si piega sul telefono

**Com'era.** Tre messaggi da quattromila caratteri di fila, uno dietro l'altro.

**Cos'è adesso.** Sopra una certa lunghezza arriva il primo paragrafo in chiaro e il resto dentro una
citazione richiudibile, che si apre con un tocco. Se anche così è enorme, l'entry arriva **in più** come
file allegato. Non si perde niente e non si taglia niente.

**Perché.** Il canale è la finestra del proprietario su tutto: se leggerlo dal telefono costa fatica,
smette di guardarlo, e allora il sistema è cieco dalla parte che conta.

**Dove.** `stage/1j-long-entries-fold-on-the-phone`.

### Meno notifiche che non chiedono niente

**Com'era.** Sette avvisi sul ciclo di vita dei membri in due ore, fra cui la chiusura di ogni
membro.

**Cos'è adesso.** La chiusura di un membro resta fra gli agenti. L'apertura invece continua ad
arrivare, e apposta: una sessione che parte è una sessione che comincia a spendere, e quelli sono
soldi del proprietario.

**Perché.** Un avviso su cui non c'è niente da fare e niente da disfare non va al telefono. È la
stessa regola per cui una lamentela sulla forma del piano va al supervisore e non al proprietario:
è il supervisore che può sistemarla.

**Dove.** commit diretto su `ours/integration` (`5c29cd6`).

### Il traduttore non c'è più

**Com'era.** L'app traduceva in italiano i messaggi diretti al proprietario prima di mandarli su
Telegram, con un interruttore per accenderlo e spegnerlo.

**Cos'è adesso.** L'app non traduce niente. Se ne occupano gli agenti, da soli: al proprietario
rispondono nella lingua in cui il proprietario ha scritto.

**Perché.** Un modello le lingue le sa già — fargli scrivere in inglese e poi tradurre è la stessa
frase scritta due volte, con un processo in più e un punto in più dove rompersi. La regola che conta
non è «in che lingua scrive l'agente», è **dove finisce il testo**: quello che resta sul disco o va a
un altro agente — file, codice, commit, il piano, le voci dei canali — è in inglese, perché è
materiale di lavoro condiviso; quello che è rivolto a una persona è nella lingua di quella persona.
Detta così, la regola sta in una riga nel comando di ruolo e non serve nessun meccanismo.
*Di contorno: tenuto acceso faceva anche danno — prendeva prosa già italiana, la passava a una
traduzione inglese→italiano, e il testo tornava indietro identico; a quel punto l'app lo marchiava
come «non tradotto» e metteva una bandierina d'avviso su un messaggio corretto. Nei registri recenti
contava 183 fallimenti, ognuno un processo in più, e ogni scambio col proprietario ne pagava due.*

**Cosa cambia per chi lo usa.** Niente interruttore, niente comando, un processo in meno per
messaggio. Su Telegram si continua a parlare in italiano.

**Dove.** `stage/7c-no-translation-layer`, `stage/7h-remove-translator`.

### Ogni tocco cambia il messaggio toccato

**Com'era.** Un'opzione toccata cambiava il messaggio e toglieva i bottoni; «Parliamone» e «Spiegami le
opzioni» no. E il testo che un tocco mandava al supervisore viaggiava come se fosse una riga scritta dal
proprietario: con esattamente un'altra domanda aperta, quel testo la chiudeva e la marcava «risposta».
Il 2026-09-09 la domanda sui processi orfani — ad alto rischio — risulta «risposta» con la frase
interna di un bottone toccato su un'altra domanda; nessuno l'ha mai decisa.

**Cos'è adesso.** Un solo bottone dell'app, «💬 Let's talk»: al tocco i bottoni spariscono e il messaggio
dice «Ok — tell me what you have in mind»; il supervisore discute e poi **ri-fa la domanda** con opzioni
fresche (prima gli era vietato). Il testo che nasce da un tocco non può più chiudere nessuna domanda.
Quando un codice a quattro cifre scade, l'app guarda il registro prima di dire «la domanda è ancora
aperta».

**Perché.** Un bottone di cui non si vede l'effetto è, per chi lo tocca, un bottone rotto. E una domanda
che si chiude da sola con parole che nessuno ha detto è il verbale di una decisione mai presa — l'errore
che tutto il sistema di domande esiste per evitare. Il prezzo: dopo la chiacchierata il supervisore deve
ri-chiedere, un messaggio in più; sulle chat del 9/9 le domande dopo una discussione cambiavano quasi
sempre, quindi i bottoni vecchi non servivano comunque.

**Dove.** `stage/8a-a-tap-closes-its-message`.

### Un ✓ è un ✓: il proprietario non viene mai rassicurato a vuoto

**Com'era.** Il ✓ partiva appena il messaggio era letto, prima di sapere dove finiva: un messaggio in un
argomento sconosciuto veniva scartato con una riga di registro, uno in un'orchestrazione chiusa finiva in
un canale che nessuno leggeva più — e il ✓ arrivava lo stesso. Un documento mandato senza didascalia non
produceva niente e veniva comunque segnato come letto. Dopo trenta minuti di Telegram irraggiungibile,
le voci non consegnate venivano buttate via con una riga di registro.

**Cos'è adesso.** Il ✓ arriva solo se il messaggio è stato scritto nel canale giusto; negli altri casi
arriva una riga che dice dove si è fermato («questa orchestrazione è chiusa»). I documenti si scaricano
come le foto e compaiono nel canale con `FILE:`. Le voci che non partono si **parcheggiano** e, alla prima
consegna riuscita, arrivano come un unico documento. Se una foto del proprietario non si scarica, lo sa
il proprietario, non solo l'agente.

**Perché.** Le parole del proprietario: «non dirmi che è arrivato quando non è arrivato». Una ricevuta
falsa è peggio di nessuna ricevuta, perché toglie la voglia di controllare.

**Dove.** `stage/8b-no-lost-messages-no-false-receipts`, `stage/11`.

### Suona chi parla al proprietario: il supervisore sì, l'app no

**Com'era.** Tutto suonava: le ricevute, lo stato ogni mezz'ora, gli avvisi, le conferme dell'app. E non
tutto quello che il supervisore scriveva arrivava subito: solo domande, risposte attese e blocchi; il
resto veniva trattenuto e arrivava dopo cinque minuti di silenzio, come testo piatto con gli asterischi
del Markdown in chiaro — proprio i messaggi più importanti della giornata (il riepilogo finale, il
browser pass) erano i meno leggibili. Misurato su un argomento il 2026-09-09, 15:50–21:29: circa
trentatré messaggi del bot contro otto del proprietario, e circa la metà non era per lui.

**Cos'è adesso.** Regola unica: **se parla il supervisore suona, se parla l'app non suona**, salvo gli
avvisi su cui il proprietario può agire (budget, limite d'uso, una domanda che aspetta da venticinque
minuti). Tutto ciò che il supervisore scrive sul canale del proprietario arriva subito, formattato, con
suono; il filtro che tratteneva la «narrazione» non c'è più, e con lui la rete dei cinque minuti. La
ricevuta è ✓ che diventa ✓✓ in silenzio; la frase «è occupato» compare solo se l'attesa supera i tre
minuti, con il contatore. L'avviso «aspetta la tua risposta» parte solo dopo una **vera** domanda, una
volta sola per domanda, mai mentre il supervisore è fermo per limite d'uso — prima partiva su qualunque
ultima parola del supervisore, anche dopo «non mi serve altro da te», e si ripeteva a ogni scambio. Le
anteprime dei link sono spente. I due percorsi che rispedivano «l'ultima cosa detta» passano dal
renderer come tutto il resto; il renderer non annida più codice dentro il grassetto (Telegram lo rifiuta
— caso mai scattato in produzione: zero su 4.558 voci specchiate, ma corretto).

**Perché.** Il telefono che vibra è l'unico segnale che il proprietario non può ignorare; spenderlo per
uno stato ripetuto significa che la domanda vera arriva in mezzo al rumore. Il prezzo: il supervisore ora
suona **sempre** quando scrive al proprietario, quindi il freno alle chiacchiere è nella sua istruzione e
nel richiamo di brevità, non più in un filtro dell'app.

**Dove.** `stage/8c-the-phone-rings-only-for-the-supervisor`.

### Uno stato solo, in fondo al topic, con sei campi veri

**Com'era.** Due stati nello stesso argomento, entrambi dedotti dall'app leggendo file: uno «STATUS» ogni
mezz'ora come messaggio nuovo da quindici righe (nove delle quali «chiuso»), e una riga «PULSE» con i
bottoni, editata. Dicevano cose vere a metà: «il supervisore aspetta te» mentre era fermo per limite
d'uso; «5 in corso» con tutti i membri chiusi (contava le righe del piano, non le sessioni); «adesso:
FIN-D-293a» ripetuto per cinque ore dopo che 293 era stato fuso; «invariato da 3 h 15» in tre ore con due
merge e tre decisioni; e gergo interno come «finestra di scrittura lasciata aperta».

**Cos'è adesso.** Lo stato periodico non esiste più. Resta **una** riga PULSE in fondo, silenziosa,
editata sul posto, ripubblicata in fondo solo se è sepolta **e** il contenuto è cambiato (il battito
«updated HH:MM» e i minuti delle durate — arrotondate a cinque — non contano come cambiamento). Sei campi,
in quest'ordine: che cosa aspetta il proprietario (domande aperte e righe del piano bloccate su di lui,
con il nome); lo stato del supervisore **dichiarato da lui** a fine turno, più «fermo per limite, riprende
alle HH:MM» quando lo sa l'app; i membri vivi, uno per riga fino a quattro, con il titolo del compito e
da quanto (i chiusi come numero); l'ultimo evento con la sua ora; fusi/totale («fusi», non «fatti»);
l'ora dell'aggiornamento. Il nome dell'argomento porta solo ❓ (aspetta te), ⏸ (fermo per limite), 🏁
(chiuso) e i due che mette il proprietario, 🧪 (`/test`) e ✅ (`/done`); le modalità (🌙 🔕 ✈ 🤐 💻)
stanno nell'intestazione di PULSE. La barra dei comandi è `/pending /left /tail /limits /merge /close`;
il General ha il suo cruscotto con `/summary /pending /limits /resume /dnd_all`. Il «non disturbare»
trattiene solo ciò che suona: PULSE e cruscotto continuano ad aggiornarsi in silenzio.

**Perché.** Quando si apre un argomento si cerca una cosa sola: «c'è qualcosa che aspetta me, e cosa?».
Nessuno dei due stati vecchi lo diceva, e una riga di stato che mente una volta insegna a non leggerla
più. Il prezzo: senza lo stato ogni mezz'ora non c'è più una storia dello stato su Telegram — resta nei
file, e la narrativa vera la fanno i messaggi del supervisore, che ora suonano tutti.

**Dove.** `stage/8c-the-phone-rings-only-for-the-supervisor`, `stage/8d-pulse-glyphs-and-the-general-bar`,
`stage/8e-durations-and-the-general-header` (durate a passi di cinque minuti; 📸 dal nome di General al cruscotto).

### Le ricevute sono reazioni, non messaggi

**Com'era.** Ogni messaggio del proprietario faceva nascere un messaggio del bot (✓, poi ✓✓, con il
bottone ⏸ «aspetta»): un messaggio dell'app per ogni messaggio della persona.

**Cos'è adesso.** Il bot mette una **reazione** sul messaggio del proprietario: 👀 quando l'ha scritto nel
canale, sostituita da 👌 quando la sessione lo prende in mano. Nessun messaggio, nessuna notifica; il
messaggio ✓ resta solo come ripiego se Telegram rifiuta la reazione. Il bottone ⏸ passa nella barra
PULSE (⏸ `/wait` ↔ ▶ `/go`, con il conteggio dei trattenuti); `WAIT` e `GO` scritti restano.

**Perché.** L'argomento deve contenere la conversazione e lo stato, non il rumore di fondo dell'app.
Un vincolo di Telegram ha scelto le emoji: i bot possono mettere una sola reazione per messaggio, da una
lista fissa in cui ✅ non c'è.

**Dove.** `stage/12`.

### Chiudere un'orchestrazione cancella il suo argomento, davvero

**Com'era.** Alla chiusura l'app chiedeva a Telegram di cancellare l'argomento e non guardava la
risposta: se falliva — rete, permessi, limite di velocità — l'argomento restava lì per sempre e l'app
non se ne ricordava.

**Cos'è adesso.** La cancellazione si ritenta rispettando i tempi che Telegram chiede, viene annotata
prima del primo tentativo, e a ogni avvio l'app paga i conti che il processo precedente non ha potuto
chiudere. Un rifiuto permanente viene detto al proprietario una volta sola in General. La scelta fra
cancellare e archiviare è del proprietario: **cancellare**, perché col tempo saranno centinaia di
argomenti e quelli chiusi in lista sono rumore; la storia resta nei file su disco.

**Dove.** `stage/9a-a-closed-topic-really-goes`.

---

### Un campanello che ha già detto «aspetta» non si suona più

**Com'era.** Quando Telegram rispondeva «troppe richieste, riprova fra trenta secondi», quella
risposta la conosceva soltanto la superficie che l'aveva ricevuta. Tutte le altre continuavano a
suonare lo stesso campanello, una volta ogni due secondi, per tutta la durata dell'attesa.

**Cos'è adesso.** La risposta di Telegram viene scritta su una nota condivisa, appesa al bersaglio
preciso che è stato rifiutato: chi arriva dopo la legge e non prova nemmeno. E il freno interno che
distanzia due riscritture dello stesso messaggio non addormenta più il ciclo del ponte: dice quanto
manca e lascia decidere a chi chiama — una riga di stato torna al giro dopo, il tocco del
proprietario aspetta e riprova, perché è l'unica cosa che qualcuno sta guardando.

**Perché.** Il principio è che il numero di Telegram batte il nostro: un limite che non possiamo
vedere lo stimiamo, ma un rifiuto porta con sé la risposta esatta, e ignorarla per una stima locale
è il modo in cui un programma continua a sbattere sullo stesso muro. La misura che l'ha reso
evidente, presa sul server il 2026-09-10 nell'ora fino alle 22:38: **388 rifiuti per eccesso di
richieste**, di cui **376 erano due sole righe di stato** ritentate al ritmo del ciclo contro
un'attesa dichiarata di 20-34 secondi. Nel giornale si vede il conto alla rovescia — 34, 31, 29,
27, 25 — che è **una sola finestra riraccontata**, non 388 violazioni: il programma non aspettava
mai, chiedeva e basta, e ogni domanda era una riga di log.

**Cosa cambia per chi lo usa.** Dentro una di quelle attese una riga di stato può restare ferma
fino a mezzo minuto: è il prezzo, ed è voluto. Quello che non resta più indietro è il tocco: prima
il bottone premuto restava con il testo vecchio e la tastiera viva su una domanda già risposta.

**Il prezzo, e un limite dichiarato.** La nota vale solo per il bersaglio rifiutato, non per
l'intera conversazione — che sarebbe difendibile, ed è ciò che fanno le librerie serie — perché
senza le corsie di priorità (che arrivano dopo) una nota su tutta la conversazione rimetterebbe il
tocco del proprietario dietro alla punizione di una riga di stato: esattamente il rovescio che
questo lavoro esiste per togliere. E il ponte adesso dice nel giornale quanto è durato un giro
quando supera i cinque secondi, una volta sola finché non torna sotto: senza quella riga un blocco
del ciclo sarebbe invisibile e verrebbe raccontato come «l'app è muta».

**Dove.** `stage/17-one-door-to-telegram`, che continua `stage/15` e `stage/16`.

---

## 2. La vita di un turno

### Un membro occupato non è un membro morto

**Com'era.** Un controllo dichiarava «orfano» un membro che non consumava il suo canale da un po'.
In tre ore, su una sola orchestrazione, ha sparato diciannove volte, e tutte e diciannove erano
false: un membro ha finito il suo turno diciotto secondi dopo essere stato dichiarato inattivo, un
altro ha consegnato un rapporto coerente undici secondi dopo.

**Cos'è adesso.** «Non lo so» è una risposta a sé, e non fa scattare niente. Un turno in corso è
vita. E l'uccisione del processo non è stata ristretta: è stata **tolta**. Quando il sospetto è
fondato, adesso si chiede al supervisore di andare a vedere.

**Perché.** Il controllo misurava quanto un membro fosse occupato o bloccato, non se fosse vivo: chi
sta dentro un turno lungo non può leggere il suo canale, quindi veniva condannato proprio perché
stava lavorando. La conferma sta nel confronto: un'orchestrazione sorella con cinque membri,
cinquantotto turni e due ore di lavoro non è stata toccata nemmeno una volta — più intensa, ma senza
un solo turno oltre i cinque minuti, contro undici e un massimo di ventuno qui. E la funzione di
uccisione era stata introdotta una volta e ristretta cinque, ogni volta reagendo a un falso
positivo, senza che da nessuna parte risultasse un solo caso in cui avesse salvato qualcuno.

**Dove.** `stage/2b-a-busy-member-is-not-a-dead-one`.

### «Sta lavorando?» — tre risposte, non due

**Com'era.** Tutte le risposte a questa domanda venivano da un file che scrive la barra di stato di
Claude Code, e che una sessione senza interfaccia non scrive mai. Quindi su ogni macchina governata
dal bridge il file non c'era, e «non lo so» veniva letto come «no». Il proprietario si è sentito
dire «inattivo, in attesa» per due ore su cinque membri che stavano tutti lavorando; la parola
«sta lavorando» non è comparsa una volta.

**Cos'è adesso.** L'app risponde da quello che ha scritto lei: è lei che fa partire i turni, tiene
l'elenco di quelli in volo e registra quando finiscono. E la risposta ha tre valori: sta lavorando,
è fermo, **non lo so**.

**Perché.** «Non lo so» non è un modo di dire «no». Confondere le due cose è ciò che ha trasformato
un file mancante in una decisione distruttiva. Vale in generale: se una fonte non c'è, il sistema
deve dirlo, non dedurne il contrario.

**Dove.** parte di `stage/2b`.

### L'orologio del silenzio parte quando parte il prompt

**Com'era.** Un turno veniva ucciso «per silenzio» se non arrivavano dati per un tot di tempo, ma il
cronometro non veniva azzerato quando si mandava una nuova richiesta: contava dal traffico
precedente. Quindi qualunque turno mandato dopo una pausa più lunga del limite veniva ucciso subito,
e ritentato un minuto dopo su un processo freddo.

**Cos'è adesso.** Il cronometro parte quando parte la richiesta.

**Perché e quanto.** Ventisei uccisioni per silenzio su ventinove erano di questa forma — il 6% dei
risvegli del supervisore, che passavano da ~25 secondi a 111 secondi mediani per arrivare a
destinazione. Due orchestrazioni hanno risposto al proprietario alle 4:47 del mattino solo dopo una
di queste uccisioni.

**Dove.** `stage/7a-stream-heartbeat`.

### Un rifiuto per limite d'uso compra un appuntamento

**Com'era.** Quando l'account tocca il limite d'uso, l'interfaccia dice a che ora si riapre la
finestra. L'app buttava via quella frase: tre tentativi a un minuto l'uno dall'altro, poi silenzio
finché non arrivava traffico nuovo. Diciotto stalli così, da 167 a 509 minuti l'uno, due
orchestrazioni ferme per un'intera notte con il proprietario che aspettava.

**Cos'è adesso.** L'ora viene letta e diventa un appuntamento: nessun tentativo sprecato,
l'appuntamento sopravvive a un riavvio, il proprietario viene avvisato nel canale di quando si
riparte, e `/resume` lo annulla.

**Perché conta anche il seguito.** La prima versione di questa correzione era peggio del difetto, e
l'ha dimostrato una revisione avversaria: un'ora letta un secondo dopo essere passata veniva
spostata al giorno dopo, e ogni ulteriore rifiuto aggiungeva un altro giorno, senza che nessun
traffico potesse svegliare la sessione. E il riconoscimento era la parola «limite»: un turno
**riuscito**, il cui unico problema era un canale su cui non riusciva a scrivere, veniva parcheggiato
quattordici ore senza spendere un tentativo — quindi senza mai fermarsi né dare l'allarme. Adesso
l'appuntamento è limitato a sei ore (la finestra di sessione è cinque, quindi ogni riapertura che una
sessione può nominare ci sta dentro; un limite settimanale nomina un giorno più in là, ed è una
lettura che non si può verificare), e per essere un rifiuto ci vuole il codice del rifiuto, non una
parola.

**Il principio.** Una lettura che non si capisce vale **niente**, mai una supposizione: il sistema
torna a comportarsi come prima e scrive una riga che dice esattamente che cosa non ha saputo leggere.

**Dove.** `stage/7b-limit-reset-retry`, `stage/7i-limit-reset-fixes`.

### Un turno ucciso alla scadenza dice dove è arrivato

**Com'era.** Dopo trenta minuti il turno veniva ucciso e, un minuto dopo, lo stesso lavoro
ricominciava da zero. Undici casi, tutti implementatori, ognuno trenta minuti buttati e poi rifatti
in un tempo fra un minuto e un quarto d'ora. Non erano bloccati — un processo muto viene già ucciso
prima — stavano lavorando.

**Cos'è adesso.** La scadenza non vuol più dire «butta via». Vuol dire un ultimo turno di chiusura,
al massimo cinque minuti, con una sola istruzione: non continuare, scrivi che cosa è fatto, che cosa
è a metà e che cosa viene dopo, poi fermati. Quel rapporto entra nel canale come qualsiasi altra
voce, quindi sveglia il supervisore, e il lavoro non viene riproposto da capo.

**Perché.** Trenta minuti di ragionamento sono la cosa più costosa che questo sistema produce.
Buttarli via e rifarli non è solo lento: la seconda volta si prendono decisioni diverse, e nessuno
sa quali. Vale ancora di più da quando le sessioni ripartono pulite, perché prima almeno restava la
trascrizione.

**Il prezzo trovato dopo.** Anche qui una revisione avversaria ha trovato il difetto grosso: il
turno di chiusura marcava come «consegnati» tutti i messaggi in attesa, mentre il suo rapporto è una
voce sola indirizzata dove gli pare. Un supervisore ucciso mentre rispondeva al proprietario poteva
scrivere il rapporto altrove, e la domanda del proprietario risultava consegnata senza che restasse
nessuno a occuparsene. Adesso si consuma solo ciò a cui si è davvero risposto.

**Un limite che non limita, detto perché non ci si conti.** Il turno di chiusura ha un tetto di
spesa dichiarato, ma quel tetto è controllato **dopo** il passo, non prima: misurato, con un tetto
di 0,0001 dollari ne sono stati spesi 0,11. Quello che davvero delimita il turno di chiusura è il
suo tempo massimo, non il tetto.

**Dove.** `stage/7f-timeout-closing-turn`, `stage/7j-closing-turn-fixes`.

### Uno stop svuota prima di uccidere

**Com'era.** Fermare il servizio chiudeva la porta e ammazzava i turni in corso nello stesso istante.
In 44 ore di lavoro ci sono stati 21 riavvii (le installazioni), e diciassette turni in volo — 112
milioni di token, il 6,4% di tutto il consumo — sono morti entro quattro minuti da una riga «mi sto
fermando», per poi essere rifatti da zero.

**Cos'è adesso.** Due segnali separati: uno chiude la porta ai turni nuovi, l'altro arriva solo dopo
che quelli in corso hanno avuto il tempo di finire da soli.

**E il seguito, che vale come lezione.** Non bastava. Il servizio annunciava «sto svuotando, fino a
31 minuti» e il sistema operativo lo dichiarava spento **esattamente trenta secondi dopo**, tre
riavvii di fila: era un tempo di attesa predefinito dell'ospite di processo, che nessuno aveva mai
impostato. Cinque sessioni fresche morte a metà turno, quindici milioni di token. Adesso una regola
sola dimensiona i tre tempi in scala, dal più interno al più esterno.

**Il principio.** Un periodo di grazia deve essere rispettato da ogni strato sotto di lui, altrimenti
vince in silenzio il valore predefinito di quello più esterno — e l'annuncio che il sistema fa di sé
diventa una bugia.

**Dove.** `stage/4a-fresh-turns-and-drain`, `stage/4e-host-shutdown-timeout`.

---

## 3. La memoria fra un turno e l'altro

### Una sessione fresca riceve la memoria in un pacchetto

**Com'era.** Una sessione che riparte pulita si avviava con il solo comando di ruolo: i messaggi che
il bridge aveva appena deciso di consegnarle non le arrivavano, e allora se li andava a cercare da
sola, aprendo canali e piani. Misurato sul supervisore generale: 9,2 chiamate al modello per turno,
di cui 7,8 solo per leggere file. E l'83% di tutto il contesto consumato sul server (1456 milioni di
token su 1756) era roba trascinata dai turni precedenti.

**Cos'è adesso.** L'app prepara un pacchetto e glielo mette in mano: i messaggi in attesa, il brief,
l'ultimo rapporto che quel membro ha scritto, le righe di piano che lo riguardano, lo stato del
codice. Il ruolo lo legge per primo e tratta il canale come una consultazione, non come il punto di
partenza.

**Perché.** Ogni cosa che una sessione deve andare a cercare è pagata due volte: una per cercarla e
una per rileggerla al turno dopo. Chi sa già che cosa serve — il bridge, che ha appena deciso lui
quali messaggi sono in attesa — deve consegnarlo, non lasciarlo indovinare.

**Il dettaglio che regge tutto.** Una sezione che non si riesce a leggere viene **nominata** come
non riempita, invece di comparire vuota: un piano assente non deve leggersi come un piano senza
righe. E un pacchetto che non si riesce a scrivere non ferma mai il turno.

**Dove.** `stage/4a-fresh-turns-and-drain`, `stage/4b-state-pack`.

### Il brief si ritrova anche dopo che il canale è stato compattato

**Com'era.** A una sessione fresca si davano «le ultime tre voci», che dopo un giro di revisione non
contengono più l'incarico.

**Cos'è adesso.** L'incarico si cerca con una regola deterministica, e la ricerca attraversa sia il
canale vivo sia l'archivio.

**Perché.** I canali vengono compattati: la parte vecchia si sposta in un archivio. Qualsiasi cosa
conti «quante voci ci sono» o «le ultime N» guardando solo il file vivo prima o poi sbaglia, e
sbaglia in silenzio.

**Dove.** `stage/4b-state-pack`.

### Strumenti per misurare il consumo

**Cos'è.** Script di sola lettura che dicono quante chiamate costa un turno, quanto contesto porta
la prima chiamata, e quante di quelle chiamate sono lavoro vero contro quante sono orientamento.
Leggono solo metadati e non scrivono niente fuori dai file temporanei.

**Perché.** Le decisioni di questo capitolo sono tutte compromessi fra costo e comodità. Senza una
misura presa prima e dopo, sullo stesso punto, sono opinioni.

**Dove.** `stage/4b-state-pack`.

### Implementatori e revisori ripartono puliti a ogni turno

**Com'era.** Ogni sessione continuava la conversazione precedente, quindi si portava dietro tutto
quello che aveva già letto e detto, turno dopo turno.

**Cos'è adesso.** Implementatori e revisori ricominciano da zero a ogni turno, e ricevono in mano il
pacchetto di stato descritto qui sopra. È una modifica di configurazione, non di codice: è
l'interruttore che accende tutto il lavoro di questa sezione.

**Perché e quanto.** L'83% del contesto consumato sul server era roba trascinata dai turni
precedenti — pagata di nuovo a ogni chiamata, senza che nessuno l'avesse chiesta. Dopo il
cambiamento, misurato: i token per chiamata di un membro passano da 345 mila a circa 100 mila e
restano stabili; su un compito breve, il costo per consegna scende di circa 4,7 volte.

**Dove è finito il costo che resta.** Non è sparito: si è spostato **dentro** i turni lunghi. Il 41%
dei token dei membri in una serata sta in turni arrivati fino alla scadenza dei trenta minuti. È il
prossimo pezzo di lavoro (vedi la sezione 6).

**Dove.** configurazione del server, e `stage/4b-state-pack` per il pacchetto che la rende possibile.

---

## 4. Velocità e costo dell'app

### Una scrittura su un canale sveglia il ciclo

**Com'era.** Un messaggio del proprietario ci metteva 11-12 secondi a raggiungere il supervisore e
36-42 a ottenere risposta. Di quel tempo l'app se ne teneva una decina: sei secondi per aggregare
messaggi vicini, fino a due di attesa del giro di controllo, tre di raggruppamento.

**Cos'è adesso.** Un osservatore del file system accorcia l'attesa appena qualcuno scrive; il giro
di controllo periodico resta come rete di sicurezza, e una macchina che non manda quelle notifiche
si comporta esattamente come prima, con una riga nel registro che lo dice. La finestra di
aggregazione per il proprietario si è dimezzata, mentre per i membri resta intera, perché ogni
risveglio del supervisore in più costa circa un milione di token in ingresso.

**Il prezzo, scritto perché è reale.** Accelerando, il margine che impedisce di specchiare una voce
scritta a metà era stato ridotto di dodici volte, e uno scrittore che si fermava quattro decimi di
secondo si vedeva la voce spedita troncata e il resto distrutto. Adesso quel margine è di nuovo una
durata, non un conteggio di giri, e vale quello che valeva prima. La conseguenza è che **l'ultima
voce di un canale ci mette di nuovo quattro secondi** ad arrivare sul telefono: è il prezzo di non
troncarla. Il guadagno sul percorso del proprietario resta, perché quel percorso non passa di lì.

**E un dettaglio che vale un milione di token.** Un messaggio già finito (che termina con un punto)
saltava del tutto la finestra di attesa: due frasi scritte a due secondi di distanza compravano due
turni di supervisore. Adesso anche un messaggio finito aspetta un paio di secondi.

**Dove.** `stage/7e-owner-path-latency`, `stage/7k-waker-fixes`.

### Il giro di controllo legge ciò che è cambiato

**Com'era.** Ogni due secondi il ciclo rileggeva tutto: l'elenco delle sessioni tredici volte, i
canali per intero anche solo per scoprire che non c'era niente da compattare, e riscriveva
comunque il file dei segnaposti.

**Cos'è adesso.** L'elenco si carica una volta per giro; il controllo di compattazione conta le
intestazioni invece di leggere tutto; i canali si rileggono solo se sono cambiati davvero
(dimensione e data, mai «sono passati tot secondi»); i segnaposti si scrivono solo se si sono
mossi.

**Quanto.** Da 195 aperture di file e una scrittura per giro a 44 aperture e zero scritture: -77%.
Niente di ciò che il bridge decide è cambiato.

**Dove.** `stage/7g-tick-hygiene`.

### Il modello di ogni membro è scelto sul compito

**Com'era.** Un modello fisso per ruolo.

**Cos'è adesso.** Chi apre una sessione ne indica il modello, e la scelta resta scritta nella scheda
del membro, così una sessione che viene fatta ripartire torna sul modello per cui il compito era
stato dimensionato. Ci sono tre livelli di precedenza: quello che ha imposto il proprietario per
quell'orchestrazione, quello del membro, il valore predefinito. Il modello più costoso lo può
scegliere solo il proprietario.

**Perché.** Una correzione di una riga e una riprogettazione non sono lo stesso mestiere, e non
devono costare uguale. Il criterio è scritto nel manuale del ruolo — il modello economico per il
lavoro meccanico e delimitato, quello grosso per il progetto, il giudizio, i percorsi che toccano
soldi e le revisioni profonde; nel dubbio si sale di un livello — e la scelta viene detta al
proprietario **insieme alla ragione**, così vede per che cosa sta pagando.

**Dove.** `stage/1j-implementer-model`.

### La memoria di un turno gliela consegna l'app, non se la va a cercare

**Com'era.** Ogni sessione ricordava tutta la propria conversazione e la rimandava al modello a ogni
mossa: il contesto cresceva per tutto il giorno e non si azzerava mai. Quando le sessioni sono
diventate fresche — una per turno — il trascinamento è sparito, ma la sessione nuova si metteva a
cercare da sola quello che le serviva, rileggendo il canale da cima a fondo.

**Cos'è adesso.** Prima di svegliare una sessione, il bridge le scrive un **pacchetto**: le voci che
l'hanno svegliata, l'incarico che sta lavorando (cercato anche nell'archivio, non solo nella parte
recente del canale), il suo ultimo rapporto, lo stato del codice, e le righe del piano che la
riguardano. La sessione lo legge come prima cosa e usa il canale solo per un fatto che al pacchetto
manca. Al supervisore, quando toccherà a lui, il pacchetto porta anche il piano intero e la coda dei
messaggi del proprietario.

**Perché.** Ciò che una sessione va a cercare l'app lo ha già in mano: farglielo cercare è pagare due
volte, una in lettura e una in contesto. **Misurato** il 2026-09-09: le sessioni fresche aprivano il
proprio canale 7,8 volte per turno prima, circa una volta dopo; il contesto della prima chiamata è
passato da 249.000 token a 39.000, e il costo per chiamata dei membri da 345.000 a circa 100.000 —
stabile su tre finestre di misura diverse.

**Il prezzo pagato, detto chiaro.** Il guadagno per *turno* è più piccolo di quello per chiamata
(1,5 volte contro 3,4), perché i turni si sono allungati: un membro fresco con un incarico grosso
lavora fino alla scadenza dei trenta minuti. Il 41% dei token di quella sera stava in turni arrivati
alla scadenza. È il problema che viene affrontato adesso, non uno risolto.

**Una cosa imparata sulla forma, non sul contenuto.** Il pacchetto viaggia in un **file**, non
attaccato al messaggio: misurato che il testo passato per quella strada viene appiccicato al comando
di ruolo e finisce dentro il suo argomento, e ci sono ruoli che quell'argomento lo usano per
costruire percorsi. La strada comoda era anche quella che rompeva.

**Dove.** `stage/4b-state-pack`.

### Una promessa di spegnimento che il processo stesso tagliava

**Com'era.** Quando il servizio veniva fermato, l'app annunciava di aspettare i turni in corso —
fino a trentuno minuti — e trenta secondi dopo il sistema la dichiarava spenta. I turni a metà
morivano e ricominciavano da zero.

**Cos'è adesso.** L'attesa dichiarata è quella vera: i turni in volo arrivano alla fine, e solo dopo
le sessioni vengono chiuse. La prima fermata con la correzione attiva ha aspettato ventuno minuti e
ha scritto in chiaro «drenaggio completo, nulla da rifare».

**Perché era sfuggito.** Il tempo di spegnimento non era stato impostato da nessuno, quindi valeva il
predefinito di trenta secondi: l'ordine di grandezza sbagliato di sessanta volte. E la spiegazione
che tutti avevano in mano era un'altra — si dava la colpa alla configurazione del server, che invece
concedeva trentacinque minuti; il taglio era del processo. **Misurato** il 2026-09-09: tre riavvii in
sei minuti hanno ucciso cinque sessioni fresche, quindici milioni di token, e lo stesso turno è
ripartito da zero quattro volte.

**Il principio.** Un'attesa promessa in un messaggio e non impostata nel codice è una promessa che
non esiste. Le tre soglie in gioco — quanto aspetta chi lavora, quanto aspetta l'app, quanto aspetta
il sistema operativo — ora si calcolano da un unico posto, l'una sopra l'altra, e se qualcuno
configura un turno più lungo di quanto lo spegnimento possa reggere l'app lo dice all'avvio.

**Cosa cambia per chi lo usa.** Fermare il servizio adesso può prendere minuti invece di secondi. È
il prezzo giusto: prima era istantaneo perché buttava via il lavoro.

**Dove.** `stage/4e-host-shutdown-timeout`.

### Un turno lungo viene avvisato una volta, e il conto lo dice per intero

**Com'era.** Un turno lungo veniva tagliato dall'orologio: alla scadenza dei trenta minuti la sessione
era uccisa e le si chiedeva un rapporto. Il punto in cui si fermava lo scegliava la scure.

**Cos'è adesso.** Passate circa trentacinque chiamate, alla sessione arriva **un solo avviso**: sei a
questo punto, se sei arrivato a qualcosa di stabile salva e racconta, se sei a metà di una modifica
finiscila prima. Non è un divieto: nulla viene bloccato, e continuare è sempre permesso. La scadenza
resta la rete dura di prima.

**Perché.** Con le sessioni fresche il trascinamento tra turni è finito, e la spesa si è concentrata
**dentro** i turni lunghi: **misurato** il 2026-09-09, il 41% dei token dei membri stava in turni
arrivati fino alla scadenza, e il contesto dentro uno di quei turni sale attorno ai 200.000, quindi
ogni chiamata in più costa più della precedente. Un turno che finisce in un punto scelto costa meno di
uno tagliato a caso.

**Quello che questa voce racconta più della funzione.** La prima versione **non funzionava affatto** e
i suoi ventuno test erano verdi: due informazioni lette dal messaggio della CLI si fondevano in una
quando la seconda mancava — e mancava sempre, sulle sessioni che questo sistema avvia. I test
passavano perché il messaggio finto che usavano conteneva un campo che quello vero non ha. Da lì due
regole: **il messaggio finto di un test parte dalla forma vera**, e un test si giudica da una domanda
sola — passerebbe anche se la funzione fosse spenta?

**Cosa cambia per chi lo usa.** Nulla di visibile, se non che i turni lunghissimi dovrebbero
diradarsi. L'avviso dice anche una cosa che prima non diceva: il numero di chiamate è **del turno**,
non solo di chi lo riceve — un agente che ha delegato letture in parallelo ne ha spese molte senza
vederle nella propria cronologia.

**Dove.** `stage/4g-soft-boundary`.

### Il supervisore viene svegliato per decidere, non per prendere nota

**Com'era.** Ogni riga scritta da un membro svegliava il supervisore, e ogni suo risveglio gli fa
rileggere tutta la propria memoria. **Misurato** dal 6 al 9 settembre: circa 400 risvegli, **247
causati dai membri**, con un contesto medio di 398.000 token per chiamata — l'ordine di un milione di
token per un risveglio, e il 15% dei suoi turni era lungo quanto un cenno del capo.

**Cos'è adesso.** I messaggi del proprietario lo svegliano subito, come prima, su qualunque canale. Lo
sveglia subito anche un membro che dichiara di essere bloccato o che fa una domanda, e il primo
messaggio di un membro appena creato. I rapporti ordinari invece **si raccolgono**, per non più di
cinque minuti, e viaggiano insieme: un equipaggio che consegna a raffica compra un turno, non tre. Gli
avvisi meccanici dell'app non lo svegliano affatto — e non lo facevano già, cosa che si è scoperta
verificando invece di supporre.

**Perché.** È l'unico taglio grosso sul ruolo più costoso che **non tocca la sua memoria**, quindi non
mette a rischio il suo giudizio. E il principio: chi decide va interrotto per una decisione, non per
un aggiornamento.

**Il prezzo pagato.** Un rapporto ordinario può aspettare fino a cinque minuti prima di essere letto.
Il tetto è cinque minuti e non di più per una ragione precisa: a otto minuti l'app comincia a
sollecitare il supervisore perché "deve un verdetto" — solleciterebbe per un rapporto che essa stessa
sta trattenendo. Una configurazione oltre il tetto viene rifiutata, e il rifiuto **viene scritto nel
registro** con entrambi i numeri.

**Due difetti che questa voce deve raccontare, perché sono stati introdotti e corretti in giornata.**
La prima versione funzionava **una volta sola**: il suo cronometro non veniva mai azzerato, quindi dal
secondo rapporto in poi il supervisore si svegliava come prima. La seconda lo azzerava quando un turno
*partiva* — ma un turno che parte e **fallisce** non consuma i messaggi, e allora il tentativo
successivo si prendeva una finestra nuova: con tre tentativi un rapporto poteva aspettare venti minuti
invece di cinque. Adesso il cronometro si spende **dove i messaggi lasciano davvero il canale**. Il
difetto era lo stesso, spostato di confine: sono i posti dove una cosa "quasi sempre accade" a
nascondere i casi in cui non accade.

**Dove.** `stage/4h-wakeup-digest`.

### Il revisore ha il suo modello, e la firma di un messaggio non dà privilegi

**Com'era.** La configurazione aveva un modello per ruolo, ma non uno per il revisore né per il solo:
entrambi ereditavano quello dell'implementatore. Dire "implementatore economico, revisore grosso" era
letteralmente inesprimibile.

**Cos'è adesso.** Ogni ruolo ha la sua chiave, lette tutte da un unico punto, e una chiave assente
continua a seguire quella dell'implementatore — quindi una configurazione esistente si comporta
esattamente come prima.

**Cosa è stato tentato e ritirato, che è la parte che vale.** Insieme a questo era stata costruita una
cosa più ambiziosa: il supervisore scriveva `MODEL: sonnet` nell'incarico e il bridge la leggeva. È
stata **cancellata** dopo che una revisione avversaria ha dimostrato che si appoggiava a una firma
falsificabile: l'autore di un messaggio, in questo sistema, è solo testo, e un membro che cita un
messaggio nel proprio rapporto — cosa che il protocollo gli chiede — può far comparire una firma che
non è sua. Un membro si sarebbe potuto assegnare il modello costoso. Il privilegio passerà per il
foglietto di richiesta che il supervisore già usa: lì conta **dove** lo scrivi, non cosa ci scrivi.

**Il principio, oltre questo caso.** Un privilegio non si concede sulla base di un'affermazione che
chi ne beneficia può scrivere da sé. Dove serve autorizzare, l'autorizzazione deve venire da qualcosa
che il richiedente non controlla.

**Un difetto corretto nella stessa giornata.** Rendendo tollerante la lettura dei valori di
configurazione (un numero scritto dove va una parola non deve impedire l'avvio dell'app) era stato
riaperto, da un'altra porta, un difetto chiuso mesi prima: il motore del piano scivolava sul
comportamento predefinito **in silenzio**. Ora un valore del tipo sbagliato arriva a chi lo deve
rifiutare, e viene rifiutato ad alta voce.

**Dove.** `stage/4i-model-per-task`.

### Un'impostazione che si legge una volta sola è un'impostazione che mente

**Com'era.** Il modello del supervisore generale veniva fissato alla prima registrazione della
sessione e non veniva più riletto. La configurazione diceva un modello, la sessione ne stava usando
un altro dal giorno prima, e nessun riavvio poteva cambiarla.

**Cos'è adesso.** Il modello viene riconciliato con la configurazione all'inizio di ogni turno, cioè
nel punto in cui serve davvero, e la riconciliazione viene scritta nel registro.

**Perché era sfuggito.** Tutte le altre sessioni ripassano da un punto che rilegge la
configurazione — un membro lo fa quando viene aggiunto o fatto ripartire — e il supervisore generale
è l'unico che non ci ripassa mai, apposta: non c'è un processo da sorvegliare, quindi non viene mai
fatto ripartire. Un caso speciale corretto da una parte lascia scoperta la lettura dall'altra.

**Il principio, che vale oltre questo caso.** Un valore va riconciliato nel punto in cui produce
effetto, non nel punto in cui è comodo leggerlo. Altrimenti il sistema mostra un'impostazione e ne
applica un'altra, e chi la cambia crede di aver cambiato qualcosa.

**Dove.** `stage/4f-general-model-refresh`.

---

## 5. Onestà: che cosa il sistema può dire di sapere

Questa sezione è la più importante da leggere, perché contiene le regole che valgono anche fuori da
questo progetto.

### Un controllo che non trova ciò che deve controllare non permette tutto in silenzio

**Com'era.** Nove punti in sei script davano per scontato dove stessero le cartelle della
supervisione, ignorando l'indicazione ricevuta all'avvio. Se il servizio era stato avviato altrove,
ogni controllo cercava un percorso inesistente, non trovava niente, e **permetteva tutto**. Senza un
errore da nessuna parte.

**Cos'è adesso.** Ogni punto legge l'indicazione ricevuta, e un test elenca gli script leggendoli dal
disco invece che da una lista scritta a mano, così uno aggiunto domani viene preso lo stesso.

**Perché.** È peggio di una regola non applicata, perché il protocollo dice alla sessione il
contrario: «l'app ti ferma». Su quelle macchine quella frase era semplicemente falsa, e una sessione
onesta che ci crede si comporta come se fosse protetta.

**Dove.** commit diretto su `ours/integration` (`b828090`).

### Sei test rossi per sempre diventano salti onesti

**Com'era.** Sei test rossi su macOS da sempre, con una diagnosi scritta accanto che era pure
sbagliata (non era una questione di permessi).

**Cos'è adesso.** Provocano un guasto tenendo un file aperto: è una cosa che su Windows fa fallire
la scrittura, mentre su macOS e Linux il sistema non consulta affatto i file aperti dagli altri, e
quindi non c'è proprio niente da provocare. Adesso il test **prova la macchina** invece di leggerne
il nome, e dove il meccanismo non esiste salta dicendo la ragione vera. Dove esiste, verifica tutto
quello che verificava prima.

**Perché.** Una suite permanentemente rossa è il posto dove si nasconde un guasto vero: si impara a
dire «ah, sono quei sei» e si lascia passare il settimo. Da qui la regola di questo fork: si
confrontano i **nomi**, non il numero.

**Dove.** commit diretto su `ours/integration` (`b2dde29`).

### I test non si rubano più le prove a vicenda

**Com'era.** Le diagnostiche di un lock finivano in un raccoglitore unico per tutto il processo, e
ogni test che avviava il motore se lo riprendeva. Chi stava raccogliendo si vedeva sparire le prove
a metà, e il furto compariva come un fallimento suo, sotto un nome che non c'entrava niente. Cinque
esecuzioni complete rosse su sei.

**Cos'è adesso.** Ogni test raccoglie sul proprio flusso; la produzione è intatta. Zero fallimenti
su dieci esecuzioni complete.

**Perché conta.** Un rosso intermittente rende **inaffidabile ogni verifica di merge**: non si sa più
se si sta guardando un difetto nuovo o il solito rumore. E un caso è istruttivo: il finto Claude
usato nei test andava in crash quando due copie scrivevano nello stesso registro, e il crash faceva
riuscire un membro che il test voleva veder rifiutato — cioè prendeva esattamente la forma di un
guasto nel limite d'uso, in una prova dove il limite non era mai stato raggiunto. Il sintomo
accusava lo strato sbagliato.

**Una cosa provata e rifiutata**, scritta perché nessuno la riprovi: mettere tutte le trentuno classi
in un gruppo esclusivo. Misurato: 6 minuti e 26 contro 1 minuto e 35. Un costo di quattro volte su
ogni esecuzione, per sempre, per rendere deterministico un test.

**Dove.** `stage/7l-diagnostics-sink`, `stage/7m-flaky-resume-test`.

### La suite gira su tempi finti

**Com'era.** I test del bridge aspettavano tempi veri: 2583 test impiegavano 107 secondi di orologio
per 27 di calcolo. Uno solo aspettava 33 secondi veri.

**Cos'è adesso.** I tre valori di tempo del ciclo si iniettano; in produzione valgono esattamente i
numeri che erano scritti prima, identici. La suite passa da 107 secondi a 37. Nessun test è stato
cancellato, saltato o indebolito.

**Perché.** Una suite lenta si esegue di meno, e una suite che si esegue di meno non è una rete.

**Dove.** `stage/7d-fake-clock-tests`.

### Le regole del messaggio al proprietario sono controllate, non sperate

**Com'era.** Un conteggio meccanico del prompt del supervisore: 229 regole imperative e 14 coppie che
non si possono rispettare entrambe. Vince sempre quella più in basso, perché è l'ultima che il
modello ha letto. Due esempi: «parti dalla decisione» a riga 116 contro un accuso di ricezione
obbligatorio 396 righe più sotto — che è letteralmente la cosa che la prima regola vieta; e «una
domanda chiude il tuo turno» contro «costruiscilo e chiedi di passaggio» 783 righe più sotto. Il
modello soddisfa entrambe chiedendo in mezzo alla prosa senza il marcatore: così non accende
nessuna spia, non risulta da nessuna parte, e nessuno la insegue. Ecco come si perdono le domande.

**Cos'è adesso.** Un controllo automatico verifica **solo ciò che una macchina può stabilire**: due
domande in una voce sola, prosa sotto la domanda, opzioni senza domanda, un accuso di ricezione al
posto della risposta, un percorso di file o una traccia di errore mandati a chi non legge codice, la
lunghezza. E riporta senza ingoiare: il messaggio arriva comunque al proprietario, e la sessione si
vede dire nel proprio canale che cosa c'era che non andava.

**Perché così e non di più.** Un controllo che scarta il messaggio trasformerebbe un errore di forma
nella perdita di una risposta, che è il guaio peggiore di questo sistema. E «rispondi in modo
completo» o «scrivi in parole semplici» sono regole vere ma sono deliberatamente escluse: un
controllo che tira a indovinare insegna alla sessione a scrivere per il controllo.

**Dove.** commit diretto su `ours/integration` (`86d695e`).

### L'applicazione Windows si compila su Windows

**Com'era.** Il progetto dell'interfaccia gira solo su Windows, e le macchine su cui si scrive questo
repo non riescono nemmeno a provarci. Il 2026-09-09 sono finiti in un merge quattro file modificati
alla cieca, con scritto nel commit «manca ancora una compilazione Windows».

**Cos'è adesso.** L'integrazione continua la fa a ogni push, e compila **solo** ciò che le altre
macchine non possono: prima l'app da sola, così un errore la nomina, poi tutto il resto.

**Ha già trovato qualcosa, il secondo giorno.** Il 2026-09-10 un metodo di CoreLib è stato sostituito
e tutti i suoi chiamanti aggiornati — tranne i due dentro l'app Windows, invisibili a chi lavora su
macOS. Il ramo è rimasto rosso per tre push senza che nessuno se ne accorgesse leggendo il codice:
l'unica cosa che l'ha detto è stata questa macchina. Corretto in `stage/12-wpf-build-fix` e
integrato: i due punti chiedono adesso la stessa cosa che CoreLib chiede a poche righe dallo stesso
commento, così la riga del supervisore, quella del solo e il blocco di stato non possono rispondere
in modo diverso a «di chi è la mossa». **Verde sul runner Windows** sia sul ramo (`aeb2295`) sia sul
merge (`cb8f9c9`) — l'unica macchina che poteva dirlo. Vale la pena raccontarlo perché è la
dimostrazione del punto: non è che l'app *si compila* su Windows, è che adesso **qualcuno se ne
accorge quando smette**, e nel frattempo il ramo principale era rosso da tre push.

**Perché.** «Manca ancora una verifica» dentro un messaggio di commit non è una verifica: è un debito
che qualcuno deve ricordarsi di pagare. Se una macchina può pagarlo da sola, lo paga la macchina.

**Dove.** commit diretto su `ours/integration` (`a2adb76`).

### La revisione avversaria dopo ogni tornata

**Cos'è.** Dopo un gruppo di modifiche, revisori indipendenti in copie di lavoro separate, con
l'obbligo di **dimostrare** ogni difetto con una prova eseguibile, non di segnalarlo.

**Perché.** Su tre modifiche che cambiavano comportamento vivo, ha trovato difetti veri in tutte e
tre — quelli raccontati qui sopra nell'appuntamento del limite d'uso, nel turno di chiusura e nella
voce troncata. Nessuno di quei difetti era visibile da una suite verde, e tutti e tre sarebbero
arrivati al proprietario nella forma «l'app si è mangiata il mio messaggio». Non è cerimonia: è
l'unico controllo che ha trovato qualcosa.

**Dove.** raccontata in `docs/superpowers/specs/2026-09-09-report-lavoro-e-runbook.md`.

### Un solo metro per «troppo lungo», e le istruzioni dicono quello che il codice fa

**Com'era.** Due misure diverse della stessa regola sulla stessa voce: cinque righe in un posto, sei in
un altro, entrambe attive; le istruzioni del supervisore dicevano «etichette fino a trenta caratteri» dove
il codice tagliava a ventotto, promettevano «da due a quattro opzioni» che nessuno controllava, e
consigliavano di aprire con «noted —», che il controllo segnalava come chiacchiera. Il supervisore poteva
ricevere due correzioni diverse per lo stesso messaggio, e una per aver seguito il suo manuale.

**Cos'è adesso.** Una costante sola — cinque righe, seicento caratteri — letta da chi misura e da chi
controlla; da due a quattro opzioni per davvero (dalla quinta in su: correzione al supervisore e bottoni
numerati); ventotto anche nel manuale; via l'esempio «noted —»; e il paragrafo «solo tre tipi di voce
arrivano al telefono» riscritto, perché dopo la sezione 6 arriva tutto quello che il supervisore scrive.

**Perché.** Una regola che vive in due posti è due regole, e prima o poi lo diventa (è la lezione della
decisione 12 del file di istruzioni). Il resto di questo lavoro — far scrivere le voci attraverso uno
strumento che controlla prima di scrivere, così la grammatica dei marcatori vive in una costante letta da
chi scrive e da chi legge — è deciso e sta nella sezione 7.

**Dove.** `stage/8e-durations-and-the-general-header`; `stage/14` (il conteggio delle righe ignora le righe-marcatore —
prima ogni domanda ben formata, sei marcatori per costruzione, veniva ammonita come troppo lunga).

### Le voci di canale si scrivono con uno strumento che controlla prima, e la grammatica ha una casa sola

**Com'era.** Per far arrivare una domanda con i bottoni, il supervisore doveva scrivere a mano, byte per
byte, `QUESTION:`, `OPTION:`, `RECOMMEND:`, `RISK:`, `ROW:` sotto un'intestazione di cui indovinava anche
numero e ora. La grammatica viveva in tre manuali e in nove file di codice, ognuno con la sua copia: da
una parte il marcatore era scritto con i due punti, dall'altra riconosciuto senza. Ogni scostamento era
silenzioso: la domanda arrivava senza bottoni, o non arrivava. Lo strumento di scrittura accettava
qualunque firma: un membro poteva firmarsi «supervisore», ed è così che una scelta di modello è sparita da
un incarico. Il pacchetto di memoria indovinava l'incarico di un membro dalla prima parola dell'oggetto.

**Cos'è adesso.** La grammatica è **un file dati** (`kit/grammar/channel-grammar.json`) letto sia dallo
strumento di scrittura (bash) sia dall'app (.NET, che lo incorpora): nessun altro posto può scrivere un
marcatore, e un test lo verifica cercandolo. Lo strumento che già era «l'unico modo di scrivere su un
canale» accetta i tipi (`--question`, `--option`, `--recommend`, `--risk`, `--row`, `--state`,
`--report`, `--attach`), calcola da sé numero e ora della voce, e **rifiuta prima di scrivere** nominando
i campi mancanti. L'autore lo deduce dal ruolo che il lanciatore esporta alla sessione: una firma che non
corrisponde è rifiutata. Ogni voce porta un **tipo dichiarato** su una riga sotto l'intestazione, e il
pacchetto di memoria sceglie l'incarico dal tipo, non dalla parola. Le voci vecchie, senza tipo, si leggono
esattamente come prima: la transizione è per costruzione, e un test fissa che una storia interamente
«senza tipo» si comporti come ieri.

**Perché.** Un formato che un modello deve riprodurre a mano è un formato che ogni tanto sbaglia, e qui
lo sbaglio costava una decisione. Spostare il controllo **prima** della scrittura elimina la classe di
errore, non il singolo caso; tenere la grammatica in un file letto da chi scrive e da chi legge elimina la
deriva fra le copie (le decisioni 12 e 13 del file di istruzioni sono la storia di quella deriva). Il
prezzo: lo strumento dipende da `jq`, che il kit già richiedeva (ora lo verificano entrambi gli installatori, e
lo strumento rifiuta in una riga se manca, senza scrivere nulla); e le sessioni vive con il manuale vecchio
continuano a scrivere a mano finché non vengono riavviate con il kit nuovo — per questo la lettura delle
due forme resta.

**Cosa cambia per chi lo usa.** Niente sul telefono. Cambia per gli agenti: non «scrivi questa
intestazione», ma «chiama lo strumento».

**Dove.** `stage/13`, `stage/14` (i manuali non contengono più nessun modello da copiare a mano: ogni campo è una
flag dello strumento, `--deadline` e `--default` compresi).

---

### Un rapporto scritto non sparisce più: se un messaggio finale viene superato, si registrano entrambi

**Com'era.** Per il bridge «la voce di canale» era l'ultimo messaggio della sessione, e basta. Se un
sub-agente lanciato in background tornava *dopo* che il rapporto era stato scritto, la sessione si
risvegliava, scriveva un altro messaggio, e quello diventava la voce: il rapporto originale (una volta
19.771 caratteri, una volta una revisione di nove agenti) non arrivava a nessuno, e il turno diceva
«riuscito». Con il runner «print», poi, non c'era modo di vederlo: chiedeva alla CLI un solo documento
con una sola stringa.

**Cos'è adesso.** Entrambi i runner guardano tutti i messaggi «da fine turno» (solo testo, senza
chiamate a strumenti, non di un sub-agente). Se il risultato non li contiene, ogni messaggio superato
viene registrato come voce a sé, **prima** di quella finale, e la sessione riceve una riga che dice
cos'è successo. Il runner «print» ora chiede alla CLI lo stesso flusso di eventi che il supervisore usa
già in produzione, così la regola vale per implementer, revisori, solo e generale. E ogni skill dice:
non scrivere il messaggio finale con un sub-agente ancora in volo.

**Perché.** Un fallimento silenzioso è il peggiore che c'è: nessuno si accorge di niente, né la
sessione che crede di aver consegnato, né chi aspetta. Registrare entrambi i messaggi costa al massimo
una voce in più; perderne uno è costato una revisione intera. Il cambio di trasporto del runner print è
la parte da tenere d'occhio dopo il deploy: i test «dal vivo» pinnano ancora il formato vecchio.

**Dove.** `stage/19-a-final-report-is-never-superseded`.

### Il revisore ha una cartella dove scrivere, e il repository resta blindato

**Com'era.** Il guardiano del revisore negava `cp`, `mkdir` e ogni redirezione: niente scrittura,
da nessuna parte. Quindi niente mutation testing — copiare un file, romperlo di proposito e vedere se la
suite se ne accorge — che su questi rami è stato l'unico rilevatore a trovare quattro test che passavano
per il motivo sbagliato. I revisori si arrangiavano con mutanti in memoria, dichiarandoli più deboli.

**Cos'è adesso.** Una sola cartella è scrivibile, `…/<orchestrazione>/<membro>/scratch/`: dentro si può
creare, copiare, modificare e cancellare; fuori niente cambia. Una copia *da* scratch *verso* il
repository è rifiutata. Il harness del guardiano ha 42 casi in più (292) e ha chiuso due rossi che
c'erano già: un contatore di righe mai aggiornato e — su macOS — una coppia di byte nel guardiano che
bash 3.2 rileggeva come apertura di traduzione, cancellando un `$` prima che python lo vedesse.

**Perché.** Lo strumento migliore del ruolo era vietato proprio al ruolo. Il prezzo è una cartella in più
per sessione; il beneficio è un revisore che può provare invece di ragionare.

**Dove.** `stage/18-the-reviewer-gets-a-scratch-folder`.

### Un numero fatto per il 97 % di una cosa sola non è un numero

**Com'era.** Lo strumento che misura quanto costa il sistema sommava in un totale unico quattro
quantità diverse: il testo che il modello elabora davvero, quello che gli viene ripassato identico
alla richiesta precedente, e la risposta. Misurato sul server il 2026-09-10, **il 97 % di quel
totale è la seconda** — contesto che il modello aveva già ricevuto e che gli viene ripresentato.
Il guadagno annunciato dopo il lavoro sulla memoria fra i turni, «meno 23 %», era un movimento
dentro quella parte lì.

**Cos'è adesso.** Le tre quantità si leggono separate e non si sommano mai. Quella che conta si
chiama contesto per chiamata, ed è la sola che il lavoro sulla memoria sposti davvero: una cache
mancata non la cambia, sposta soltanto token da «ripassato» a «nuovo». Lo strumento accetta un
intervallo di date qualsiasi, e non ha più dentro né una data, né un elenco di sessioni, né un
divisore.

**Perché.** Un metro che somma cose di significato diverso non misura: dà un numero che si muove
per la ragione sbagliata. E quattro dei difetti che aveva erano di questa stessa famiglia — ogni
membro che non fosse un implementatore veniva dichiarato revisore; i divisori per «cosa abbiamo
consegnato» erano stati stimati a occhio, con accanto un commento dell'autore che diceva di non
esserne sicuro; lo stesso istante era scritto in due formati diversi in due file, confrontati come
testo. Il quinto era il più istruttivo: **i supervisori venivano riconosciuti da un elenco di due
sessioni scritto a mano**, perché l'informazione vera vive in un file nascosto che il criterio di
ricerca usato non poteva vedere. Non era pigrizia, era una cosa invisibile. Leggendola, per il solo
8 settembre, **423 chiamate da 539 mila token di contesto l'una** escono da «non attribuito» ed
entrano sotto «supervisore».

**Il prezzo.** I due strumenti precedenti sono stati rimossi, quindi i numeri pubblicati a suo tempo
non si rigenerano più con il codice che li aveva prodotti: restano nella storia del repository e
nel rapporto che li discute. La scelta è voluta — un metro rotto lasciato lì viene riusato.

**Verificato** il 2026-09-10 contro gli stessi dati misurati a mano, senza modificare nessuna
costante: il contesto per chiamata di un implementatore è 390 mila nella finestra dell'8 settembre e
104 mila in quella del 10; la serie ora per ora sale da 146 a 442 mila prima del cambiamento e resta
piatta dopo. Una seconda lettura, che prende i dati da tutt'altra parte, dà lo stesso risultato: il
contesto della prima chiamata di un turno passa da 207 a 66 mila.

**E un errore evitato per un soffio, scritto qui perché non torni.** La prima versione dello
strumento mostrava anche «il contesto della prima chiamata» in una lettura per intervallo, e dava
zero. Su una finestra non si distingue l'avvio di una sessione dalla prima chiamata che capita
dentro l'intervallo, e una chiamata a metà sessione è tutta cache. La colonna è stata tolta, non
spiegata: un numero sbagliato è peggio di un numero mancante.

**Dove.** `stage/21-the-instrument`.

### Il conto di quello che è stato consegnato adesso ha una storia

**Com'era.** Il sistema sapeva dire quanto consuma e non quanto produce. «Ieri sono stati bruciati
600 milioni di token» non è giudicabile da solo: lo stesso numero è un affare contro venti righe
consegnate e un disastro contro una. Il conto non mancava — il piano di lavoro lo porta — ma
mancava la sua storia. Misurato sul server il 2026-09-10: **nessuna delle sei lavorazioni tiene il
proprio piano sotto controllo di versione**, il file viene riscritto sopra se stesso, e il registro
dell'app annota «il piano è in ritardo sui verdetti» e non annota mai «questa riga si è chiusa alle
14:23». Il conto di ieri non esiste più e non è recuperabile.

**Cos'è adesso.** Un campionatore legge i piani a ogni ora e ne annota i conteggi. Da lì si ricava
quante righe si sono chiuse fra due momenti, che è il divisore che mancava: non più «ieri abbiamo
speso Y», ma «questa consegna è costata X».

**Perché non dentro l'app.** Un evento scritto dall'applicazione sarebbe esatto al minuto e
costerebbe una compilazione e una messa in produzione che riavvia sei lavorazioni vive — e le tre
regole più dure di questo progetto parlano proprio di quanto si perda fra ciò che si compila, ciò
che si installa e ciò che gira davvero. Il costo per consegna non ha bisogno del minuto: «fra le 14
e le 15 si sono chiuse due righe» divide altrettanto bene. Il campionatore vive accanto
all'applicazione, non tocca niente di suo, e si toglie cancellando due file di avvio, uno script e
una cartella.

**Conta come conta l'app, non «più o meno».** I sei marcatori del piano, la voce «non si fa» tenuta
fuori dal totale, «bloccato sul proprietario» contato sia fra i bloccati sia a parte, e soprattutto
le sezioni **parcheggiate** saltate — così una scoperta che nessuno ha chiesto non può muovere la
barra del proprietario. Un divisore che non è d'accordo con la barra di avanzamento è peggio di
nessun divisore.

**La metà che i dati veri non toccano.** Al momento dell'installazione tutti e sette i piani vivi
avevano zero righe sotto una sezione parcheggiata: un'esclusione che avesse smesso di funzionare
sarebbe sembrata corretta, perché ogni conteggio coincideva con quello ingenuo. Il controllo interno
usa quindi un piano finto che la esercita, e **è stato rotto apposta tre volte prima di crederci** —
spegnendo l'esclusione, contando «non si fa» nel totale, togliendo «bloccato sul proprietario» dai
bloccati: rosso tutte e tre le volte.

**Il prezzo, e va detto subito.** Il campionatore **parte da zero**: la prima misura è la linea di
partenza, e di tutto quello che è stato consegnato prima non resta niente. E duplica le regole di
conteggio dell'applicazione: se quelle cambiano, va cambiato anche lui, altrimenti i due numeri
divergono in silenzio.

**Dove.** `stage/21-the-instrument`; installato sul server come temporizzatore d'utente, che è la
convenzione della macchina, senza privilegi di amministratore.

### Una promessa scritta nello script, annullata in silenzio da un'impostazione globale

**Com'era.** Lo script che mette in produzione dichiara, nei suoi commenti: *«il clone è HTTPS e in
sola lettura di proposito: nessuna chiave di deploy, nessun token, **niente su questa macchina può
pubblicare**»*. Misurato il 2026-09-11: il server si autentica a GitHub come utente reale, e da
quella cartella un `push` **passava**. Non l'aveva cambiato nessuno per distrazione. Sulla macchina
c'era una riscrittura **globale** degli indirizzi — ogni indirizzo `https://github.com/` viene
tradotto in quello con la chiave — messa perché altri due repository, che il server pubblica
legittimamente, funzionassero senza attriti. Lo script clonava in HTTPS come dichiarava, il file di
configurazione conteneva l'indirizzo HTTPS, e git ne usava un altro. La garanzia era falsa da chissà
quando, e nessuna delle due parti era sbagliata di per sé.

**Cos'è adesso.** Quel clone porta una regola locale che neutralizza la riscrittura solo per sé —
vince perché è più specifica — e la garanzia è tornata vera, verificata con il fallimento che deve
produrre: scaricare funziona, pubblicare risponde *«nessun accesso in scrittura anonimo»*. La chiave
resta dov'è: serve agli altri due repository. La copia di lavoro separata, quella su cui si lavora a
mano, continua a poter pubblicare perché è il suo mestiere.

**Perché.** Non è la protezione che conta — chi vuole pubblicare da quella macchina può ancora farlo
in dieci modi, ed è la decisione 21 applicata a sé stessa: una guardia lì trattiene una sessione
onesta e non ferma nessun altro. Quello che conta è che **una frase scritta in un file non può
restare vera per conto suo**. Valeva quando è stata scritta; una configurazione aggiunta altrove,
per un motivo del tutto ragionevole, l'ha resa falsa senza toccare né il file né chi l'aveva scritto.
La sola cosa che l'ha scoperta è stato provarla.

**Il prezzo.** La guardia **non si ripristina da sola**: se quel clone viene cancellato e rifatto, la
regola locale sparisce e la riscrittura globale torna a valere in silenzio. Lo script che dovrebbe
rimetterla non sta in questo repository. Finché non ci sta, va riprovata dopo ogni ricostruzione —
ed è registrata fra le cose aperte per quello.

## 6. Il ponte con Telegram: che cosa regge sotto

L'audit del 2026-09-09 sull'integrazione Telegram (i brief in
`docs/superpowers/specs/2026-09-09-telegram-briefs-A-B-C.md`) ha trovato, oltre a quello che si vede sul
telefono, difetti nel modo in cui il ponte parla con Telegram. Queste sono le regole che valgono adesso.

### Due ponti sullo stesso bot vengono detti, non subiti

**Com'era.** Telegram accetta un solo ascoltatore per bot. Se l'app Windows e il servizio sul server
ascoltavano insieme, Telegram rispondeva «conflitto» e il ponte lo trattava come un errore qualunque:
riprovava ogni minuto per sempre, in silenzio, e i messaggi del proprietario non arrivavano più a nessuno.
È il guasto più comune di tutto l'ecosistema dei ponti Telegram, plugin ufficiali compresi.

**Cos'è adesso.** Il conflitto è riconosciuto per quello che è: un messaggio in General che nomina la
macchina che sta parlando («un altro ponte ascolta con questo bot; io sono …»), una riga di registro per
cambio di stato, una al ripristino. All'avvio il ponte verifica il bot e toglie un eventuale webhook. Una
chiave di configurazione (`telegramInbound`: `poll` o `off`) dice a un host di non ascoltare; senza chiave
ascolta, così niente smette di ricevere in silenzio.

**Perché.** Un lucchetto su file protegge solo due processi sulla stessa macchina; due macchine con
cartelle diverse nessun file le vede. L'unica cosa onesta è dire al proprietario quale delle due fermare.

**Dove.** `stage/8b-no-lost-messages-no-false-receipts`.

### Un aggiornamento cattivo costa solo se stesso

**Com'era.** Il ponte segnava «letto fino a qui» solo dopo aver gestito tutto il lotto di aggiornamenti
ricevuto da Telegram: un'eccezione su uno faceva rigiocare tutti gli altri. Il 2026-09-08 un comando
solo-Windows lanciato sul server Linux ha fatto saltare il gestore, e lo stesso lotto — tocchi compresi —
è stato rieseguito quattro volte.

**Cos'è adesso.** Ogni aggiornamento è isolato e segnato da solo; un tocco arrivato due volte (Telegram lo
fa) viene riconosciuto dal suo identificativo e agito una volta. I comandi che hanno bisogno di uno
schermo (mostrare una finestra, fotografarla, disporle) chiedono all'host se ce l'ha: su Linux e, in
futuro, su macOS rispondono in una riga «non disponibile su questo host» invece di lanciare.

**Perché.** La scelta è «al più una volta»: un messaggio perso si vede (manca il ✓), un comando eseguito
due volte no.

**Dove.** `stage/8b-no-lost-messages-no-false-receipts`.

### Il limite di velocità conta tutto, e sopravvive a un riavvio

**Com'era.** Il contatore che rispetta il tetto di Telegram (venti messaggi al minuto per gruppo)
contava solo gli invii che creano un messaggio; modifiche, cancellazioni e risposte ai tocchi — le
chiamate più frequenti — passavano fuori dal contatore e non venivano ritentate. Al riavvio il contatore
partiva pieno: dopo un crash in loop, dieci messaggi in un secondo. La lettura degli aggiornamenti non
passava da nessun contatore.

**Cos'è adesso.** Due contatori: uno per gli invii, uno per il resto (modifiche, cancellazioni, tocchi,
letture), con i tempi di attesa che Telegram chiede rispettati in entrambi. Il contatore degli invii
**si ricorda** dov'era: i suoi due numeri stanno nello stesso file di stato riscritto a ogni giro, e un
riavvio riparte da lì — niente raffica gratis dopo un crash, niente attesa artificiale dopo un riavvio
normale. Il secondo contatore parte vuoto.

**Perché.** Un limitatore che una parte delle chiamate aggira non limita niente; un contatore che si
azzera a ogni riavvio premia proprio il processo che si riavvia troppo.

**Dove.** `stage/9b-telegram-hygiene`, `stage/11`.

### Gli errori di Telegram si riconoscono da una tabella, non da una frase

**Com'era.** In quattro punti diversi il ponte decideva cosa fare cercando pezzi di frasi inglesi nel
testo dell'errore di Telegram («message to edit not found», «not modified»…): una parola cambiata da
Telegram e il comportamento cambiava in silenzio.

**Cos'è adesso.** Una tabella sola, da codice e descrizione dell'errore al caso che il ponte distingue,
fissata da test. Lo stesso per i file: i limiti di Telegram (dieci megabyte per una foto, con le regole su
dimensioni e proporzioni; cinquanta per un documento; venti in scaricamento) sono controllati prima di
inviare, e un rifiuto viene detto all'agente con il rimedio. I messaggi vengono misurati come li misura
Telegram (dopo il parsing dei tag, non contando il markup) e non si spezzano mai a metà di un'emoji.

**Perché.** Ogni regola letta da Telegram e non scritta da noi è una regola che possiamo solo scoprire
rotta; scriverla una volta, con un test, è l'unico modo di saperlo prima.

**Dove.** `stage/9b-telegram-hygiene`.

### Colori, menu e filtri

**Cos'è adesso.** Ogni argomento nasce con il colore del suo repository (sei colori, a rotazione stabile).
Il menu ☰ tiene tutti i trentadue comandi, in inglese come ogni stringa dell'app, ordinati per uso e con
quelli solo-Windows in fondo. I tocchi sui bottoni e i messaggi di servizio vengono accettati solo se
arrivano dal nostro supergruppo, non solo dall'utente giusto. Il livello che parla con Telegram ha i suoi
primi test.

**Perché.** Sono le cose che non si notano finché ci sono, e si notano tutte insieme quando mancano.

**Dove.** `stage/9b-telegram-hygiene`, `stage/10`, `stage/11`.

---

### Il supervisore non fa la fila dietro ai suoi operai

**Com'era.** Tre turni in parallelo per orchestrazione, in coda, per tutti. Con cinque implementer
avviati il messaggio del proprietario si metteva in fila **dietro** a loro: misurato il 2026-09-10,
ventinove minuti dalle 21:18 alle 21:47, con il turno del supervisore partito nel secondo esatto in cui
un implementer veniva ucciso al suo limite. E intanto il telefono diceva «still at it», perché per l'app
«ammesso» e «in esecuzione» erano la stessa cosa.

**Cos'è adesso.** I ruoli che parlano col proprietario (supervisore, solo, generale) non prendono uno
slot: ognuno è una sessione sola con un turno alla volta, quindi al massimo un turno in più per
orchestrazione. L'app distingue «in coda» da «in esecuzione», lo dice sul telefono con le parole giuste,
e non scrive «il proprietario ti aspetta» a una sessione che non può nemmeno partire.

**Perché.** Il turno del supervisore *è* la linea telefonica del proprietario. I limiti esistono per
il ventaglio degli implementer, non per lei.

**Dove.** `stage/16-the-supervisor-never-queues`.

### Un tocco vale un'attesa: la riscrittura del messaggio ritenta, e la riga di stato smette di martellare

**Com'era.** Telegram limita le modifiche a uno stesso messaggio molto più delle nuove invii al gruppo.
La riga PULSE aveva un freno di 30 s dopo un rifiuto — testato, in produzione mai raggiunto: un blocco
aggiunto per i cambi «solo bottoni» rifaceva la decisione da solo, e dopo un fallimento il testo
risultava «cambiato» a ogni tick. Risultato: 382 rifiuti in 56 minuti, 357 della PULSE, ritentata ogni
2 secondi con Telegram che diceva «aspetta 34, 31, 29…». Dentro quella tempesta, il tocco del
proprietario: la riscrittura del messaggio rifiutata, il ripiego «almeno togli i bottoni» rifiutato,
nessuno dei due ritentato. Sul telefono: testo vecchio, bottoni vivi su una domanda già risposta —
mentre l'azione era passata.

**Cos'è adesso.** La promozione a «modifica» passa per lo stesso freno del pianificatore. La riscrittura
dopo un tocco aspetta il numero che Telegram le dice (fino a 30 s), tre tentativi in tutto, fuori dal
ciclo che legge i tocchi, e solo dopo si arrende come prima. Insieme a stage 15 (PULSE ogni 5 minuti e
un freno per singolo messaggio) va in produzione nello stesso riavvio.

**Perché.** Un tocco del proprietario vale più di una riga di stato: se deve aspettare mezzo minuto per
vedersi rispondere, aspetta. E un freno che esiste ma non morde è peggio di nessun freno: rassicura chi
legge il codice.

**Dove.** `stage/16-the-supervisor-never-queues` (piano `2026-09-10-stage-17-…`).

### Una domanda nuova chiude quelle a cui il proprietario aveva già risposto a parole

**Com'era.** Con più domande aperte una risposta scritta non si lega a nessuna (giusto: l'app non
indovina), e nient'altro le chiudeva se non un tocco, una scadenza, la modalità «assente» o la chiusura
dell'orchestrazione. Il proprietario rispondeva a parole, il supervisore andava avanti, e le domande
restavano su PULSE come «waiting on you» per ore — sei, alle 21:50 del 2026-09-10, la più vecchia
delle 15:58.

**Cos'è adesso.** Se il proprietario ha scritto qualcosa **e poi** lo stesso supervisore fa una domanda
nuova nello stesso topic, le domande precedenti a quella risposta vengono chiuse come «superate»:
fuori dal registro, bottoni consumati (un tocco tardivo viene rifiutato, non inoltrato come risposta
vecchia), messaggio riscritto per dirlo, e una riga al supervisore: se una serviva ancora, la rifà.
Due domande davvero parallele, senza un messaggio del proprietario in mezzo, restano entrambe aperte.

**Perché.** La risposta a parole seguita da una domanda nuova è la prova che le vecchie sono state
trattate in prosa; chiuderle *sempre* invece avrebbe rotto il contratto «più decisioni aperte, ognuna
risolvibile da sola» che tre sonde pinnano. Scelta del proprietario, la variante prudente.

**Dove.** `stage/16-the-supervisor-never-queues` (piano `2026-09-10-stage-20-…`).

## 7. Che cosa stiamo facendo adesso

Questa sezione è l'unica che parla al futuro, quindi invecchia in fretta: **aggiornata al
2026-09-10, notte**. Chi la legge dopo la confronti con `docs/superpowers/specs/`, dove il piano vive.

**Sei rami fusi e non ancora in produzione — un solo riavvio, quando i batch in corso hanno finito
(decisione del proprietario, 2026-09-10 sera).** `stage/15` (la PULSE ogni cinque minuti, il freno
per messaggio), `16` (il supervisore non fa la fila; il tocco ritenta; la PULSE rispetta il suo freno;
le domande superate), `18` (la cartella scratch del revisore) e `19` (il rapporto finale non viene
mai superato in silenzio; il runner print passa a `stream-json`). Deploy: `bash ~/aiorch-vps/03-aiorch.sh`
sul server (binario **e** kit nello stesso passo). Due misure la sera dopo: i 429 nel journal
(`journalctl -u aiorchestrator --since … | grep -c "HTTP 429"` — erano 382 in 56 minuti) e che un turno
implementer produca la sua voce con il trasporto nuovo: i test «dal vivo» pinnano ancora
`--output-format json`, quindi la prima prova vera del lettore `stream-json` sul runner print è il deploy
stesso. Le sonde, i controlli per mutazione e i parcheggi sono nei piani `2026-09-10-stage-16…20`.

**Il blocco Telegram va in produzione oggi.** Binario **e** kit nello stesso passo: le istruzioni del
supervisore sono cambiate (ri-fa la domanda dopo «Let's talk», dichiara il suo stato a fine turno per
PULSE); con il binario nuovo e le istruzioni vecchie le decisioni discusse non verrebbero più riproposte.
La prima sera è una misura: quante volte vibra il telefono, e se i sei campi di PULSE dicono il vero.

**Consegnato tutto il blocco.** I sei pacchetti dell'audit Telegram sono fusi su `ours/integration`
(le ricevute come reazioni e le voci tipizzate sono atterrate il 10/9 pomeriggio); il deploy — binario e
kit nello stesso passo, lo strumento di scrittura compreso — è del proprietario. Resta da fare G, la suite
affidabile sotto carico, deciso per dopo.

**Il lavoro sui token è stato riletto da fuori, e il risultato regge — ma non per la ragione che
era stata scritta.** Una sessione che non l'aveva fatto ha rimisurato la produzione il 2026-09-10.
Il guadagno c'è ed è più grande di quanto annunciato: il contesto per chiamata di un implementatore
scende del 76 %, a modello e ruolo costanti. La prova però non è il totale di giornata — quel
confronto metteva una giornata intera contro mezza, sommava classi di token diverse, e cambiava di
un ordine di grandezza a seconda di quale giorno si prendesse come partenza. La prova è **la
forma**: prima il contesto di ogni chiamata cresceva senza fermarsi dentro la sessione, adesso è
piatto. E il merito è di **una sola** delle cinque modifiche spedite, il pacchetto di memoria, andato
in produzione la sera dell'8 e non del 9: attraverso la messa in produzione del 9 sera i numeri non
si muovono di niente. Il rapporto, gli script e le due revisioni che l'hanno corretto stanno in
`docs/superpowers/specs/2026-09-10-independent-review/`.

**Il passo del supervisore resta in calendario, e adesso si sa che ci si arriva senza la protezione
prevista.** Il piano dei token voleva prima il raggruppamento dei rapporti, sul conto che avrebbe
dimezzato i risvegli del supervisore — «meno 40 %» a stima. Misurato: **circa il 9 %**, un quarto
di quello. La condizione non era «se questo fallisce il passo è pericoloso», era di ordine: prima la
cosa gratis. Quella cosa gratis non è arrivata, il supervisore è ancora l'unico ruolo che si
trascina la trascrizione a **546 mila token per chiamata** — sei volte un implementatore — e il suo
pacchetto è già scritto e spento. Il rischio, che è sul giudizio e non sul costo, è esattamente
quello di prima.

**Cose aperte, dette perché non sembrino risolte.**
- **La sola lettura del clone di produzione non si ripristina da sola.** La regola locale che
  neutralizza la riscrittura globale degli indirizzi vive solo in quel clone: cancellarlo e
  rifarlo la perde, e la garanzia scritta nello script torna falsa senza che niente lo dica. Il
  posto giusto per rimetterla è lo script del server, che non sta in questo repository. Da
  riprovare dopo ogni ricostruzione, con il comando che deve **fallire**: un `push` di prova deve
  rispondere «nessun accesso in scrittura anonimo». Da valutare a parte se la riscrittura globale
  serva ancora: i due repository per cui è stata messa usano già indirizzi con la chiave, quindi
  forse non serviva a loro nemmeno allora.
- **Lo sportello unico verso Telegram — progettato, non costruito (2026-09-10, notte).** Oggi ogni
  parte del programma parla con Telegram *dentro* il battito da due secondi, e siccome il battito non
  può aspettare i trenta secondi che Telegram chiede, ogni punto di chiamata si è inventato la sua
  regola: alcuni rallentano, alcuni si arrendono, tre non fanno né l'uno né l'altro. Il disegno
  approvato toglie gli invii dal battito — il battito deposita intenzioni, un solo incaricato per
  conversazione le spedisce, gli incarichi non si perdono e i ridisegni superati si buttano — ed è
  **vincolato a una misura che non esiste ancora**: il server gira ancora il binario delle 20:56, che
  precede tutte le correzioni, quindi i 388 dell'ora sono codice vecchio. Prima si mette in produzione
  quello che c'è, si conta, e poi si decide se lo sportello serve com'è disegnato. La spec è in
  `docs/superpowers/specs/`.
- **Sette cose sono state proposte dalla revisione indipendente, tre fatte e quattro no. Le quattro
  stanno qui perché non sembrino sviste.** Fatte: la verifica che il promemoria di metà turno non
  rompe la cache (non la rompe — costa circa mille token per turno, venti volte in cinque giorni),
  lo strumento di misura rifatto e il campionatore del piano. Le altre quattro non sono state fatte
  di proposito, e ognuna ha una ragione diversa.
- **Il tetto al pacchetto va riscritto come avviso, non come taglio — ed è un errore del
  proponente.** La proposta originale era di limitare la dimensione del pacchetto di memoria. Ma le
  voci che hanno svegliato il turno sono senza tetto **per scelta**, e il codice lo dice: sono la
  ragione per cui il turno esiste. Tagliarle significa svegliare una sessione per del traffico e poi
  non dirglielo — cioè fabbricare esattamente il guasto che il rapporto di chiusura di uno spoke
  esiste per intercettare. La forma giusta è un avviso quando il pacchetto supera una soglia, così
  lo vede una persona. Nessun incidente misurato: è un buco strutturale.
- **Lo sforzo scelto per compito dal supervisore — idea del proprietario, parcheggiata per mancanza
  di metro.** Lo sforzo è la leva che muove i token in uscita, che è l'unica quantità che nessun
  altro cambiamento tocca. Un valore fisso per ruolo è cieco; il supervisore invece sceglie caso per
  caso, e già lo fa per il modello. Resta però un baratto qualità contro token, e oggi il supervisore
  non ha nessun modo di sapere se lo sforzo basso ha fatto danni. Si sblocca con il banco di prova
  qui sotto, non prima.
- **Il freno esiste già, ed è mal tarato — misurato il 2026-09-11, e ha una specifica sua.** Un
  turno di membro su tre viene ucciso al limite dei trenta minuti, e nei trenta minuti prima di
  morire quei turni **stavano lavorando**: le chiamate al modello non calano mai verso lo zero.
  In tre giorni quel limite ha tagliato lavoro 57 volte e non ha intercettato **nessun** blocco
  vero. Il freno gentile che dovrebbe precederlo — l'avviso a 35 chiamate — raggiunge 25 di quei
  57 turni, mentre quei turni ne fanno 74 di mediana. Il disegno proposto, le eccezioni che
  servono (il supervisore sta zitto sette ore di mediana **per mestiere**) e cosa non è stato
  misurato stanno in `docs/superpowers/specs/2026-09-11-the-brake-that-watches-work.md`.
- **Il freno ai sub-agenti: ritirato in questa forma.** La riga qui sotto resta come storia; la
  specifica del 2026-09-11 spiega perché aggiungere un secondo freno prima di aver sistemato il
  primo è il modo di ritrovarsene tre che si contraddicono. Per i sub-agenti la leva giusta è un
  patto su quanto restituiscono, non una ghigliottina.
- **~~Il freno ai sub-agenti: rimandato, non scartato.~~** I sub-agenti sono il capitolo di spesa più
  grosso rimasto — il 10 settembre costano più degli implementatori che li lanciano, e il loro
  contesto per chiamata è salito del 51 % rispetto all'8. Il freno esiste già nello strumento a riga
  di comando ed è verificato presente su questa macchina (un'opzione inventata dà «unknown option»,
  quella no, pur non comparendo nell'aiuto). Non è stato messo perché un freno che tronca un lavoro a
  metà produce **rifacimento**, e rifare costa più di quanto il freno risparmi; e perché non è stato
  verificato che cosa conti né come termini. Va tarato sulla distribuzione misurata delle chiamate
  per turno, mai a occhio, e deve fallire verso il completamento.
- **Il taglio dell'output dei comandi: rimandato per lo stesso motivo.** Un agente che non vede
  l'output del test che fallisce lo rilancia, o tira a indovinare. Il taglio va fatto con criterio —
  tenere la coda di una traccia d'errore, buttare il mezzo — non a un numero di caratteri.
- **Manca il banco di prova fisso, ed è la cosa che rende giudicabili le tre qui sopra.** Lo stesso
  identico insieme di compiti eseguito con e senza la modifica: è l'unico disegno che elimina la
  differenza di carico di lavoro **per costruzione**, invece di correggerla dopo. Senza, applicare
  quelle leve è fede, e confrontare due periodi di produzione resta un disegno che non regge — è il
  difetto che ha reso fragile il numero di partenza di tutto questo lavoro.
- **Se la scelta del modello sia «solo prezzo» è una domanda aperta.** La seconda revisione l'ha
  tolta dall'analisi perché non muove il conteggio dei token. Vero per un conteggio grezzo; non
  ovvio per il vincolo che morde davvero qui, che è il limite d'uso dell'abbonamento — e l'app quel
  limite lo sorveglia già, con gli allarmi al 90, 95, 97 per cento. Se quel limite è pesato per
  modello, un token del modello grande e uno del piccolo non sono la stessa unità della risorsa
  scarsa, e la scelta del modello torna a essere una leva di prima classe. Si risolve con i dati che
  l'applicazione già raccoglie.
- **Tre turni di implementatore falliti a livello di sistema su una lavorazione, visti passando.**
  Preesistenti al lavoro di revisione e non indagati: non era quello che era stato chiesto, e il
  costo di una scoperta non è la sua correzione, è l'orizzonte che apre.
- **La suite non è affidabile sotto carico — deciso: si fa dopo A–F.** Verde in seriale a macchina
  quieta; in parallelo con un'altra suite in corsa ha circa un rosso a corsa, sempre in un test a tempo
  reale diverso, sempre verde da solo. Misurato il 10/9: su 352 file di test, 62 aspettano tempo reale,
  91 leggono l'orologio vero, 7 usano un orologio finto, 144 cancellano la cartella temporanea senza
  guardia. Il proprietario ha deciso di finire prima i sei pacchetti Telegram e poi fare **G**: prima la
  versione leggera (bonifica delle 144 cancellazioni; le sonde a tempo reale in una collezione che gira
  una alla volta; la regola «nessuna suite mentre ne gira un'altra», e «chi tocca una sonda a tempo
  reale la converte»), circa un giorno `[stima]`; la conversione completa all'orologio finto (3–5 giorni
  `[stima]`) solo se la leggera non basta. Fatto quando: cinque corse parallele consecutive verdi con
  un'altra suite in corsa. Il brief è la sezione G del file dei brief.
- ~~I guadagni sono misurati prima della messa in produzione~~ — **rifatti sul server il
  2026-09-10**, diciannove ore dopo la messa in produzione delle correzioni del 9: **zero** righe
  «said nothing for» (il difetto dell'orologio del silenzio, che prima ne produceva ventisei),
  sessantanove turni di chiusura eseguiti, cinque riarmi del sorvegliante su Linux — il percorso che
  su macOS si poteva solo dedurre — e nove errori in tutto, tutti chiamate a Telegram che va e
  viene. Quel che resta non misurato in produzione è il costo per turno: i numeri sulle aperture di
  file vengono ancora dal banco di prova.
- ~~La grazia di spegnimento non è onorata dall'unità di sistema~~ — **chiusa, e la diagnosi era
  sbagliata.** Chi l'ha scritta (io, il 2026-09-09) aveva dato la colpa alla configurazione del
  server dopo aver misurato novanta secondi. Il taglio vero era dentro l'host .NET, che nessuno
  aveva impostato e che si fermava a trenta: sessanta volte più corto, e in un posto dove nessuno
  guardava. Corretto in `stage/4e-host-shutdown-timeout`; misurato il 2026-09-10 sul server,
  l'attesa effettiva è di quaranta minuti. La lezione è la solita di questo file: un numero
  osservato in un punto non nomina la causa, e la spiegazione comoda — «sarà la configurazione» —
  è quella che nessuno ricontrolla.
- Il file di istruzioni del progetto principale è stato modificato benché sia territorio di chi ha
  scritto l'originale: andrà risolto al prossimo allineamento. Stesso destino per due voci di quel file
  che il codice non rispecchia più (la vista unificata «taggata» dei canali, che non esiste; il guardiano
  elencato fra i tagli e costruito).
- Un ramo (`stage/4c-closing-turn`) è rimasto fuori dall'integrazione senza che nessuno ne rivendichi la
  paternità: va fuso o buttato, a chi l'ha scritto.

**Le tre modifiche di quella notte sono state fermate, corrette e consegnate il giorno dopo.** Il
consiglio a metà turno, il riassunto di risveglio del supervisore e la chiave del revisore sono state
scritte la notte del 9 settembre, **rotte una per una da una revisione avversaria indipendente** con
una prova per ogni difetto, corrette, **rotte di nuovo** dalla seconda revisione — che ha trovato una
regressione introdotta dalla prima correzione — corrette ancora, e messe in produzione il 10 settembre
alle 11:43. Le tre voci qui sopra raccontano cosa fanno e cosa è costato; i difetti con le loro sonde
stanno in `docs/superpowers/specs/2026-09-09-review-findings-4g-4h-4i.md`.

Vale la pena dire *come* erano rotte la prima volta, perché sono tre volte lo stesso errore in tre
posti diversi: **una funzione che non funziona affatto, dietro una suite verde.** Il consiglio a metà
turno non arrivava mai, perché due informazioni lette dal messaggio della CLI si fondevano in una — e
tutti i suoi ventuno test passavano perché il messaggio finto che usavano conteneva un campo che
quello vero non manda. Il riassunto di risveglio funzionava una volta sola, perché il suo cronometro
non veniva mai azzerato. La scelta del modello si appoggiava alla firma di un messaggio, che in questo
sistema è solo testo.

**La lezione, che conta più delle tre funzioni.** Cinque revisioni su cinque hanno trovato difetti
reali *dopo* che qualcuno aveva letto il diff e visto la suite verde — e la seconda tornata ha trovato
difetti nelle correzioni della prima, compresa una regressione che spostava un difetto da un confine
raro a uno frequente. Quindi: nessuna modifica che cambia comportamento vivo va integrata senza la sua
revisione avversaria, chi la commissiona non è chi la certifica, e **una correzione va revisionata come
una modifica** — è codice nuovo, quindi superficie nuova. E un test si giudica da una domanda sola:
passerebbe anche se la funzione fosse spenta? Se sì, non fissa nulla.

**Una lezione di processo, che vale più di una funzione.** Il 2026-09-09 due filoni di lavoro sono
andati avanti in parallelo sugli stessi file, e **la stessa funzione è stata costruita due volte** —
il turno di chiusura, da due parti che non sapevano l'una dell'altra. Ha vinto la versione migliore
e l'altra è stata tenuta solo come traccia, ma sono ore buttate. Da lì la regola: chi sta per
mettere mano a un pezzo lo dichiara prima, in un file che gli altri leggono, con l'elenco dei file
che intende toccare. Il costo del coordinamento è di gran lunga inferiore al costo di scoprirlo
dopo.
