#!/usr/bin/env python3
"""Inspect legacy PFH0 Total War pack entries without running game content."""
import argparse,json,struct
from pathlib import Path,PurePosixPath
p=argparse.ArgumentParser();p.add_argument('pack',type=Path);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
with a.pack.open('rb') as f:
 h=f.read(24)
 if h[:4]!=b'PFH0': raise ValueError('Only verified PFH0 layout supported')
 flags,depcount,depsize,count,indexsize=struct.unpack('<5I',h[4:])
 f.seek(depsize,1); index=f.read(indexsize);pos=0;offset=24+depsize+indexsize;rows=[]
 for _ in range(count):
  size=struct.unpack_from('<I',index,pos)[0];pos+=4
  end=index.index(b'\0',pos);name=index[pos:end].decode('utf-8').replace('\\','/');pos=end+1
  if name.startswith('/') or '..' in PurePosixPath(name).parts:raise ValueError('Unsafe path')
  rows.append(dict(path=name,size=size,offset=offset));offset+=size
 if pos!=indexsize or offset!=a.pack.stat().st_size:raise ValueError('Index/file length mismatch')
a.out.parent.mkdir(parents=True,exist_ok=True);a.out.write_text(json.dumps(dict(pack=str(a.pack),files=rows),indent=2));print(f'{len(rows)} entries; '+str(sum(r['size'] for r in rows))+' content bytes')
