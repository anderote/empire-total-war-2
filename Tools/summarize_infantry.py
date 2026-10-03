#!/usr/bin/env python3
"""Summarize completed frame captures without hiding no-tick frames or missing GPU data."""
import csv,json,math,statistics,sys
from pathlib import Path
folder=Path(sys.argv[1]);rows=list(csv.DictReader((folder/'frames.csv').open()))
def stats(values):
 values=sorted(values)
 return {'samples':len(values),'median':statistics.median(values),'p95':values[max(0,math.ceil(len(values)*.95)-1)],'max':values[-1],'mean':statistics.mean(values)} if values else None
out={'frames':len(rows),'metrics':{},'subsystems':{}}
for key in rows[0]:
 if key.endswith('Ms') and not key.endswith('MaxMs') or key.endswith('GcBytes') or key in ['gcBytes','allocatedBytes','drawCalls','crowdDrawCalls','crowdInstances','deaths','activeRagdolls','frozenRagdolls']:
  values=[float(row[key]) for row in rows if float(row[key])>=0]
  out['metrics'][key]=stats(values)
  calls=key[:-2]+'Calls'
  if calls in rows[0]:
   count=sum(int(float(r[calls])) for r in rows)
   out['subsystems'][key]={'calls':count,'meanPerCallMs':sum(values)/count if count else 0,'maxCallMs':max(float(r[key[:-2]+'MaxMs']) for r in rows)}
out['simulationSeconds']=float(rows[-1]['simulationSeconds'])
out['wallSeconds']=float(rows[-1]['wallSeconds'])
out['simulationRate']=(float(rows[-1]['simulationSeconds'])-float(rows[0]['simulationSeconds']))/max(.0001,float(rows[-1]['wallSeconds'])-float(rows[0]['wallSeconds']))
out['slowestFrames']=[{k:float(v) for k,v in row.items()} for row in sorted(rows,key=lambda r:float(r['frameMs']),reverse=True)[:10]]
(folder/'summary.json').write_text(json.dumps(out,indent=2)+'\n')
print(json.dumps(out['subsystems'],indent=2))
