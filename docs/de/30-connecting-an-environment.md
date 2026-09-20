# Eine Umgebung verbinden

Es gibt drei Wege hinein, und sie erreichen unterschiedlich viel. Wählen Sie den, den Sie diese
Woche tatsächlich genehmigt bekommen, nicht den, der am meisten erreicht.

## Anwendungsbenutzer, nur lesend

Eine Entra-App-Registrierung, die der Umgebung als Anwendungsbenutzer mit einer rein lesenden
Sicherheitsrolle hinzugefügt wird. Das ist der Modus für alles, was wiederholbar sein soll.

**Erreicht:** die Dataverse-Web-API vollständig, eine exportierte Lösung vollständig, den
Power Apps Checker vollständig, und Laufzeitbelege teilweise.

**Braucht:** eine Tenant-ID, eine Client-ID, eine Umgebungs-URL und ein Clientgeheimnis. Das
Geheimnis kommt in Key Vault und wird über seinen Namen referenziert. Es wird nie eine
Anmeldeinformation in der Produktdatenbank gespeichert.

Die Rollendefinition wird mit dem Produkt ausgeliefert, damit das Sicherheitsteam eines Kunden
eine Datei prüft statt einer Beschreibung.

Ausführungsverlauf von Flows und Ablaufverfolgungsprotokolle von Plug-ins brauchen mehr als
einen einfachen Leser. Wo dieses Recht fehlt, nennt das Produkt die Regeln, die unbewertet
blieben, statt sie als sauber zu melden.

## Delegierter Benutzer

Sie, angemeldet, lesen das, was Sie ohnehin lesen dürfen.

**Erreicht:** alles, einschließlich Laufzeitbelege, soweit Ihr eigenes Konto herankommt.

**Braucht:** nichts, was angelegt werden müsste. Das ist der schnellste Weg, etwas Echtes zu
sehen.

Der Haken ist, dass es nicht wiederholbar ist: ein geplanter Lauf kann Ihre Sitzung nicht
ausleihen, und das Ergebnis hängt von Ihren Berechtigungen ab statt von einer deklarierten
Rolle.

## Lösungsdatei

Eine exportierte unmanaged Lösung, entpackt und offline gelesen.

**Erreicht:** die Lösungsdatei vollständig und den Checker vollständig. Metadaten teilweise.
Laufzeit gar nicht.

**Braucht:** eine `.zip` und sonst nichts. Keine Verbindung, keine Anmeldeinformation, keine
Sicherheitsprüfung.

Das ist kein abgeschwächter Notbehelf. Es ist der Modus, der in Woche eins an einer
Sicherheitsprüfung vorbeikommt, während der Antrag für einen Dienstprinzipal in einer
Warteschlange liegt, und für eine Qualitäts- und Schuldenbewertung erreicht er das meiste von
dem, worauf es ankommt. Was er nicht sehen kann, ist Nutzung: welche Flows tatsächlich laufen,
welche Workflows ruhen, wie oft etwas fehlschlägt.

## Rolle der Umgebung

Welchen Modus Sie auch nutzen, Sie erklären, **wofür** die Umgebung da ist: Entwicklung, Test,
Abnahme oder Produktion.

Mehrere Regeln feuern nur gegen Produktion. Falsch zu raten macht einen Bericht entweder
alarmistisch oder nutzlos, deshalb wird dies erklärt statt aus dem Namen der Umgebung
abgeleitet. Lassen Sie es auf unbekannt, melden sich die produktionsgebundenen Regeln selbst
als nicht bewertet — sie nehmen stillschweigend nichts an.

## Eine Verbindung testen

Testen Sie sie, bevor Sie irgendetwas ausführen. Der Test meldet, als welche Identität er sich
authentifiziert hat und was er erreichen konnte, je Belegquelle.

Eine Verbindung, die mit zu wenigen Rechten gelingt, scheitert später auf eine Weise, die
genau wie eine Landschaft ohne Inhalt aussieht. Das Reichweitenpanel ist dafür da, dass Sie es
jetzt erfahren.
