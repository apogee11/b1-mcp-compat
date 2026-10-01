# Guía de estilo: diagramas de arquitectura de nodos (estilo corporativo SAP)

Reglas para repetir, siempre igual, el diagrama de arquitectura que se hizo para el post de observabilidad del Web Client de SAP Business One. Cuando el usuario pida "un diagrama de nodos", "una arquitectura" o "una imagen como la del Web Client", se sigue esta guía sin reinterpretarla.

El resultado es **solo el diagrama**: el usuario captura la parte de los nodos y la sube al post. La explicación va en el texto del post, no en la imagen.

## 1. Qué es y para qué sirve

- Un diagrama de **arquitectura**: qué piezas hay, dentro de qué entorno viven y hacia dónde viajan los datos.
- Público: gente que **no sabe nada** del tema (por ejemplo, observabilidad). Cada decisión busca que no se pueda malinterpretar.
- Es **soporte visual** del texto. Si una cosa se explica mejor con una frase en el post, no va en el dibujo.
- Referente de estilo: la guía oficial de diagramas de soluciones de SAP (BTP Solution Diagram Guideline, basada en Fiori Horizon): https://sap.github.io/btp-solution-diagrams/ . Se copian de ahí las áreas de 16 px, el azul para lo de SAP, el gris para lo que no es de SAP y los colores de texto.

## 2. Reglas de contenido

1. **Solo arquitectura.** Nada de configuración, variables, puertos, versiones, leyendas ni comentarios.
2. **Nada de texto sobre las líneas.** Las flechas van sin etiquetas.
3. **Sin leyenda.** Por eso solo hay un tipo de línea (ver sección 5).
4. **Todo en inglés** (es para la comunidad de SAP). Nombres de producto tal cual: Web Client, OpenSearch, Grafana.
5. **Nombres cortos.** Cada caja lleva el nombre del componente. Si hace falta, una segunda línea pequeña con su función, de **una o dos palabras** (por ejemplo `Traces`, `Logs`, `Metrics`, `Visualization & Alerting`).
6. **Abreviaturas conocidas por el sector**, no inventadas: `OTel Collector` y no `Collector` a secas. Si el nombre largo no cabe, se usa la abreviatura oficial del producto.
7. **Se distingue lo que ya existe de lo que hay que instalar** (sección 4). Es lo primero que debe entender el lector.
8. **Sin usuario/navegador** en el dibujo salvo que se pida. Sin iconos, sin emojis, sin logos (no hay iconos oficiales disponibles y el estilo es de texto).
9. **Sin título dentro del lienzo.** El título lo pone el post.
10. Un **único servidor** por defecto. Varios servidores solo si se pide explícitamente.

## 3. Paleta (única permitida)

| Uso | Color | Hex |
|---|---|---|
| Lienzo (siempre blanco) | blanco | `#FFFFFF` |
| Fondo de la página que rodea el lienzo | gris azulado claro | `#EEF1F4` |
| Relleno de áreas (SAP y a instalar) | gris SAP | `#F5F6F7` |
| Relleno de cajas de componentes | blanco | `#FFFFFF` |
| Borde de áreas y cajas normales | gris azulado SAP | `#475E75` |
| Borde de la caja del Web Client | azul SAP | `#0070F2` |
| Texto principal y títulos | azul marino SAP | `#1D2D3E` |
| Texto secundario (subtexto) | gris azulado SAP | `#556B82` |
| Cables y puntas de flecha | azul marino SAP | `#1D2D3E` |
| Sombra de las cajas | azul marino al 20 % | `#1D2D3E` con opacidad 0.2 |

Reglas de color:
- **Solo estos colores.** No añadir acentos (amarillo, teal, índigo, rosa) salvo petición expresa.
- El **cable no puede tener el mismo color que el borde de las cajas** (se probó y "marea a la vista"). Por eso el cable es azul marino oscuro `#1D2D3E` y el borde de las cajas gris azulado `#475E75`.
- El **azul SAP `#0070F2`** se reserva para lo que pertenece al producto SAP (la caja del Web Client). Los cables no son azules: ya se probó y se descartó porque se confundía con esa caja.
- El **fondo del área SAP es gris `#F5F6F7`**, igual que el del área a instalar. No usar azul claro ni amarillo de relleno.
- Se probó un borde amarillo/dorado para el área SAP y se quitó. No hay un amarillo oficial de SAP Business One comprobado.

## 4. Estructura: tres niveles de áreas

Siempre se anida así, de fuera a dentro:

1. **Entorno** (por ejemplo `Windows Server`): rectángulo exterior, fondo blanco, borde `#475E75` continuo de 1.8 px.
2. **Dos áreas lado a lado dentro del entorno:**
   - **Lo que ya existe** (por ejemplo `SAP Business One`): área a la izquierda, borde `#475E75` **continuo** 1.8 px, fondo `#F5F6F7`. Dentro, la caja del Web Client.
   - **Lo que se instala** (rótulo `Components to install`): área a la derecha, borde `#475E75` **de puntos** (`stroke-dasharray="1 6"`, `stroke-linecap="round"`, 2.2 px), fondo `#F5F6F7`.
3. **Cajas de componentes** dentro de cada área.

El contraste **continuo frente a punteado** es lo que separa "ya viene" de "lo instalamos". No usar color para eso.

Alternancia: el entorno es blanco, las áreas grises, las cajas blancas (se alterna relleno y sin relleno, como pide la guía de SAP).

## 5. Flechas

**Una sola regla: la flecha apunta hacia donde viajan los datos.**

- Nunca apuntan hacia el sistema "que pregunta". Si Grafana lee de Prometheus, la flecha va **de Prometheus a Grafana** (los datos se mueven en ese sentido). Con la otra dirección, un lector nuevo cree que Grafana envía información.
- Un único tipo de línea: continua, `#1D2D3E`, 2 px. Sin discontinuas, sin colores por tipo de dato, sin etiquetas.
- Punta de flecha **pequeña**: marcador `markerWidth="4.5" markerHeight="4.5"` (con las puntas grandes parecía un dibujo animado).
- Trazado **ortogonal**: segmentos horizontales y verticales, nunca diagonales ni curvas.
- Cuando una caja alimenta a varias, o varias a una, las flechas salen o entran por **puntos distintos del borde, separados 28 px** como mínimo, para que las puntas no se amontonen. Para eso la caja del origen y la del destino son más altas (96 px) que las cajas simples (56 px).
- Las flechas no se cruzan. Si hay riesgo de cruce, se reordenan las cajas, no se usan puentes.
- Una flecha entre dos cajas pegadas (por ejemplo Jaeger a OpenSearch) mide solo el hueco entre ambas, sin etiqueta.

## 6. Cajas (nodos)

- Rectángulo con esquinas redondeadas `rx="10"` (las áreas usan `rx="16"`).
- Relleno blanco, borde `#475E75` de 1.8 px (el Web Client usa borde `#0070F2`).
- **Sombra ligera** en todas las cajas de componentes para que "sobresalgan un pelín": filtro `feDropShadow dx="0" dy="2" stdDeviation="2.5"`, color `#1D2D3E`, opacidad `0.2`. Las áreas y el entorno **no** llevan sombra.
- Altura estándar 56 px. Alta (96 px) para las cajas que reparten o reciben varias flechas (OTel Collector, Grafana). El Web Client tiene 68 px porque lleva dos líneas.
- Ancho: 150–192 px según el texto, con unos 20 px de margen interior.
- **Texto:** nombre en 16 px negrita `#1D2D3E`, centrado. Subtexto en 13 px normal `#556B82`, centrado, 19 px por debajo del nombre.
- Si la caja lleva solo el nombre, el texto va centrado en vertical (línea base a centro + 5 px). Con subtexto: nombre en centro − 3 px, subtexto en centro + 16 px.
- Si el nombre tiene dos líneas (por ejemplo `Web Client` / `Services`), las dos van en negrita 16 px, separadas 20 px.

## 7. Tipografía

- Familia: `"72", "72full", Arial, Helvetica, sans-serif` (la fuente de SAP si está instalada, Arial si no; no hay forma de cargar la 72 desde la web).
- Títulos de área: 16 px, negrita, `#1D2D3E`, alineados a la izquierda con el borde interior (24 px de margen).
- Solo dos tamaños en el dibujo (16 y 13 px). Sin monoespaciada, sin mayúsculas con espaciado, sin cursivas.

## 8. Geometría de referencia (lienzo y rejilla)

Lienzo `viewBox="0 0 1078 454"` (ancho 1078; alto depende de las filas).

- Entorno: `x=20 y=20 w=1038 h=414`, título en `x=44 y=50`.
- Márgenes: 24 px entre el borde de cada área y su contenido; 16 px entre el contenido y el borde inferior.
- Áreas a `y=72`, altura 338 (hasta 410). Hueco entre áreas: 36 px (284 a 320).
- Título de área en `y=104`.
- **Filas** de componentes dentro del área a instalar (centros verticales): `170`, `260`, `350`. Es decir, 90 px entre filas. Columnas de nodos: 344, 560, 840 (x de inicio).
- La **fila central** (centro y=260) es donde van el Web Client, el Collector, el almacén principal y Grafana, para que la flecha principal sea una recta horizontal.
- Los colectores de los lados (filas 170 y 350) se alcanzan con flechas en ángulo recto con el codo a medio camino (527 y 785).

Para un diagrama nuevo: mantener entorno, dos áreas y rejilla de filas cada 90 px; añadir filas o columnas ajustando el `viewBox` y las alturas de áreas y entorno (todo se mueve junto).

## 9. Plantilla completa (copiar y adaptar)

```html
<title>Mapa de Observabilidad B1</title>
<meta name="claude:viewport-fit-sides" content="cover">
<style>
:root { --page: #eef1f4; --ink: #1d2d3e; --font: "72", "72full", Arial, Helvetica, sans-serif; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --page: #0b131c; color-scheme: dark; } }
:root[data-theme="dark"] { --page: #0b131c; color-scheme: dark; }
body { background: var(--page); color: var(--ink); font-family: var(--font); margin: 0;
  padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); padding-block: 20px 40px; }
main { max-width: 1120px; margin-inline: auto; }
.scroll { overflow-x: auto; background: #fff; border-radius: 6px; }
.scroll svg { display: block; min-width: 820px; width: 100%; height: auto; }
.sr { position: absolute; left: -9999px; }

svg text { font-family: var(--font); fill: #1d2d3e; }
svg .area-t { font-size: 16px; font-weight: 700; }
svg .name { font-size: 16px; font-weight: 700; text-anchor: middle; }
svg .sub { font-size: 13px; fill: #556b82; text-anchor: middle; }
svg .ln { fill: none; stroke: #1d2d3e; stroke-width: 2; }
svg .head { fill: #1d2d3e; }
svg .node { filter: url(#sh); }
</style>

<main>
  <h1 class="sr">Título accesible del diagrama</h1>
  <div class="scroll">
    <svg viewBox="0 0 1078 454" role="img" aria-label="Descripción completa del flujo, en una frase.">
      <defs>
        <filter id="sh" x="-10%" y="-10%" width="120%" height="135%"><feDropShadow dx="0" dy="2" stdDeviation="2.5" flood-color="#1d2d3e" flood-opacity="0.2"/></filter>
        <marker id="ah" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="4.5" markerHeight="4.5" orient="auto"><path class="head" d="M0 0 L10 5 L0 10 z"/></marker>
      </defs>

      <!-- entorno -->
      <rect x="20" y="20" width="1038" height="414" rx="16" fill="#ffffff" stroke="#475e75" stroke-width="1.8"/>
      <text class="area-t" x="44" y="50">Windows Server</text>

      <!-- área de lo que ya existe (continua) -->
      <rect x="44" y="72" width="240" height="338" rx="16" fill="#f5f6f7" stroke="#475e75" stroke-width="1.8"/>
      <text class="area-t" x="68" y="104">SAP Business One</text>
      <rect class="node" x="68" y="226" width="192" height="68" rx="10" fill="#fff" stroke="#0070f2" stroke-width="1.8"/>
      <text class="name" x="164" y="255">Web Client</text>
      <text class="name" x="164" y="275">Services</text>

      <!-- área de lo que se instala (puntos) -->
      <rect x="320" y="72" width="714" height="338" rx="16" fill="#f5f6f7" stroke="#475e75" stroke-width="2.2" stroke-dasharray="1 6" stroke-linecap="round"/>
      <text class="area-t" x="344" y="104">Components to install</text>

      <rect class="node" x="344" y="212" width="150" height="96" rx="10" fill="#fff" stroke="#475e75" stroke-width="1.8"/>
      <text class="name" x="419" y="265">OTel Collector</text>

      <rect class="node" x="560" y="142" width="170" height="56" rx="10" fill="#fff" stroke="#475e75" stroke-width="1.8"/>
      <text class="name" x="645" y="167">Jaeger</text>
      <text class="sub" x="645" y="186">Traces</text>
      <rect class="node" x="560" y="232" width="170" height="56" rx="10" fill="#fff" stroke="#475e75" stroke-width="1.8"/>
      <text class="name" x="645" y="257">OpenSearch</text>
      <text class="sub" x="645" y="276">Logs</text>
      <rect class="node" x="560" y="322" width="170" height="56" rx="10" fill="#fff" stroke="#475e75" stroke-width="1.8"/>
      <text class="name" x="645" y="347">Prometheus</text>
      <text class="sub" x="645" y="366">Metrics</text>

      <rect class="node" x="840" y="212" width="170" height="96" rx="10" fill="#fff" stroke="#475e75" stroke-width="1.8"/>
      <text class="name" x="925" y="257">Grafana</text>
      <text class="sub" x="925" y="276">Visualization &amp; Alerting</text>

      <!-- flechas: apuntan hacia donde viajan los datos -->
      <line class="ln" x1="260" y1="260" x2="341" y2="260" marker-end="url(#ah)"/>
      <polyline class="ln" points="494,232 527,232 527,170 557,170" marker-end="url(#ah)"/>
      <line class="ln" x1="494" y1="260" x2="557" y2="260" marker-end="url(#ah)"/>
      <polyline class="ln" points="494,288 527,288 527,350 557,350" marker-end="url(#ah)"/>
      <line class="ln" x1="645" y1="198" x2="645" y2="229" marker-end="url(#ah)"/>
      <polyline class="ln" points="733,170 785,170 785,232 837,232" marker-end="url(#ah)"/>
      <line class="ln" x1="733" y1="260" x2="837" y2="260" marker-end="url(#ah)"/>
      <polyline class="ln" points="733,350 785,350 785,288 837,288" marker-end="url(#ah)"/>
    </svg>
  </div>
</main>
```

Notas de la plantilla:
- Las flechas terminan **3 px antes** del borde de la caja destino (por la punta). Por eso `x2` es 341, 557, 837, etc.
- `&amp;` en vez de `&` dentro del SVG.
- El `<title>` es el nombre del artifact, no se ve dentro del dibujo. El `<h1 class="sr">` es solo para lectores de pantalla.
- El selector de idioma que hubo al principio se quitó: el diagrama es solo en inglés.

## 10. Cómo se presenta y entrega

- Se publica como **Artifact HTML** con la plantilla de arriba: página con fondo `#EEF1F4`, y el diagrama dentro de un **lienzo blanco** con esquinas de 6 px. El lienzo es **siempre blanco** (aunque la página tenga modo oscuro) porque el usuario hace una captura.
- **Nada más en la página:** ni título visible, ni texto, ni botones, ni leyendas. El usuario recorta solo la parte de los nodos.
- Se mantiene el mismo archivo y la misma dirección en cada cambio, para no generar enlaces nuevos.
- Se avisa de lo que el diagrama da por decidido (por ejemplo, que Grafana hace alertas) cuando no esté cerrado en `docs/decisions.md`.

## 11. Flujo de trabajo para generar o cambiar uno

1. Confirmar las piezas y, **por cada una, si ya existe o se instala** (decide en qué área va).
2. Decidir el sentido de los datos de cada flecha (sección 5). Dibujar siempre en el sentido del dato.
3. Partir de la plantilla de la sección 9. Ajustar filas, columnas, alturas y `viewBox` (sección 8).
4. Escribir el HTML en el directorio temporal de la sesión y **renderizar una captura** para mirarla antes de publicar (en Windows: Chrome headless con `--screenshot`, leyendo el HTML como UTF-8 y envolviéndolo en `<!doctype html>` con `<meta charset="utf-8">`).
5. Comprobar en la captura:
   - texto que se sale de la caja;
   - puntas de flecha amontonadas (separar 28 px);
   - flechas que se cruzan o que tocan texto;
   - hueco vacío grande dentro de las áreas;
   - que lo continuo (existe) y lo punteado (se instala) se distinguen a simple vista.
6. Publicar con la herramienta Artifact usando la **misma ruta de archivo**. Si hay un cambio pequeño, editar el archivo y publicar; si un hook impide `edits`, usar Edit y publicar después.
7. Contar al usuario qué cambió y qué no se comprobó, en pocas líneas.

## 12. Decisiones del usuario que no se deben revertir

Cada una salió de una corrección explícita:

| Decisión | Motivo |
|---|---|
| Solo arquitectura; sin configuración, leyenda ni texto sobre las líneas | Es soporte visual; la explicación va en el post |
| Una sola regla de flechas (hacia donde van los datos) | Con flechas hacia el origen, Grafana parecía enviar datos a los almacenes |
| Cables de color distinto al del borde de las cajas | "Marea a la vista" |
| Cables no azules (el azul es del Web Client) | Se confundían con la caja del Web Client |
| Cables no teal | Se pidió un color "más corporativo" |
| Punta de flecha pequeña | Parecía un dibujo animado |
| Sin amarillo en el área SAP; fondo igual al otro área (`#F5F6F7`) | Pedido explícito |
| Área a instalar con borde de puntos | Distingue lo que se instala sin usar color |
| Sombra ligera en las cajas de componentes, no en las áreas | Que "sobresalgan un pelín" |
| `Windows Server` como rótulo del entorno | Más corto y claro que "Un único servidor Windows" |
| `OTel Collector` en lugar de `Collector` | Nombre reconocible para quien no conoce el producto |
| `Web Client` + `Services` en la caja | Que se entienda que son los servicios y no la web |
| Subtextos `Traces`, `Logs`, `Metrics`, `Visualization & Alerting` | Dicen en una palabra qué hace cada componente |
| Sin caja de usuario/navegador | Pedido explícito |
| Todo en inglés | Es para la comunidad de SAP |

## 13. Lo que no se hace

- Iconos, emojis, logos, ilustraciones, degradados, brillos.
- Más de un color de acento. Rellenos de color en las áreas.
- Etiquetas en las líneas, leyendas, numeración de pasos, títulos dentro del lienzo.
- Líneas diagonales o curvas.
- Fuentes monoespaciadas, mayúsculas con espaciado, cursivas.
- Sombras en áreas o en el entorno.
- Un tipo de línea distinto para otro significado (si hiciera falta, hay que pedirlo y añadir leyenda).
