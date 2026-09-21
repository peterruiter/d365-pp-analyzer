# Schätzungen und der Backlog

## Woher eine Zahl kommt

Jede Schätzung ist eine Spanne in Stunden, und jede Schätzung nennt, welche von drei Schichten
sie hervorgebracht hat:

- **Bandvorgabe** — das Schätzband, das die Regel im Katalog deklariert: trivial, klein,
  mittel, groß. Eine Schnellprüfung verwendet durchgehend diese, und das macht sie schnell.
- **Modell** — eine Schätzung je Befund, erzeugt von einem Sprachmodell aus dem Beleg des
  Befunds und der Komplexität der Komponente. Das ist es, was ein Bewertungslauf hinzufügt.
- **Überschreibung im Engagement** — eine Zahl, die ein Mensch gesetzt hat und die beide
  schlägt.

Die Schicht steht neben jeder Schätzung. Ein Kunde, der fragt "woher kommen die 40 Stunden",
stellt eine berechtigte Frage und sollte eine konkrete Antwort bekommen.

## Fixkosten

Manche Kosten fallen je Engagement an statt je Befund: Umgebung einrichten, ein
Regressionsdurchlauf, eine Übergabe. Die kommen aus dem Vertrag und werden getrennt von der
Summe der Befunde ausgewiesen, denn sie in eine Summe je Befund einzurechnen macht die Zahlen
je Befund falsch.

## Die Komplexitätsbewertung

Komponenten werden als einfach, mittel oder komplex bewertet, anhand von Maßen, die im Vertrag
deklariert sind — die Aktionsanzahl eines Flows, die Steuerelementanzahl einer App, die Größe
einer Assembly. Die Bewertung fließt in die Modellschätzung und das Anpassungsdiagramm ein.

Eine Komponente, deren Maß nicht erreichbar war, wird als **nicht bewertet** geführt statt als
einfach.

## Der Backlog

Ein Arbeitselement je Befund ergäbe vierhundert Aufgaben, die niemand pflegt. Eines je Regel
verlöre den Beleg, und der ist gerade der Teil, für den ein Kunde zahlt.

Also ist der Backlog so gruppiert, wie ein Berater ihn von Hand gruppiert hätte:

- ein **Epic** je Kategorie,
- ein **Feature** je Regel, die genug Befunde hat, um eines zu brauchen,
- eine **Story** je Befund, der es verdient, benannt zu werden,
- und triviale Befunde **gebündelt** zu einer einzigen Aufgabe je Regel.

Jedes Element trägt Abnahmekriterien in given/when/then-Form und eine Testanforderung, beides
aus dem Vertrag statt je Element geschrieben. Sie entstehen in der Backlog-Sprache des
Engagements, die je Engagement gesetzt wird und von der Sprache getrennt ist, in der Sie das
Produkt lesen.

## Veröffentlichen

Eine Veröffentlichung schreibt die gewählten Elemente in ein Azure-DevOps-Projekt, ein
Jira-Projekt oder ein GitHub-Repository, beim Veröffentlichen gewählt und nicht auf der
Verbindung gespeichert. Sie analysiert nichts neu: sie veröffentlicht die gewählten Elemente
und sonst nichts.

Jedes Element trägt einen deterministischen Schlüssel, abgeleitet aus dem Engagement und dem
Schlüssel des Elements, sodass zweimal veröffentlichen aktualisiert, was da ist, statt eine
zweite Kopie von allem anzulegen. In Azure DevOps ist das ein Tag, in Jira und GitHub ein
Label: das einzige Feld, das alle drei haben, ohne dass jemand es erst einrichten muss.

Die drei Ziele bieten nicht dieselben Formen, und das Produkt tut nicht so. Azure DevOps wird
gefragt, welche Arbeitselementtypen das Projekt hat; Jira wird nach seinen Vorgangstypen
gefragt, und eine Ebene, die es nicht gibt, landet flach statt zu scheitern; GitHub hat
überhaupt keine Typen, also werden unsere zu Labels und die Verbindung zum Elternelement ist
ein Sub-Issue, wo das Repository es unterstützt. Auf keinem der drei wird je etwas geschlossen
oder wieder geöffnet.

Sie können eine Veröffentlichung zuerst als Probelauf ausführen, der meldet, was angelegt und
aktualisiert würde, ohne etwas zu schreiben.
