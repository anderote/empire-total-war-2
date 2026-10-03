#!/usr/bin/env python3
"""Preserve CC0 source evidence and prepare bounded working audio from public HQ previews."""
from pathlib import Path
import concurrent.futures
import hashlib
import json
import re
import subprocess

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'AssetSources/Audio/Atmosphere'
DEST = ROOT / 'Assets/Resources/BattleAudio'
SOUNDS = {
    'Gallop': ('Max_Headroom', 175356),
    'HoofWalk': ('MrFizzywig', 581833),
    'HorseCall': ('felix.blume', 385947),
    'Leather': ('ALLANZ10D', 269761),
    'Carriage': ('Garuda1982', 631829),
    'Nature': ('Antonio_Lai', 517859),
    'Rain': ('jmbphilmes', 200272),
    'Wood': ('Rudmer_Rotteveel', 502505),
    'Effort': ('melisandepope', 557131),
}

def run(*args):
    subprocess.run(args, check=True, capture_output=True)

def fetch(item):
    name, (creator, number) = item
    url = f'https://freesound.org/people/{creator}/sounds/{number}/'
    page = SOURCE / f'{name}.html'
    if not page.exists():
        run('curl', '-fLsS', '--retry', '2', url, '-o', str(page))
    html = page.read_text()
    if 'creativecommons.org/publicdomain/zero/1.0/' not in html:
        raise RuntimeError(f'CC0 license evidence missing: {url}')
    preview = re.search(r'https://cdn\.freesound\.org/previews/[^"\s]+-hq\.mp3', html).group()
    mp3 = SOURCE / f'{name}.mp3'
    if not mp3.exists():
        run('curl', '-fLsS', '--retry', '2', preview, '-o', str(mp3))
    run('ffmpeg', '-v', 'error', '-i', str(mp3), '-f', 'null', '-')
    return dict(name=name, creator=creator, source=url, preview=preview,
                license='https://creativecommons.org/publicdomain/zero/1.0/',
                sha256=hashlib.sha256(mp3.read_bytes()).hexdigest())

def working(name, output=None, start=0, duration=None, loop=False, highpass=70, lowpass=10000, channels=1):
    args=['ffmpeg', '-v', 'error', '-y', '-ss', str(start), '-i', str(SOURCE/f'{name}.mp3')]
    if duration is not None: args += ['-t', str(duration)]
    # Preserve dynamics; output is normalized later by the deterministic loop editor.
    args += ['-ac', str(channels), '-ar', '44100', '-af', f'highpass=f={highpass},lowpass=f={lowpass}',
             '-c:a', 'pcm_s16le', str(DEST/f'{output or name}.wav')]
    run(*args)
    import numpy as np
    import wave
    path=DEST/f'{output or name}.wav'
    with wave.open(str(path)) as w:
        rate=w.getframerate(); data=np.frombuffer(w.readframes(w.getnframes()),dtype='<i2').astype(float).reshape(-1,channels)/32768
    peak=max(float(np.max(np.abs(data))),.001)
    data *= .7/peak
    if loop:
        n=min(int(rate*.14),len(data)//8)
        blend=np.linspace(0,1,n)[:,None]
        # Remove the overlapped tail. The boundary follows naturally into the untouched body.
        data[:n]=data[-n:]*(1-blend)+data[:n]*blend
        data=data[:-n]
    else:
        n=min(int(rate*.02),len(data)//4)
        data[:n]*=np.linspace(0,1,n)[:,None];data[-n:]*=np.linspace(1,0,n)[:,None]
    with wave.open(str(path),'wb') as w:
        w.setparams((channels,2,rate,0,'NONE','not compressed'))
        w.writeframes((np.clip(data,-1,1)*32767).astype('<i2').tobytes())

def main():
    SOURCE.mkdir(parents=True,exist_ok=True)
    DEST.mkdir(parents=True,exist_ok=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        records=list(pool.map(fetch,SOUNDS.items()))
    (SOURCE/'inventory.json').write_text(json.dumps(records,indent=2)+'\n')
    # Full gallop and walk cues retain their observed rhythm. Calls/foley are trimmed after audition.
    working('Gallop',loop=True,highpass=100,lowpass=7500)
    working('HoofWalk',loop=True,highpass=100,lowpass=7500)
    working('HorseCall',start=4.95,duration=2.15)
    
    for i,start in enumerate([1.8,5.0,18.7],1):
        working('Leather',output=f'Leather{i}',start=start,duration=.75,highpass=180,lowpass=5500)
    working('Nature',duration=40,loop=True,highpass=200,lowpass=9000,channels=2)
    working('Rain',start=5,duration=24,loop=True,highpass=150,lowpass=9000)
    working('Wood');working('Effort')
    print('Prepared',len(records),'CC0 sources; originals require account login, HQ previews preserved.')

if __name__=='__main__':main()
