# Rapports et exports

Deux documents sortent d'une exécution. Tous deux sont produits à partir des résultats
enregistrés de cette exécution : un rapport téléchargé un mois plus tard dit ce que
l'exécution a dit, pas ce que les règles disent aujourd'hui.

## Le classeur des constats

Un `.xlsx` avec une feuille par angle de vue sur la même exécution :

- **Synthèse** — les décomptes, le ratio, les totaux.
- **Constats** — chaque constat, avec sa règle, sa gravité, sa catégorie, son composant, sa preuve et son estimation.
- **Inventaire** — chaque composant, avec type, solution, facture, cycle de vie et domaine.
- **Non évalué** — chaque règle qui n'a pas pu s'exécuter, et pourquoi.
- **Backlog** — les éléments de travail, avec leurs critères d'acceptation.

C'est celui dans lequel l'architecte d'un client travaillera réellement. Il est délibérément
sobre : pas de cellules fusionnées, pas d'images, des filtres sur chaque ligne d'en-tête, pour
qu'il puisse être trié et croisé plutôt qu'admiré.

## Le rapport d'évaluation

Un `.pdf` écrit pour être lu par quelqu'un qui n'ouvrira pas le classeur. Il porte le récit :
ce qui a été lu, ce qui ne l'a pas été, ce qui a été trouvé, ce que cela coûterait, et la
feuille de route.

La section « non évalué » n'est pas une annexe. Elle se trouve près du début, parce qu'un
lecteur qui atteint les chiffres sans elle a été induit en erreur.

## La langue dans laquelle sort un rapport

Les rapports sont produits dans la **langue de rapport** de la mission, qui se règle sur la
mission et qui est distincte de la langue dans laquelle vous lisez le produit.

Une consultante néerlandaise peut lire une interface néerlandaise et produire un rapport en
anglais pour une équipe offshore. C'est le cas normal, pas un cas limite, et c'est pourquoi ce
sont deux réglages.

Le backlog a encore sa propre langue, parce que les personnes qui affinent un backlog ne sont
fréquemment pas celles qui lisent le rapport.

## Télécharger

Les rapports sont listés sur l'écran Rapports pour chaque exécution. Ils sont générés quand
vous les demandez plutôt que conservés, pour qu'un rapport corresponde toujours à l'exécution
qu'il nomme.

Le nom de fichier porte le nom du client et la date de l'exécution, parce qu'un dossier de
fichiers tous appelés `report.pdf` est un dossier inutilisable.
