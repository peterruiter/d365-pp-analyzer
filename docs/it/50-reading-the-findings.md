# Leggere i rilievi

## Categorie

Ogni regola sta in una di undici categorie. La schermata dei rilievi raggruppa per categoria.

| Categoria | Riguarda |
|---|---|
| **Ciclo di vita e deprecazione** | Componenti che Microsoft ha rimosso, dichiarato obsoleti o su cui non investe più. |
| **Modernizzazione** | Qualcosa che funziona e per cui oggi esiste una risposta migliore. |
| **Qualità di costruzione** | Con quanta cura è stato costruito ciò che esiste: gestione degli errori, denominazione, struttura. |
| **Architettura** | Se la logica sta dove la logica dovrebbe stare. Il rapporto low code sta qui. |
| **ALM e igiene delle soluzioni** | Se questo può spostarsi fra ambienti senza che qualcuno debba ricordarsi qualcosa. |
| **Governance** | Proprietà, proliferazione, componenti orfani, esposizione alle licenze. |
| **Prestazioni** | Cose lente adesso, o che lo diventeranno con il volume. |
| **Sicurezza** | Privilegi, segreti ed esposizione. |
| **Operabilità** | Se qualcuno si accorgerebbe che si è rotto. |
| **Componenti IA** | Agenti, prompt e modelli: se ciò che è costruito sull'IA è fondato, aggiornato e di qualcuno. |
| **Dynamics 365 Contact Center** | Flussi di lavoro, code e capacità: se una conversazione in arrivo raggiunge qualcuno in grado di prenderla. Letto solo dove Contact Center è installato, quindi un ambiente senza non ha nulla qui anziché un elenco di controlli che non sono potuti partire. |

Modernizzazione è la categoria che un cliente desidera di più e quella che più facilmente
viene sopravvenduta, perciò ogni regola al suo interno porta anche una ragione per lasciare
stare la cosa.

## Gravità

Critica, alta, media, bassa, informativa. La gravità viene dalla regola, non dal componente, e
un handler può abbassarla per un motivo che registra nella prova.

Gravità non è priorità. Un rilievo critico su un componente che nessuno usa è meno urgente di
uno medio nel mezzo del processo quotidiano, e il prodotto non finge di sapere quale sia
quale. Quel giudizio è vostro, e il backlog è dove lo registrate.

## Prove

Ogni rilievo porta con sé ciò che lo ha fatto scattare. È la parte che un cliente compra:
un'affermazione che non potete verificare davanti a lui è un'affermazione che perde la stanza.

Aprite un rilievo e ottenete i valori concreti: il numero di azioni, la modalità di
isolamento, l'URL trovato, il numero di librerie sul modulo. Non una riformulazione della
regola.

## Da dove viene un rilievo

I rilievi sono contrassegnati come **catalogo**, **checker** o **modello**.

Un rilievo del checker viene dal Power Apps checker di Microsoft e porta l'identificatore di
regola di Microsoft, così potete cercarlo nella loro documentazione. Quelle regole restano
aggiornate perché le mantiene Microsoft, non perché lo faccia questo prodotto.

Esattamente una regola è decisa da un modello linguistico: se una descrizione dice qualcosa.
Legge una descrizione alla volta, indica su ogni rilievo che a giudicare è stato un modello,
e porta la frase del modello stesso perché possiate contestarla ad alta voce. Ogni altra
regola è una misura. Senza un modello configurato per l'esecuzione, quella regola è
riportata come non valutata, mai come superata.

## Componenti managed

Un rilievo contro un componente arrivato in una soluzione managed viene riportato e mai
stimato. Correggerlo spetta a qualcun altro, e stimare lavoro su una soluzione che non
rilasciate voi significa inventare un numero. Sollevatelo con chi la rilascia.

## Sovrascrivere un rilievo

Potete sovrascrivere la stima di un rilievo su un incarico. La sovrascrittura si lega alla
chiave stabile del rilievo — regola più componente — e sopravvive quindi a una nuova
esecuzione. Se qualcuno rinomina un flusso, non resta orfana la stima su cui un workshop ha
speso un'ora.

## La roadmap

La roadmap colloca ogni rilievo su una griglia: una riga per il tipo di lavoro e una colonna a
seconda che riguardi persone, processo o tecnologia, in fasce che partono da "sgomberare"
verso l'esterno. È un modo di mostrare a un cliente la forma del lavoro anziché un elenco di
300 voci.

Le regole con rilievi e senza posizione sulla roadmap vengono segnalate in fondo alla fase di
punteggio, così la griglia non può lasciar cadere una categoria in silenzio.
