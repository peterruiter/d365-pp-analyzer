# Was das ist

Eine rein lesende Bewertung einer Power-Platform-Landschaft. Sie richten es auf eine Umgebung
oder geben ihm eine exportierte Lösungsdatei, es liest, was da ist, und es liefert vier Dinge:

1. **Eine Inventarisierung.** Jede Komponente, nach Typ, nach Lösung, nach Domäne.
2. **Ein Low-Code-Verhältnis.** Eine Zahl, mit ihrer Definition daneben, weil die Zahl ohne
   sie zitiert wird.
3. **Befunde.** Was veraltet, schlecht gebaut, ungesteuert, langsam oder exponiert ist, jeweils
   mit dem Beleg, der ihn ausgelöst hat.
4. **Eine Schätzung.** Eine Stundenspanne je Befund, mit Begründung, aufaddiert zu einer Summe,
   deren Rechenweg Sie vor einem Kunden nachprüfen können.

## Was es nicht tut

**Es schreibt nie in eine Power-Platform-Umgebung.** In keinem Modus, und es gibt keine
Einstellung, die das ändert. Sie können eine Erkundung in einem ersten Gespräch durchführen,
ohne ein Change Advisory Board, und genau darum geht es.

Das Einzige, was es irgendwohin schreibt, sind Arbeitselemente in Azure DevOps, Jira oder GitHub, und nur die,
die jemand im Backlog-Bildschirm ausgewählt hat. Es gibt einen Probelauf, der genau zeigt,
was landen würde, und nichts schreibt.

**Es ist kein Ersatz für den Power Apps Checker.** Es ruft den Checker auf und fügt die
Ergebnisse unter Microsofts eigenen Regelkennungen ein. Die Regeln, die es selbst deklariert,
sind gerade die, die der Checker nicht hat: Position im Lebenszyklus, über Komponenten
verteilte Schuld, Wildwuchs und Lösungshygiene.

**Es ist keine Lizenzbewertung.** Es meldet, wo ein Premium-Connector verwendet wird, und hört
dort auf. Es kann nicht sehen, was der Tenant hält, und Raten wäre schlimmer als Schweigen.

**Es ist kein Penetrationstest.** Die Sicherheitsregeln betreffen Berechtigungen, Geheimnisse
in Definitionen und Schreibrechte auf Organisationsebene. Sie sind keine Bewertung, ob in die
Landschaft eingebrochen werden kann.

## Was "nicht bewertet" bedeutet

Das ist der wichtigste Gedanke im Produkt, deshalb bekommt er einen eigenen Abschnitt.

Jede Regel deklariert, welchen Beleg sie braucht. Wenn die Art, wie Sie sich verbunden haben,
diesen Beleg nicht erreichen kann, wird die Regel als **nicht bewertet** gemeldet, namentlich,
mit Begründung. Sie wird nie als bestanden gemeldet und nie als null Befunde gezählt.

Ein Bericht, der sagt, ein Kunde habe keine technischen Schulden, während die Wahrheit ist,
dass niemand seine Umgebung lesen konnte, ist das Schädlichste, was dieses Produkt erzeugen
kann. Deshalb trägt der Bericht immer eine Liste dessen, was nicht geprüft werden konnte, und
diese Liste sollten Sie im Raum laut vorlesen.

## Die Demonstrationslandschaft

Jeder, der zum Produkt zugelassen ist, kann ein Engagement namens **Demonstrationslandschaft**
öffnen. Es ist eine synthetische Power-Platform-Landschaft in der Form eines mittelgroßen
Versorgers, und nichts darin stammt von einem Kunden.

Die Befunde darin sind nicht erfunden. Sie entstehen, indem dieselbe Regelmaschine, derselbe
Scorer und derselbe Backlog-Builder über die synthetische Landschaft laufen, die auch über eine
echte laufen. Sie ist für alle nur lesbar, Sie können also jeden Bildschirm erkunden, ohne
etwas kaputt machen zu können.

Nutzen Sie sie, um das Produkt kennenzulernen, und nutzen Sie sie, um das Produkt vorzuführen,
bevor ein Kunde Ihnen irgendetwas gegeben hat.
