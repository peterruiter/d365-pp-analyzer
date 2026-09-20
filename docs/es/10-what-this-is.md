# Qué es esto

Una evaluación de solo lectura de un patrimonio de Power Platform. Lo apunta a un entorno o le
entrega un archivo de solución exportado, lee lo que hay, y produce cuatro cosas:

1. **Un inventario.** Cada componente, por tipo, por solución, por dominio.
2. **Una proporción de low code.** Un número, con su definición al lado, porque el número se
   cita sin ella.
3. **Hallazgos.** Lo que está obsoleto, mal construido, sin gobierno, lento o expuesto, cada
   uno con la evidencia que lo activó.
4. **Una estimación.** Un rango de horas por hallazgo, con su justificación, sumando a un
   total cuya aritmética puede comprobar delante de un cliente.

## Lo que no hará

**Nunca escribe en un entorno de Power Platform.** En ningún modo, y no hay ajuste que lo
cambie. Puede ejecutar un descubrimiento en una primera conversación sin comité de cambios, y
ese es precisamente el objetivo.

Lo único que escribe en algún sitio son elementos de trabajo en Azure DevOps, y solo los que
alguien ha elegido en la pantalla del backlog. Hay una ejecución en seco que muestra
exactamente qué aterrizaría y no escribe nada.

**No sustituye al Power Apps checker.** Llama al checker e incorpora los resultados bajo los
identificadores de regla de Microsoft. Las reglas que declara por su cuenta son justamente las
que el checker no tiene: posición en el ciclo de vida, deuda repartida entre componentes,
proliferación e higiene de soluciones.

**No es una evaluación de licencias.** Informa de dónde se usa un conector premium y ahí se
detiene. No puede ver lo que tiene el tenant, y adivinar sería peor que callar.

**No es una prueba de intrusión.** Las reglas de seguridad tratan de privilegios, secretos en
definiciones y escritura a nivel de organización. No evalúan si se puede entrar por la fuerza
en el patrimonio.

## Qué significa "no evaluado"

Es la idea más importante del producto, así que tiene su propia sección.

Cada regla declara la evidencia que necesita. Si la forma en que se ha conectado no alcanza
esa evidencia, la regla se informa como **no evaluada**, por su nombre, con el motivo. Nunca
se informa como correcta y nunca se cuenta como cero hallazgos.

Un informe que dice que un cliente no tiene deuda técnica cuando la verdad es que nadie pudo
leer su entorno es lo más dañino que este producto puede producir. Por eso el informe siempre
lleva una lista de lo que no se pudo comprobar, y esa lista conviene leerla en voz alta en la
sala.

## El patrimonio de demostración

Cualquier persona admitida en el producto puede abrir un encargo llamado **Patrimonio de
demostración**. Es un patrimonio de Power Platform sintético con la forma de una empresa de
servicios públicos de tamaño medio, y nada de lo que contiene procede de un cliente.

Los hallazgos que contiene no están inventados. Se producen ejecutando sobre el patrimonio
sintético el mismo motor de reglas, el mismo cálculo de puntuación y el mismo constructor de
backlog que se ejecutan sobre uno real. Es de solo lectura para todos, así que puede explorar
cada pantalla sin poder romperla.

Úselo para aprender el producto, y úselo para demostrar el producto antes de que un cliente le
haya dado nada.
