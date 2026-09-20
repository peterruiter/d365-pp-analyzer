# Idiomas y ajustes

## Tres ajustes de idioma, a propósito

| Ajuste | Controla | Se configura en |
|---|---|---|
| **Idioma de la interfaz** | Las pantallas que está leyendo ahora. | Usted. |
| **Idioma del informe** | El libro y el PDF. | El encargo. |
| **Idioma del backlog** | Títulos, descripciones y criterios de aceptación de los elementos de trabajo. | El encargo. |

Están separados porque en la práctica difieren de verdad. Una consultora neerlandesa lee una
interfaz en neerlandés, escribe un informe en inglés para la TI corporativa de un cliente, y
produce un backlog en español para el equipo que hará el trabajo.

Hay seis idiomas disponibles en todas partes: inglés, neerlandés, alemán, francés, español e
italiano.

## Dónde se guarda su preferencia

En su registro de usuario en la base de datos del producto, no en su navegador. Su idioma y su
tema claro u oscuro le siguen a una segunda máquina, y no se guarda nada en el almacenamiento
local.

## Qué está traducido

La interfaz, los nombres y explicaciones de las reglas, el texto de los hallazgos, las
etiquetas del inventario y el texto del backlog están traducidos a los seis idiomas.

Las guías escritas que está leyendo también están traducidas, a esos mismos seis idiomas.

Una guía recurre al inglés guía por guía y no producto por producto, así que si alguna vez se
añade una página más rápido de lo que se traduce, verá esa página en inglés y todo lo demás en
su propio idioma, en lugar de que todo el conjunto retroceda.

## Tema

Claro y oscuro, siguiendo a su máquina salvo que usted decida otra cosa. La elección se guarda
en su usuario, igual que el idioma.

## Administración

Los administradores globales disponen de una pantalla de Administración con:

- **Usuarios** — quién está admitido, quién es administrador global y quién los admitió.
- **Encargos** — cada encargo del producto, y quién tiene qué rol en cada uno.
- **Salud del sistema** — si la API, la base de datos, el worker y Key Vault son accesibles, y
  qué versión ejecuta cada uno.

La pantalla de salud del sistema es el primer sitio donde mirar cuando algo no funciona.
Distingue "el worker no está en marcha" de "el worker ejecuta una imagen antigua", que son dos
tardes muy distintas.
