#!/usr/bin/env python3
"""Offline terrain-checked campaign corridors and authored navigable river reaches.
All routes are gameplay approximations, not historical roads or hydrographic charts.
"""
import heapq
import json
import math
from pathlib import Path
import numpy as np
from shapely.geometry import shape,box
from shapely.ops import unary_union,linemerge
import build_europe as eu

OUT=eu.OUT.parent

def km(a,b):
    return math.hypot((a['lon']-b['lon'])*111.32*math.cos(math.radians((a['lat']+b['lat'])/2)),(a['lat']-b['lat'])*111.32)

class Grid:
    def __init__(self,folder,manifest):
        self.folder=folder;self.d=json.loads((OUT/folder/manifest).read_text());d=self.d
        self.land=np.fromfile(OUT/folder/'landmask.bytes',np.uint8).reshape(d['height'],d['width'])
        self.h=np.fromfile(OUT/folder/'elevation.bytes','<i2').reshape(d['height'],d['width'])
    def contains(self,t):
        d=self.d;return d['west']<=t['lon']<=d['east'] and d['south']<=t['lat']<=d['north']
    def cell(self,t):
        d=self.d;x=round((t['lon']-d['west'])/(d['east']-d['west'])*(d['width']-1));y=round((t['lat']-d['south'])/(d['north']-d['south'])*(d['height']-1))
        candidates=[(dx*dx+dy*dy,y+dy,x+dx) for dy in range(-5,6) for dx in range(-5,6) if 0<=y+dy<d['height'] and 0<=x+dx<d['width'] and self.land[y+dy,x+dx]]
        return min(candidates)[1:] if candidates else None
    def point(self,y,x):
        d=self.d;return {'lon':round(d['west']+x/(d['width']-1)*(d['east']-d['west']),5),'lat':round(d['south']+y/(d['height']-1)*(d['north']-d['south']),5)}
    def route(self,a,b):
        start,goal=self.cell(a),self.cell(b)
        if start is None or goal is None:return None
        queue=[(0,start)];scores={start:0};prev={};d=self.d
        factor=max(.2,math.cos(math.radians((a['lat']+b['lat'])/2)))
        while queue and len(scores)<90000:
            _,cur=heapq.heappop(queue)
            if cur==goal:
                cells=[cur]
                while cur in prev:cur=prev[cur];cells.append(cur)
                points=[self.point(y,x) for y,x in reversed(cells)]
                if len(points)<2:return None
                return points,scores[goal]
            y,x=cur
            for dy,dx in ((0,1),(0,-1),(1,0),(-1,0),(1,1),(-1,-1),(1,-1),(-1,1)):
                yy,xx=y+dy,x+dx
                if not (0<=yy<d['height'] and 0<=xx<d['width'] and self.land[yy,xx]):continue
                if dx and dy and not (self.land[y,xx] and self.land[yy,x]):continue
                rise=abs(float(self.h[y,x])-float(self.h[yy,xx]));height=max(0,float(self.h[yy,xx]))
                cost=scores[(y,x)]+math.hypot(dx*factor,dy)*(1+height/2200+rise/220)
                nxt=(yy,xx)
                if cost<scores.get(nxt,math.inf):
                    scores[nxt]=cost;prev[nxt]=(y,x)
                    heapq.heappush(queue,(cost+math.hypot((xx-goal[1])*factor,yy-goal[0]),nxt))
        return None

def build_roads(world):
    towns=world['settlements'];existing={tuple(sorted((r['fromId'],r['toId']))) for r in world['routes']}
    grids=[Grid(folder,manifest) for folder,manifest in [('EuropeGeography','europe.json'),('NorthAmerica','atlas.json'),('SouthAmerica','atlas.json'),('Africa','atlas.json'),('Asia','atlas.json'),('Australasia','atlas.json'),('WorldGeography','world.json')]]
    pairs={}
    for a in towns:
        # Existing European graph remains the foundation; focus new routes on sparse theaters.
        if grids[0].contains(a) and a['lat']>34:continue
        neighbors=sorted(((km(a,b),b) for b in towns if b['id']!=a['id']),key=lambda p:p[0])
        for dist,b in neighbors[:3]:
            key=tuple(sorted((a['id'],b['id'])))
            if key in existing or not 40<dist<1800:continue
            pairs[key]=(a,b,dist)
    made=0
    for a,b,dist in sorted(pairs.values(),key=lambda p:p[2]):
        grid=next(g for g in grids if g.contains(a) and g.contains(b))
        result=grid.route(a,b)
        if result is None:continue
        points,cost=result
        # Keep all mask-tested samples; simplification can turn a land road into a ferry.
        world['routes'].append({'name':a['name']+' — '+b['name'],'fromId':a['id'],'toId':b['id'],'mode':'land',
            'surface':'trail' if dist>650 or max(grid.h[grid.cell(a)],grid.h[grid.cell(b)])>1200 else 'road',
            'distanceKm':round(sum(km(p,q) for p,q in zip(points,points[1:]))),'terrainCost':round(cost,2),
            'provisional':True,'terrainAtlas':grid.folder,'points':points})
        made+=1
        if made%20==0:print('Terrain corridors:',made,flush=True)
    return made

def build_rivers():
    # Deliberately bounded reaches exclude known major upstream cataracts/rapids.
    # Draft values are gameplay limits. Riverboats use inland reaches; coastal ships
    # only get the explicitly authored tidal approaches, not automatic access inland.
    specs=[
        ('mississippi','Mississippi',box(-94,28,-88,38.65),2.0,True,'New Orleans to St Louis'),
        ('st-lawrence','St. Lawrence',box(-73.55,44,-55,52),2.0,True,'Sea approach to Montreal; upstream rapids excluded'),
        ('nile','Nile',box(29,24.1,33,32),1.5,True,'Delta to Aswan; first cataract excluded'),
        ('rhine','Rhine',box(3,50.35,9,53),1.8,True,'Lower Rhine to Koblenz'),
        ('danube','Danube',box(22.5,43,30,47),1.6,True,'Lower Danube; Iron Gates excluded'),
        ('yangtze','Yangtze',box(114.25,28,123,34),2.0,True,'Lower Yangtze to Wuhan'),
        ('ganges','Ganges',box(82.9,21,91,26.2),1.2,True,'Lower Ganges; shallow river craft'),
        ('mekong','Mekong',box(104.7,9,107,12),1.2,True,'Delta and lower Cambodia; upstream rapids excluded'),
        ('irrawaddy','Ayeyarwady',box(94,15,97,22),1.2,True,'Lower Irrawaddy; shallow river craft'),
        ('volga','Volga',box(43,45,51,57),1.5,False,'Volga inland/Caspian system; no ocean connection'),
        ('amazon','Amazonas',box(-64,-5,-48,0),2.0,True,'Lower Amazon'),
        ('niger-inland','Niger',box(-7,12,-1,18),1.0,False,'Inland Niger reach; no sea connection'),
        ('congo-inland','Congo',box(15.2,-4.4,24,3),1.6,False,'Inland Congo; lower cataracts disconnect the sea')]
    features=json.loads((eu.CACHE/'rivers_lake_centerlines.geojson').read_text())['features'];reaches=[]
    def lines(g):
        if g.geom_type=='LineString':return [g]
        return [line for child in getattr(g,'geoms',[]) for line in lines(child)]
    for rid,name,bounds,draft,sea,note in specs:
        pieces=[shape(f['geometry']).intersection(bounds) for f in features if f['properties'].get('name')==name]
        merged=unary_union(pieces)
        if merged.geom_type=='MultiLineString':merged=linemerge(merged)
        # Prune tiny distributary stubs, then merge the main stem through junctions.
        main=[line for line in lines(merged) if line.length>.35]
        merged=unary_union(main)
        if merged.geom_type=='MultiLineString':merged=linemerge(merged)
        for i,line in enumerate(lines(merged)):
            if line.length<.08:continue
            reaches.append({'id':rid+'-'+str(i),'name':name,'maxDraftMeters':draft,'seaConnected':sea,'notes':note,'points':[{'lon':round(x,5),'lat':round(y,5)} for x,y,*_ in line.coords]})
    # The Natural Earth river layer omits broad estuaries already represented as water.
    # Authored channel waypoints fill the St Lawrence approach without routing across land.
    reaches.append({'id':'st-lawrence-estuary','name':'St. Lawrence','maxDraftMeters':2.0,'seaConnected':True,
        'notes':'Authored schematic estuary channel to Montreal; upstream rapids excluded.',
        'points':[{'lon':x,'lat':y} for x,y in [(-73.55,45.5),(-73.40,45.65),(-72.70,46.25),(-72.50,46.37),(-71.95,46.65),(-71.20,46.81),(-70.80,47.05),(-70.15,47.50),(-69.30,48.1),(-68.0,48.7),(-66.5,49.2),(-64.5,49.5)]]})
    result={'schemaVersion':1,'notes':'Authored prototype navigation reaches. No canal locks, portage, seasonal water levels or ocean fleet access are implied. Draft limits are gameplay approximations. Natural Earth river geometry; riverboat routing stays on one continuous reach.','reaches':reaches}
    (OUT/'WorldGeography'/'riverways.json').write_text(json.dumps(result,separators=(',',':'),ensure_ascii=False))
    return len(reaches)

def main():
    path=OUT/'WorldGeography'/'world.json';world=json.loads(path.read_text())
    # Idempotent: regenerated roads carry surface metadata; original European corridors do not.
    world['routes']=[r for r in world['routes'] if 'surface' not in r]
    roads=build_roads(world);rivers=build_rivers()
    path.write_text(json.dumps(world,separators=(',',':'),ensure_ascii=False))
    print(f'CONNECTIONS: {roads} new terrain roads/trails; {rivers} navigable river reaches.',flush=True)
if __name__=='__main__':main()
