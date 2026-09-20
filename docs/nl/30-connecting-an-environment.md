# Verbinding maken met een omgeving

Er zijn drie manieren naar binnen, en ze bereiken verschillend veel. Kies degene die u deze
week ook echt goedgekeurd krijgt, niet degene die het meest bereikt.

## Toepassingsgebruiker, alleen-lezen

Een Entra-app-registratie die als toepassingsgebruiker met een alleen-lezen beveiligingsrol
aan de omgeving is toegevoegd. Dit is de modus voor alles wat herhaalbaar moet zijn.

**Bereikt:** de Dataverse Web API volledig, een geëxporteerde solution volledig, de Power Apps
checker volledig, en runtimebewijs gedeeltelijk.

**Nodig:** een tenant-id, een client-id, een omgevings-URL en een clientgeheim. Het geheim gaat
in Key Vault en wordt op naam aangehaald. Er wordt nooit een credential in de productdatabase
opgeslagen.

De roldefinitie wordt met het product meegeleverd, zodat het beveiligingsteam van een klant een
bestand beoordeelt in plaats van een beschrijving.

Uitvoeringsgeschiedenis van flows en traceerlogboeken van plug-ins vragen meer dan een gewone
lezer. Waar dat recht ontbreekt, noemt het product de regels die onbeoordeeld bleven in plaats
van ze schoon te rapporteren.

## Gedelegeerde gebruiker

U, aangemeld, die leest wat u toch al mag lezen.

**Bereikt:** alles, inclusief runtimebewijs, voor zover uw eigen account erbij kan.

**Nodig:** niets dat aangemaakt moet worden. Dit is de snelste manier om iets echts te zien.

Het addertje is dat het niet herhaalbaar is: een geplande run kan uw sessie niet lenen, en de
uitkomst hangt af van uw rechten in plaats van van een gedeclareerde rol.

## Solutionbestand

Een geëxporteerde unmanaged solution, uitgepakt en offline gelezen.

**Bereikt:** het solutionbestand volledig en de checker volledig. Metadata gedeeltelijk.
Runtime helemaal niet.

**Nodig:** een `.zip` en verder niets. Geen verbinding, geen credential, geen
beveiligingsbeoordeling.

Dit is geen afgezwakte terugvaloptie. Het is de modus die in week één langs een
beveiligingsbeoordeling komt terwijl de aanvraag voor een service principal in een wachtrij
staat, en voor een kwaliteits- en schuldbeoordeling bereikt hij het meeste van wat ertoe doet.
Wat hij niet kan zien is gebruik: welke flows daadwerkelijk lopen, welke workflows sluimeren,
hoe vaak er iets misgaat.

## Rol van de omgeving

Welke modus u ook gebruikt, u verklaart waar de omgeving **voor** is: ontwikkeling, test,
acceptatie of productie.

Verschillende regels vuren alleen tegen productie. Verkeerd gokken maakt een rapport
alarmerend of nutteloos, dus dit wordt verklaard in plaats van afgeleid uit de naam van de
omgeving. Laat u het op onbekend staan, dan rapporteren de productiegebonden regels zichzelf
als niet beoordeeld — ze nemen stilzwijgend niets aan.

## Een verbinding testen

Test hem voordat u iets uitvoert. De test meldt als welke identiteit hij zich heeft
geauthenticeerd en wat hij kon bereiken, per bewijsbron.

Een verbinding die slaagt met te weinig rechten faalt later op een manier die er precies
uitziet als een landschap met niets erin. Het bereikpaneel is er zodat u het nu ontdekt.
