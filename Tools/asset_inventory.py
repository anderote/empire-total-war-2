#!/usr/bin/env python3
"""List a completed source archive; never extract files or execute archive contents."""
import argparse
from collections import Counter
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys


KEYWORDS = {
    "infantry": r"infantry|grenadier|fusilier|soldier|uniform",
    "french": r"french|france|francais|français",
    "british": r"british|english|england|britain|redcoat",
    "horse": r"horse|equine|steed",
    "cavalry": r"cavalry|hussar|dragoon|cuirassier|lancer|rider",
    "cannon": r"cannon|artillery|howitzer|gun_crew|guncrew",
    "musket": r"musket|flintlock|bayonet|rifle",
    "animations": r"anim|skeleton|rigged|reload|shoot|firing|walk|gallop|idle",
}


def suspicious_path(name):
    normalized = name.replace("\\", "/")
    return (normalized.startswith("/") or bool(re.match(r"^[A-Za-z]:", normalized))
            or ".." in PurePosixPath(normalized).parts)


def parse_listing(listing):
    records = []
    current = {}
    for line in listing.splitlines() + [""]:
        if not line.strip():
            if "Path" in current:
                records.append(current)
            current = {}
        elif " = " in line:
            key, value = line.split(" = ", 1)
            current[key] = value
    return records


def inventory(archive, executable):
    archive = Path(archive).expanduser().resolve(strict=True)
    if not archive.is_file():
        raise ValueError("Archive must be a completed file.")
    if archive.suffix.lower() in {".crdownload", ".part", ".partial", ".download", ".tmp"}:
        raise ValueError("Download is incomplete; wait for the final archive filename.")
    if archive.suffix.lower() not in {".rar", ".zip", ".7z"}:
        raise ValueError("Expected a completed .rar, .zip, or .7z archive.")
    before = archive.stat()
    # A list-only operation with stdin disconnected: encrypted archives cannot block for a password.
    result = subprocess.run([executable, "l", "-slt", "-ba", "--", str(archive)],
                            stdin=subprocess.DEVNULL, capture_output=True, text=True,
                            encoding="utf-8", errors="replace", timeout=180)
    if result.returncode != 0:
        raise ValueError("7zz could not list the archive: " + result.stderr.strip()[-2000:])
    after = archive.stat()
    if before.st_size != after.st_size or before.st_mtime_ns != after.st_mtime_ns:
        raise ValueError("Archive changed during inspection; wait until download completes.")
    files = []
    extensions = Counter()
    matches = {key: [] for key in KEYWORDS}
    unsafe = []
    for record in parse_listing(result.stdout):
        if record.get("Folder") == "+" or record.get("Attributes", "").startswith("D"):
            continue
        name = record["Path"]
        suffix = PurePosixPath(name.replace("\\", "/")).suffix.lower() or "(no extension)"
        extensions[suffix] += 1
        try:
            size = int(record.get("Size", "0"))
        except ValueError:
            size = 0
        files.append({"path": name, "size": size, "extension": suffix})
        if suspicious_path(name):
            unsafe.append(name)
        for category, pattern in KEYWORDS.items():
            if re.search(pattern, name, re.IGNORECASE):
                matches[category].append(name)
    if not files:
        raise ValueError("No files were listed. This may be an unsupported or encrypted archive.")
    return {"archive": str(archive), "archive_bytes": after.st_size,
            "file_count": len(files), "uncompressed_bytes": sum(f["size"] for f in files),
            "extensions": dict(sorted(extensions.items())),
            "source_formats": {"fbx_files": extensions[".fbx"], "png_files": extensions[".png"]},
            "keyword_matches": matches, "suspicious_paths": unsafe, "files": files,
            "limitations": ["Names identify candidates only; models, licenses, rigs and animation clips have not been verified.",
                            "Animations can be embedded in FBX files without animation keywords in their names.",
                            "Listing does not verify archive integrity or extract any content."]}


def report_text(data):
    lines = ["# Bannerlord asset archive inventory", "", f"Archive: {data['archive']}",
             f"Files: {data['file_count']:,}",
             f"Uncompressed bytes: {data['uncompressed_bytes']:,}",
             f"FBX: {data['source_formats']['fbx_files']:,}; PNG: {data['source_formats']['png_files']:,}",
             "", "## Extensions", ""]
    lines += [f"- {extension}: {count:,}" for extension, count in data["extensions"].items()]
    for category, names in data["keyword_matches"].items():
        lines += ["", f"## {category.title()} candidates ({len(names):,})", ""]
        lines += [f"- {name}" for name in names[:100]] or ["- No filename matches."]
        if len(names) > 100:
            lines += ["- Further matches are retained in the JSON output."]
    lines += ["", "## Inspection limits", ""]
    lines += [f"- {note}" for note in data["limitations"]]
    if data["suspicious_paths"]:
        lines += ["", "## Suspicious archive paths — do not extract without review", ""]
        lines += [f"- {name}" for name in data["suspicious_paths"]]
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--json", dest="json_path", type=Path, help="Optional JSON inventory output.")
    parser.add_argument("--report", type=Path, help="Optional Markdown report output.")
    parser.add_argument("--sevenzip", default=shutil.which("7zz"), help="Path to 7zz (defaults to PATH).")
    args = parser.parse_args()
    try:
        if not args.sevenzip:
            raise ValueError("7zz is required; install sevenzip or supply --sevenzip.")
        data = inventory(args.archive, args.sevenzip)
        for output in (args.json_path, args.report):
            if output and output.expanduser().resolve() == Path(data["archive"]):
                raise ValueError("An output cannot overwrite the source archive.")
        if args.json_path and args.report and args.json_path.expanduser().resolve() == args.report.expanduser().resolve():
            raise ValueError("JSON and report outputs must have different paths.")
        if args.json_path:
            args.json_path.expanduser().write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        if args.report:
            args.report.expanduser().write_text(report_text(data), encoding="utf-8")
        print(f"Listed {data['file_count']:,} files; FBX {data['source_formats']['fbx_files']:,}; PNG {data['source_formats']['png_files']:,}.")
        print("Candidates: " + ", ".join(f"{key} {len(names)}" for key, names in data["keyword_matches"].items()))
        if data["suspicious_paths"]:
            print(f"Flagged {len(data['suspicious_paths'])} suspicious paths; extraction requires review.")
        if not args.json_path and not args.report:
            print(json.dumps({key: value for key, value in data.items() if key != "files"}, indent=2, ensure_ascii=False))
        return 0
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"Inventory failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
