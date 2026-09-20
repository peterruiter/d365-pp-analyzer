# Connecter un environnement

Il y a trois façons d'entrer, et elles atteignent des choses différentes. Choisissez celle que
vous pouvez réellement faire approuver cette semaine, pas celle qui atteint le plus.

## Utilisateur d'application, lecture seule

Une inscription d'application Entra ajoutée à l'environnement comme utilisateur d'application
avec un rôle de sécurité en lecture seule. C'est le mode pour tout ce qui doit être
reproductible.

**Atteint :** l'API web Dataverse en totalité, une solution exportée en totalité, le Power
Apps checker en totalité, et les preuves d'exécution partiellement.

**Nécessite :** un identifiant de tenant, un identifiant client, une URL d'environnement et un
secret client. Le secret va dans Key Vault et est référencé par son nom. Aucune information
d'identification n'est jamais stockée dans la base du produit.

La définition du rôle est livrée avec le produit, pour que l'équipe sécurité d'un client
examine un fichier plutôt qu'une description.

L'historique d'exécution des flux et les journaux de trace des plug-ins demandent plus qu'un
simple lecteur. Là où ce privilège manque, le produit nomme les règles restées non évaluées
plutôt que de les signaler comme conformes.

## Utilisateur délégué

Vous, connecté, lisant ce que vous pouvez déjà lire.

**Atteint :** tout, y compris les preuves d'exécution, dans la mesure où votre propre compte y
accède.

**Nécessite :** rien à créer. C'est le moyen le plus rapide de voir quelque chose de réel.

L'inconvénient est que ce n'est pas reproductible : une exécution planifiée ne peut pas
emprunter votre session, et les résultats dépendent de vos privilèges plutôt que d'un rôle
déclaré.

## Fichier de solution

Une solution non gérée exportée, décompressée et lue hors ligne.

**Atteint :** le fichier de solution en totalité et le checker en totalité. Les métadonnées
partiellement. L'exécution pas du tout.

**Nécessite :** un `.zip` et rien d'autre. Aucune connexion, aucune information
d'identification, aucun examen de sécurité.

Ce n'est pas un repli dégradé. C'est le mode qui passe un examen de sécurité dès la première
semaine pendant que la demande de principal de service attend dans une file, et pour une
évaluation de qualité et de dette il atteint l'essentiel. Ce qu'il ne peut pas voir, c'est
l'usage : quels flux tournent réellement, quels workflows dorment, à quelle fréquence quelque
chose échoue.

## Rôle de l'environnement

Quel que soit le mode utilisé, vous déclarez à **quoi sert** l'environnement : développement,
test, recette ou production.

Plusieurs règles ne se déclenchent que contre la production. Se tromper rend un rapport soit
alarmiste soit inutile, c'est pourquoi cela se déclare au lieu de se déduire du nom de
l'environnement. Si vous le laissez sur inconnu, les règles propres à la production se
signalent elles-mêmes comme non évaluées : elles ne supposent rien en silence.

## Tester une connexion

Testez-la avant de lancer quoi que ce soit. Le test indique l'identité avec laquelle il s'est
authentifié et ce qu'il a pu atteindre, par source de preuve.

Une connexion qui réussit avec trop peu de privilèges échoue plus tard d'une manière qui
ressemble exactement à un patrimoine vide. Le panneau de portée est là pour que vous
l'appreniez maintenant.
