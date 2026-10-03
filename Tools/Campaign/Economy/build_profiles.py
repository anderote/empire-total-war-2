#!/usr/bin/env python3
"""Build transparent prototype economy estimates; not historical demographic data."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
CATALOG = ROOT / 'Assets/Scripts/Campaign/Geography/Resources/WorldGeography/campaign.json'
OUTPUT = ROOT / 'Assets/Resources/CampaignEconomyProfiles.json'
TEMPLATES = {
    'agrarian': (100000, 125, 300),
    'port': (150000, 125, 600),
    'river_valley': (200000, 270, 450),
    'urban': (250000, 180, 850),
    'arid': (60000, 55, 220),
    'frontier': (20000, 20, 100),
}
OVERRIDES = {
    'Cairo': 'river_valley', 'Calcutta': 'river_valley', 'Benares': 'river_valley',
    'Nanjing': 'river_valley', 'Hangzhou': 'river_valley', 'London': 'urban',
    'Paris': 'urban', 'Beijing': 'urban', 'Peking': 'urban',
    'Sitka': 'frontier', 'Kodiak': 'frontier', 'Unalaska': 'frontier',
    'Irkutsk': 'frontier', 'Yakutsk': 'frontier', 'Okhotsk': 'frontier',
    'Petropavlovsk': 'frontier',
}

def build():
    catalog = json.loads(CATALOG.read_text())
    rows = []
    for region in catalog['regions']:
        town = region['capital']
        kind = 'port' if town['port'] else 'agrarian'
        basis = 'Port flag or agrarian fallback; requires historical review.'
        if abs(town['lat']) > 60:
            kind, basis = 'frontier', 'High latitude prototype; requires regional review.'
        if 15 < town['lat'] < 35 and -20 < town['lon'] < 60:
            kind, basis = 'arid', 'Coarse dry-zone prototype; local irrigation not inferred.'
        if town['name'] in OVERRIDES:
            kind, basis = OVERRIDES[town['name']], 'Explicit named prototype override; uncalibrated.'
        population, food, wealth = TEMPLATES[kind]
        rows.append(dict(regionId=region['id'], template=kind, confidence='prototype estimate',
                         basis=basis, population=population, foodCapacity=food, taxableWealth=wealth))
    OUTPUT.write_text(json.dumps(dict(schemaVersion=1,
        notes='Uncalibrated game estimates, not historical statistics. One turn provisionally represents six months. One food unit supports 1000 residents for a turn. Population counts the whole region, not just its city. Fixed value-added prices; no imports or population growth yet.',
        regions=rows), indent=2) + '\n')
    print(f'Wrote {len(rows)} explicit regional profiles to {OUTPUT.relative_to(ROOT)}')

if __name__ == '__main__':
    build()
