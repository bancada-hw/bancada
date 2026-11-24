#!/usr/bin/env python3
"""Genera la web de Bancada (GitHub Pages, carpeta docs/).

    python3 herramientas/generar-web.py

Lee docs/web/plantilla.html, docs/web/textos/<idioma>.json y docs/web/config.json
y escribe docs/<idioma>/index.html para cada idioma, más docs/index.html, que
redirige al idioma del navegador (inglés si no hay ninguno de los disponibles).

Para activar el botón de donativos, pon el enlace en config.json:
    { "donar": "https://github.com/sponsors/bancada-hw" }
Con "donar": "" el botón no aparece.
"""
import json, pathlib, re, sys

RAIZ = pathlib.Path(__file__).resolve().parent.parent
DOCS = RAIZ / "docs"
WEB = DOCS / "web"
ORDEN = ["es", "en", "pt", "fr", "de", "it"]
VERSION = "0.2"
URL = "https://bancada-hw.github.io/bancada"


# --- Guía de uso: de docs/guia/<idioma>.md a HTML (solo lo que usa la guía) ---
REPO = "https://github.com/bancada-hw/bancada/blob/main/"


def escapar(t):
    return t.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def enlace(url, idiomas):
    if re.match(r"^[a-z]+://|^#|^mailto:", url):
        return url
    m = re.match(r"^([a-z]{2})\.md(#.*)?$", url)
    if m and m.group(1) in idiomas:
        return f"../../{m.group(1)}/guia/" + (m.group(2) or "")
    if url.startswith("../capturas/"):
        return "../../capturas/" + url[len("../capturas/"):]
    limpio = url
    while limpio.startswith("../"):
        limpio = limpio[3:]
    return REPO + ("docs/" + limpio if not url.startswith("../../") else limpio)


def en_linea(t, idiomas):
    codigos = []
    t = re.sub(r"`([^`]+)`", lambda m: codigos.append(m.group(1)) or f"\x00{len(codigos) - 1}\x00", t)
    t = escapar(t)
    t = re.sub(r"!\[([^\]]*)\]\(([^)]+)\)",
               lambda m: f'<img src="{enlace(m.group(2), idiomas)}" alt="{m.group(1)}" width="1200" height="780" loading="lazy">', t)
    t = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", lambda m: f'<a href="{enlace(m.group(2), idiomas)}">{m.group(1)}</a>', t)
    t = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", t)
    t = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<em>\1</em>", t)
    t = re.sub(r"<(https?://[^>\s]+)>", r'<a href="\1">\1</a>', t)
    t = t.replace("&lt;https://", "<https://")
    t = re.sub(r"&lt;(https?://[^&\s]+)&gt;", r'<a href="\1">\1</a>', t)
    return re.sub(r"\x00(\d+)\x00", lambda m: f"<code>{escapar(codigos[int(m.group(1))])}</code>", t)


def ancla(texto, usadas):
    a = re.sub(r"[^\w\s-]", "", texto.lower()).strip()
    a = re.sub(r"\s+", "-", a) or "seccion"
    base, n = a, 2
    while a in usadas:
        a, n = f"{base}-{n}", n + 1
    usadas.add(a)
    return a


def guia_html(md, idiomas):
    lineas = md.splitlines()
    html, indice, usadas = [], [], set()
    i = 0
    while i < len(lineas):
        l = lineas[i]
        if not l.strip():
            i += 1
            continue
        # barra de idiomas y enlace de vuelta del Markdown: en la web sobran
        if re.search(r"\]\([a-z]{2}\.md\)", l) and "·" in l or l.startswith("←"):
            i += 1
            continue
        m = re.match(r"^(#{1,3})\s+(.*)$", l)
        if m:
            n, texto = len(m.group(1)), m.group(2).strip()
            if n == 1:
                html.append(f"<h1>{en_linea(texto, idiomas)}</h1>")
            else:
                a = ancla(texto, usadas)
                html.append(f'<h{n} id="{a}">{en_linea(texto, idiomas)}</h{n}>')
                indice.append((n, a, re.sub(r"<[^>]+>", "", en_linea(texto, idiomas))))
            i += 1
            continue
        if l.startswith("|"):
            filas = []
            while i < len(lineas) and lineas[i].startswith("|"):
                filas.append([c.strip() for c in lineas[i].strip().strip("|").split("|")])
                i += 1
            cab, cuerpo = filas[0], [f for f in filas[1:] if not all(re.match(r"^:?-+:?$", c) for c in f)]
            html.append("<table><thead><tr>" + "".join(f"<th>{en_linea(c, idiomas)}</th>" for c in cab) +
                        "</tr></thead><tbody>" + "".join("<tr>" + "".join(f"<td>{en_linea(c, idiomas)}</td>" for c in f) + "</tr>" for f in cuerpo) +
                        "</tbody></table>")
            continue
        if re.match(r"^(\s*)([-*]|\d+\.)\s", l):
            html.append(lista(lineas, i, idiomas))
            i = lista.fin
            continue
        if re.match(r"^!\[[^\]]*\]\([^)]+\)\s*$", l):
            html.append(en_linea(l.strip(), idiomas))
            i += 1
            continue
        parrafo = []
        while i < len(lineas) and lineas[i].strip() and not re.match(r"^(#|\||\s*([-*]|\d+\.)\s|!\[)", lineas[i]):
            parrafo.append(lineas[i].strip())
            i += 1
        html.append(f"<p>{en_linea(' '.join(parrafo), idiomas)}</p>")
    ind = "<ul>" + "".join(f'<li class="{"sub" if n == 3 else ""}"><a href="#{a}">{t}</a></li>' for n, a, t in indice) + "</ul>"
    return "\n".join(html), ind


def lista(lineas, i, idiomas):
    sangria = len(lineas[i]) - len(lineas[i].lstrip())
    ordenada = bool(re.match(r"^\s*\d+\.", lineas[i]))
    items = []
    while i < len(lineas):
        l = lineas[i]
        if not l.strip():
            break
        s = len(l) - len(l.lstrip())
        m = re.match(r"^\s*([-*]|\d+\.)\s+(.*)$", l)
        if m and s == sangria:
            items.append([m.group(2).strip(), ""])
            i += 1
        elif m and s > sangria:
            items[-1][1] += lista(lineas, i, idiomas)
            i = lista.fin
        elif s > sangria and items:
            items[-1][0] += " " + l.strip()
            i += 1
        else:
            break
    lista.fin = i
    etiqueta = "ol" if ordenada else "ul"
    return f"<{etiqueta}>" + "".join(f"<li>{en_linea(t, idiomas)}{sub}</li>" for t, sub in items) + f"</{etiqueta}>"


def main():
    plantilla = (WEB / "plantilla.html").read_text(encoding="utf-8")
    config = json.loads((WEB / "config.json").read_text(encoding="utf-8"))
    textos = {p.stem: json.loads(p.read_text(encoding="utf-8"))
              for p in sorted((WEB / "textos").glob("*.json"))}
    idiomas = [l for l in ORDEN if l in textos] + sorted(set(textos) - set(ORDEN))

    claves = set(re.findall(r"{{(\w+)}}", plantilla)) - {"alternativas", "opciones_idioma", "version", "donar"}
    claves |= {"guia_titulo", "firma_titulo", "pie_firma"}
    error = False
    for l in idiomas:
        falta = claves - set(textos[l])
        if falta:
            print(f"{l}.json: faltan {sorted(falta)}", file=sys.stderr)
            error = True
    if error:
        sys.exit(1)

    donar = config.get("donar", "").strip()
    base = plantilla if donar else re.sub(r"<!--donar-->.*?<!--/donar-->\n?", "", plantilla, flags=re.S)
    base = base.replace("<!--donar-->", "").replace("<!--/donar-->", "")

    alternativas = "\n".join(f'<link rel="alternate" hreflang="{l}" href="{URL}/{l}/">' for l in idiomas)
    alternativas += f'\n<link rel="alternate" hreflang="x-default" href="{URL}/">'

    plantilla_pagina = (WEB / "plantilla-guia.html").read_text(encoding="utf-8")
    # Páginas de texto sacadas de docs/<pagina>/<idioma>.md: la guía de uso y la política de firma de código.
    paginas = {"guia": "guia_titulo", "firma": "firma_titulo"}
    alternativas_pagina = {p: "\n".join(f'<link rel="alternate" hreflang="{l}" href="{URL}/{l}/{p}/">' for l in idiomas)
                           for p in paginas}

    for l in idiomas:
        t = dict(textos[l])
        t["alternativas"] = alternativas
        t["opciones_idioma"] = "".join(
            f'<option value="{o}"{" selected" if o == l else ""}>{textos[o]["nombre"]}</option>' for o in idiomas)
        t["version"] = VERSION
        t["donar"] = donar
        html = re.sub(r"{{(\w+)}}", lambda m: t[m.group(1)], base)
        destino = DOCS / l / "index.html"
        destino.parent.mkdir(exist_ok=True)
        destino.write_text(html, encoding="utf-8")
        print(f"docs/{l}/index.html")

        for pagina, clave_titulo in paginas.items():
            md = DOCS / pagina / f"{l}.md"
            if not md.exists():
                continue
            contenido, indice = guia_html(md.read_text(encoding="utf-8"), idiomas)
            t["contenido"], t["indice"] = contenido, indice
            t["pagina"] = pagina
            t["pagina_titulo"] = t[clave_titulo]
            t["pagina_github_url"] = f"{REPO}docs/{pagina}/{l}.md"
            t["alternativas_pagina"] = alternativas_pagina[pagina]
            t["actual_guia"] = ' aria-current="page"' if pagina == "guia" else ""
            html = re.sub(r"{{(\w+)}}", lambda m: t[m.group(1)], plantilla_pagina)
            destino = DOCS / l / pagina / "index.html"
            destino.parent.mkdir(exist_ok=True)
            destino.write_text(html, encoding="utf-8")
            print(f"docs/{l}/{pagina}/index.html")

    lista = json.dumps(idiomas)
    (DOCS / "index.html").write_text(f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Bancada</title>
<link rel="canonical" href="{URL}/en/">
{alternativas}
<link rel="icon" href="assets/bancada.svg" type="image/svg+xml">
<meta name="theme-color" content="#0B241C">
<script>
(function () {{
  var hay = {lista}, pref = navigator.languages || [navigator.language || 'en'];
  for (var i = 0; i < pref.length; i++) {{
    var l = (pref[i] || '').slice(0, 2).toLowerCase();
    if (hay.indexOf(l) >= 0) {{ location.replace(l + '/' + location.hash); return; }}
  }}
  location.replace('en/' + location.hash);
}})();
</script>
<noscript><meta http-equiv="refresh" content="0; url=en/"></noscript>
<style>body{{background:#0B241C;color:#EEEBE2;font-family:system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;margin:0}}a{{color:#F0B27A}}</style>
</head>
<body><p>{" · ".join(f'<a href="{l}/">{textos[l]["nombre"]}</a>' for l in idiomas)}</p></body>
</html>
""", encoding="utf-8")
    print("docs/index.html")
    (DOCS / ".nojekyll").write_text("", encoding="utf-8")


if __name__ == "__main__":
    main()
