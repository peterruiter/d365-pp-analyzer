# Estimations et backlog

## D'où vient un chiffre

Chaque estimation est une fourchette en heures, et chaque estimation nomme laquelle des trois
couches l'a produite :

- **Valeur par défaut de fourchette** — la fourchette d'estimation que la règle déclare dans
  le catalogue : triviale, petite, moyenne, grande. Une analyse rapide n'utilise que
  celles-ci, et c'est ce qui la rend rapide.
- **Modèle** — une estimation par constat produite par un modèle de langage, à partir de la
  preuve du constat et de la complexité du composant. C'est ce qu'ajoute une exécution
  d'évaluation.
- **Remplacement de mission** — un chiffre posé par un humain, qui l'emporte sur les deux
  autres.

La couche est affichée à côté de chaque estimation. Un client qui demande « d'où viennent ces
40 heures » pose une question raisonnable et doit obtenir une réponse précise.

## Coûts fixes

Certains coûts sont par mission plutôt que par constat : mise en place d'un environnement, une
passe de régression, une reprise. Ils viennent du contrat et sont présentés séparément de la
somme des constats, car les inclure dans un total par constat fausse les chiffres par constat.

## L'évaluation de complexité

Les composants sont évalués simples, moyens ou complexes à partir de mesures déclarées dans le
contrat — le nombre d'actions d'un flux, le nombre de contrôles d'une application, la taille
d'un assemblage. L'évaluation alimente l'estimation par modèle et le graphique de
personnalisation.

Un composant dont la mesure n'était pas atteignable est évalué **non évalué** plutôt que
simple.

## Le backlog

Un élément de travail par constat produirait quatre cents tâches que personne n'affine. Un par
règle perdrait la preuve, et c'est justement la partie que le client achète.

Le backlog est donc regroupé comme un consultant l'aurait regroupé à la main :

- un **epic** par catégorie,
- une **feature** par règle ayant assez de constats pour en justifier une,
- une **story** par constat qui mérite d'être nommé,
- et les constats triviaux **regroupés** en une seule tâche par règle.

Chaque élément porte des critères d'acceptation en forme given/when/then et une exigence de
test, tous deux issus du contrat plutôt qu'écrits élément par élément. Ils sont produits dans
la langue de backlog de la mission, qui se règle par mission et qui est distincte de la langue
dans laquelle vous lisez le produit.

## Publier

Une publication écrit les éléments choisis dans un projet Azure DevOps, un projet Jira ou un
dépôt GitHub, choisi au moment de publier et non enregistré sur la connexion. Elle ne réanalyse
rien : elle publie les éléments choisis et rien d'autre.

Chaque élément porte une clé déterministe dérivée de la mission et de la clé de l'élément, si
bien que publier deux fois met à jour ce qui existe au lieu de créer une deuxième copie de
tout. C'est une étiquette dans Azure DevOps et un label dans Jira et GitHub : le seul champ que
les trois possèdent sans que personne ait à le configurer d'abord.

Les trois cibles n'offrent pas les mêmes formes et le produit ne fait pas semblant. On demande
à Azure DevOps quels types d'éléments de travail le projet possède ; on demande à Jira ses
types de tickets, et un niveau qu'il n'a pas atterrit à plat plutôt que d'échouer ; GitHub n'a
aucun type, donc les nôtres deviennent des labels et le lien au parent est une sous-issue là où
le dépôt la prend en charge. Sur aucune des trois rien n'est jamais fermé ni rouvert.

Vous pouvez d'abord lancer une publication à blanc, qui indique ce qu'elle créerait et
mettrait à jour sans rien écrire.
