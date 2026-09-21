# Lire les constats

## Catégories

Chaque règle appartient à l'une des dix catégories. L'écran des constats regroupe par
catégorie.

| Catégorie | Porte sur |
|---|---|
| **Cycle de vie et dépréciation** | Les composants que Microsoft a retirés, dépréciés ou dans lesquels il n'investit plus. |
| **Modernisation** | Quelque chose qui fonctionne et pour quoi il existe désormais une meilleure réponse. |
| **Qualité de construction** | Avec quel soin l'existant a été construit : gestion des erreurs, nommage, structure. |
| **Architecture** | Si la logique se trouve là où la logique devrait être. Le ratio low code se trouve ici. |
| **ALM et hygiène des solutions** | Si cela peut passer d'un environnement à l'autre sans que quelqu'un doive se souvenir de quelque chose. |
| **Gouvernance** | Propriété, prolifération, composants orphelins, exposition aux licences. |
| **Performance** | Ce qui est lent maintenant, ou le deviendra à volume. |
| **Sécurité** | Privilèges, secrets et exposition. |
| **Exploitabilité** | Si quelqu'un s'apercevrait que cela a cassé. |
| **Composants IA** | Agents, prompts et modèles : si ce qui est bâti sur l'IA est fondé, à jour et rattaché à quelqu'un. |

La modernisation est la catégorie qu'un client veut le plus et celle que l'on survend le plus
facilement, donc chaque règle qu'elle contient porte aussi une raison de laisser la chose
tranquille.

## Gravité

Critique, élevée, moyenne, faible, informative. La gravité vient de la règle, pas du
composant, et un gestionnaire peut l'abaisser pour une raison qu'il consigne dans la preuve.

La gravité n'est pas la priorité. Un constat critique sur un composant que personne n'utilise
est moins urgent qu'un constat moyen au milieu du processus quotidien, et le produit ne
prétend pas savoir lequel est lequel. Ce jugement vous appartient, et le backlog est l'endroit
où vous le consignez.

## Preuves

Chaque constat porte ce qui l'a déclenché. C'est la partie que le client achète : une
affirmation que vous ne pouvez pas vérifier devant lui est une affirmation qui perd la salle.

Ouvrez un constat et vous obtenez les valeurs précises — le nombre d'actions, le mode
d'isolation, l'URL trouvée, le nombre de bibliothèques sur le formulaire. Pas une reformulation
de la règle.

## D'où vient un constat

Les constats sont marqués **catalogue**, **checker** ou **modèle**.

Un constat du checker vient du Power Apps checker de Microsoft et porte l'identifiant de règle
de Microsoft, vous pouvez donc le rechercher dans leur documentation. Ces règles restent à
jour parce que Microsoft les maintient, pas parce que ce produit le fait.

Exactement une règle est décidée par un modèle de langage : si une description dit quelque
chose. Il lit une description à la fois, indique sur chaque constat qu'un modèle a jugé, et
porte la phrase du modèle pour que vous puissiez la contester à voix haute. Toutes les
autres règles sont des mesures. Sans modèle configuré pour l'exécution, cette règle est
signalée comme non évaluée, jamais comme conforme.

## Composants gérés

Un constat contre un composant arrivé dans une solution gérée est signalé et jamais estimé.
C'est à quelqu'un d'autre de le corriger, et estimer du travail sur une solution que vous ne
livrez pas revient à inventer un chiffre. Soulevez-le auprès de celui qui la livre.

## Remplacer un constat

Vous pouvez remplacer l'estimation d'un constat sur une mission. Le remplacement est attaché à
la clé stable du constat — règle plus composant — et survit donc à une nouvelle exécution.
Quelqu'un qui renomme un flux ne rend pas orpheline l'estimation sur laquelle un atelier a
passé une heure.

## La feuille de route

La feuille de route place chaque constat sur une grille : une ligne pour le type de travail et
une colonne selon qu'il s'agit de personnes, de processus ou de technologie, en bandes allant
de « désencombrer » vers l'extérieur. C'est une manière de montrer à un client la forme du
travail plutôt qu'une liste de 300 éléments.

Les règles ayant des constats et aucune position sur la feuille de route sont signalées au bas
de l'étape de scoring, pour que la grille ne puisse pas laisser tomber une catégorie en
silence.
