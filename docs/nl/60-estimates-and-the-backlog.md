# Ramingen en de backlog

## Waar een getal vandaan komt

Elke raming is een bandbreedte in uren, en elke raming noemt welke van drie lagen hem heeft
voortgebracht:

- **Bandstandaard** — de ramingsband die de regel in de catalogus declareert: triviaal, klein,
  middel, groot. Een snelle scan gebruikt deze overal, en dat is wat hem snel maakt.
- **Model** — een raming per bevinding, gemaakt door een taalmodel op basis van het bewijs van
  de bevinding en de complexiteit van het component. Dit is wat een beoordelingsrun toevoegt.
- **Overschrijving op de opdracht** — een getal dat een mens heeft gezet, dat het van beide
  wint.

De laag staat naast elke raming. Een klant die vraagt "waar komen die 40 uur vandaan" stelt
een redelijke vraag en hoort een specifiek antwoord te krijgen.

## Vaste kosten

Sommige kosten gelden per opdracht in plaats van per bevinding: inrichten van een omgeving,
een regressieronde, een overdracht. Die komen uit het contract en worden apart getoond van de
som van de bevindingen, want ze in een totaal per bevinding optellen maakt de getallen per
bevinding onjuist.

## De complexiteitsbeoordeling

Componenten worden beoordeeld als eenvoudig, middel of complex op basis van maatstaven die in
het contract zijn gedeclareerd — het aantal acties van een flow, het aantal besturingselementen
van een app, de omvang van een assembly. De beoordeling voedt de modelraming en de
maatwerkgrafiek.

Een component waarvan de maatstaf niet bereikbaar was, krijgt **niet beoordeeld** in plaats van
eenvoudig.

## De backlog

Eén werkitem per bevinding zou vierhonderd taken opleveren die niemand groomt. Eén per regel
zou het bewijs verliezen, en dat is nu juist het deel waar een klant voor betaalt.

Dus de backlog is gegroepeerd zoals een consultant hem met de hand zou hebben gegroepeerd:

- een **epic** per categorie,
- een **feature** per regel met genoeg bevindingen om er een te rechtvaardigen,
- een **story** per bevinding die het verdient bij naam genoemd te worden,
- en triviale bevindingen **gebundeld** tot één taak per regel.

Elk item draagt acceptatiecriteria in given/when/then-vorm en een testvereiste, beide uit het
contract in plaats van per item geschreven. Ze worden gemaakt in de backlogtaal van de
opdracht, die per opdracht wordt ingesteld en losstaat van de taal waarin u het product leest.

## Publiceren

Een publicatie schrijft werkitems in het Azure DevOps-project dat op de opdracht is
geconfigureerd. Er wordt niets opnieuw geanalyseerd: de gekozen items worden
gepubliceerd en verder niets.

Elk werkitem draagt een deterministische tag die is afgeleid van de opdracht en de sleutel van
het item, zodat twee keer publiceren de bestaande items bijwerkt in plaats van een tweede kopie
van alles te maken.

U kunt een publicatie eerst als proefronde uitvoeren, die meldt wat er aangemaakt en bijgewerkt
zou worden zonder iets te schrijven.
