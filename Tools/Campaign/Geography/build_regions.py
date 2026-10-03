#!/usr/bin/env python3
"""Sparse authored campaign roster. Modern reference geography is not a political 1800 reconstruction."""
import json
import unicodedata
from collections import defaultdict
import numpy as np
from scipy.spatial import cKDTree
from shapely import voronoi_polygons,coverage_simplify,make_valid,set_precision
from shapely.geometry import shape, MultiPoint, Point, box
from shapely.ops import unary_union
from shapely.affinity import scale
import build_europe as eu
import campaign_politics

# Province | source geographic envelope country | regional capitals. Italy has 4 × 4.
ROSTER = '''Scotland|GBR|Edinburgh,Glasgow,Aberdeen,Inverness
England|GBR|London,Bristol,York,Liverpool
Wales|GBR|Cardiff
Ireland|IRL,GBR|Dublin,Cork,Belfast
Northern France|FRA|Paris,Lille,Rouen,Strasbourg
Southern France|FRA|Lyon,Marseille,Bordeaux,Toulouse
Atlantic France|FRA|Nantes,Brest
Corsica|FRA|Ajaccio
Northern Italy|ITA|Turin,Genoa,Milan,Venice
Central Italy|ITA|Bologna,Florence,Rome,Cagliari
Southern Italy|ITA|Naples,Bari,Taranto,Reggio di Calabria
Sicily|ITA|Palermo,Messina,Catania,Agrigento
Northern Iberia|ESP|Madrid,Barcelona,Bilbao
Southern Iberia|ESP|Valencia,Seville,Cadiz
Portugal|PRT|Lisbon,Porto
Netherlands|NLD|Amsterdam,Rotterdam
Belgian Lands|BEL|Brussels,Antwerp
Northern German Lands|DEU|Berlin,Hamburg,Hanover
Southern German Lands|DEU|Frankfurt,Munich,Stuttgart
Swiss Lands|CHE|Bern,Geneva
Austrian Lands|AUT|Vienna,Innsbruck
Bohemia|CZE|Prague
Hungarian Lands|HUN|Budapest
Polish Lands|POL|Warsaw,Krakow,Gdansk
Baltic Lands|LVA,EST,LTU|Riga,Tallinn,Vilnius
Western Russian Lands|RUS|Saint Petersburg,Moscow,Smolensk
Volga and Urals|RUS|Kazan,Perm,Ufa
Siberia|RUS|Tobolsk,Irkutsk,Yakutsk
Russian Pacific|RUS|Okhotsk,Petropavlovsk-Kamchatsky
Ukrainian Lands|UKR|Kyiv,Odessa
Scandinavia|NOR,SWE,DNK|Oslo,Stockholm,Copenhagen
Finnish Lands|FIN|Helsinki
Balkan Lands|GRC,SRB,BGR,ROU,HRV|Athens,Thessaloniki,Belgrade,Sofia,Bucharest,Zagreb
Anatolia|TUR|Istanbul,Ankara,Izmir
Morocco|MAR|Tangier,Fez,Marrakesh
Algeria|DZA|Algiers,Oran,Constantine
Tunisia|TUN|Tunis,Sfax,Kairouan
Libya|LBY|Tripoli,Benghazi
Lower Nile|EGY|Cairo,Alexandria
New England|USA|Boston
Mid Atlantic|USA|New York City,Philadelphia,Albany
Chesapeake|USA|Richmond,Baltimore
Southern Atlantic|USA|Charleston,Savannah
Ohio Valley|USA|Cincinnati,Lexington
Great Lakes|USA|Detroit
Mississippi Lands|USA|New Orleans,St. Louis
Western America|USA|San Francisco,Santa Fe
Russian Alaska|USA|Sitka,Kodiak,Unalaska
Upper Canada|CAN|Toronto
Lower Canada|CAN|Quebec,Montreal
Maritime Canada|CAN|Halifax,Fredericton
Newfoundland and Labrador|CAN|St. John's
Hudson Bay Lands|CAN|Churchill
Western Canadian Lands|CAN|Edmonton
Mexican Lands|MEX|Mexico City,Veracruz,Acapulco
Caribbean|CUB,JAM,DOM,PRI|Havana,Kingston,Santo Domingo,San Juan
Guatemala|GTM|Guatemala
Northern Andes|COL,VEN|Bogota,Caracas,Cartagena
Central Andes|PER,ECU,BOL|Lima,Quito,La Paz
Southern Andes|CHL|Santiago,Valparaiso
River Plate|ARG,URY|Buenos Aires,Cordoba,Montevideo
Brazilian Atlantic|BRA|Rio de Janeiro,Sao Paulo
Northern Brazil|BRA|Salvador,Recife,Belem
Southern Brazil|BRA|Porto Alegre
Guianas|GUY,SUR|Georgetown,Paramaribo
West African Coast|SEN|Dakar
Gold Coast|GHA|Accra,Kumasi
Yoruba Lands|NGA|Lagos
Hausa Lands|NGA|Kano
Middle Niger|MLI|Timbuktu,Djenne,Segou
Sahel Caravan Lands|NER|Agadez,Zinder
Wadai Lands|TCD|Abeche
Sudanese Interior|SDN|Sennar,Al-Fashir
Congo Lands|AGO|Mbanza-Kongo,Luanda
Madagascar|MDG|Antananarivo
Southern Africa|ZAF|Cape Town
East African Coast|TZA|Zanzibar
Zambezi Lands|MOZ|Maputo,Tete
Ethiopian Highlands|ETH|Gondar,Harar,Axum
Red Sea Coast|ERI|Massawa
Arabia|SAU,YEM|Jeddah,Sanaa
Mesopotamia|IRQ|Baghdad,Basra
Persian Lands|IRN|Tehran,Isfahan,Shiraz
North India|IND|Delhi,Lucknow,Agra,Jaipur,Varanasi
Bengal|IND,BGD|Kolkata,Dhaka
Western India|IND|Mumbai,Pune
Southern India|IND|Chennai,Bengaluru,Hyderabad
Punjab|PAK|Lahore
Sri Lanka|LKA|Colombo
Chinese Heartland|CHN|Beijing,Nanjing,Guangzhou,Wuhan,Hangzhou,Fuzhou
Chinese Interior|CHN|Chengdu,Xian,Kunming
Chinese Western Lands|CHN|Lhasa,Urumqi,Kashgar
Japan|JPN|Tokyo,Kyoto,Osaka
Korea|KOR,PRK|Seoul,Pyongyang
Siam|THA|Bangkok,Chiang Mai,Ayutthaya
Vietnam|VNM|Hanoi,Hue,Saigon
Burma|MMR|Yangon,Amarapura
Lao Lands|LAO|Vientiane,Luang Prabang
Cambodian Lands|KHM|Phnom Penh
Malay Archipelago|IDN,MYS,PHL|Jakarta,Malacca,Manila
Australian Coast|AUS|Sydney'''

zones={
    'USA':[
        ('New England',box(-73,41,-65,50)),
        ('Mid Atlantic',box(-80,39.6,-65,50)),
        ('Chesapeake',box(-83,36,-65,39.6)),
        ('Southern Atlantic',box(-86,24,-65,36)),
        ('Ohio Valley',box(-89,36,-80,41)),
        ('Great Lakes',box(-103,41,-65,50)),
        ('Western America',box(-130,24,-103,50)),
        ('Mississippi Lands',box(-130,24,-65,50))],
    'CAN':[
        ('Newfoundland and Labrador',box(-60,40,-50,85)),
        ('Maritime Canada',box(-67,40,-50,48)),
        ('Lower Canada',box(-79,40,-50,85)),
        ('Western Canadian Lands',box(-141,40,-105,85)),
        ('Upper Canada',box(-95,40,-79,56)),
        ('Hudson Bay Lands',box(-141,40,-50,85))]}

ALIASES={'Sao Paulo':'São Paulo','Belem':'Belém','Cordoba':'Córdoba','Bogota':'Bogotá',
         'Quebec':'Québec','Xian':"Xi'an",'Bengaluru':'Bangalore','Izmir':'İzmir',
         'Acapulco':'Acapulco de Juárez','Malacca':'Melaka City',
         'Reggio di Calabria':'Reggio Calabria','Guatemala':'Guatemala City','Zanzibar':'Zanzibar City',
         'Mbanza-Kongo':"M'banza-Kongo",'Djenne':'Djenné','Segou':'Ségou','Abeche':'Abéché',
         'Urumqi':'Ürümqi','Kashgar':'Kashgar City','Ayutthaya':'Phra Nakhon Si Ayutthaya',
         'Hue':'Huế','Saigon':'Ho Chi Minh City'}

def norm(s):return ''.join(c for c in unicodedata.normalize('NFKD',s.lower()) if not unicodedata.combining(c)).replace('’',"'")
def idof(s):return norm(s).replace(' ','-').replace("'",'')
def polygons(g):
    if g.geom_type=='Polygon':return [g]
    if hasattr(g,'geoms'):return [p for part in g.geoms for p in polygons(part)]
    return []
def geometry(g,tolerance=0):
    return [{'rings':[{'points':[{'lon':round(x,5),'lat':round(y,5)} for x,y,*_ in ring.coords]}
                      for ring in [p.exterior,*p.interiors]]} for p in polygons(g.simplify(tolerance,preserve_topology=True))]

def main():
    folder=eu.OUT.parent/'WorldGeography'
    world=json.loads((folder/'world.json').read_text())
    reference=eu.CACHE/'world-reference.json'
    if len(world['settlements'])>1000:reference.write_text(json.dumps(world,separators=(',',':'),ensure_ascii=False))
    else:world=json.loads(reference.read_text())
    towns=world['settlements']
    # Smaller Messina satellite absent from cities15000. Coordinates verified in GeoNames.
    # https://www.geonames.org/advanced-search.html?q=Messina&startRow=50
    towns.append({'id':'supplement-san-pier-niceto','name':'San Pier Niceto',
                  'lon':15.349824864292312,'lat':38.160503834910415,'catalogOnly':True,'displayRank':9})
    lookup=defaultdict(list)
    for t in towns:lookup[norm(t['name'])].append(t)
    countries=eu.fetch('https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_10m_admin_0_countries.geojson',eu.CACHE/'countries.geojson')
    country_features=json.loads(countries.read_text())['features']
    # Assign only broad geographic masks. No modern boundary is claimed as an 1800 political border.
    by_iso={f['properties']['ADM0_A3']:shape(f['geometry']) for f in country_features}
    entries=[]
    for row in ROSTER.splitlines():
        province,country,names=row.split('|')
        mask=unary_union([by_iso[c] for c in country.split(',')])
        if country=='FRA':mask=mask.intersection(box(-6,41,10,52))
        if province=='Russian Alaska':mask=mask.intersection(box(-180,50,-130,75))
        if country=='USA' and province!='Russian Alaska':mask=mask.intersection(box(-130,24,-65,50))
        if province=='Ireland':mask=mask.intersection(box(-11,51,-5,56))
        if country=='GBR' and province!='Ireland':mask=mask.intersection(box(-8,49,3,61))
        if country in zones and province!='Russian Alaska':
            prior=[]
            for zone_name,envelope in zones[country]:
                if zone_name==province:
                    mask=mask.intersection(envelope).difference(unary_union(prior));break
                prior.append(envelope)
        for name in names.split(','):
            options=lookup[norm(name)]+lookup[norm(ALIASES.get(name,name))]
            options=[t for t in options if mask.buffer(.1).covers(Point(t['lon'],t['lat']))]
            if not options:raise ValueError('Missing region city: '+name)
            t=sorted(options,key=lambda t:(t['catalogOnly'],t.get('displayRank',10)))[0].copy()
            period_names={'Tokyo':'Edo','Mumbai':'Bombay','Chennai':'Madras','Kolkata':'Calcutta',
                          'Bengaluru':'Bangalore','Yangon':'Rangoon','Maputo':'Lourenço Marques',
                          'Ho Chi Minh City':'Saigon','Edmonton':'Fort Edmonton','Toronto':'York (Upper Canada)'}
            t['name']=period_names.get(t['name'],t['name'])
            t.update(kind='capital',catalogOnly=False,displayRank=1)
            entries.append({'province':province,'maskKey':'Alaska' if province=='Russian Alaska' else country if province!='Ireland' else 'Ireland','mask':mask,'city':t})
    by_mask=defaultdict(list)
    for e in entries:by_mask[e['maskKey']].append(e)
    # India and Bengal's source envelopes overlap. Partition their union together;
    # otherwise the Bengal polygons overwrite Lucknow/Delhi's ownership layer.
    by_mask['IND'].extend(by_mask.pop('IND,BGD'))
    # Northern Ireland belongs to the Irish gameplay province, not a second British cell.
    ireland_mask=unary_union([e['mask'] for e in by_mask['Ireland']])
    for e in by_mask['GBR']:e['mask']=e['mask'].difference(ireland_mask)
    # Carve Sicily out of Italy before partitioning it into exactly four regions.
    italian=by_mask['ITA'];italy=italian[0]['mask']
    sicily=unary_union([p for p in polygons(italy) if 12<p.representative_point().x<16 and 36<p.representative_point().y<39])
    by_mask['ITA']=[e for e in italian if e['province']!='Sicily']
    by_mask['Sicily']=[e for e in italian if e['province']=='Sicily']
    for e in by_mask['ITA']:e['mask']=italy.difference(sicily)
    for e in by_mask['Sicily']:e['mask']=sicily
    # Hand-authored geographic province envelopes prevent a single coastal seed from
    # acquiring a giant diagonal slice across the continent. Country coast/lake holes
    # still come from Natural Earth. These are gameplay envelopes, not surveyed 1800 lines.
    for key,provinces in zones.items():
        group=by_mask.pop(key);remaining=unary_union([e['mask'] for e in group])
        for name,envelope in provinces:
            cell=remaining.intersection(envelope);remaining=remaining.difference(cell)
            targets=[e for e in group if e['province']==name]
            if not targets:raise ValueError('Province envelope has no capital: '+name)
            for e in targets:
                if not cell.buffer(.05).covers(Point(e['city']['lon'],e['city']['lat'])):
                    raise ValueError('Capital outside authored province envelope: '+e['city']['name'])
                e['mask']=cell
            by_mask[key+'-'+name]=targets
        if remaining.area>.001:raise ValueError('Unassigned North American province land: '+key)
    town_tree=cKDTree([(t["lon"],t["lat"]) for t in towns])
    regions=[];region_cells=[]
    for group in by_mask.values():
        mask=unary_union([e['mask'] for e in group]);factor=max(.3,np.cos(np.radians(mask.centroid.y)))
        seeds=MultiPoint([(e['city']['lon']*factor,e['city']['lat']) for e in group])
        if len(group)==1:cells=[mask]
        else:
            diagram=voronoi_polygons(seeds,extend_to=scale(mask,xfact=factor,yfact=1,origin=(0,0)).envelope.buffer(10))
            cells=[]
            for e in group:
                p=Point(e['city']['lon']*factor,e['city']['lat'])
                cell=next(g for g in diagram.geoms if g.covers(p))
                cells.append(scale(cell,xfact=1/factor,yfact=1,origin=(0,0)).intersection(mask))
        for e,cell in zip(group,cells):
            t=e['city'];pid=idof(e['province']);rid='region-'+t['id'];t['regionId']=rid
            owner='britain' if e['province'] in ('Scotland','England','Wales','Ireland') else 'france' if e['maskKey']=='FRA' else 'sicily' if pid=='sicily' else 'local'
            sites=[]
            candidates=[]
            for ti in town_tree.query_ball_point((t["lon"],t["lat"]),1.25/factor):
                other=towns[ti]
                dx=(other['lon']-t['lon'])*factor;dy=other['lat']-t['lat'];dist=np.hypot(dx,dy)
                if .12<dist<1.25 and cell.covers(Point(other['lon'],other['lat'])):candidates.append((dist,other))
            # Satellites are controlled by the region, not independently conquerable cities.
            candidates.sort(key=lambda item:item[0]);chosen=[]
            for dist,other in candidates:
                if all(np.hypot((other['lon']-s['lon'])*factor,other['lat']-s['lat'])>.065 for s in chosen):chosen.append(other)
                if len(chosen)==3:break
            for i,s in enumerate(chosen):
                sites.append({'id':rid+'-site-'+str(i),'name':s['name'],'lon':s['lon'],'lat':s['lat'],
                              'kind':['Fortress','Workshop','University'][i]})
            region_cells.append(cell)
            regions.append({'id':rid,'name':t['name']+' Region','provinceId':pid,'capital':t,
                            'owner':owner,'buildSlots':4,'sites':sites,'polygons':geometry(cell)})
    # Simplify the entire coverage together. Independently simplifying country/region
    # edges creates slivers, overlaps and mismatched strokes along shared boundaries.
    region_cells=[set_precision(make_valid(cell),.00001) for cell in region_cells]
    simplified=coverage_simplify(np.array(region_cells,dtype=object),.01)
    for region,cell in zip(regions,simplified):region['polygons']=geometry(set_precision(make_valid(cell),.00001))
    provinces=[]
    for name in dict.fromkeys(e['province'] for e in entries):
        pid=idof(name);provinces.append({'id':pid,'name':name,'regionIds':[r['id'] for r in regions if r['provinceId']==pid]})
    campaign={'schemaVersion':1,'provinces':provinces,'regions':regions,'notes':'Authored gameplay roster and partitioning; geographic country masks are modern source envelopes, not validated 1800 political borders. Minor site roles are gameplay opportunities, not historic building claims.'}
    # Only shared interior boundaries are stroked: no extra coastline or lake outline.
    from shapely.strtree import STRtree
    from shapely.geometry import Polygon
    cells=[unary_union([Polygon([(p['lon'],p['lat']) for p in poly['rings'][0]['points']],
                               [[(p['lon'],p['lat']) for p in ring['points']] for ring in poly['rings'][1:]])
                       for poly in region['polygons']]) for region in regions]
    tree=STRtree(cells);campaign['provinceBorders']=[];campaign['regionBorders']=[]
    def lines(g):
        if g.geom_type in ('LineString','LinearRing'):return [g] if g.length>.005 else []
        return [line for part in getattr(g,'geoms',[]) for line in lines(part)]
    for i,cell in enumerate(cells):
        for j in tree.query(cell):
            if j<=i:continue
            shared=cell.boundary.intersection(cells[j].boundary)
            target='provinceBorders' if regions[i]['provinceId']!=regions[j]['provinceId'] else 'regionBorders'
            for line in lines(shared):
                campaign[target].append({'name':regions[i]['name']+' / '+regions[j]['name'],
                    'points':[{'lon':round(x,5),'lat':round(y,5)} for x,y in line.simplify(.008).coords]})
    campaign_politics.apply(campaign)
    campaign_politics.raster(campaign)
    (folder/'campaign.json').write_text(json.dumps(campaign,separators=(',',':'),ensure_ascii=False))
    # Ship the game roster, not the entire modern urban gazetteer. Preserve source catalogue in cache.
    world['settlements']=[r['capital'] for r in regions]
    world['waypoints']=json.loads((eu.OUT/'europe.json').read_text())['settlements']
    (folder/'world.json').write_text(json.dumps(world,separators=(',',':'),ensure_ascii=False))
    print(f'CAMPAIGN: {len(provinces)} provinces, {len(regions)} regional cities; Italy 4 provinces / 16 regions; Sicily 4 regions.',flush=True)

if __name__=='__main__':main()
