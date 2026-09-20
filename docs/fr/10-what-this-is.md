# Ce que c'est

Une évaluation en lecture seule d'un patrimoine Power Platform. Vous le pointez vers un
environnement ou vous lui donnez un fichier de solution exporté, il lit ce qui s'y trouve, et
il produit quatre choses :

1. **Un inventaire.** Chaque composant, par type, par solution, par domaine.
2. **Un ratio low code.** Un chiffre, accompagné de sa définition, parce que le chiffre est
   cité sans elle.
3. **Des constats.** Ce qui est déprécié, mal construit, non gouverné, lent ou exposé, chacun
   avec la preuve qui l'a déclenché.
4. **Une estimation.** Une fourchette d'heures par constat, avec une justification, s'ajoutant
   à un total dont vous pouvez vérifier le calcul devant un client.

## Ce qu'il ne fera pas

**Il n'écrit jamais dans un environnement Power Platform.** Dans aucun mode, et aucun réglage
ne change cela. Vous pouvez lancer une découverte dès le premier entretien sans comité
consultatif du changement, et c'est tout l'intérêt.

La seule chose qu'il écrit quelque part, ce sont des éléments de travail dans Azure DevOps, et
seulement après que quelqu'un disposant du rôle Administrateur sur la mission a approuvé le
backlog exact qui sera publié.

**Il ne remplace pas le Power Apps checker.** Il appelle le checker et intègre les résultats
sous les identifiants de règle de Microsoft. Les règles qu'il déclare lui-même sont justement
celles que le checker n'a pas : position dans le cycle de vie, dette répartie entre
composants, prolifération et hygiène des solutions.

**Ce n'est pas une évaluation de licences.** Il signale où un connecteur premium est utilisé
et s'arrête là. Il ne peut pas voir ce que détient le tenant, et deviner serait pire que se
taire.

**Ce n'est pas un test d'intrusion.** Les règles de sécurité portent sur les privilèges, les
secrets dans les définitions et l'écriture au niveau de l'organisation. Elles n'évaluent pas
si le patrimoine peut être pénétré.

## Ce que « non évalué » signifie

C'est l'idée la plus importante du produit, elle a donc sa propre section.

Chaque règle déclare la preuve dont elle a besoin. Si la manière dont vous vous êtes connecté
ne peut pas atteindre cette preuve, la règle est signalée comme **non évaluée**, nommément,
avec le motif. Elle n'est jamais signalée comme conforme et jamais comptée comme zéro constat.

Un rapport affirmant qu'un client n'a pas de dette technique alors que la vérité est que
personne n'a pu lire son environnement est la chose la plus dommageable que ce produit puisse
produire. Le rapport porte donc toujours la liste de ce qui n'a pas pu être vérifié, et vous
devriez lire cette liste à voix haute dans la salle.

## Le patrimoine de démonstration

Toute personne admise dans le produit peut ouvrir une mission appelée **Patrimoine de
démonstration**. C'est un patrimoine Power Platform synthétique à la forme d'un opérateur de
services publics de taille moyenne, et rien à l'intérieur ne provient d'un client.

Les constats qu'il contient ne sont pas inventés. Ils sont produits en faisant tourner sur le
patrimoine synthétique le même moteur de règles, le même calcul de score et le même
constructeur de backlog que sur un patrimoine réel. Il est en lecture seule pour tout le
monde : vous pouvez explorer chaque écran sans pouvoir le casser.

Utilisez-le pour apprendre le produit, et utilisez-le pour démontrer le produit avant qu'un
client ne vous ait donné quoi que ce soit.
