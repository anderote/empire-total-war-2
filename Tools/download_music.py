#!/usr/bin/env python3
"""Download authorized playlist audio, keep metadata, and resume without transcoding."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import sys

DEFAULT_PLAYLIST = "https://www.youtube.com/playlist?list=PLfbHiSO3YRsjKGsyBOMR7j42AKayHJ4JP"
ROOT = Path(__file__).resolve().parents[1]


def summarize(destination, playlist_url):
    index_path = destination / "playlist-index.json"
    index = json.loads(index_path.read_text()) if index_path.exists() else {}
    info = {}
    for directory in (destination / "metadata", destination / "audio"):
        for path in directory.glob("*.info.json"):
            data = json.loads(path.read_text())
            if data.get("_type") != "playlist":
                info[data.get("id")] = data
    media = [p for p in (destination / "audio").glob("*")
             if p.suffix.lower() in {".webm", ".m4a", ".mp3", ".ogg", ".opus", ".flac", ".aac", ".wav"}]
    tracks = []
    for entry in index.get("entries", []):
        if not entry:
            continue
        video_id = entry.get("id")
        metadata = info.get(video_id, {})
        files = [p for p in media if f"_{video_id}." in p.name]
        license_name = metadata.get("license")
        tracks.append({"id": video_id, "title": metadata.get("title", entry.get("title")),
                       "url": metadata.get("webpage_url", f"https://www.youtube.com/watch?v={video_id}"),
                       "uploader": metadata.get("uploader", entry.get("uploader")),
                       "duration": metadata.get("duration"), "availability": metadata.get("availability", entry.get("availability")),
                       "license": license_name,
                       "license_status": "YouTube metadata indicates Creative Commons" if license_name and "creative commons" in license_name.lower()
                       else "User describes material as open source; no explicit open license verified",
                       "description": metadata.get("description"),
                       "downloaded": bool(files), "files": [str(p.relative_to(destination)) for p in files],
                       "bytes": sum(p.stat().st_size for p in files)})
    result = {"playlist_url": playlist_url, "playlist_title": index.get("title"),
              "listed_tracks": len(tracks), "downloaded_tracks": sum(t["downloaded"] for t in tracks),
              "total_audio_bytes": sum(t["bytes"] for t in tracks), "tracks": tracks,
              "notes": ["Original best-audio container preserved; no re-encoding performed.",
                        "Recording rights are distinct from the underlying compositions; open-source status is not established by an old composition or private use.",
                        "No authentication, age restriction, paywall or private-video access controls are bypassed."]}
    (destination / "music-inventory.json").write_text(json.dumps(result, indent=2, ensure_ascii=False)+"\n")
    lines = ["# Playlist audio inventory", "", f"Source: {playlist_url}",
             f"Downloaded: {result['downloaded_tracks']}/{result['listed_tracks']}",
             f"Audio size: {result['total_audio_bytes']/1024/1024:.1f} MiB", "",
             "| Track | Status | Uploader | Explicit license metadata |", "| --- | --- | --- | --- |"]
    for track in tracks:
        title = (track["title"] or track["id"]).replace("|", "\\|")
        uploader = (track["uploader"] or "Unknown").replace("|", "\\|")
        lines.append(f"| [{title}]({track['url']}) | {'Downloaded' if track['downloaded'] else 'Unavailable / see download.log'} | {uploader} | {track['license'] or 'Not supplied'} |")
    lines += ["", *result["notes"], "", "Per-track full source descriptions and uploader identifiers remain in the JSON sidecars. See `metadata.log` and `download.log` for access errors."]
    (destination / "INVENTORY.md").write_text("\n".join(lines)+"\n")
    return result


def run_logged(command, log_path):
    with log_path.open("a", encoding="utf-8") as log:
        process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                   stderr=subprocess.STDOUT, text=True, errors="replace")
        for line in process.stdout:
            log.write(line)
            log.flush()
            print(line, end="", flush=True)
        return process.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--playlist", default=DEFAULT_PLAYLIST)
    parser.add_argument("--summarize-only", action="store_true")
    parser.add_argument("--refresh-metadata", action="store_true")
    args = parser.parse_args()
    destination = ROOT / "AssetSources" / "Music"
    destination.mkdir(parents=True, exist_ok=True)
    executable = shutil.which("yt-dlp")
    if not executable and not args.summarize_only:
        parser.error("yt-dlp is required.")
    if not args.summarize_only:
        index = destination / "playlist-index.json"
        if not index.exists() or args.refresh_metadata:
            result = subprocess.run([executable, "--ignore-config", "--flat-playlist", "--dump-single-json", "--skip-download", args.playlist],
                                    stdin=subprocess.DEVNULL, capture_output=True, text=True)
            if result.returncode:
                print(result.stderr, file=sys.stderr)
                return result.returncode
            json.loads(result.stdout)  # Validate before replacing the index.
            index.write_text(result.stdout)
        common = [executable, "--ignore-config", "--ignore-errors", "--no-overwrites", "--continue", "--socket-timeout", "25", "--retries", "3"]
        if shutil.which("node"):
            common += ["--js-runtimes", "node"]
        if args.refresh_metadata or not (destination / "metadata").exists():
            run_logged(common + ["--skip-download", "--write-info-json", "--paths", str(destination/"metadata"),
                                  "--output", "%(playlist_index)03d_%(id)s.%(ext)s", args.playlist], destination/"metadata.log")
        run_logged(common + ["--format", "bestaudio", "--write-info-json", "--download-archive", str(destination/"download-archive.txt"),
                              "--paths", str(destination/"audio"), "--output", "%(playlist_index)03d_%(id)s.%(ext)s", args.playlist], destination/"download.log")
    result = summarize(destination, args.playlist)
    print(f"Downloaded {result['downloaded_tracks']}/{result['listed_tracks']} tracks; {result['total_audio_bytes']/1024/1024:.1f} MiB.")
    return 0 if result["listed_tracks"] and result["downloaded_tracks"] == result["listed_tracks"] else 1


if __name__ == "__main__":
    sys.exit(main())
