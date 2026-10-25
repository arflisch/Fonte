"""Builds src/Fonte.Core/Nutrition/ciqual.csv from the ANSES CIQUAL 2020 XML tables.

Source: ANSES, Table de composition nutritionnelle des aliments Ciqual 2020 (https://ciqual.anses.fr),
Licence Ouverte / Open Licence (Etalab). Usage: python3 tools/ciqual_to_csv.py <folder with the unzipped XML>
"""
import csv, re, sys
from pathlib import Path

folder = Path(sys.argv[1])
out = Path(__file__).resolve().parent.parent / 'src' / 'Fonte.Core' / 'Nutrition' / 'ciqual.csv'

# Nutrient codes: energy (EU regulation 1169/2011, kcal), protein (Jones factor), carbohydrates, fat, sugars,
# fibre, salt.
CODES = {'328': 'kcal', '333': 'kcal_jones', '60000': 'alcohol', '34000': 'polyols', '65000': 'acids', '25000': 'protein', '31000': 'carbs', '40000': 'fat', '32000': 'sugars', '34100': 'fiber', '10004': 'salt'}
SKIPPED_GROUPS = {'11'}  # baby food


def text(block, tag):
    m = re.search(rf'<{tag}>(.*?)</{tag}>', block, re.S)
    return m.group(1).strip() if m else ''


def value(raw):
    """'12,5' → 12.5; 'traces' and '< 0,5' → 0 (below what can be measured); '-' → None (not analysed)."""
    raw = raw.strip()
    if raw in ('', '-'):
        return None
    if raw.lower() == 'traces' or raw.startswith('<'):
        return 0.0
    try:
        return float(raw.replace(',', '.'))
    except ValueError:
        return None


foods = {}
for block in re.findall(r'<ALIM>(.*?)</ALIM>', (folder / 'alim_2020_07_07.xml').read_text('cp1252'), re.S):
    code, group = text(block, 'alim_code'), text(block, 'alim_grp_code')
    if group in SKIPPED_GROUPS:
        continue
    foods[code] = {'code': code, 'group': group, 'fr': text(block, 'alim_nom_fr'), 'en': text(block, 'alim_nom_eng')}

for block in re.findall(r'<COMPO>(.*?)</COMPO>', (folder / 'compo_2020_07_07.xml').read_text('cp1252'), re.S):
    code, const = text(block, 'alim_code'), text(block, 'const_code')
    if code in foods and const in CODES:
        foods[code][CODES[const]] = value(text(block, 'teneur'))

# Energy factors of EU regulation 1169/2011 (kcal per gram), the ones CIQUAL itself uses.
FACTORS = {'protein': 4, 'carbs': 4, 'fat': 9, 'fiber': 2, 'alcohol': 7, 'polyols': 2.4, 'acids': 3}

for f in foods.values():
    # Some foods only have the energy computed with the Jones factor; others (apples, many fruits and
    # vegetables) only their macronutrients: the energy is then computed as CIQUAL does.
    if f.get('kcal') is None:
        f['kcal'] = f.get('kcal_jones')
    if f.get('kcal') is None and all(f.get(k) is not None for k in ('protein', 'carbs', 'fat')):
        f['kcal'] = round(sum((f.get(k) or 0) * factor for k, factor in FACTORS.items()), 1)
rows = [f for f in foods.values() if f.get('kcal') is not None]
rows.sort(key=lambda f: int(f['code']))
fmt = lambda v: '' if v is None else f'{v:g}'
with out.open('w', encoding='utf-8', newline='') as f:
    w = csv.writer(f, delimiter=';', lineterminator='\n')
    w.writerow(['code', 'group', 'name_fr', 'name_en', 'kcal', 'protein', 'carbs', 'fat', 'sugars', 'fiber', 'salt'])
    for r in rows:
        w.writerow([r['code'], r['group'], r['fr'], r['en']] + [fmt(r.get(k)) for k in ['kcal', 'protein', 'carbs', 'fat', 'sugars', 'fiber', 'salt']])
print(f'{len(rows)} foods written to {out} ({len(foods) - len(rows)} without energy skipped)')
