# Stime e backlog

## Da dove viene un numero

Ogni stima è un intervallo in ore, e ogni stima dice quale dei tre livelli l'ha prodotta:

- **Valore predefinito di fascia** — la fascia di stima che la regola dichiara nel catalogo:
  banale, piccola, media, grande. Una scansione rapida usa queste dappertutto, ed è ciò che la
  rende rapida.
- **Modello** — una stima per rilievo prodotta da un modello linguistico, a partire dalla
  prova del rilievo e dalla complessità del componente. È ciò che aggiunge un'esecuzione di
  valutazione.
- **Sovrascrittura dell'incarico** — un numero messo da una persona, che batte entrambi.

Il livello è mostrato accanto a ogni stima. Un cliente che chiede "da dove vengono quelle 40
ore" pone una domanda ragionevole e deve ricevere una risposta precisa.

## Costi fissi

Alcuni costi sono per incarico anziché per rilievo: predisporre un ambiente, un giro di
regressione, un passaggio di consegne. Vengono dal contratto e sono mostrati separatamente
dalla somma dei rilievi, perché sommarli in un totale per rilievo rende sbagliati i numeri per
rilievo.

## La valutazione di complessità

I componenti sono valutati semplici, medi o complessi a partire da misure dichiarate nel
contratto: il numero di azioni di un flusso, il numero di controlli di un'app, la dimensione
di un assembly. La valutazione alimenta la stima da modello e il grafico delle
personalizzazioni.

Un componente la cui misura non era raggiungibile viene valutato **non valutato** anziché
semplice.

## Il backlog

Un elemento di lavoro per rilievo produrrebbe quattrocento attività che nessuno cura. Uno per
regola perderebbe la prova, che è proprio la parte che il cliente compra.

Quindi il backlog è raggruppato come lo avrebbe raggruppato a mano un consulente:

- un **epic** per categoria,
- una **feature** per regola che abbia abbastanza rilievi da giustificarne una,
- una **story** per ogni rilievo che meriti di essere nominato,
- e i rilievi banali **accorpati** in un'unica attività per regola.

Ogni elemento porta criteri di accettazione in forma given/when/then e un requisito di test,
entrambi dal contratto anziché scritti elemento per elemento. Vengono prodotti nella lingua di
backlog dell'incarico, che si imposta per incarico ed è distinta dalla lingua in cui leggete
il prodotto.

## Approvare

La pubblicazione su Azure DevOps richiede un'approvazione, e solo un Amministratore
dell'incarico può darla.

L'approvazione registra esattamente il backlog che è stato approvato, come un hash. Se il
backlog cambia dopo — perché qualcuno ha rieseguito l'analisi o corretto una stima — l'hash
non corrisponde più e la pubblicazione rifiuta. Approvare una cosa e pubblicarne un'altra è il
fallimento per cui quella porta esiste.

## Pubblicare

Una pubblicazione scrive elementi di lavoro nel progetto Azure DevOps configurato
sull'incarico. Non rianalizza nulla: pubblica il backlog approvato e nient'altro.

Ogni elemento di lavoro porta un tag deterministico derivato dall'incarico e dalla chiave
dell'elemento, così pubblicare due volte aggiorna gli elementi esistenti anziché creare una
seconda copia di tutto.

Potete eseguire prima una pubblicazione di prova, che riporta cosa creerebbe e aggiornerebbe
senza scrivere nulla.
