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


def main():
    plantilla = (WEB / "plantilla.html").read_text(encoding="utf-8")
    config = json.loads((WEB / "config.json").read_text(encoding="utf-8"))
    textos = {p.stem: json.loads(p.read_text(encoding="utf-8"))
              for p in sorted((WEB / "textos").glob("*.json"))}
    idiomas = [l for l in ORDEN if l in textos] + sorted(set(textos) - set(ORDEN))

    claves = set(re.findall(r"{{(\w+)}}", plantilla)) - {"alternativas", "opciones_idioma", "version", "donar"}
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
