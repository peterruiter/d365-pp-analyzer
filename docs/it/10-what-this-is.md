# Che cos'è

Una valutazione in sola lettura di un patrimonio Power Platform. Lo puntate su un ambiente o
gli consegnate un file di soluzione esportato, legge quello che c'è, e produce quattro cose:

1. **Un inventario.** Ogni componente, per tipo, per soluzione, per dominio.
2. **Un rapporto low code.** Un numero, con la sua definizione accanto, perché il numero viene
   citato senza di essa.
3. **Rilievi.** Ciò che è deprecato, costruito male, non governato, lento o esposto, ciascuno
   con la prova che lo ha fatto scattare.
4. **Una stima.** Un intervallo di ore per rilievo, con una motivazione, che si somma in un
   totale la cui aritmetica potete verificare davanti a un cliente.

## Che cosa non farà

**Non scrive mai in un ambiente Power Platform.** In nessuna modalità, e non c'è impostazione
che lo cambi. Potete eseguire una ricognizione nel primo colloquio senza un comitato di
approvazione delle modifiche, ed è proprio questo il punto.

L'unica cosa che scrive da qualche parte sono elementi di lavoro in Azure DevOps, Jira o GitHub, e solo
quelli che qualcuno ha scelto nella schermata del backlog. C'è un'esecuzione a vuoto che
mostra esattamente cosa atterrerebbe e non scrive nulla.

**Non sostituisce il Power Apps checker.** Richiama il checker e integra i risultati sotto gli
identificatori di regola di Microsoft. Le regole che dichiara da sé sono proprio quelle che il
checker non ha: posizione nel ciclo di vita, debito distribuito fra componenti, proliferazione
e igiene delle soluzioni.

**Non è una valutazione delle licenze.** Segnala dove viene usato un connettore premium e si
ferma lì. Non può vedere cosa possiede il tenant, e tirare a indovinare sarebbe peggio del
silenzio.

**Non è un penetration test.** Le regole di sicurezza riguardano i privilegi, i segreti nelle
definizioni e la scrittura a livello di organizzazione. Non valutano se sia possibile
introdursi nel patrimonio.

## Cosa significa "non valutato"

È l'idea più importante del prodotto, quindi ha una sezione tutta sua.

Ogni regola dichiara la prova di cui ha bisogno. Se il modo in cui vi siete collegati non
riesce a raggiungere quella prova, la regola viene riportata come **non valutata**, per nome,
con il motivo. Non viene mai riportata come superata e mai conteggiata come zero rilievi.

Un rapporto che afferma che un cliente non ha debito tecnico quando la verità è che nessuno è
riuscito a leggere il suo ambiente è la cosa più dannosa che questo prodotto possa produrre.
Perciò il rapporto porta sempre l'elenco di ciò che non è stato possibile verificare, e quell'
elenco andrebbe letto ad alta voce nella stanza.

## Il patrimonio dimostrativo

Chiunque sia ammesso al prodotto può aprire un incarico chiamato **Patrimonio dimostrativo**. È
un patrimonio Power Platform sintetico con la forma di una utility di media dimensione, e
nulla al suo interno proviene da un cliente.

I rilievi che contiene non sono inventati. Sono prodotti facendo girare sul patrimonio
sintetico lo stesso motore di regole, lo stesso calcolo del punteggio e lo stesso costruttore
di backlog che girano su uno reale. È in sola lettura per tutti, quindi potete esplorare ogni
schermata senza poterla rompere.

Usatelo per imparare il prodotto, e usatelo per dimostrare il prodotto prima che un cliente vi
abbia dato qualcosa.
