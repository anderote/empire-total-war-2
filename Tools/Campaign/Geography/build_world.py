#!/usr/bin/env python3
"""Whole Earth relief + gazetteer, with a detailed campaign theater layered separately."""
import hashlib
import json
import math
import time
import zipfile

import numpy as np
from PIL import Image
from scipy.io import netcdf_file
from scipy.spatial import cKDTree
from shapely import contains_xy
from shapely.geometry import box, shape
from shapely.ops import unary_union
import build_europe as eu

OUT = eu.OUT.parent/'WorldGeography'
WIDTH, HEIGHT = 2049, 1025
ETOPO_URL = 'https://oceanwatch.pifsc.noaa.gov/erddap/griddap/ETOPO_2022_v1_60s.nc?z[0:8:10799][0:8:21599]'


def normal(x,y):
    x,y=np.radians(x),np.radians(y)
    return np.array([np.cos(y)*np.sin(x),np.sin(y),np.cos(y)*np.cos(x)]).T


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    src=eu.fetch(ETOPO_URL,eu.CACHE/'etopo-world.nc')
    with netcdf_file(src,mmap=False) as nc:
        raw=nc.variables['z'].data.copy()
        sy=nc.variables['latitude'].data.copy()
        sx=nc.variables['longitude'].data.copy()
    sx=(sx+180)%360-180;order=np.argsort(sx);sx=sx[order];raw=raw[:,order]
    # Periodic longitude interpolation across the antimeridian; poles use nearest source cell.
    sx=np.r_[sx[-1]-360,sx,sx[0]+360]
    raw=np.concatenate([raw[:,-1:],raw,raw[:,:1]],axis=1)
    lon=np.linspace(-180,180,WIDTH);lat=np.linspace(-90,90,HEIGHT)
    horizontal=np.array([np.interp(lon,sx,row) for row in raw])
    h=np.array([np.interp(lat,sy,col) for col in horizontal.T]).T
    if np.any(~np.isfinite(h)) or np.min(h)<-12000 or np.max(h)>10000:
        raise ValueError('Invalid elevation samples')
    land_features=eu.natural('land')
    land=contains_xy(unary_union([shape(f['geometry']) for f in land_features]),lon[None,:],lat[:,None])
    lake_features=eu.natural('lakes')
    lakes=contains_xy(unary_union([shape(f['geometry']) for f in lake_features]),lon[None,:],lat[:,None])
    # Keep original campaign town IDs/roles, and use sourced records only as a geographic catalog.
    detail=json.loads((eu.OUT/'europe.json').read_text())
    settlements=detail['settlements']
    for t in settlements:t.update(displayRank=1 if t['kind']=='capital' else 5,catalogOnly=False,country='')
    ne=eu.natural('populated_places')
    points=np.array([[t['lon'],t['lat']] for t in settlements])
    tree=cKDTree(normal(points[:,0],points[:,1]))
    for f in ne:
        x,y=f['geometry']['coordinates'][:2];p=f['properties']
        if tree.query(normal(x,y))[0]<.0016:continue # Existing campaign town within ~10 km.
        settlements.append({'id':'ne-'+str(p['NE_ID']),'name':p.get('NAME_EN') or p['NAME'],
            'lon':x,'lat':y,'kind':'town','port':False,'source':'Natural Earth populated places',
            'displayRank':int(p.get('SCALERANK',8)),'catalogOnly':True,'country':p.get('ADM0NAME','')})
    points=np.array([[t['lon'],t['lat']] for t in settlements]);tree=cKDTree(normal(points[:,0],points[:,1]))
    gaz=eu.fetch('https://download.geonames.org/export/dump/cities15000.zip',eu.CACHE/'cities15000.zip')
    with zipfile.ZipFile(gaz) as z:
        rows=[row.split('\t') for row in z.read('cities15000.txt').decode('utf8').splitlines()]
    for row in rows:
        y,x=float(row[4]),float(row[5])
        if tree.query(normal(x,y))[0]<.0004:continue # Within ~2.5 km of an existing named center.
        pop=int(row[14] or 0)
        rank=3 if pop>1000000 else 5 if pop>200000 else 7 if pop>50000 else 9
        settlements.append({'id':'gn-'+row[0],'name':row[1],'lon':x,'lat':y,'kind':'town',
            'port':False,'source':'GeoNames CC BY 4.0','displayRank':rank,'catalogOnly':True,'country':row[8]})
    # Smaller rank first so label decluttering is stable; no population becomes campaign data.
    settlements.sort(key=lambda t:(t['catalogOnly'],t['displayRank'],t['id']))
    clip=box(-180,-90,180,90)
    data={'schemaVersion':1,'name':'Earth','west':-180,'east':180,'south':-90,'north':90,
          'width':WIDTH,'height':HEIGHT,'referenceLatitude':0,'settlements':settlements,
          'routes':detail['routes'],'coastlines':eu.paths(eu.natural('coastline'),clip),
          'rivers':eu.paths(eu.natural('rivers_lake_centerlines'),clip),'lakes':[],
          'mountains':detail['mountains']+[
              {'name':name,'lon':x,'lat':y} for name,x,y in [('ATLAS',-4,31),('HIMALAYAS',85,29),
              ('ANDES',-70,-20),('ROCKY MOUNTAINS',-110,43),('URAL MOUNTAINS',60,58),('ETHIOPIAN HIGHLANDS',38,10)]]}
    (OUT/'world.json').write_text(json.dumps(data,separators=(',',':'),ensure_ascii=False))
    (OUT/'elevation.bytes').write_bytes(np.rint(h).astype('<i2').tobytes())
    (OUT/'landmask.bytes').write_bytes((land & ~lakes).astype('uint8').tobytes())
    ne_url='https://naciscdn.org/naturalearth/10m/raster/NE2_LR_LC.zip'
    ne_zip=eu.fetch(ne_url,eu.CACHE/'natural-earth-2.zip')
    with zipfile.ZipFile(ne_zip) as z:
        tif=next(name for name in z.namelist() if name.lower().endswith('.tif'))
        with z.open(tif) as f:
            full=Image.open(f).convert('RGB')
            dd=detail
            region=full.crop(((dd['west']+180)/360*full.width,(90-dd['north'])/180*full.height,
                              (dd['east']+180)/360*full.width,(90-dd['south'])/180*full.height))
            region=region.resize((dd['width'],dd['height']),Image.Resampling.LANCZOS)
            image=full.resize((WIDTH,HEIGHT),Image.Resampling.LANCZOS)
    # Use the same idealized palette in the detailed theater, retaining its finer relief.
    rh=np.frombuffer((eu.OUT/'elevation.bytes').read_bytes(),dtype='<i2').reshape(dd['height'],dd['width'])
    rl=np.frombuffer((eu.OUT/'landmask.bytes').read_bytes(),dtype='uint8').reshape(rh.shape).astype(bool)
    rlon=np.linspace(dd['west'],dd['east'],dd['width']);rlat=np.linspace(dd['south'],dd['north'],dd['height'])
    rland=contains_xy(unary_union([shape(f['geometry']) for f in land_features]),rlon[None,:],rlat[:,None])
    ry,rx=np.gradient(np.maximum(rh.astype(float),0));rshade=np.clip(.93+(-rx+ry)/1100,.7,1.1)
    rcolor=np.asarray(region,dtype=float)[::-1]*.9*rshade[:,:,None]
    rdepth=np.clip(-rh.astype(float)/3500,0,1)
    rocean=np.stack([38-rdepth*14,74-rdepth*20,87-rdepth*19],axis=-1)
    rcolor[~rland]=rocean[~rland];rcolor[rland & ~rl]=[54,88,99]
    Image.fromarray(np.uint8(np.clip(rcolor[::-1],0,255))).save(eu.OUT/'relief.png')
    color=np.asarray(image,dtype=float)[::-1]*.9
    # Apply actual elevation shading to the idealized Natural Earth land cover palette.
    dy,dx=np.gradient(np.maximum(h,0));shade=np.clip(.93+(-dx+dy)/2000,.73,1.08)
    color*=shade[:,:,None]
    depth=np.clip(-h/3500,0,1)
    ocean=np.stack([38-depth*14,74-depth*20,87-depth*19],axis=-1)
    color[~land]=ocean[~land];color[lakes]=[54,88,99]
    Image.fromarray(np.uint8(np.clip(color[::-1],0,255))).save(OUT/'relief.png')
    manifest={'sources':eu.SOURCES+[{'url':ETOPO_URL,'sha256':hashlib.sha256(src.read_bytes()).hexdigest()},
              {'url':ne_url,'sha256':hashlib.sha256(ne_zip.read_bytes()).hexdigest()},
              {'url':'https://download.geonames.org/export/dump/cities15000.zip',
               'sha256':hashlib.sha256(gaz.read_bytes()).hexdigest(),'license':'CC BY 4.0','credit':'GeoNames'}],
              'generatedUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),
              'periodData':'Gazetteer coordinates/names and display priorities are modern references, not a validated 1800 settlement roster.',
              'elevation':'NOAA ETOPO 2022 ice-surface model, 60 arc-second source decimated to 8 arc-minute samples, resampled to grid nodes. Polar boundary nodes use nearest samples.',
              'palette':'Natural Earth II idealized land cover, not historical land-use measurements.'}
    (OUT/'sources.json').write_text(json.dumps(manifest,indent=2))
    sicily=sum(12<t['lon']<15.7 and 36.6<t['lat']<38.4 for t in settlements)
    print(f'WORLD BUILT: {len(settlements)} settlements; {sicily} Sicily-area records; full polar coverage; {WIDTH}x{HEIGHT} elevation.',flush=True)


if __name__=='__main__':main()
