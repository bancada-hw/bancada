# Activar los donativos

Todo está preparado para que activarlos sea cambiar dos archivos. Mientras no lo hagas, ni la web ni
GitHub muestran ningún botón de donar.

## Opción recomendada: GitHub Sponsors

Sin comisiones para donativos de personas, el dinero llega por Stripe a tu cuenta bancaria y España está
admitida. El botón **Sponsor** aparece arriba en el repositorio.

1. Entra en <https://github.com/sponsors/bancada-hw> (o en *Settings → Sponsors* de la organización
   bancada-hw) y pulsa **Join the waitlist / Get started**.
2. Rellena el perfil: descripción del proyecto, niveles (por ejemplo 2 €, 5 € y 15 € al mes, y donativo
   único libre) y la cuenta de Stripe Connect con tu IBAN y tus datos fiscales.
3. GitHub revisa la solicitud (suele tardar unos días).
4. Cuando esté aprobada:
   - En `.github/FUNDING.yml`, quita el `#` de la línea `github: bancada-hw`.
   - En `docs/web/config.json`, pon `{ "donar": "https://github.com/sponsors/bancada-hw" }`.
   - Ejecuta `python3 herramientas/generar-web.py`, haz commit y sube los cambios.

## Alternativas o complementos

- **Ko-fi** (<https://ko-fi.com>): donativos sueltos con PayPal o tarjeta, sin comisión en el plan
  gratuito. Crea la página `bancada`, quita el `#` de `ko_fi: bancada` y, si quieres que sea el botón de la
  web, usa `https://ko-fi.com/bancada` en `config.json`.
- **Liberapay** (<https://liberapay.com>): donativos periódicos, sin ánimo de lucro, pensado para
  software libre. Igual: `liberapay: bancada`.

Puedes tener varias a la vez en `FUNDING.yml`; GitHub las muestra todas en el botón **Sponsor**. La web
enseña un solo botón, el del enlace de `config.json`.

## Hacienda

Los donativos son ingresos: en España cuentan como rendimientos de actividad económica o, si son
ocasionales y sin contraprestación, pueden tributar como donación. Consulta con una gestoría cuando
empiecen a llegar cantidades regulares.
