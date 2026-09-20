# Collegare un ambiente

Ci sono tre modi per entrare, e raggiungono quantità diverse. Scegliete quello che questa
settimana riuscite davvero a far approvare, non quello che raggiunge di più.

## Utente applicazione, sola lettura

Una registrazione app Entra aggiunta all'ambiente come utente applicazione con un ruolo di
sicurezza in sola lettura. È la modalità per tutto ciò che deve essere ripetibile.

**Raggiunge:** l'API web di Dataverse per intero, una soluzione esportata per intero, il Power
Apps checker per intero, e le prove di esecuzione parzialmente.

**Richiede:** un id tenant, un id client, un URL di ambiente e un segreto client. Il segreto
va in Key Vault e viene richiamato per nome. Nessuna credenziale viene mai conservata nel
database del prodotto.

La definizione del ruolo è fornita con il prodotto, così il team di sicurezza di un cliente
esamina un file anziché una descrizione.

La cronologia di esecuzione dei flussi e i log di traccia dei plug-in richiedono più di un
semplice lettore. Dove quel privilegio manca, il prodotto nomina le regole rimaste non
valutate anziché riportarle come pulite.

## Utente delegato

Voi, autenticati, che leggete ciò che già potete leggere.

**Raggiunge:** tutto, comprese le prove di esecuzione, nella misura in cui ci arriva il vostro
account.

**Richiede:** nulla da creare. È il modo più rapido per vedere qualcosa di reale.

L'inconveniente è che non è ripetibile: un'esecuzione pianificata non può prendere in prestito
la vostra sessione, e i risultati dipendono dai vostri privilegi anziché da un ruolo
dichiarato.

## File di soluzione

Una soluzione unmanaged esportata, decompressa e letta offline.

**Raggiunge:** il file di soluzione per intero e il checker per intero. I metadati
parzialmente. L'esecuzione per nulla.

**Richiede:** uno `.zip` e nient'altro. Nessuna connessione, nessuna credenziale, nessuna
verifica di sicurezza.

Non è un ripiego degradato. È la modalità che supera una verifica di sicurezza nella prima
settimana mentre la richiesta di un service principal è in coda, e per una valutazione di
qualità e debito raggiunge la maggior parte di ciò che conta. Quello che non può vedere è
l'utilizzo: quali flussi girano davvero, quali workflow dormono, quanto spesso qualcosa
fallisce.

## Ruolo dell'ambiente

Qualunque modalità usiate, dichiarate **a cosa serve** l'ambiente: sviluppo, test, collaudo o
produzione.

Diverse regole scattano solo contro la produzione. Sbagliare rende un rapporto allarmistico
oppure inutile, perciò questo si dichiara anziché dedurlo dal nome dell'ambiente. Se lo
lasciate su sconosciuto, le regole legate alla produzione si riportano da sé come non
valutate: non danno nulla per scontato in silenzio.

## Provare una connessione

Provatela prima di eseguire qualsiasi cosa. La prova riporta con quale identità si è
autenticata e cosa è riuscita a raggiungere, per fonte di evidenza.

Una connessione che riesce con troppi pochi privilegi fallisce più tardi in un modo che
sembra esattamente un patrimonio vuoto. Il pannello della portata è lì perché lo scopriate
adesso.
