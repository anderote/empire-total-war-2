#!/usr/bin/env python3
"""Build offline campaign geography from real data. Never infer period borders/roads.

Run with the adjacent venv after installing requirements.txt. Cached downloads are
outside Assets. All output coordinates are WGS84; raster rows run south to north.
"""
import concurrent.futures as futures
import hashlib
import heapq
import json
import math
from pathlib import Path
import time
import subprocess
import zipfile

import numpy as np
from PIL import Image
from shapely import contains_xy
from shapely.geometry import box, shape, LineString
from shapely.ops import unary_union

ROOT = Path(__file__).resolve().parents[3]
CACHE = Path(__file__).parent / 'cache'
OUT = ROOT / 'Assets/Scripts/Campaign/Geography/Resources/EuropeGeography'
WEST, SOUTH, EAST, NORTH = -18., 18., 50., 72.
SIZE = 1537
ZOOM = 6
SOURCES = []


def fetch(url, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        for attempt in range(4):
            try:
                tmp = path.with_suffix(path.suffix + '.part')
                subprocess.run(['curl','--globoff','--fail','--location','--silent','--show-error',
                                '--max-time','60','--output',str(tmp),url],check=True)
                tmp.replace(path)
                break
            except Exception:
                if attempt == 3:
                    raise
                time.sleep(attempt + 1)
    return path


def natural(name):
    url = f'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_{name}.geojson'
    p = fetch(url, CACHE / f'{name}.geojson')
    SOURCES.append({'url': url, 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()})
    return json.loads(p.read_text())['features']


def tilexy(lon, lat):
    n = 2 ** ZOOM
    return (lon + 180) / 360 * n, (1 - np.arcsinh(np.tan(np.radians(lat))) / np.pi) / 2 * n


def elevations():
    x0, y1 = tilexy(WEST, SOUTH)
    x1, y0 = tilexy(EAST, NORTH)
    xmin, xmax, ymin, ymax = math.floor(x0), math.floor(x1), math.floor(y0), math.floor(y1)
    jobs = [(x, y) for y in range(ymin, ymax + 1) for x in range(xmin, xmax + 1)]
    def load(t):
        x, y = t
        source_x=x%(2**ZOOM)
        url = f'https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{ZOOM}/{source_x}/{y}.png'
        p = fetch(url, CACHE / 'tiles' / str(ZOOM) / str(source_x) / f'{y}.png')
        rgb = np.asarray(Image.open(p).convert('RGB'), dtype=np.float32)
        return x, y, rgb[:, :, 0] * 256 + rgb[:, :, 1] + rgb[:, :, 2] / 256 - 32768
    mosaic = np.empty(((ymax-ymin+1)*256, (xmax-xmin+1)*256), dtype=np.float32)
    with futures.ThreadPoolExecutor(max_workers=8) as pool:
        for i, (x, y, h) in enumerate(pool.map(load, jobs)):
            mosaic[(y-ymin)*256:(y-ymin+1)*256, (x-xmin)*256:(x-xmin+1)*256] = h
            if i % 25 == 0:
                print(f'Elevation tiles {i+1}/{len(jobs)}', flush=True)
    lon = np.linspace(WEST, EAST, SIZE)
    lat = np.linspace(SOUTH, NORTH, SIZE)
    tx, _ = tilexy(lon, 0)
    _, ty = tilexy(0, lat)
    # Tile samples are pixel centers, not pixel corners.
    px, py = (tx-xmin)*256-.5, (ty-ymin)*256-.5
    ix, iy = np.floor(px).astype(int), np.floor(py).astype(int)
    fx, fy = px-ix, py-iy
    h = ((1-fy[:,None])*((1-fx)*mosaic[iy[:,None],ix] + fx*mosaic[iy[:,None],ix+1]) +
         fy[:,None]*((1-fx)*mosaic[iy[:,None]+1,ix] + fx*mosaic[iy[:,None]+1,ix+1]))
    SOURCES.append({'url': 'https://registry.opendata.aws/terrain-tiles/', 'zoom': ZOOM,
                    'tileCount': len(jobs), 'encoding': 'R*256+G+B/256-32768'})
    return h, lon, lat


def paths(features, clip):
    result = []
    def emit(g, name):
        if g.is_empty:
            return
        if g.geom_type in ('LineString', 'LinearRing'):
            coords = list(g.simplify(.008).coords)
            if len(coords) > 1:
                result.append({'name': name or '', 'points': [{'lon': round(x,5), 'lat': round(y,5)} for x,y,*_ in coords]})
        elif g.geom_type == 'Polygon':
            emit(g.exterior, name)
        elif hasattr(g, 'geoms'):
            for part in g.geoms:
                emit(part, name)
    for f in features:
        geom = shape(f['geometry'])
        if geom.intersects(clip):
            emit(geom.intersection(clip), f['properties'].get('name', ''))
    return result


# Authored selection of historic settlement locations, not modern population rankings.
# NE coordinates are used where matched; UK secondary locations are explicit supplements.
TOWNS = '''London|capital,port; Edinburgh|capital,port; Glasgow|port; Aberdeen|port; Inverness|port; Perth|town; Dundee|port; Stirling|town; Newcastle|port; York|town; Leeds|town; Manchester|town; Liverpool|port; Birmingham|town; Nottingham|town; Norwich|town; Cambridge|town; Oxford|town; Bristol|port; Plymouth|port; Portsmouth|port; Dover|port; Exeter|town; Shrewsbury|town; Cardiff|port; Swansea|port; Dublin|capital,port; Cork|port; Belfast|port; Galway|port; Limerick|port; Paris|capital; Lyon|town; Marseille|port; Bordeaux|port; Nantes|port; Brest|port; Rouen|port; Lille|town; Strasbourg|town; Toulouse|town; Dijon|town; Calais|port; Brussels|capital; Antwerp|port; Amsterdam|capital,port; Rotterdam|port; Luxembourg|town; Cologne|town; Frankfurt|town; Hanover|town; Hamburg|port; Bremen|port; Berlin|capital; Dresden|town; Leipzig|town; Munich|town; Stuttgart|town; Nuremberg|town; Vienna|capital; Salzburg|town; Innsbruck|town; Prague|capital; Bern|capital; Geneva|town; Zurich|town; Turin|town; Milan|town; Genoa|port; Venice|port; Bologna|town; Florence|town; Rome|capital; Naples|port; Palermo|port; Cagliari|port; Madrid|capital; Barcelona|port; Valencia|port; Seville|town; Cadiz|port; Lisbon|capital,port; Porto|port; Bilbao|port; Zaragoza|town; Copenhagen|capital,port; Stockholm|capital,port; Oslo|capital,port; Bergen|port; Trondheim|port; Helsinki|port; Warsaw|capital; Krakow|town; Gdansk|port; Riga|port; Tallinn|port; Vilnius|town; Minsk|town; Kyiv|town; Lviv|town; Budapest|capital; Zagreb|town; Belgrade|town; Bucharest|town; Sofia|town; Sarajevo|town; Athens|capital; Thessaloniki|port; Istanbul|capital,port; Saint Petersburg|capital,port; Moscow|capital; Smolensk|town; Odessa|port; Valletta|port; Ajaccio|port'''
SUPPLEMENTS = {
    'Stirling':(-3.94,56.12), 'Perth':(-3.43,56.40), 'Dundee':(-2.97,56.46),
    'York':(-1.08,53.96), 'Cambridge':(.12,52.20), 'Oxford':(-1.26,51.75),
    'Shrewsbury':(-2.75,52.71), 'Dover':(1.31,51.13), 'Portsmouth':(-1.09,50.80),
    'Calais':(1.86,50.95), 'Limerick':(-8.63,52.66), 'Galway':(-9.05,53.27),
}
ALIASES = {'Newcastle':'Newcastle upon Tyne','Cologne':'Köln','Hanover':'Hannover',
           'Nuremberg':'Nürnberg','Turin':'Torino','Genoa':'Genova','Florence':'Firenze',
           'Seville':'Sevilla','Cadiz':'Cádiz','Gdansk':'Gdańsk','Kyiv':'Kiev',
           'Saint Petersburg':'Saint Petersburg'}
TOWNS += '; Messina|port; Catania|port; Siracusa|port; Ragusa|town; Marsala|port; Trapani|port; Agrigento|town; Caltanissetta|town; Enna|town; Noto|town; Sciacca|port; Tunis|capital,port; Sfax|port; Sousse|port; Bizerte|port; Kairouan|town; Gabes|port; Tripoli|capital,port; Benghazi|port; Derna|port; Tobruk|port; Alexandria|port; Cairo|capital; Algiers|capital,port; Oran|port; Constantine|town; Annaba|port; Tangier|port; Tetouan|town; Fez|town; Meknes|town; Rabat|port; Marrakesh|town'


def towns(features):
    result = []
    gazetteer_path = fetch('https://download.geonames.org/export/dump/cities15000.zip',CACHE/'cities15000.zip')
    with zipfile.ZipFile(gazetteer_path) as z:
        gazetteer = [line.split('\t') for line in z.read('cities15000.txt').decode('utf8').splitlines()]
    for item in TOWNS.replace('\n','').split(';'):
        name, tags = item.strip().split('|')
        match = next((f for f in features if any(f['properties'].get(k) in (name,ALIASES.get(name))
                     and f['properties'].get(k) is not None for k in ('NAME','NAMEASCII','NAME_EN','name'))
                     and WEST <= f['geometry']['coordinates'][0] <= EAST
                     and SOUTH <= f['geometry']['coordinates'][1] <= NORTH), None)
        if name in SUPPLEMENTS:
            x,y = SUPPLEMENTS[name]
            source = 'authored coordinate supplement'
        elif match:
            x,y = match['geometry']['coordinates'][:2]
            source = 'Natural Earth populated places'
        else:
            candidate=next((row for row in gazetteer if name in (row[1],row[2])
                            and WEST<float(row[5])<EAST and SOUTH<float(row[4])<NORTH),None)
            if candidate is None:
                raise ValueError(f'No coordinates for {name}; add explicit source supplement')
            x,y=float(candidate[5]),float(candidate[4]);source='GeoNames CC BY 4.0'
        result.append({'id': name.lower().replace(' ','-'), 'name':name, 'lon':x, 'lat':y,
                       'kind':'capital' if 'capital' in tags else 'town', 'port':'port' in tags, 'source':source})
    return result


# Provisional strategic corridors. A* follows actual terrain, not historical road surveys.
CORRIDORS = '''London-Dover;London-Portsmouth;London-Oxford;London-Cambridge;Cambridge-Norwich;Oxford-Bristol;Bristol-Exeter;Exeter-Plymouth;Bristol-Cardiff;Cardiff-Swansea;Oxford-Birmingham;Birmingham-Shrewsbury;Birmingham-Nottingham;Birmingham-Manchester;Manchester-Liverpool;Manchester-Leeds;Leeds-York;York-Newcastle;Newcastle-Edinburgh;Edinburgh-Stirling;Stirling-Glasgow;Stirling-Perth;Perth-Dundee;Dundee-Aberdeen;Perth-Inverness;Dublin-Belfast;Dublin-Galway;Dublin-Cork;Cork-Limerick;Limerick-Galway;Paris-Rouen;Rouen-Calais;Paris-Lille;Lille-Brussels;Brussels-Antwerp;Antwerp-Rotterdam;Rotterdam-Amsterdam;Paris-Strasbourg;Paris-Dijon;Dijon-Lyon;Lyon-Marseille;Paris-Nantes;Nantes-Brest;Nantes-Bordeaux;Bordeaux-Toulouse;Toulouse-Marseille;Bordeaux-Madrid;Madrid-Lisbon;Lisbon-Porto;Madrid-Seville;Seville-Cadiz;Madrid-Valencia;Valencia-Barcelona;Madrid-Zaragoza;Zaragoza-Barcelona;Barcelona-Toulouse;Madrid-Bilbao;Brussels-Cologne;Cologne-Frankfurt;Frankfurt-Strasbourg;Frankfurt-Stuttgart;Stuttgart-Munich;Frankfurt-Nuremberg;Nuremberg-Munich;Cologne-Hanover;Hanover-Hamburg;Hamburg-Bremen;Hanover-Berlin;Berlin-Leipzig;Leipzig-Dresden;Dresden-Prague;Prague-Vienna;Vienna-Salzburg;Salzburg-Munich;Salzburg-Innsbruck;Innsbruck-Milan;Milan-Turin;Turin-Lyon;Milan-Genoa;Milan-Venice;Venice-Bologna;Bologna-Florence;Florence-Rome;Rome-Naples;Geneva-Lyon;Geneva-Bern;Bern-Zurich;Zurich-Munich;Berlin-Warsaw;Warsaw-Krakow;Warsaw-Gdansk;Krakow-Lviv;Lviv-Kyiv;Kyiv-Minsk;Minsk-Vilnius;Vilnius-Riga;Riga-Tallinn;Tallinn-Saint Petersburg;Saint Petersburg-Moscow;Moscow-Smolensk;Smolensk-Minsk;Vienna-Budapest;Budapest-Zagreb;Zagreb-Sarajevo;Sarajevo-Belgrade;Budapest-Belgrade;Belgrade-Bucharest;Bucharest-Sofia;Sofia-Thessaloniki;Thessaloniki-Athens;Sofia-Istanbul;Bucharest-Odessa;Odessa-Kyiv;Oslo-Bergen;Oslo-Trondheim;Oslo-Stockholm'''
FERRIES = [('Dover','Calais'),('Portsmouth','Brest'),('Liverpool','Dublin'),('Glasgow','Belfast'),
           ('Edinburgh','Amsterdam'),('Hamburg','Copenhagen'),('Copenhagen','Stockholm'),
           ('Stockholm','Helsinki'),('Helsinki','Saint Petersburg'),('Naples','Palermo'),
           ('Genoa','Ajaccio'),('Ajaccio','Cagliari'),('Marseille','Barcelona'),('Athens','Istanbul')]
CORRIDORS += ';Palermo-Trapani;Trapani-Marsala;Marsala-Sciacca;Sciacca-Agrigento;Agrigento-Caltanissetta;Caltanissetta-Enna;Enna-Catania;Catania-Messina;Catania-Siracusa;Siracusa-Noto;Noto-Ragusa;Ragusa-Agrigento;Palermo-Enna;Palermo-Messina;Tunis-Bizerte;Tunis-Sousse;Sousse-Kairouan;Sousse-Sfax;Sfax-Gabes;Gabes-Tripoli;Tripoli-Benghazi;Benghazi-Derna;Derna-Tobruk;Tobruk-Alexandria;Alexandria-Cairo;Algiers-Oran;Algiers-Constantine;Constantine-Annaba;Annaba-Tunis;Tangier-Tetouan;Tetouan-Fez;Fez-Meknes;Meknes-Rabat;Rabat-Marrakesh'
FERRIES += [('Palermo','Tunis'),('Messina','Naples'),('Valletta','Siracusa'),('Cadiz','Tangier'),('Marseille','Algiers')]


def routes(settlements, h, land, lakes):
    stride = 3
    dry = (land & ~lakes)[::stride,::stride]
    elevation = np.maximum(h[::stride,::stride],0)
    n = len(dry)
    lookup = {t['name']:t for t in settlements}
    def cell(t):
        x = round((t['lon']-WEST)/(EAST-WEST)*(n-1))
        y = round((t['lat']-SOUTH)/(NORTH-SOUTH)*(n-1))
        candidates = [(dx*dx+dy*dy,y+dy,x+dx) for dy in range(-5,6) for dx in range(-5,6)
                      if 0<=y+dy<n and 0<=x+dx<n and dry[y+dy,x+dx]]
        if not candidates:
            raise ValueError('Settlement lacks dry terrain: '+t['name'])
        _,y,x = min(candidates)
        return y,x
    def search(start, goal):
        q = [(0,start)]; prev = {}; score = {start:0}
        while q:
            _,cur = heapq.heappop(q)
            if cur == goal:
                p = [cur]
                while cur in prev:
                    cur = prev[cur]; p.append(cur)
                return p[::-1],score[goal]
            y,x = cur
            for dy,dx in ((0,1),(0,-1),(1,0),(-1,0),(1,1),(-1,-1),(1,-1),(-1,1)):
                yy,xx = y+dy,x+dx
                if not (0<=yy<n and 0<=xx<n and dry[yy,xx]):
                    continue
                if dx and dy and not (dry[y,xx] and dry[yy,x]):
                    continue
                rise = abs(elevation[y,x]-elevation[yy,xx])
                # Strong elevation and slope penalty creates navigable valley/pass corridors.
                step = math.hypot(dx,dy)*(1+elevation[yy,xx]/1400+rise/180)
                cand = score[cur]+step
                nxt = yy,xx
                if cand < score.get(nxt,float('inf')):
                    score[nxt] = cand; prev[nxt] = cur
                    heapq.heappush(q,(cand+math.hypot(goal[0]-yy,goal[1]-xx),nxt))
        return None,None
    result = []
    for entry in CORRIDORS.split(';'):
        a,b = entry.split('-'); ta,tb = lookup[a],lookup[b]
        path,cost = search(cell(ta),cell(tb))
        if path is None:
            print('No land route:',a,b,flush=True); continue
        points = [(WEST+x/(n-1)*(EAST-WEST),SOUTH+y/(n-1)*(NORTH-SOUTH)) for y,x in path]
        # Preserve A* samples: simplifying could cut across water or mountain barriers.
        km = sum(math.hypot((q[0]-p[0])*111.32*math.cos(math.radians((p[1]+q[1])/2)),(q[1]-p[1])*111.32)
                 for p,q in zip(points,points[1:]))
        result.append({'name':a+' — '+b,'fromId':ta['id'],'toId':tb['id'],'mode':'land',
                       'distanceKm':round(km), 'terrainCost':round(cost,2),
                       'provisional':True,'points':[{'lon':round(x,5),'lat':round(y,5)} for x,y in points]})
    # Maritime lines are display-only connections, not validated sea navigation routes.
    for a,b in FERRIES:
        ta,tb = lookup[a],lookup[b]
        result.append({'name':a+' — '+b,'fromId':ta['id'],'toId':tb['id'],'mode':'sea',
                       'provisional':True,'distanceKm':0,'terrainCost':0,
                       'points':[{'lon':ta['lon'],'lat':ta['lat']},{'lon':tb['lon'],'lat':tb['lat']}]})
    return result


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    clip = box(WEST,SOUTH,EAST,NORTH)
    h,lon,lat = elevations()
    land_features = natural('land')
    land_geom = unary_union([shape(f['geometry']).intersection(clip) for f in land_features
                            if shape(f['geometry']).intersects(clip)])
    land = contains_xy(land_geom,lon[None,:],lat[:,None])
    lake_features = natural('lakes')
    lake_geom = unary_union([shape(f['geometry']).intersection(clip) for f in lake_features
                            if shape(f['geometry']).intersects(clip)])
    lakes = contains_xy(lake_geom,lon[None,:],lat[:,None])
    settlements = towns(natural('populated_places'))
    road_data = routes(settlements,h,land,lakes)
    height = np.clip(np.rint(h),-32768,32767).astype('<i2')
    (OUT/'elevation.bytes').write_bytes(height.tobytes())
    (OUT/'landmask.bytes').write_bytes((land & ~lakes).astype('uint8').tobytes())
    # Hypsometric tint from measured height, with subdued, original cartographic styling.
    stops = [0,150,450,900,1600,2400,3500,5000]
    colors = np.array([[107,130,88],[126,146,94],[151,157,112],[162,153,119],
                       [152,139,117],[165,155,140],[211,208,195],[240,239,225]])
    rgb = np.stack([np.interp(np.maximum(h,0),stops,colors[:,i]) for i in range(3)],axis=-1)
    dy,dx = np.gradient(np.maximum(h,0))
    shade = np.clip(.91+(-dx+dy)/1100,.63,1.15)
    rgb *= shade[:,:,None]
    depth = np.clip(-h/2500,0,1)
    ocean = np.stack([36-depth*12,71-depth*17,83-depth*15],axis=-1)
    rgb[~land] = ocean[~land]; rgb[lakes] = [55,91,103]
    Image.fromarray(np.uint8(np.clip(rgb[::-1],0,255))).save(OUT/'relief.png')
    data = {'schemaVersion':1,'name':'Europe and the British Isles','west':WEST,'south':SOUTH,
            'east':EAST,'north':NORTH,'width':SIZE,'height':SIZE,'referenceLatitude':53.,
            'settlements':settlements,'coastlines':paths(natural('coastline'),clip),
            'rivers':paths(natural('rivers_lake_centerlines'),clip),'lakes':paths(lake_features,clip),
            'routes':road_data,'mountains':[
                {'name':name,'lon':x,'lat':y} for name,x,y in [
                    ('HIGHLANDS',-4.6,57.2),('PENNINES',-2.3,54.1),('CAMBRIAN MOUNTAINS',-3.7,52.4),
                    ('PYRENEES',.3,42.65),('ALPS',9.5,46.6),('APENNINES',12.5,43.1),
                    ('CARPATHIANS',23.8,47.6),('SCANDINAVIAN MOUNTAINS',12.2,64),
                    ('BALKAN MOUNTAINS',25.2,42.8),('SIERRA NEVADA',-3.3,37.1)]]}
    (OUT/'europe.json').write_text(json.dumps(data,separators=(',',':')))
    (OUT/'sources.json').write_text(json.dumps({'sources':SOURCES,'generatedUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),
        'rasterRegistration':'nodes, south-to-north rows, little-endian int16 metres',
        'periodData':'Town roles and routes are authored provisional content; no historical borders supplied.'},indent=2))
    print(f'Built {len(settlements)} towns, {len(road_data)} connections, {len(data["rivers"])} river segments.',flush=True)


if __name__ == '__main__':
    main()
