# Lancer une analyse

## Les quatre modes

| Mode | Fait | Dure |
|---|---|---|
| **Analyse rapide** | Lit le patrimoine et applique chaque règle, avec des fourchettes d'estimation plutôt que des estimations individuelles. | Des minutes. |
| **Évaluation** | Une analyse rapide, plus une estimation par constat et un backlog affiné. | Plus longtemps, et elle appelle un modèle de langage. |
| **Comparer** | Une évaluation mesurée par rapport à une exécution antérieure, pour montrer ce qui a changé. | Comme une évaluation. |
| **Publier** | Écrit les éléments de backlog choisis dans Azure DevOps, Jira ou GitHub. Ne lit rien et ne réanalyse rien. | Des minutes. |

Commencez par une analyse rapide. C'est le mode que l'on peut lancer sans risque dans un
entretien commercial, et il répond à la plupart des questions d'un premier rendez-vous.

## Choisir ce qui sera lu

Une exécution ne lit pas tout ce qu'elle peut trouver, et elle ne décide pas à votre place.

Quelques secondes après le démarrage, elle se connecte, énumère chaque solution de
l'environnement, puis **s'arrête et vous demande**. Vous obtenez la liste, avec pour chaque
solution son éditeur, sa version et son nombre de composants, et vous cochez celles que le
rapport doit couvrir.

Les solutions de Microsoft sont décochées par défaut. L'essentiel de ce que contient un
environnement Dataverse y a été mis par Microsoft, les lire est de loin la partie la plus
longue d'une exécution, et le rapport qui en sort parle de Dynamics plutôt que du travail pour
lequel votre client a payé quelqu'un. Rien n'est caché : chaque solution trouvée figure sur la
liste et est enregistrée, que vous la cochiez ou non, car un rapport couvrant quatre solutions
sur dix-neuf et un rapport couvrant les dix-neuf sont identiques en page de garde.

Quatre vérifications peuvent être désactivées sur le même écran :

| Vérification | Coûte | Si vous la désactivez |
|---|---|---|
| **Exporter les solutions choisies** | Environ une minute par solution, donc une douzaine prend un quart d'heure. Rien n'est écrit : un export est une lecture. | Les quatorze règles qui lisent un fichier de solution, et les trois qui ont besoin du checker, sont signalées non évaluées. Une connexion en direct n'est alors pas plus riche qu'une lecture de métadonnées. |
| **Solution checker** | De loin la partie la plus lente d'une exécution. | Chaque règle dont la preuve est un résultat du checker est signalée non évaluée, jamais conforme. |
| **Estimations par modèle** | Des minutes, et un point de terminaison de modèle. | Les estimations reviennent aux valeurs par défaut, comme le fait une analyse rapide. Le rapport indique lesquelles il a utilisées. |
| **Santé de l'environnement** | Quelques secondes. | L'exécution se poursuit avec ce que l'identification atteint par hasard. |

Ne rien cocher est permis et produit un rapport qui dit qu'il s'agit d'un patrimoine non lu
plutôt que sain. L'écran vous avertit avant que vous continuiez.

## Les étapes

Une exécution passe par dix étapes, et l'écran d'exécution montre où elle en est, combien de
temps chacune a pris et sur laquelle elle travaille :

1. **Vérifier les connexions** — s'authentifie et indique sous quelle identité.
2. **Choisir les solutions** — énumère ce que contient l'environnement, puis vous attend.
3. **Extraire** — lit les solutions que vous avez choisies.
4. **Checker** — soumet la solution au Power Apps checker et attend.
5. **Résoudre** — relie les composants entre eux, pour que les règles puissent demander ce qui pointe vers quoi.
6. **Analyser** — applique chaque règle du catalogue.
7. **Estimer** — pose une fourchette d'heures sur chaque constat. Ignorée par une analyse rapide.
8. **Scorer** — calcule le ratio, le graphique de personnalisation et la feuille de route.
9. **Backlog** — transforme les constats en éléments de travail que quelqu'un affinerait vraiment.
10. **Publier** — écrit dans Azure DevOps, Jira ou GitHub. Seulement en mode publication, et seulement les éléments que quelqu'un a choisis.

L'étape en cours dit ce qu'elle fait pendant qu'elle le fait — quelle solution elle exporte,
laquelle est chez le checker, où elle en est dans l'environnement — et l'écran se met à jour
tout seul. Une étape qui lit le patrimoine d'un client prend des minutes, et sans cela une
étape lente et une étape arrêtée se ressemblent exactement.

Une étape peut être relancée seule depuis l'écran d'exécution. Cela écarte aussi toutes les
étapes qui la suivent, ce que le bouton vous dit avant de le faire : une exécution dont les
constats viennent d'une extraction et dont le score vient d'une autre paraîtrait parfaitement
saine et serait fausse.

Une étape peut aussi se terminer **partielle**, ce qui veut dire qu'elle a fait son travail et
que quelque chose à l'intérieur n'a pas pu être fait. Le cas le plus courant est l'analyse :
certaines règles n'ont pas pu s'exécuter. Ce n'est pas un échec et l'exécution continue.

## Pendant qu'elle tourne

Vous pouvez partir. L'exécution est réalisée par un worker en arrière-plan, pas par votre
navigateur, et elle continue si vous fermez l'onglet. Revenez à l'écran Exécutions et ouvrez
l'exécution pour reprendre la chronologie là où elle en est.

## Supprimer une exécution

Une mission accumule une exécution à chaque tentative, et la liste est ce que vous parcourez
quand vous cherchez celle que vous avez en tête. Un Administrateur peut supprimer une
exécution et tout ce qu'elle a produit.

Deux choses à savoir. Une exécution sur laquelle une exécution ultérieure a été construite ne
peut pas être supprimée, car cette exécution ultérieure décrirait alors une évaluation qui
n'existe plus. Et les éléments de travail déjà publiés dans Azure DevOps, Jira ou GitHub restent
exactement où ils sont : supprimer l'exécution supprime la trace, dans ce produit, de les
avoir écrits, et rien dans le tableau du client.

Les écrans de constats lisent toujours une seule exécution, jamais une pile, supprimer les
anciennes relève donc du rangement et non de la correction.

## Quand elle se termine

Commencez par la **Vue d'ensemble**. Elle vous donne les décomptes, le ratio, la répartition
par gravité et l'estimation totale.

Lisez ensuite la liste **non évalué** avant toute autre chose, pour savoir ce que les chiffres
en dessous couvrent et ne couvrent pas.
