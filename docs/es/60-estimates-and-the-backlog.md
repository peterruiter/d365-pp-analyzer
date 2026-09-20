# Estimaciones y el backlog

## De dónde sale un número

Cada estimación es un rango en horas, y cada estimación nombra cuál de las tres capas la
produjo:

- **Valor por defecto de banda** — la banda de estimación que la regla declara en el
  catálogo: trivial, pequeña, media, grande. Un escaneo rápido usa estas en todo momento, y
  eso es lo que lo hace rápido.
- **Modelo** — una estimación por hallazgo producida por un modelo de lenguaje, a partir de la
  evidencia del hallazgo y la complejidad del componente. Esto es lo que añade una ejecución
  de evaluación.
- **Sustitución del encargo** — un número que ha puesto una persona, y que gana a los otros
  dos.

La capa se muestra junto a cada estimación. Un cliente que pregunta "¿de dónde salen esas 40
horas?" hace una pregunta razonable y merece una respuesta concreta.

## Costes fijos

Algunos costes son por encargo y no por hallazgo: preparar un entorno, una pasada de
regresión, un traspaso. Vienen del contrato y se muestran aparte de la suma de los hallazgos,
porque sumarlos a un total por hallazgo hace que los números por hallazgo estén mal.

## La valoración de complejidad

Los componentes se valoran como simples, medios o complejos a partir de medidas declaradas en
el contrato: el número de acciones de un flujo, el número de controles de una aplicación, el
tamaño de un ensamblado. La valoración alimenta la estimación por modelo y el gráfico de
personalización.

Un componente cuya medida no se pudo alcanzar se valora como **no valorado** en lugar de
simple.

## El backlog

Un elemento de trabajo por hallazgo produciría cuatrocientas tareas que nadie refina. Uno por
regla perdería la evidencia, que es justo la parte que el cliente compra.

Así que el backlog se agrupa como lo habría agrupado a mano un consultor:

- un **epic** por categoría,
- una **feature** por regla con suficientes hallazgos para justificarla,
- una **story** por hallazgo que merezca ser nombrado,
- y los hallazgos triviales **agrupados** en una única tarea por regla.

Cada elemento lleva criterios de aceptación en forma given/when/then y un requisito de prueba,
ambos del contrato en lugar de escritos elemento a elemento. Se producen en el idioma de
backlog del encargo, que se configura por encargo y es distinto del idioma en el que usted lee
el producto.

## Publicar

Una publicación escribe elementos de trabajo en el proyecto de Azure DevOps configurado en el
encargo. No vuelve a analizar nada: publica los elementos elegidos y nada más.

Cada elemento de trabajo lleva una etiqueta determinista derivada del encargo y de la clave
del elemento, de modo que publicar dos veces actualiza los elementos existentes en lugar de
crear una segunda copia de todo.

Puede ejecutar primero una publicación en seco, que informa de lo que crearía y actualizaría
sin escribir nada.
