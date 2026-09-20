# Obtener acceso

## Iniciar sesión

Inicie sesión con su cuenta profesional de Microsoft. Cualquier cuenta profesional de Microsoft, en
cualquier organización: no necesita estar en el mismo tenant que el producto, y nadie tiene
que invitarle primero como invitado. Eso por sí solo no le da nada.

Es deliberado. Los clientes de una consultora no están en el tenant de la consultora, y un
cliente que quiera leer su propia evaluación no debería necesitar una segunda cuenta para
hacerlo.

Una herramienta que lee todo el patrimonio de soluciones de un cliente no es una herramienta
en la que cualquiera del tenant deba poder entrar, así que haber iniciado sesión y estar
admitido son dos cosas distintas. Alguien que ya tenga el producto debe admitirle.

Si inicia sesión y ve una página que dice que no ha sido admitido, ahí figura a quién debe
preguntar.

## Qué ve una vez admitido

Toda persona admitida puede leer siempre el **Patrimonio de demostración**, sin que nadie
tenga que concederlo. Todo lo demás lo ve porque alguien le ha dado un rol sobre ello.

## Los tres roles

Los roles son por encargo, no globales. Puede ser Administrador en un encargo y no tener nada
en otro.

| Rol | Puede |
|---|---|
| **Lector** | Leer todo lo del encargo: inventario, hallazgos, estimaciones, backlog, informes. |
| **Colaborador** | Todo lo que puede un Lector, más configurar conexiones, iniciar ejecuciones y ajustar estimaciones. |
| **Administrador** | Todo lo que puede un Colaborador, más dar acceso a otras personas y aprobar un backlog para su publicación. |

Aprobar un backlog es deliberadamente una acción de Administrador y deliberadamente distinta
de iniciar una ejecución. Es la puerta entre una evaluación y el proyecto de Azure DevOps de
alguien.

## Administradores globales

Un administrador global ve todos los encargos del producto y puede crear otros nuevos. Esto es
para quien opera el producto, no para quien lleva un encargo.

## Cambiar de encargo

El selector de encargo está en la parte superior de la barra lateral. Todo lo que hay debajo
—resumen, inventario, hallazgos, backlog, informes— se refiere al encargo que allí se nombra.
Si la navegación aparece atenuada, es porque aún no hay ningún encargo seleccionado.
