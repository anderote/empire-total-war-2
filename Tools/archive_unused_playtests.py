#!/usr/bin/env python3
"""Losslessly archive generated, unreferenced player bundles to make build space."""
import hashlib,json,os,plistlib,shutil,subprocess,tarfile
from pathlib import Path
root=Path(__file__).resolve().parent.parent
archive=root/'Builds/ArchivedPlaytests';archive.mkdir(parents=True,exist_ok=True)
protected=set()
launchers=[]
for directory in [Path('/Applications'),Path.home()/'Applications',Path.home()/'Desktop',root/'Builds/Launchers']:
    for app in directory.glob('ETW2*.app'):
        executable=app/'Contents/MacOS/ETW2Launcher'
        if not executable.exists():continue
        launchers.append(app)
        for mode in ['Battle','Campaign map']:
            result=subprocess.run([str(executable),'--verify-target',mode],text=True,capture_output=True)
            if result.returncode==0:protected.add(Path(result.stdout.strip()).resolve())
assert len(launchers)>=4,'Cannot prove current launcher inventory'
index_path=archive/'archive-index.json'
index=json.loads(index_path.read_text()) if index_path.exists() else []
def digest_file(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda:f.read(1048576),b''):h.update(b)
    return h.hexdigest()
for category in ['Playtests','InfantryPlaytests']:
    base=root/'Builds'/category
    for snapshot in sorted(base.iterdir()):
        if not snapshot.is_dir() or not snapshot.name.startswith('20'):continue
        # Keep the entire snapshot if any current launcher references part of it.
        if any(snapshot.resolve() in p.parents for p in protected):continue
        for app in sorted(snapshot.glob('*.app')):
            if shutil.disk_usage(root).free>=7*1024**3:break
            target=archive/(category+'-'+snapshot.name+'-'+app.stem+'.tar.gz')
            if target.exists():continue
            partial=target.with_suffix('.partial')
            manifest={}
            for p in sorted(app.rglob('*')):
                key=str(p.relative_to(app))
                if p.is_symlink():manifest[key]={'symlink':str(p.readlink())}
                elif p.is_file():manifest[key]={'size':p.stat().st_size,'mode':p.stat().st_mode&0o777,'sha256':digest_file(p)}
            print('ARCHIVING',app,flush=True)
            try:
                subprocess.run(['/usr/bin/tar','-czf',str(partial),'--options','gzip:compression-level=9','-C',str(app.parent),app.name],check=True)
                found=set()
                with tarfile.open(partial,'r:gz') as bundle:
                    for entry in bundle:
                        prefix=app.name+'/'
                        if not entry.name.startswith(prefix):continue
                        key=entry.name[len(prefix):];expected=manifest.get(key)
                        if expected is None:continue
                        if 'symlink' in expected:assert entry.issym() and entry.linkname==expected['symlink']
                        else:
                            assert entry.isfile() and entry.size==expected['size'] and entry.mode&0o777==expected['mode']
                            h=hashlib.sha256()
                            with bundle.extractfile(entry) as stream:
                                for b in iter(lambda:stream.read(1048576),b''):h.update(b)
                            assert h.hexdigest()==expected['sha256'],key
                        found.add(key)
                assert found==set(manifest),'Archive omits original files'
                partial.rename(target)
                row={'original':str(app),'archive':str(target),'files':len(manifest),'archive_sha256':digest_file(target),'verified':True,'manifest':manifest}
                if (snapshot/'snapshot.json').exists():row['snapshot_metadata']=json.loads((snapshot/'snapshot.json').read_text())
                index.append(row);index_path.write_text(json.dumps(index,indent=2)+'\n')
                # Removal happens only after a complete lossless archive has been verified.
                shutil.rmtree(app)
                print('VERIFIED_ARCHIVE',target,'free_GiB',round(shutil.disk_usage(root).free/1024**3,2),flush=True)
            except BaseException:
                partial.unlink(missing_ok=True)
                raise
(archive/'README.txt').write_text('Older generated playtest bundles were archived after every file, executable mode and symlink was verified. Current launcher snapshots were excluded. archive-index.json records original locations and SHA-256 manifests. Restore any bundle by extracting its tar.gz into the original parent directory. macOS tar retains extended attributes and resource forks.\n')
print('Current launcher targets preserved:',*[str(p) for p in sorted(protected)],sep='\n')
