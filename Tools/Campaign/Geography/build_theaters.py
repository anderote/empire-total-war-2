#!/usr/bin/env python3
"""Baked ~4–5 km theater rasters; runtime loads one remote theater at a time."""
import json
import numpy as np
from PIL import Image
from shapely import contains_xy
from shapely.geometry import shape,box
from shapely.ops import unary_union
import build_europe as eu

THEATERS=[('NorthAmerica',-170,8,-45,82,2049),('SouthAmerica',-85,-58,-32,15,1537),
          ('Africa',-20,-36,55,38,2049),('Asia',45,-12,160,75,2049),('Australasia',95,-50,180,6,2049)]

def main():
    land_features=eu.natural('land');lake_features=eu.natural('lakes')
    for name,w,s,e,n,size in THEATERS:
        out=eu.OUT.parent/name;out.mkdir(parents=True,exist_ok=True)
        if (out/'atlas.json').exists():continue
        print('BUILDING THEATER '+name,flush=True)
        eu.WEST,eu.SOUTH,eu.EAST,eu.NORTH,eu.SIZE=w,s,e,n,size
        eu.SOURCES.clear();h,lon,lat=eu.elevations();clip=box(w,s,e,n)
        land_geom=unary_union([shape(f['geometry']).intersection(clip) for f in land_features if shape(f['geometry']).intersects(clip)])
        lake_geom=unary_union([shape(f['geometry']).intersection(clip) for f in lake_features if shape(f['geometry']).intersects(clip)])
        land=contains_xy(land_geom,lon[None,:],lat[:,None]);lakes=contains_xy(lake_geom,lon[None,:],lat[:,None])
        (out/'elevation.bytes').write_bytes(np.rint(h).astype('<i2').tobytes())
        (out/'landmask.bytes').write_bytes((land & ~lakes).astype('uint8').tobytes())
        # The globe's palette is sampled in the shader; theater files carry only measured relief.
        metadata={'schemaVersion':1,'name':name,'west':w,'south':s,'east':e,'north':n,
                  'width':size,'height':size,'referenceLatitude':(s+n)/2,'settlements':[],
                  'routes':[],'rivers':[],'lakes':[],'coastlines':[],'mountains':[]}
        (out/'sources.json').write_text(json.dumps({'sources':eu.SOURCES,'sampling':'Grid nodes; south-to-north rows; signed metres. AWS zoom-6 source imagery; ~4–5 km output sampling.'},indent=2))
        (out/'atlas.json').write_text(json.dumps(metadata))
        print(name+' READY',flush=True)

if __name__=='__main__':main()
