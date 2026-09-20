# Een analyse uitvoeren

## De vier modi

| Modus | Doet | Duurt |
|---|---|---|
| **Snelle scan** | Leest het landschap en past elke regel toe, met ramingsbandbreedtes in plaats van afzonderlijke ramingen. | Minuten. |
| **Beoordeling** | Een snelle scan, plus een raming per bevinding en een gegroomde backlog. | Langer, en er wordt een taalmodel aangeroepen. |
| **Vergelijken** | Een beoordeling afgezet tegen een eerdere run, zodat u kunt laten zien wat er is veranderd. | Als een beoordeling. |
| **Publiceren** | Schrijft de goedgekeurde backlog naar Azure DevOps. Leest niets en analyseert niets opnieuw. | Minuten. |

Begin met een snelle scan. Dat is de modus die veilig is om in een verkoopgesprek uit te
voeren, en hij beantwoordt de meeste vragen van een eerste afspraak.

## Kiezen wat er gelezen wordt

Een run leest niet alles wat hij kan vinden, en beslist dat niet voor u.

Binnen enkele seconden na de start maakt hij verbinding, somt hij elke solution in de omgeving
op, en dan **stopt hij en vraagt het u**. U krijgt de lijst, met per solution de uitgever, de
versie en het aantal componenten, en u vinkt aan wat het rapport moet beslaan.

Microsofts eigen solutions staan standaard uit. Het meeste van wat een Dataverse-omgeving
bevat is er door Microsoft neergezet, ze lezen is verreweg het langste deel van een run, en
het rapport dat eruit komt gaat over Dynamics in plaats van over het werk waar uw klant
iemand voor heeft betaald. Niets wordt verborgen: elke gevonden solution staat op de lijst en
wordt vastgelegd, of u hem nu aanvinkt of niet, want een rapport over vier van de negentien
solutions en een rapport over alle negentien zien er op het voorblad identiek uit.

Op hetzelfde scherm kunnen vier controles worden uitgezet:

| Controle | Kost | Als u hem uitzet |
|---|---|---|
| **Gekozen oplossingen exporteren** | Ongeveer een minuut per oplossing, dus een dozijn is een kwartier. Er wordt niets geschreven: een export is een leesactie. | De veertien regels die een solutionbestand lezen, en de drie die de checker nodig hebben, worden gerapporteerd als niet beoordeeld. Een live verbinding is dan niet rijker dan een metadatalezing. |
| **Solution checker** | Verreweg het traagste onderdeel van een run. | Elke regel waarvan het bewijs een checkerresultaat is wordt gerapporteerd als niet beoordeeld, nooit als geslaagd. |
| **Modelramingen** | Minuten, en een modelendpoint. | Ramingen vallen terug op bandstandaarden, wat een snelle scan ook doet. Het rapport vermeldt welke gebruikt zijn. |
| **Gezondheid van de omgeving** | Seconden. | De run gaat verder op wat de credential toevallig bereikt. |

Niets aanvinken mag, en levert een rapport op dat zegt dat dit een ongelezen landschap is in
plaats van een schoon landschap. Het scherm waarschuwt u voordat u doorgaat.

## De fasen

Een run doorloopt tien fasen, en het runscherm laat zien waar hij is, hoe lang elke fase duurde
en met welke hij nu bezig is:

1. **Verbindingen controleren** — authenticeert en meldt als welke identiteit dat gebeurde.
2. **Solutions kiezen** — somt op wat de omgeving bevat en wacht dan op u.
3. **Extraheren** — leest de solutions die u hebt gekozen.
4. **Checker** — dient de solution in bij de Power Apps checker en wacht.
5. **Oplossen** — verbindt componenten onderling, zodat de regels kunnen vragen wat naar wat wijst.
6. **Analyseren** — past elke regel uit de catalogus toe.
7. **Ramen** — zet een bandbreedte in uren op elke bevinding. Wordt overgeslagen bij een snelle scan.
8. **Scoren** — berekent de verhouding, de maatwerkgrafiek en de roadmap.
9. **Backlog** — maakt van bevindingen werkitems die iemand echt zou groomen.
10. **Publiceren** — schrijft naar Azure DevOps of Jira. Alleen in publicatiemodus, alleen na goedkeuring.

De fase die draait vertelt wat hij aan het doen is terwijl hij het doet — welke oplossing hij
exporteert, welke de checker onder handen heeft, hoe ver hij in de omgeving is — en het scherm
werkt zichzelf bij. Een fase die het landschap van een klant leest duurt minuten, en zonder dat
zien een trage en een gestopte fase er precies hetzelfde uit.

Een fase kan los opnieuw worden uitgevoerd vanaf het runscherm. Daarbij wordt ook elke fase
erna weggegooid, wat de knop u zegt voordat hij het doet: een run waarvan de bevindingen uit
de ene extractie komen en de score uit een andere, ziet er volkomen gezond uit en klopt niet.

Een fase kan ook **gedeeltelijk** eindigen, wat betekent dat hij zijn werk heeft gedaan en er
iets binnenin niet kon. Het meest voorkomende geval is analyseren: sommige regels konden niet
lopen. Dat is geen mislukking en de run gaat door.

## Terwijl hij loopt

U kunt weglopen. De run wordt uitgevoerd door een achtergrondworker, niet door uw browser, en
gaat door als u het tabblad sluit. Kom terug naar het scherm Analyses en open de run om de
tijdlijn op te pakken waar hij gebleven is.

## Een run verwijderen

Een opdracht verzamelt bij elke poging een run, en de lijst is wat u doorscrolt als u die ene
zoekt die u bedoelt. Een Beheerder kan een run verwijderen met alles wat hij heeft opgeleverd.

Twee dingen zijn het weten waard. Een run waarop een latere run is gebouwd kan niet worden
verwijderd, want die latere run zou dan een beoordeling beschrijven die niet meer bestaat. En
werkitems die al naar Azure DevOps of Jira zijn gepubliceerd blijven precies waar ze zijn: het
verwijderen van de run haalt de registratie van dit product weg dat het ze heeft geschreven,
en niets in het bord van de klant.

De bevindingenschermen lezen altijd één run, nooit een stapel, dus oude runs verwijderen is
opruimen en geen correctie.

## Als hij klaar is

Begin bij het **Overzicht**. Dat geeft u de aantallen, de verhouding, de spreiding over
ernstniveaus en de totale raming.

Lees daarna eerst de lijst **niet beoordeeld**, voordat u iets anders leest, zodat u weet wat
de getallen eronder wel en niet dekken.
