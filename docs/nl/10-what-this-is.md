# Wat dit is

Een alleen-lezen beoordeling van een Power Platform-landschap. U richt het op een omgeving of
geeft het een geëxporteerd solutionbestand, het leest wat er is, en het levert vier dingen op:

1. **Een inventarisatie.** Elk component, per type, per solution, per domein.
2. **Een low-codeverhouding.** Eén getal, met de definitie eraan vast, want het getal wordt
   zonder die definitie geciteerd.
3. **Bevindingen.** Wat verouderd, slecht gebouwd, ongereguleerd, traag of blootgesteld is,
   elk met het bewijs dat het heeft aangetoond.
4. **Een raming.** Een bandbreedte in uren per bevinding, met onderbouwing, opgeteld tot een
   totaal waarvan u het rekenwerk voor de neus van een klant kunt controleren.

## Wat het niet doet

**Het schrijft nooit naar een Power Platform-omgeving.** In geen enkele modus, en er is geen
instelling die dat verandert. U kunt een verkenning uitvoeren in een eerste gesprek zonder
change advisory board, en dat is precies het punt.

Het enige dat het ergens wegschrijft zijn werkitems in Azure DevOps, en alleen de items die
iemand op het backlogscherm heeft uitgekozen. Er is een proefrun die precies laat zien wat
er zou landen en niets wegschrijft.

**Het is geen vervanging van de Power Apps checker.** Het roept de checker aan en voegt de
resultaten in onder Microsofts eigen regelidentificaties. De regels die het zelf declareert
zijn juist die de checker niet heeft: positie in de levenscyclus, schuld verspreid over
componenten, wildgroei en solutionhygiëne.

**Het is geen licentiebeoordeling.** Het meldt waar een premium connector wordt gebruikt en
houdt daar op. Het kan niet zien wat de tenant heeft, en gissen zou erger zijn dan zwijgen.

**Het is geen penetratietest.** De beveiligingsregels gaan over rechten, geheimen in
definities en schrijfrechten op organisatieniveau. Ze zijn geen beoordeling van de vraag of
er ingebroken kan worden.

## Wat "niet beoordeeld" betekent

Dit is het belangrijkste idee in het product, dus het krijgt een eigen paragraaf.

Elke regel declareert welk bewijs hij nodig heeft. Als de manier waarop u verbinding hebt
gemaakt dat bewijs niet kan bereiken, wordt de regel gerapporteerd als **niet beoordeeld**,
bij naam, met de reden. Hij wordt nooit als geslaagd gerapporteerd en nooit meegeteld als nul
bevindingen.

Een rapport dat zegt dat een klant geen technische schuld heeft terwijl de waarheid is dat
niemand hun omgeving kon lezen, is het schadelijkste dat dit product kan opleveren. Daarom
draagt het rapport altijd een lijst van wat niet gecontroleerd kon worden, en die lijst moet u
hardop voorlezen in de zaal.

## Het demonstratielandschap

Iedereen die tot het product is toegelaten kan een opdracht openen die **Demonstratielandschap**
heet. Het is een synthetisch Power Platform-landschap in de vorm van een middelgroot
nutsbedrijf, en niets erin komt van een klant.

De bevindingen erin zijn niet verzonnen. Ze worden geproduceerd door dezelfde regelmachine,
scorer en backlogbouwer over het synthetische landschap te laten lopen die ook over een echt
landschap lopen. Het is voor iedereen alleen-lezen, dus u kunt elk scherm verkennen zonder het
te kunnen breken.

Gebruik het om het product te leren kennen, en gebruik het om het product te demonstreren
voordat een klant u iets heeft gegeven.
