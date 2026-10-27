"""Checks the lengths of the App Store texts in app-store-listing.md against Apple's limits."""
import pathlib
import re

LIMITS = {
    "Nom": 30, "Name": 30, "Naam": 30,
    "Sous-titre": 30, "Subtitle": 30, "Ondertitel": 30,
    "Texte promotionnel": 170, "Promotional text": 170, "Promotietekst": 170,
    "Mots-clés": 100, "Keywords": 100, "Trefwoorden": 100,
    "Description": 4000, "Beschrijving": 4000,
}

text = (pathlib.Path(__file__).parent / "app-store-listing.md").read_text(encoding="utf-8")
ok = True
for section in text.split("\n---\n"):
    fields = re.split(r"\n\*\*([^*]+)\*\* \(\d+\) ?:", section)
    for name, body in zip(fields[1::2], fields[2::2]):
        value = re.split(r"\n\*\*", body)[0].strip()
        limit = LIMITS.get(name.strip())
        if limit is None:
            continue
        status = "OK " if len(value) <= limit else "TROP LONG"
        ok &= len(value) <= limit
        print(f"{status} {name.strip():20} {len(value):5} / {limit}")
print("Tout est dans les limites." if ok else "Certains textes dépassent.")
