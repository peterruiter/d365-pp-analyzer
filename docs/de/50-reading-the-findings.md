# Die Befunde lesen

## Kategorien

Jede Regel sitzt in einer von neun Kategorien. Der Befundbildschirm gruppiert danach.

| Kategorie | Handelt von |
|---|---|
| **Lebenszyklus und Veraltung** | Komponenten, die Microsoft entfernt, für veraltet erklärt oder nicht mehr weiterentwickelt hat. |
| **Modernisierung** | Etwas, das funktioniert und für das es inzwischen eine bessere Antwort gibt. |
| **Baugüte** | Wie gut das Vorhandene gebaut wurde: Fehlerbehandlung, Benennung, Struktur. |
| **Architektur** | Ob Logik dort sitzt, wo Logik sitzen sollte. Das Low-Code-Verhältnis gehört hierher. |
| **ALM und Lösungshygiene** | Ob sich das zwischen Umgebungen bewegen lässt, ohne dass jemand an etwas denken muss. |
| **Governance** | Eigentümerschaft, Wildwuchs, verwaiste Komponenten, Lizenzexposition. |
| **Leistung** | Dinge, die jetzt langsam sind oder es bei Volumen werden. |
| **Sicherheit** | Berechtigungen, Geheimnisse und Exposition. |
| **Betreibbarkeit** | Ob überhaupt jemand merken würde, wenn es kaputtgeht. |

Modernisierung ist die Kategorie, die ein Kunde sich am meisten wünscht und die am ehesten
überverkauft wird, deshalb trägt jede Regel darin auch einen Grund, die Sache in Ruhe zu
lassen.

## Schweregrad

Kritisch, hoch, mittel, niedrig, informativ. Der Schweregrad kommt von der Regel, nicht von der
Komponente, und ein Handler darf ihn aus einem Grund senken, den er im Beleg festhält.

Schweregrad ist keine Priorität. Ein kritischer Befund an einer Komponente, die niemand nutzt,
ist weniger dringend als ein mittlerer mitten im Tagesgeschäft, und das Produkt tut nicht so,
als wüsste es, welcher welcher ist. Dieses Urteil ist Ihres, und der Backlog ist der Ort, wo
Sie es festhalten.

## Belege

Jeder Befund trägt das mit sich, was ihn ausgelöst hat. Das ist der Teil, für den ein Kunde
zahlt: eine Behauptung, die Sie vor ihm nicht nachprüfen können, ist eine Behauptung, die den
Raum verliert.

Öffnen Sie einen Befund und Sie bekommen die konkreten Werte — die Anzahl der Aktionen, den
Isolationsmodus, die gefundene URL, die Anzahl der Bibliotheken auf dem Formular. Keine
Wiederholung der Regel.

## Woher ein Befund kommt

Befunde sind als **Katalog** oder **Checker** gekennzeichnet.

Ein Checker-Befund stammt aus Microsofts Power Apps Checker und trägt Microsofts eigene
Regelkennung, sodass Sie ihn in deren Dokumentation nachschlagen können. Diese Regeln bleiben
aktuell, weil Microsoft sie pflegt, nicht weil dieses Produkt es tut.

## Managed Komponenten

Ein Befund gegen eine Komponente, die in einer managed Lösung angekommen ist, wird gemeldet und
nie geschätzt. Sie zu beheben ist Sache eines anderen, und Arbeit an einer Lösung zu schätzen,
die Sie nicht ausliefern, heißt eine Zahl zu erfinden. Sprechen Sie es bei dem an, der sie
ausliefert.

## Einen Befund überschreiben

Sie können die Schätzung eines Befunds in einem Engagement überschreiben. Die Überschreibung
hängt am stabilen Schlüssel des Befunds — Regel plus Komponente — und überlebt damit einen
erneuten Lauf. Wenn jemand einen Flow umbenennt, verwaist nicht die Schätzung, über die ein
Workshop eine Stunde gebraucht hat.

## Die Roadmap

Die Roadmap platziert jeden Befund auf einem Raster: eine Zeile für die Art der Arbeit und
eine Spalte dafür, ob es um Menschen, Prozess oder Technik geht, in Bändern von "entrümpeln"
nach außen. Das ist eine Art, einem Kunden die Gestalt der Arbeit zu zeigen, statt einer Liste
mit 300 Punkten.

Regeln mit Befunden und ohne Platz auf der Roadmap werden am Ende der Bewertungsphase gemeldet,
damit das Raster nicht stillschweigend eine Kategorie fallen lassen kann.
