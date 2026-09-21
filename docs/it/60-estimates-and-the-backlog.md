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

## Pubblicare

Una pubblicazione scrive gli elementi scelti in un progetto Azure DevOps, un progetto Jira o
un repository GitHub, scelto al momento della pubblicazione e non memorizzato sulla
connessione. Non rianalizza nulla: pubblica gli elementi scelti e nient'altro.

Ogni elemento porta una chiave deterministica derivata dall'incarico e dalla chiave
dell'elemento, così pubblicare due volte aggiorna ciò che c'è anziché creare una seconda copia
di tutto. È un tag in Azure DevOps e un'etichetta in Jira e GitHub: l'unico campo che tutti e
tre hanno senza che nessuno debba configurarlo prima.

Le tre destinazioni non offrono le stesse forme e il prodotto non finge di sì. Ad Azure DevOps
si chiede quali tipi di elemento di lavoro ha il progetto; a Jira si chiedono i suoi tipi di
ticket, e un livello che non ha atterra piatto anziché fallire; GitHub non ha tipi affatto,
quindi i nostri diventano etichette e il collegamento al padre è una sub-issue dove il
repository la supporta. Su nessuna delle tre viene mai chiuso o riaperto alcunché.

Potete eseguire prima una pubblicazione di prova, che riporta cosa creerebbe e aggiornerebbe
senza scrivere nulla.
