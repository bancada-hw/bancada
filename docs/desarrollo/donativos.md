# Donativos

Bancada recibe donativos con GitHub Sponsors: <https://github.com/sponsors/bancada-hw>. El botón
**Sponsor** del repositorio sale de `.github/FUNDING.yml` y el botón de la web, del enlace `donar` de
`docs/web/config.json` (con `""` desaparece). Después de cambiar `config.json`, ejecuta
`python3 herramientas/generar-web.py`.

## Cómo se dio de alta (por si hay que repetirlo)

1. Verificación en dos pasos activada en la cuenta de GitHub (obligatoria).
2. *Settings → Sponsors* de la organización bancada-hw → **Get started**.
3. En Stripe: país España y tipo de empresa **Vendedor**, que es como Stripe llama a la persona física.
   «Empresa» es para sociedades con CIF y «Sin fines de lucro» para asociaciones registradas.
4. Datos personales tal como figuran en el DNI, teléfono móvil (verificación por SMS) e IBAN. Stripe los
   guarda para verificar la identidad; no se publican.
5. Formulario fiscal W-8BEN en GitHub: NIF en «Foreign tax identifying number» y España como país.
6. Perfil público con descripción y niveles de donativo.

## Otras plataformas

Se pueden añadir a la vez en `FUNDING.yml` (GitHub las enseña todas en el botón **Sponsor**):
`ko_fi: bancada` (Ko-fi, donativos sueltos con PayPal) o `liberapay: bancada` (Liberapay, periódicos).

## Hacienda

Cuando lleguen donativos regulares, conviene consultarlo con una gestoría.
