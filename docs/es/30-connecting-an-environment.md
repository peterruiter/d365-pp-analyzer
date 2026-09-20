# Conectar un entorno

Hay tres formas de entrar, y alcanzan cantidades distintas. Elija la que realmente pueda
conseguir aprobada esta semana, no la que más alcance.

## Usuario de aplicación, solo lectura

Un registro de aplicación de Entra añadido al entorno como usuario de aplicación con un rol de
seguridad de solo lectura. Este es el modo para todo lo que deba ser repetible.

**Alcanza:** la API web de Dataverse por completo, una solución exportada por completo, el
Power Apps checker por completo, y la evidencia de ejecución parcialmente.

**Necesita:** un id. de tenant, un id. de cliente, una URL de entorno y un secreto de cliente.
El secreto va a Key Vault y se referencia por su nombre. Nunca se guarda ninguna credencial en
la base de datos del producto.

La definición del rol se entrega con el producto, para que el equipo de seguridad de un
cliente revise un archivo en lugar de una descripción.

El historial de ejecución de flujos y los registros de seguimiento de complementos requieren
más que un lector simple. Donde falta ese privilegio, el producto nombra las reglas que
quedaron sin evaluar en lugar de informarlas como limpias.

## Usuario delegado

Usted, con la sesión iniciada, leyendo lo que ya puede leer.

**Alcanza:** todo, incluida la evidencia de ejecución, en la medida en que su propia cuenta
llegue.

**Necesita:** que no se cree nada. Es la forma más rápida de ver algo real.

El inconveniente es que no es repetible: una ejecución programada no puede tomar prestada su
sesión, y los resultados dependen de sus privilegios en lugar de un rol declarado.

## Archivo de solución

Una solución no administrada exportada, descomprimida y leída sin conexión.

**Alcanza:** el archivo de solución por completo y el checker por completo. Los metadatos
parcialmente. La ejecución en absoluto.

**Necesita:** un `.zip` y nada más. Sin conexión, sin credencial, sin revisión de seguridad.

Esto no es un recurso degradado. Es el modo que pasa una revisión de seguridad en la primera
semana mientras la solicitud de una entidad de servicio espera en una cola, y para una
evaluación de calidad y deuda alcanza la mayor parte de lo que importa. Lo que no puede ver es
el uso: qué flujos se ejecutan realmente, qué workflows están dormidos, con qué frecuencia
falla algo.

## Rol del entorno

Sea cual sea el modo, usted declara **para qué** es el entorno: desarrollo, prueba, aceptación
o producción.

Varias reglas solo se disparan contra producción. Equivocarse hace que un informe sea
alarmista o inútil, así que esto se declara en lugar de deducirse del nombre del entorno. Si
lo deja como desconocido, las reglas propias de producción se informan a sí mismas como no
evaluadas: no suponen nada en silencio.

## Probar una conexión

Pruébela antes de ejecutar nada. La prueba informa de la identidad con la que se autenticó y
de lo que pudo alcanzar, por fuente de evidencia.

Una conexión que tiene éxito con demasiados pocos privilegios falla más tarde de una forma que
se parece exactamente a un patrimonio vacío. El panel de alcance está ahí para que se entere
ahora.
