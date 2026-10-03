#!/usr/bin/env python3
"""Capture every common death endpoint and all paired knockdown/recovery variants."""
from playtest_guard import run_qa, popen_qa
import argparse
import json
from pathlib import Path
import plistlib
import re
import subprocess

root=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app',type=Path,default=root/'Builds/Infantry Inspection.app')
options=parser.parse_args()
app=options.app.resolve()
with (app/'Contents/Info.plist').open('rb') as handle:executable=plistlib.load(handle)['CFBundleExecutable']
folder=root/'Artifacts/GroundContact/Final'
folder.mkdir(parents=True,exist_ok=True)
rows=[]
cases=[('Knockdown',v,1) for v in range(4)]+[('GetUp',v,.45) for v in range(4)]+[('Death',v,1) for v in range(10)]+[('OfficerDeath',0,1),('PikeDeath',0,1)]
for motion,variant,phase in cases:
    label=f'{motion.lower()}-{variant}'
    shot=folder/(label+'.png');log=folder/(label+'.log')
    result=run_qa([str(app/'Contents/MacOS'/executable),'--motion',motion,'--variant',str(variant),'--pose-phase',str(phase),'--ground-close','--screenshot',str(shot),'--capture-at','.2','-screen-width','1440','-screen-height','900','-screen-fullscreen','0','-logFile',str(log)],cwd=root,capture_output=True,text=True,timeout=90)
    issues=re.findall(r'^.*(?:\w+Exception:|Shader error|_FAILED).*$' ,log.read_text(),re.M) if log.exists() else ['missing log']
    if result.returncode or issues or not shot.exists():raise RuntimeError(f'{label}: exit={result.returncode}, issues={issues}, screenshot={shot.exists()}')
    rows.append({'motion':motion,'variant':variant,'phase':phase,'screenshot':str(shot),'passed':True})
    print(label+' captured',flush=True)
(folder/'captures.json').write_text(json.dumps(rows,indent=2)+'\n')
from PIL import Image,ImageDraw
sheet=Image.new('RGB',(1440,5*200),(30,37,44));draw=ImageDraw.Draw(sheet)
for i,row in enumerate(rows):
    shot=Image.open(row['screenshot']).convert('RGB').crop((0,280,1440,900)).resize((360,155))
    x=(i%4)*360;y=(i//4)*200
    sheet.paste(shot,(x,y+25));draw.text((x+8,y+6),f"{row['motion']} {row['variant']+1}",fill='white')
sheet.save(folder/'contact-sheet.png')
