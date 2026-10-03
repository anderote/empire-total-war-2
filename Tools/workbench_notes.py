#!/usr/bin/env python3
"""Read user review notes, or mark a validated note complete and regenerate the queue."""
import argparse,json,os,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent
def atomic(path,value):
    path.parent.mkdir(parents=True,exist_ok=True)
    fd,name=tempfile.mkstemp(prefix=path.name+'.',suffix='.tmp',dir=path.parent)
    with os.fdopen(fd,'w') as f:json.dump(value,f,indent=2);f.write('\n')
    os.replace(name,path)
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data',type=Path,default=ROOT/'WorkbenchData')
    parser.add_argument('--all',action='store_true')
    parser.add_argument('--search',default='')
    parser.add_argument('--done',metavar='NOTE_ID',help='Only use after the requested change has been validated.')
    args=parser.parse_args();path=args.data/'notes.json'
    data=json.loads(path.read_text()) if path.exists() else {'Version':1,'Notes':[]}
    if args.done:
        import fcntl,datetime
        args.data.mkdir(parents=True,exist_ok=True)
        with (args.data/'notes.lock').open('a') as lock:
            fcntl.flock(lock,fcntl.LOCK_EX)
            data=json.loads(path.read_text())
            matches=[n for n in data['Notes'] if n['Id']==args.done]
            if len(matches)!=1:parser.error('Note ID is absent or ambiguous.')
            matches[0]['Status']='done';matches[0]['UpdatedUtc']=datetime.datetime.now(datetime.timezone.utc).isoformat()
            atomic(path,data)
            atomic(args.data/'codex-queue.json',{'Version':1,'Notes':[n for n in data['Notes'] if n.get('Status')=='todo' and n.get('Text','').strip()]})
    notes=[n for n in data['Notes'] if (args.all or n.get('Status')=='todo') and args.search.lower() in json.dumps(n).lower()]
    print(json.dumps({'Version':1,'Notes':notes},indent=2))
if __name__=='__main__':main()
