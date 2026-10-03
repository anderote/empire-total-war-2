#!/usr/bin/env python3
"""Fetch a small, attributed audition library from Wikimedia Commons.

Original files and source metadata stay outside Unity in AssetSources/Music/National.
The source's license statements are preserved; this tool does not assert worldwide clearance.
"""
import hashlib
import html
import json
from pathlib import Path
import re
import subprocess
from urllib.parse import urlencode

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'AssetSources/Music/National'
TRACKS = [
    ('france-marseillaise', 'France', 'La_Marseillaise.ogg', 'Modern US Navy band; ceremony, not field fife.'),
    ('britain-grenadiers-strings', 'Britain', 'British_Grenadiers.ogg', 'US Army strings; melody reference.'),
    ('britain-grenadiers-fife-render', 'Britain', 'The_British_Grenadeirs.ogg', 'Synthesized flute/piccolo/drum arrangement; CC BY 3.0.'),
    ('spain-marcha-real', 'Spain', 'Marcha_Real-Royal_March_by_US_Navy_Band.ogg', 'Modern US Navy band; melody derives from eighteenth-century march.'),
    ('netherlands-wilhelmus', 'Netherlands', 'United_States_Navy_Band_-_Het_Wilhelmus_(tempo_corrected).ogg', 'Modern US Navy band; corrected tempo.'),
    ('portugal-a-portuguesa-late', 'Portugal', 'A_Portuguesa.ogg', 'Modern US Navy band; composition 1890, late-era use only.'),
    ('prussia-hohenfriedberger-music-box', 'Prussia', 'Polyphon_-_Hohenfriedberger_Marsch.ogg', 'CC0 music-box recording; melody reference, not military ensemble.'),
    ('australia-waltzing-matilda-render-late', 'Australia', 'Waltzing_Matilda.ogg', 'Synthesized melody; CC BY-SA 3.0 option; song 1895, late-era use only.'),
]
IMSLP_TRACKS = [
    ('prussia-yorck-marine-band', 'Prussia', '814407',
     'https://imslp.org/wiki/2_Marches_for_Military_Band,_WoO_18-19_(Beethoven,_Ludwig_van)',
     'Public Domain', 'United States Marine Band; Capt. Michelle A. Rakers, conductor.',
     'Beethoven WoO 18, 1809; documented Prussian collection use 1817–19; modern military-band recording.'),
    ('austria-haydn-military-allegretto', 'Austria', '907150',
     'https://imslp.org/wiki/Symphony_No.100_in_G_major,_Hob.I:100_(Haydn,_Joseph)',
     'CC BY 4.0', 'Das Orchester Tsumugi; Takashi Inoue, conductor; Fukuoka Symphony Hall, 10 March 2024.',
     'Haydn Military Symphony, second movement; orchestral campaign music, not a field march.'),
]
RECORDING_CREDITS = {
    'france-marseillaise': 'United States Navy Band',
    'britain-grenadiers-strings': 'United States Army Strings',
    'britain-grenadiers-fife-render': 'Sfan00 IMG — MuseScore arrangement/render; source arrangement by Charles John Vincent and T. Tertius Noble; GeneralUserGS sound set.',
    'spain-marcha-real': 'United States Navy Band',
    'netherlands-wilhelmus': 'United States Navy Band; source file includes tempo correction.',
    'portugal-a-portuguesa-late': 'United States Navy Band',
    'australia-waltzing-matilda-render-late': 'CambridgeBayWeather',
}

def curl(url, path):
    subprocess.run(['curl', '--fail', '--location', '--retry', '2', '--max-time', '90',
                    '--silent', '--show-error', '--user-agent', 'ETW2MusicResearch/1.0',
                    url, '--output', str(path)], check=True)

def is_audio_recording(media):
    streams = media.get('streams', [])
    return any(s['codec_type'] == 'audio' for s in streams) and all(
        s['codec_type'] == 'audio' or s.get('disposition', {}).get('attached_pic') == 1
        for s in streams)

def main():
    DEST.mkdir(parents=True, exist_ok=True)
    records = []
    for slug, nation, filename, note in TRACKS:
        params = {'action': 'query', 'format': 'json', 'titles': 'File:' + filename,
                  'prop': 'imageinfo', 'iiprop': 'url|extmetadata'}
        meta_path = DEST / (slug + '.source.json')
        if not meta_path.exists():
            temporary_meta = meta_path.with_suffix('.json.part')
            curl('https://commons.wikimedia.org/w/api.php?' + urlencode(params), temporary_meta)
            temporary_meta.replace(meta_path)
        response = json.loads(meta_path.read_text())
        page = next(iter(response['query']['pages'].values()))
        info = page['imageinfo'][0]
        target = DEST / (slug + '.ogg')
        if not target.exists():
            temporary = target.with_suffix('.ogg.part')
            curl(info['url'], temporary)
            temporary.replace(target)
        probe = subprocess.run(['ffprobe', '-v', 'error', '-show_format', '-show_streams',
                                '-of', 'json', str(target)], check=True, capture_output=True, text=True)
        media = json.loads(probe.stdout)
        if not is_audio_recording(media):
            raise RuntimeError('Expected an audio recording: ' + str(target))
        metadata = info.get('extmetadata', {})
        record = {'nation': nation, 'file': target.name, 'source_page': info['descriptionurl'],
                  'original_url': info['url'], 'license_as_reported': metadata.get('LicenseShortName', {}).get('value'),
                  'license_url': metadata.get('LicenseUrl', {}).get('value'),
                  'artists': RECORDING_CREDITS.get(slug, html.unescape(re.sub('<[^>]+>', '', metadata.get('Artist', {}).get('value', '')))),
                  'note': note, 'bytes': target.stat().st_size,
                  'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
                  'duration_seconds': float(media['format']['duration']), 'media': media,
                  'source_metadata_file': meta_path.name}
        records.append(record)
        print(f"{nation}: {target.name}, {record['duration_seconds']:.1f}s, {record['license_as_reported']}", flush=True)
        (DEST / 'inventory.json').write_text(json.dumps(records, indent=2) + '\n')
    for slug, nation, index, page_url, license_name, artists, note in IMSLP_TRACKS:
        meta_path = DEST / (slug + '.source.html')
        if not meta_path.exists():
            temporary_meta = meta_path.with_suffix('.html.part')
            curl(page_url, temporary_meta)
            temporary_meta.replace(meta_path)
        page_html = meta_path.read_text()
        player = re.search(r'"' + index + r'":\{"jwplayersrc".*?"file":("(?:\\.|[^"\\])*")', page_html)
        if not player:
            raise RuntimeError('Public audio player not found for IMSLP item ' + index)
        original_url = json.loads(player.group(1))
        target = DEST / (slug + Path(original_url).suffix)
        if not target.exists():
            temporary = target.with_suffix(target.suffix + '.part')
            curl(original_url, temporary)
            temporary.replace(target)
        probe = subprocess.run(['ffprobe', '-v', 'error', '-show_format', '-show_streams',
                                '-of', 'json', str(target)], check=True, capture_output=True, text=True)
        media = json.loads(probe.stdout)
        if not is_audio_recording(media):
            raise RuntimeError('Expected an audio recording: ' + str(target))
        record = {'nation': nation, 'file': target.name, 'source_page': page_url,
                  'source_item': index, 'original_url': original_url,
                  'license_as_reported': license_name, 'artists': artists, 'note': note,
                  'bytes': target.stat().st_size, 'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
                  'duration_seconds': float(media['format']['duration']), 'media': media,
                  'source_metadata_file': meta_path.name}
        records.append(record)
        print(f"{nation}: {target.name}, {record['duration_seconds']:.1f}s, {license_name}", flush=True)
        (DEST / 'inventory.json').write_text(json.dumps(records, indent=2) + '\n')
    print(f'Verified {len(records)} original audio files. Inventory: {DEST / "inventory.json"}')
    credits = ['# National music audition credits', '',
               'Original files downloaded without audio edits. Source pages supply the license statements; US government public-domain status is identified for the US. Embedded MP3 artwork has no separate clearance asserted.', '']
    for record in records:
        credits.extend([f"## {record['nation']} — {record['file']}", '',
                        record.get('artists', ''), '',
                        f"Source: {record['source_page']}",
                        f"Reported license: {record['license_as_reported']}",
                        f"License link: {record.get('license_url') or ('https://creativecommons.org/licenses/by/4.0/' if record['license_as_reported'] == 'CC BY 4.0' else 'See source page')}",
                        f"Use note: {record['note']}", ''])
    (DEST / 'CREDITS.md').write_text('\n'.join(credits))

if __name__ == '__main__':
    main()
