#!/usr/bin/env python3
"""Authored 1800 faction scenario and geographic region-ID raster (not historical borders)."""
import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Assets/Scripts/Campaign/Geography/Resources/WorldGeography'
CONFIG=Path(__file__).with_name('factions_1800.json')
WIDTH,HEIGHT=4096,2048

def apply(campaign):
    config=json.loads(CONFIG.read_text())
    campaign['factions']=config['factions']
    campaign['scenarioName']='1800 opening scenario'
    valid={f['id'] for f in config['factions']}
    assignments=config['provinceOwners'];overrides=config['cityOwners']
    for r in campaign['regions']:
        owner=overrides.get(r['capital']['name'],assignments.get(r['provinceId']))
        if owner not in valid:raise ValueError('Missing faction assignment: '+r['capital']['name'])
        r['owner']=owner
    campaign['politicalRasterWidth']=WIDTH;campaign['politicalRasterHeight']=HEIGHT
    campaign['politicsNotes']=config['notes']
    return campaign

def raster(campaign):
    if len(campaign['regions'])>255:raise ValueError('Region-ID byte raster capacity exceeded')
    image=Image.new('L',(WIDTH,HEIGHT));draw=ImageDraw.Draw(image)
    def coords(ring):return [((p['lon']+180)/360*WIDTH-.5,(90-p['lat'])/180*HEIGHT-.5) for p in ring['points']]
    for i,r in enumerate(campaign['regions'],1):
        # Each polygon's holes are excluded; no ownership is painted across its lakes/seas.
        for p in r['polygons']:
            if not p['rings']:continue
            draw.polygon(coords(p['rings'][0]),fill=i)
            for hole in p['rings'][1:]:draw.polygon(coords(hole),fill=0)
    np.flipud(np.asarray(image)).copy().tofile(OUT/'region-ids.bytes')

def main():
    path=OUT/'campaign.json';campaign=apply(json.loads(path.read_text()));raster(campaign)
    path.write_text(json.dumps(campaign,separators=(',',':'),ensure_ascii=False))
    print(f"POLITICS: {len(campaign['factions'])} factions, {len(campaign['regions'])} owned regions; raster {WIDTH}x{HEIGHT}")
if __name__=='__main__':main()
