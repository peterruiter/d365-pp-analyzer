# Informes y exportaciones

De una ejecución salen dos documentos. Ambos se producen a partir de los resultados guardados
de esa ejecución, así que un informe descargado un mes después dice lo que dijo la ejecución,
no lo que dicen las reglas hoy.

## El libro de hallazgos

Un `.xlsx` con una hoja por cada vista de la misma ejecución:

- **Resumen** — los recuentos, la proporción, los totales.
- **Hallazgos** — cada hallazgo, con su regla, gravedad, categoría, componente, evidencia y estimación.
- **Inventario** — cada componente, con tipo, solución, factura, ciclo de vida y dominio.
- **No evaluado** — cada regla que no pudo ejecutarse, y por qué.
- **Backlog** — los elementos de trabajo, con sus criterios de aceptación.

Este es el que el arquitecto de un cliente usará de verdad. Es deliberadamente sobrio: sin
celdas combinadas, sin imágenes, con filtros en cada fila de encabezado, para que se pueda
ordenar y tabular en lugar de admirar.

## El informe de evaluación

Un `.pdf` escrito para que lo lea alguien que no abrirá el libro. Lleva el relato: qué se
leyó, qué no se leyó, qué se encontró, cuánto costaría y la hoja de ruta.

La sección "no evaluado" no es un anexo. Está cerca del principio, porque un lector que llega
a los números sin ella ha sido inducido a error.

## El idioma en el que sale un informe

Los informes se producen en el **idioma de informe** del encargo, que se configura en el
encargo y es distinto del idioma en el que usted lee el producto.

Una consultora neerlandesa puede leer una interfaz en neerlandés y producir un informe en
inglés para un equipo deslocalizado. Ese es el caso normal, no un caso límite, y por eso son
dos ajustes.

El backlog tiene además su propio idioma, porque quienes refinan un backlog con frecuencia no
son quienes leen el informe.

## Descargar

Los informes se listan en la pantalla Informes para cada ejecución. Se generan cuando usted
los pide en lugar de guardarse, para que un informe siempre corresponda a la ejecución que
nombra.

El nombre del archivo lleva el nombre del cliente y la fecha de la ejecución, porque una
carpeta de archivos todos llamados `report.pdf` es una carpeta que nadie puede usar.
