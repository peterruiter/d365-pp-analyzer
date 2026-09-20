# Ejecutar un análisis

## Los cuatro modos

| Modo | Hace | Tarda |
|---|---|---|
| **Escaneo rápido** | Lee el patrimonio y aplica cada regla, usando bandas de estimación en lugar de estimaciones individuales. | Minutos. |
| **Evaluación** | Un escaneo rápido, más una estimación por hallazgo y un backlog refinado. | Más, y llama a un modelo de lenguaje. |
| **Comparar** | Una evaluación medida contra una ejecución anterior, para poder mostrar qué ha cambiado. | Como una evaluación. |
| **Publicar** | Escribe los elementos de backlog elegidos en Azure DevOps. No lee nada ni vuelve a analizar nada. | Minutos. |

Empiece con un escaneo rápido. Es el modo que se puede ejecutar sin riesgo en una conversación
comercial, y responde a la mayoría de las preguntas de una primera reunión.

## Elegir qué se lee

Una ejecución no lee todo lo que puede encontrar, y no lo decide por usted.

A los pocos segundos de empezar se conecta, enumera cada solución del entorno y entonces **se
detiene y le pregunta**. Obtiene la lista, con el editor, la versión y el número de
componentes de cada solución, y marca las que el informe debe cubrir.

Las soluciones propias de Microsoft empiezan desmarcadas. La mayor parte de lo que contiene un
entorno de Dataverse lo puso allí Microsoft, leerlas es con diferencia la parte más larga de
una ejecución, y el informe que sale habla de Dynamics en lugar del trabajo por el que su
cliente pagó a alguien. No se oculta nada: cada solución encontrada figura en la lista y queda
registrada, la marque o no, porque un informe que cubre cuatro de diecinueve soluciones y uno
que cubre las diecinueve son idénticos en la portada.

En la misma pantalla se pueden desactivar cuatro comprobaciones:

| Comprobación | Cuesta | Si la desactiva |
|---|---|---|
| **Exportar las soluciones elegidas** | Alrededor de un minuto por solución, así que una docena es un cuarto de hora. No se escribe nada: una exportación es una lectura. | Las catorce reglas que leen un archivo de solución, y las tres que necesitan el checker, se informan como no evaluadas. Una conexión en vivo no es entonces más rica que una lectura de metadatos. |
| **Solution checker** | Con diferencia la parte más lenta de una ejecución. | Toda regla cuya evidencia sea un resultado del checker se informa como no evaluada, nunca como correcta. |
| **Estimaciones por modelo** | Minutos, y un punto de conexión de modelo. | Las estimaciones vuelven a los valores por defecto de banda, que es lo que hace un escaneo rápido. El informe indica cuáles usó. |
| **Salud del entorno** | Segundos. | La ejecución continúa con lo que la credencial alcance por casualidad. |

No marcar nada está permitido y produce un informe que dice que este es un patrimonio no
leído en lugar de uno limpio. La pantalla le avisa antes de que continúe.

## Las fases

Una ejecución pasa por diez fases, y la pantalla de ejecución muestra dónde está, cuánto tardó
cada una y en cuál se encuentra ahora:

1. **Comprobar conexiones** — se autentica e informa con qué identidad lo hizo.
2. **Elegir soluciones** — enumera lo que contiene el entorno y luego le espera.
3. **Extraer** — lee las soluciones que ha elegido.
4. **Checker** — envía la solución al Power Apps checker y espera.
5. **Resolver** — une componentes entre sí, para que las reglas puedan preguntar qué apunta a qué.
6. **Analizar** — aplica cada regla del catálogo.
7. **Estimar** — pone un rango de horas en cada hallazgo. Se omite en un escaneo rápido.
8. **Puntuar** — calcula la proporción, el gráfico de personalización y la hoja de ruta.
9. **Backlog** — convierte hallazgos en elementos de trabajo que alguien refinaría de verdad.
10. **Publicar** — escribe en Azure DevOps o Jira. Solo en modo publicación, y solo los elementos que alguien eligió.

La fase en curso dice qué está haciendo mientras lo hace — qué solución está exportando, cuál
tiene el checker, por dónde va en el entorno — y la pantalla se actualiza sola. Una fase que
lee el patrimonio de un cliente tarda minutos, y sin eso una lenta y una parada se ven
exactamente igual.

Una fase puede volver a ejecutarse por separado desde la pantalla de ejecución. Al hacerlo
también se descarta toda fase posterior, cosa que el botón le dice antes de hacerlo: una
ejecución cuyos hallazgos vienen de una extracción y cuya puntuación viene de otra parecería
perfectamente sana y estaría mal.

Una fase también puede terminar **parcial**, lo que significa que hizo su trabajo y algo
dentro de ella no pudo hacerse. El caso más habitual es analizar: algunas reglas no pudieron
ejecutarse. Eso no es un fallo y la ejecución continúa.

## Mientras se ejecuta

Puede marcharse. La ejecución la lleva a cabo un worker en segundo plano, no su navegador, y
sigue si cierra la pestaña. Vuelva a la pantalla Ejecuciones y abra la ejecución para retomar
la cronología donde está.

## Eliminar una ejecución

Un encargo acumula una ejecución por cada intento, y la lista es lo que recorre cuando busca
la que tiene en mente. Un Administrador puede eliminar una ejecución y todo lo que produjo.

Dos cosas que conviene saber. Una ejecución sobre la que se construyó otra posterior no se
puede eliminar, porque esa ejecución posterior quedaría describiendo una evaluación que ya no
existe. Y los elementos de trabajo ya publicados en Azure DevOps o Jira se quedan exactamente
donde están: eliminar la ejecución elimina el registro que este producto tiene de haberlos
escrito, y nada en el tablero del cliente.

Las pantallas de hallazgos leen siempre una ejecución, nunca un montón, así que eliminar
ejecuciones antiguas es ordenar y no corregir.

## Cuando termina

Empiece por el **Resumen**. Le da los recuentos, la proporción, el reparto por gravedad y la
estimación total.

Después lea la lista **no evaluado** antes que cualquier otra cosa, para saber qué cubren y
qué no cubren los números que hay debajo.
