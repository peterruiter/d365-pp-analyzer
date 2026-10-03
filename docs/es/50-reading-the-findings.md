# Leer los hallazgos

## Categorías

Cada regla pertenece a una de once categorías. La pantalla de hallazgos agrupa por ellas.

| Categoría | Trata de |
|---|---|
| **Ciclo de vida y obsolescencia** | Componentes que Microsoft ha retirado, declarado obsoletos o dejado de desarrollar. |
| **Modernización** | Algo que funciona y para lo que ahora existe una respuesta mejor. |
| **Calidad de construcción** | Con qué cuidado se construyó lo que existe: gestión de errores, nomenclatura, estructura. |
| **Arquitectura** | Si la lógica está donde debe estar la lógica. La proporción de low code va aquí. |
| **ALM e higiene de soluciones** | Si esto puede moverse entre entornos sin que alguien tenga que acordarse de algo. |
| **Gobierno** | Propiedad, proliferación, componentes huérfanos, exposición a licencias. |
| **Rendimiento** | Cosas que son lentas ahora, o lo serán con volumen. |
| **Seguridad** | Privilegios, secretos y exposición. |
| **Operabilidad** | Si alguien se enteraría de que se ha roto. |
| **Componentes de IA** | Agentes, prompts y modelos: si lo construido sobre IA está fundamentado, al día y tiene dueño. |
| **Dynamics 365 Contact Center** | Secuencias de trabajo, colas y capacidad: si una conversación que llega alcanza a alguien que pueda atenderla. Solo se lee donde Contact Center está instalado, así que un entorno sin él no tiene nada aquí en lugar de una lista de comprobaciones que no pudieron ejecutarse. |

Modernización es la categoría que más quiere un cliente y la que más fácilmente se sobrevende,
así que cada regla que contiene lleva también una razón para dejar la cosa en paz.

## Gravedad

Crítica, alta, media, baja, informativa. La gravedad viene de la regla, no del componente, y
un manejador puede rebajarla por un motivo que registra en la evidencia.

Gravedad no es prioridad. Un hallazgo crítico sobre un componente que nadie usa es menos
urgente que uno medio en mitad del proceso diario, y el producto no pretende saber cuál es
cuál. Ese juicio es suyo, y el backlog es donde lo deja registrado.

## Evidencia

Cada hallazgo lleva consigo lo que lo activó. Esta es la parte que el cliente compra: una
afirmación que no puede comprobar delante de él es una afirmación que pierde la sala.

Abra un hallazgo y obtendrá los valores concretos: el número de acciones, el modo de
aislamiento, la URL encontrada, el número de bibliotecas del formulario. No una reformulación
de la regla.

## De dónde viene un hallazgo

Los hallazgos están marcados como **catálogo**, **checker** o **modelo**.

Un hallazgo del checker viene del Power Apps checker de Microsoft y lleva el identificador de
regla de Microsoft, de modo que puede buscarlo en su documentación. Esas reglas se mantienen
al día porque Microsoft las mantiene, no porque lo haga este producto.

Exactamente una regla la decide un modelo de lenguaje: si una descripción dice algo. Lee una
descripción cada vez, indica en cada hallazgo que lo ha juzgado un modelo, y lleva la frase
del propio modelo para que usted pueda rebatirla en voz alta. Todas las demás reglas son
mediciones. Sin un modelo configurado para la ejecución, esa regla se informa como no
evaluada, nunca como correcta.

## Componentes administrados

Un hallazgo contra un componente que llegó en una solución administrada se informa y nunca se
estima. Corregirlo es cosa de otro, y estimar trabajo sobre una solución que usted no entrega
es inventarse un número. Plantéeselo a quien la entrega.

## Sustituir un hallazgo

Puede sustituir la estimación de un hallazgo en un encargo. La sustitución se adhiere a la
clave estable del hallazgo —regla más componente— así que sobrevive a una nueva ejecución. Que
alguien renombre un flujo no deja huérfana la estimación que un taller tardó una hora en
acordar.

## La hoja de ruta

La hoja de ruta sitúa cada hallazgo en una cuadrícula: una fila para el tipo de trabajo y una
columna según sea de personas, proceso o tecnología, en bandas desde "despejar" hacia fuera.
Es una manera de enseñar a un cliente la forma del trabajo en lugar de una lista de 300
puntos.

Las reglas con hallazgos y sin posición en la hoja de ruta se informan al final de la fase de
puntuación, para que la cuadrícula no pueda dejar caer una categoría en silencio.
