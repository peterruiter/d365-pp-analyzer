# De bevindingen lezen

## Categorieën

Elke regel valt in een van elf categorieën. Het bevindingenscherm groepeert erop.

| Categorie | Gaat over |
|---|---|
| **Levenscyclus en veroudering** | Componenten die Microsoft heeft verwijderd, verouderd verklaard of waarin niet meer wordt geïnvesteerd. |
| **Modernisering** | Iets dat werkt en waarvoor nu een beter antwoord bestaat. |
| **Bouwkwaliteit** | Hoe goed gebouwd is wat er staat: foutafhandeling, naamgeving, structuur. |
| **Architectuur** | Of logica zit waar logica hoort. De low-codeverhouding hoort hier. |
| **ALM en solutionhygiëne** | Of dit tussen omgevingen kan verhuizen zonder dat iemand ergens aan moet denken. |
| **Governance** | Eigenaarschap, wildgroei, verweesde componenten, licentieblootstelling. |
| **Prestaties** | Dingen die nu traag zijn, of dat bij volume worden. |
| **Beveiliging** | Rechten, geheimen en blootstelling. |
| **Beheersbaarheid** | Of iemand erachter zou komen wanneer het stukgaat. |
| **AI-componenten** | Agents, prompts en modellen: of wat er op AI is gebouwd gefundeerd, actueel en van iemand is. |
| **Dynamics 365 Contact Center** | Werkstromen, wachtrijen en capaciteit: of een gesprek dat binnenkomt iemand bereikt die het kan aannemen. Alleen gelezen waar Contact Center is geïnstalleerd, dus een omgeving zonder heeft hier niets in plaats van een lijst controles die niet konden draaien. |

Modernisering is de categorie die een klant het liefst wil en die het vaakst wordt overdreven,
dus elke regel erin draagt ook een reden om het ding met rust te laten.

## Ernst

Kritiek, hoog, middel, laag, informatief. De ernst komt van de regel, niet van het component,
en een handler mag hem verlagen om een reden die hij in het bewijs vastlegt.

Ernst is geen prioriteit. Een kritieke bevinding op een component dat niemand gebruikt is
minder urgent dan een middelmatige midden in het dagelijkse proces, en het product doet niet
alsof het weet welke welke is. Dat oordeel is aan u, en de backlog is waar u het vastlegt.

## Bewijs

Elke bevinding draagt bij zich wat hem heeft aangetoond. Dit is het deel waar een klant voor
betaalt: een bewering die u niet voor hun neus kunt controleren is een bewering die de zaal
verliest.

Open een bevinding en u krijgt de concrete waarden — het aantal acties, de isolatiemodus, de
gevonden URL, het aantal bibliotheken op het formulier. Geen herhaling van de regel.

## Waar een bevinding vandaan komt

Bevindingen zijn gemarkeerd als **catalogus**, **checker** of **model**.

Een checkerbevinding komt van de Power Apps checker van Microsoft en draagt Microsofts eigen
regelidentificatie, zodat u hem in hun documentatie kunt opzoeken. Die regels blijven actueel
omdat Microsoft ze onderhoudt, niet omdat dit product dat doet.

Precies één regel wordt door een taalmodel beoordeeld: of een beschrijving iets zegt. Het
leest één beschrijving tegelijk, vermeldt bij elke bevinding dat een model het heeft
beoordeeld, en draagt de eigen zin van het model zodat u er hardop op tegen kunt zijn. Elke
andere regel is een meting. Is er geen model ingesteld voor de run, dan wordt die regel
gerapporteerd als niet beoordeeld en niet als geslaagd.

## Managed componenten

Een bevinding tegen een component dat in een managed solution is binnengekomen wordt
gerapporteerd en nooit geraamd. Het is aan iemand anders om op te lossen, en werk ramen aan
een solution die u niet levert is een getal verzinnen. Kaart het aan bij wie hem levert.

## Een bevinding overschrijven

U kunt de raming van een bevinding op een opdracht overschrijven. De overschrijving hangt aan
de stabiele sleutel van de bevinding — regel plus component — dus hij overleeft een nieuwe
run. Iemand die een flow hernoemt, laat de raming waar een workshop een uur over heeft gedaan
niet verweesd achter.

## De roadmap

De roadmap plaatst elke bevinding op een raster: een rij voor het soort werk en een kolom voor
de vraag of het om mensen, proces of techniek gaat, in banden vanaf "opruimen" naar buiten.
Het is een manier om een klant de vorm van het werk te laten zien in plaats van een lijst van
300 punten.

Regels met bevindingen en zonder plek op de roadmap worden onderaan de scorefase gemeld, zodat
het raster niet stilzwijgend een categorie kan laten vallen.
