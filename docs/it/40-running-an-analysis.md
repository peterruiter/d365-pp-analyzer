# Eseguire un'analisi

## Le quattro modalità

| Modalità | Fa | Dura |
|---|---|---|
| **Scansione rapida** | Legge il patrimonio e applica ogni regola, usando fasce di stima anziché stime individuali. | Minuti. |
| **Valutazione** | Una scansione rapida, più una stima per rilievo e un backlog curato. | Di più, e richiama un modello linguistico. |
| **Confronta** | Una valutazione misurata rispetto a un'esecuzione precedente, per mostrare che cosa è cambiato. | Come una valutazione. |
| **Pubblica** | Scrive gli elementi di backlog scelti in Azure DevOps. Non legge nulla e non rianalizza nulla. | Minuti. |

Iniziate con una scansione rapida. È la modalità che si può eseguire senza rischi in un
colloquio commerciale, e risponde alla maggior parte delle domande di un primo incontro.

## Scegliere cosa leggere

Un'esecuzione non legge tutto quello che riesce a trovare, e non decide al posto vostro.

Pochi secondi dopo l'avvio si collega, elenca ogni soluzione dell'ambiente e poi **si ferma e
ve lo chiede**. Ottenete l'elenco, con per ogni soluzione l'editore, la versione e il numero
di componenti, e spuntate quelle che il rapporto deve coprire.

Le soluzioni di Microsoft partono deselezionate. La maggior parte di ciò che contiene un
ambiente Dataverse ce l'ha messo Microsoft, leggerle è di gran lunga la parte più lunga di
un'esecuzione, e il rapporto che ne esce parla di Dynamics anziché del lavoro per cui il
vostro cliente ha pagato qualcuno. Nulla viene nascosto: ogni soluzione trovata è nell'elenco
e viene registrata, che la spuntiate o no, perché un rapporto che copre quattro soluzioni su
diciannove e uno che le copre tutte e diciannove sono identici in copertina.

Nella stessa schermata si possono disattivare quattro controlli:

| Controllo | Costa | Se lo disattivate |
|---|---|---|
| **Esportare le soluzioni scelte** | Circa un minuto per soluzione, quindi una dozzina è un quarto d'ora. Non viene scritto nulla: un'esportazione è una lettura. | Le quattordici regole che leggono un file di soluzione, e le tre che hanno bisogno del checker, sono riportate come non valutate. Una connessione dal vivo non è allora più ricca di una lettura di metadati. |
| **Solution checker** | Di gran lunga la parte più lenta di un'esecuzione. | Ogni regola la cui prova è un risultato del checker viene riportata come non valutata, mai come superata. |
| **Stime da modello** | Minuti, e un endpoint di modello. | Le stime tornano ai valori predefiniti di fascia, come fa una scansione rapida. Il rapporto dice quali ha usato. |
| **Salute dell'ambiente** | Secondi. | L'esecuzione procede con ciò che la credenziale raggiunge per caso. |

Non spuntare nulla è permesso e produce un rapporto che dice che questo è un patrimonio non
letto anziché pulito. La schermata vi avverte prima che proseguiate.

## Le fasi

Un'esecuzione attraversa dieci fasi, e la schermata dell'esecuzione mostra dov'è, quanto è
durata ciascuna e su quale si trova ora:

1. **Verifica connessioni** — si autentica e riporta con quale identità.
2. **Scelta soluzioni** — elenca cosa contiene l'ambiente, poi vi aspetta.
3. **Estrazione** — legge le soluzioni che avete scelto.
4. **Checker** — invia la soluzione al Power Apps checker e attende.
5. **Risoluzione** — collega i componenti fra loro, così le regole possono chiedere cosa punta a cosa.
6. **Analisi** — applica ogni regola del catalogo.
7. **Stima** — mette un intervallo di ore su ogni rilievo. Saltata da una scansione rapida.
8. **Punteggio** — calcola il rapporto, il grafico delle personalizzazioni e la roadmap.
9. **Backlog** — trasforma i rilievi in elementi di lavoro che qualcuno curerebbe davvero.
10. **Pubblicazione** — scrive in Azure DevOps o Jira. Solo in modalità pubblicazione, e solo gli elementi che qualcuno ha scelto.

La fase in corso dice cosa sta facendo mentre lo fa — quale soluzione sta esportando, quale ha
il checker, a che punto è nell'ambiente — e la schermata si aggiorna da sola. Una fase che
legge il patrimonio di un cliente richiede minuti, e senza questo una lenta e una ferma
sembrano esattamente uguali.

Una fase può essere rieseguita da sola dalla schermata dell'esecuzione. Facendolo si scarta
anche ogni fase successiva, cosa che il pulsante vi dice prima di farlo: un'esecuzione i cui
rilievi vengono da un'estrazione e il cui punteggio viene da un'altra sembrerebbe
perfettamente sana e sarebbe sbagliata.

Una fase può anche concludersi **parziale**, il che significa che ha fatto il suo lavoro e
qualcosa al suo interno non è stato possibile. Il caso più comune è l'analisi: alcune regole
non hanno potuto essere eseguite. Non è un fallimento e l'esecuzione prosegue.

## Mentre gira

Potete andarvene. L'esecuzione è svolta da un worker in background, non dal vostro browser, e
prosegue se chiudete la scheda. Tornate alla schermata Esecuzioni e aprite l'esecuzione per
riprendere la cronologia da dove si trova.

## Rimuovere un'esecuzione

Un incarico accumula un'esecuzione per ogni tentativo, e l'elenco è ciò che scorrete quando
cercate quella che avete in mente. Un Amministratore può rimuovere un'esecuzione e tutto ciò
che ha prodotto.

Due cose da sapere. Un'esecuzione su cui ne è stata costruita una successiva non può essere
rimossa, perché quella successiva resterebbe a descrivere una valutazione che non esiste più.
E gli elementi di lavoro già pubblicati in Azure DevOps o Jira restano esattamente dove sono:
rimuovere l'esecuzione rimuove la traccia che questo prodotto ha di averli scritti, e nulla
nella board del cliente.

Le schermate dei rilievi leggono sempre una sola esecuzione, mai una pila, quindi rimuovere le
vecchie è riordinare e non correggere.

## Quando finisce

Iniziate dalla **Panoramica**. Vi dà i conteggi, il rapporto, la distribuzione per gravità e
la stima complessiva.

Poi leggete l'elenco **non valutato** prima di qualsiasi altra cosa, così sapete cosa coprono
e cosa non coprono i numeri sottostanti.
