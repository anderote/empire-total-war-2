#!/usr/bin/env python3
"""Independent offline checks for authored region geometry and terrain corridors."""
import json
from collections import Counter
from pathlib import Path
import numpy as np
from shapely.geometry import Polygon,Point
from shapely.ops import unary_union
from shapely.strtree import STRtree
import build_europe as eu

folder=eu.OUT.parent/'WorldGeography'
world=json.loads((folder/'world.json').read_text());catalog=json.loads((folder/'campaign.json').read_text())
regions=catalog['regions'];assert 230<=len(regions)<=255
assert len(set(r['id'] for r in regions))==len(regions)
assert len(world['settlements'])==len(regions)
ids=np.fromfile(folder/'region-ids.bytes',np.uint8)
assert ids.size==4096*2048 and ids.max()==len(regions)
assert set(range(1,len(regions)+1)).issubset(set(ids))
polygons=[]
for region in regions:
    parts=[Polygon([(p['lon'],p['lat']) for p in polygon['rings'][0]['points']],
                   [[(p['lon'],p['lat']) for p in ring['points']] for ring in polygon['rings'][1:]]) for polygon in region['polygons']]
    cell=unary_union(parts);assert cell.is_valid,region['name']
    capital=region['capital'];assert cell.buffer(.04).covers(Point(capital['lon'],capital['lat'])),capital['name']
    polygons.append(cell)
# Areas should belong to one region, including in overlapping importer envelopes.
tree=STRtree(polygons)
for i,p in enumerate(polygons):
    for j in tree.query(p):
        if j<=i:continue
        area=p.intersection(polygons[j]).area
        assert area<.002,(regions[i]['name'],regions[j]['name'],area)
for kind in ('provinceBorders','regionBorders'):
    assert len(catalog[kind])>50
    assert all(len(line['points'])>=2 for line in catalog[kind])
lookup={t['id']:t for t in world['settlements']+world['waypoints']}
loaded={};generated=0
for route in world['routes']:
    assert route['fromId'] in lookup and route['toId'] in lookup
    if 'terrainAtlas' not in route:continue
    generated+=1;key=route['terrainAtlas']
    if key not in loaded:
        manifest='europe' if key=='EuropeGeography' else 'world' if key=='WorldGeography' else 'atlas'
        d=json.loads((eu.OUT.parent/key/(manifest+'.json')).read_text())
        dry=np.fromfile(eu.OUT.parent/key/'landmask.bytes',np.uint8).reshape(d['height'],d['width']);loaded[key]=(d,dry)
    d,dry=loaded[key]
    assert route['distanceKm']>0 and route['terrainCost']>0
    for p in route['points']:
        x=round((p['lon']-d['west'])/(d['east']-d['west'])*(d['width']-1));y=round((p['lat']-d['south'])/(d['north']-d['south'])*(d['height']-1))
        assert 0<=x<d['width'] and 0<=y<d['height'] and dry[y,x],route['name']
assert generated>=150
rivers=json.loads((folder/'riverways.json').read_text())['reaches']
assert len(rivers)>=15 and len(set(r['id'] for r in rivers))==len(rivers)
assert all(len(r['points'])>=2 and r['maxDraftMeters']>0 for r in rivers)
assert any(r['name']=='St. Lawrence' for r in rivers)
assert all(p['lat']>=24.1 for r in rivers if r['name']=='Nile' for p in r['points'])
assert not any(r['seaConnected'] for r in rivers if r['name']=='Congo')
report={'cities':len(regions),'provinces':len(catalog['provinces']),'newTerrainCorridors':generated,'surfaces':dict(Counter(r.get('surface','original') for r in world['routes'])),'navigableRiverReaches':len(rivers),'checks':'capital containment, valid nonoverlapping polygons, categorical ownership coverage, every generated road sample on its source dry-land mask, river limits'}
output=eu.ROOT/'Artifacts'/'CampaignValidation';output.mkdir(parents=True,exist_ok=True)
(output/'geography-verification.json').write_text(json.dumps(report,indent=2))
print('CAMPAIGN_EXPANSION_DATA_VERIFIED',json.dumps(report))
