# Eine Analyse durchführen

## Die vier Modi

| Modus | Tut | Dauert |
|---|---|---|
| **Schnellprüfung** | Liest die Landschaft und wendet jede Regel an, mit Schätzbandbreiten statt Einzelschätzungen. | Minuten. |
| **Bewertung** | Eine Schnellprüfung, plus eine Schätzung je Befund und ein gepflegter Backlog. | Länger, und es wird ein Sprachmodell aufgerufen. |
| **Vergleichen** | Eine Bewertung gemessen an einem früheren Lauf, damit Sie zeigen können, was sich geändert hat. | Wie eine Bewertung. |
| **Veröffentlichen** | Schreibt den freigegebenen Backlog nach Azure DevOps. Liest nichts und analysiert nichts neu. | Minuten. |

Beginnen Sie mit einer Schnellprüfung. Das ist der Modus, der sich gefahrlos in einem
Verkaufsgespräch ausführen lässt, und er beantwortet die meisten Fragen eines ersten Termins.

## Auswählen, was gelesen wird

Ein Lauf liest nicht alles, was er finden kann, und entscheidet das nicht für Sie.

Innerhalb von Sekunden nach dem Start verbindet er sich, listet jede Lösung in der Umgebung
auf, und dann **hält er an und fragt Sie**. Sie bekommen die Liste, mit Herausgeber, Version
und Komponentenzahl je Lösung, und haken ab, was der Bericht abdecken soll.

Microsofts eigene Lösungen sind vorab abgewählt. Das meiste von dem, was eine
Dataverse-Umgebung enthält, hat Microsoft dort abgelegt, sie zu lesen ist mit Abstand der
längste Teil eines Laufs, und der Bericht, der dabei herauskommt, handelt von Dynamics statt
von der Arbeit, für die Ihr Kunde jemanden bezahlt hat. Nichts wird verborgen: jede gefundene
Lösung steht auf der Liste und wird festgehalten, ob Sie sie anhaken oder nicht, denn ein
Bericht über vier von neunzehn Lösungen und einer über alle neunzehn sehen auf dem Deckblatt
identisch aus.

Auf demselben Bildschirm lassen sich vier Prüfungen abschalten:

| Prüfung | Kostet | Wenn Sie sie abschalten |
|---|---|---|
| **Gewählte Lösungen exportieren** | Etwa eine Minute je Lösung, ein Dutzend also eine Viertelstunde. Es wird nichts geschrieben: ein Export ist ein Lesevorgang. | Die vierzehn Regeln, die eine Lösungsdatei lesen, und die drei, die den Checker brauchen, werden als nicht bewertet gemeldet. Eine Liveverbindung ist dann nicht reicher als ein Metadatenabruf. |
| **Solution Checker** | Mit Abstand der langsamste Teil eines Laufs. | Jede Regel, deren Beleg ein Checkerergebnis ist, wird als nicht bewertet gemeldet, nie als bestanden. |
| **Modellschätzungen** | Minuten, und ein Modellendpunkt. | Schätzungen fallen auf Bandvorgaben zurück, wie es eine Schnellprüfung tut. Der Bericht nennt, welche verwendet wurden. |
| **Zustand der Umgebung** | Sekunden. | Der Lauf arbeitet mit dem, was die Anmeldeinformation zufällig erreicht. |

Nichts anzuhaken ist erlaubt und erzeugt einen Bericht, der sagt, dies sei ein ungelesener
Bestand und kein sauberer. Der Bildschirm warnt Sie, bevor Sie fortfahren.

## Die Phasen

Ein Lauf durchläuft zehn Phasen, und der Laufbildschirm zeigt, wo er ist, wie lange jede
gedauert hat und in welcher er gerade steckt:

1. **Verbindungen prüfen** — authentifiziert und meldet, als welche Identität.
2. **Lösungen auswählen** — listet auf, was die Umgebung enthält, und wartet dann auf Sie.
3. **Extrahieren** — liest die Lösungen, die Sie gewählt haben.
4. **Checker** — reicht die Lösung beim Power Apps Checker ein und wartet.
5. **Auflösen** — verbindet Komponenten miteinander, damit die Regeln fragen können, was worauf zeigt.
6. **Analysieren** — wendet jede Regel aus dem Katalog an.
7. **Schätzen** — legt eine Stundenspanne auf jeden Befund. Bei einer Schnellprüfung übersprungen.
8. **Bewerten** — berechnet das Verhältnis, das Anpassungsdiagramm und die Roadmap.
9. **Backlog** — macht aus Befunden Arbeitselemente, die jemand tatsächlich pflegen würde.
10. **Veröffentlichen** — schreibt nach Azure DevOps oder Jira. Nur im Veröffentlichungsmodus, nur nach Freigabe.

Eine Phase kann vom Laufbildschirm aus einzeln erneut ausgeführt werden. Dabei wird auch jede
Phase danach verworfen, was die Schaltfläche Ihnen sagt, bevor sie es tut: ein Lauf, dessen
Befunde aus der einen Extraktion und dessen Bewertung aus einer anderen stammen, sähe völlig
gesund aus und wäre falsch.

Eine Phase kann auch **teilweise** enden, was bedeutet, dass sie ihre Arbeit getan hat und
etwas darin nicht möglich war. Der häufigste Fall ist Analysieren: einige Regeln konnten nicht
laufen. Das ist kein Fehlschlag und der Lauf geht weiter.

## Während er läuft

Sie können weggehen. Der Lauf wird von einem Hintergrundworker ausgeführt, nicht von Ihrem
Browser, und läuft weiter, wenn Sie den Tab schließen. Kommen Sie auf den Bildschirm Läufe
zurück und öffnen Sie den Lauf, um die Zeitleiste dort aufzunehmen, wo sie steht.

## Einen Lauf entfernen

Ein Engagement sammelt bei jedem Versuch einen Lauf an, und die Liste ist das, was Sie
durchscrollen, wenn Sie den einen suchen, den Sie meinen. Ein Administrator kann einen Lauf
entfernen, mit allem, was er erzeugt hat.

Zwei Dinge sind wissenswert. Ein Lauf, auf dem ein späterer Lauf aufgebaut wurde, kann nicht
entfernt werden, denn jener spätere Lauf bliebe dann zurück und beschriebe eine Bewertung, die
es nicht mehr gibt. Und Arbeitselemente, die bereits nach Azure DevOps oder Jira veröffentlicht
wurden, bleiben genau dort: den Lauf zu entfernen entfernt den Nachweis dieses Produkts, sie
geschrieben zu haben, und nichts im Board des Kunden.

Die Befundbildschirme lesen immer genau einen Lauf, nie einen Stapel, alte Läufe zu entfernen
ist also Aufräumen und keine Korrektur.

## Wenn er fertig ist

Beginnen Sie bei der **Übersicht**. Sie gibt Ihnen die Zahlen, das Verhältnis, die Verteilung
nach Schweregrad und die Gesamtschätzung.

Lesen Sie danach zuerst die Liste **nicht bewertet**, bevor Sie irgendetwas anderes lesen,
damit Sie wissen, was die Zahlen darunter abdecken und was nicht.
