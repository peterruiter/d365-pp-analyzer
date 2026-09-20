# Rapporti ed esportazioni

Da un'esecuzione escono due documenti. Entrambi sono prodotti dai risultati salvati di quella
esecuzione, quindi un rapporto scaricato un mese dopo dice quello che ha detto l'esecuzione,
non quello che dicono le regole oggi.

## La cartella dei rilievi

Un `.xlsx` con un foglio per ogni sguardo sulla stessa esecuzione:

- **Sintesi** — i conteggi, il rapporto, i totali.
- **Rilievi** — ogni rilievo, con la sua regola, gravità, categoria, componente, prova e stima.
- **Inventario** — ogni componente, con tipo, soluzione, fattura, ciclo di vita e dominio.
- **Non valutato** — ogni regola che non è potuta essere eseguita, e perché.
- **Backlog** — gli elementi di lavoro, con i loro criteri di accettazione.

È quella in cui l'architetto di un cliente lavorerà davvero. È deliberatamente essenziale:
niente celle unite, niente immagini, filtri su ogni riga di intestazione, così da poter essere
ordinata e incrociata anziché ammirata.

## Il rapporto di valutazione

Un `.pdf` scritto per essere letto da chi non aprirà la cartella. Porta il racconto: cosa è
stato letto, cosa non è stato letto, cosa è stato trovato, quanto costerebbe, e la roadmap.

La sezione "non valutato" non è un'appendice. Sta vicino all'inizio, perché un lettore che
arriva ai numeri senza di essa è stato fuorviato.

## La lingua in cui esce un rapporto

I rapporti sono prodotti nella **lingua del rapporto** dell'incarico, che si imposta
sull'incarico ed è distinta dalla lingua in cui leggete il prodotto.

Una consulente olandese può leggere un'interfaccia in olandese e produrre un rapporto in
inglese per un team offshore. È il caso normale, non un caso limite, ed è per questo che sono
due impostazioni.

Il backlog ha poi una lingua tutta sua, perché chi cura un backlog spesso non è chi legge il
rapporto.

## Scaricare

I rapporti sono elencati nella schermata Rapporti per ogni esecuzione. Vengono generati quando
li chiedete anziché conservati, così un rapporto corrisponde sempre all'esecuzione che nomina.

Il nome del file porta il nome del cliente e la data dell'esecuzione, perché una cartella di
file tutti chiamati `report.pdf` è una cartella che nessuno può usare.
