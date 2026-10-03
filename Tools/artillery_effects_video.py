#!/usr/bin/env python3
"""Capture the actual game renderer, encode labeled MP4s and a comparison montage.

Call only in an allocated Unity/render slot. This never launches a native game or
touches installed playtests. Unity operations use the shared serialization wrapper.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
NAMES = {"Roundshot": "Roundshot", "Explosive": "Explosive shell",
         "Carcass": "Carcass", "Lime": "Lime", "Canister": "Canister"}


def run(argv, **kwargs):
    return subprocess.run([str(x) for x in argv], check=True, **kwargs)


def probe(path):
    result = run(["ffprobe", "-v", "error", "-count_frames", "-select_streams", "v:0",
                  "-show_entries", "stream=codec_name,width,height,avg_frame_rate,nb_read_frames:format=duration",
                  "-of", "json", path], capture_output=True, text=True)
    return json.loads(result.stdout)


def validate_video(path, config, count):
    metadata = probe(path)
    stream = metadata["streams"][0]
    assert stream["codec_name"] == "h264", metadata
    assert (stream["width"], stream["height"]) == (config["width"], config["height"]), metadata
    assert stream["avg_frame_rate"] == f'{config["fps"]}/1', metadata
    assert int(stream["nb_read_frames"]) == count, metadata
    assert abs(float(metadata["format"]["duration"]) - count/config["fps"]) < .05, metadata
    run(["ffmpeg", "-v", "error", "-i", path, "-f", "null", "-"], stdout=subprocess.DEVNULL)
    return metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=ROOT/"Tools/artillery_effects_video.json")
    parser.add_argument("--output", type=Path, required=True, help="fresh deliverable directory")
    parser.add_argument("--ammo", nargs="+", choices=tuple(NAMES), help="override selected variants")
    parser.add_argument("--verification-log", type=Path, help="reuse a successful verification from this source revision")
    parser.add_argument("--keep-frames", action="store_true", help="retain exact raw captures for detailed review")
    args = parser.parse_args()
    if not shutil.which("ffmpeg") or not shutil.which("ffprobe"):
        parser.error("ffmpeg and ffprobe are required")
    output = args.output.resolve()
    if output.exists():
        parser.error("Choose a fresh output directory; existing captures are never replaced")
    config = json.loads(args.config.read_text())
    ammunition = args.ammo or config.pop("ammunition")
    config.pop("ammunition", None)
    if not ammunition or any(x not in NAMES for x in ammunition):
        parser.error("Unsupported ammunition name")
    font = Path("/System/Library/Fonts/Supplemental/Arial.ttf")
    if not font.is_file():
        parser.error("Expected macOS label font is unavailable")
    free = shutil.disk_usage(output.parent if output.parent.exists() else ROOT).free
    if free < 2*1024**3:
        parser.error("At least 2 GiB free on the internal disk is required")
    output.mkdir(parents=True)
    (output/"config.json").write_text(json.dumps({**config, "ammunition": ammunition}, indent=2)+"\n")
    evidence = {"config": config, "ammunition": ammunition, "videos": [], "native_game_launches": 0}
    # Serialized verification uses actual gameplay, with two same-seed runs per core type.
    if args.verification_log:
        log = args.verification_log.read_text()
        if "ARTILLERY_VIEWER_VERIFIED" not in log or "Application will exit with return code 0" not in log:
            raise ValueError("The supplied verification did not succeed")
        shutil.copy2(args.verification_log, output/"verification.log")
    else:
        run([sys.executable, ROOT/"Tools/unity_editor.py", "-executeMethod", "ArtilleryEffectsViewer.Verify",
             "-logFile", output/"verification.log"], cwd=ROOT)
    assert "ARTILLERY_VIEWER_VERIFIED" in (output/"verification.log").read_text()
    for ammo in ammunition:
        print(f"Capturing {NAMES[ammo]} through the actual game renderer", flush=True)
        with tempfile.TemporaryDirectory(prefix="etw2-artillery-", dir=output) as temporary:
            temp = Path(temporary)
            frames = temp/"frames"
            selected = {**config, "ammunition": ammo, "output": str(frames)}
            job = temp/"capture.json"
            job.write_text(json.dumps(selected))
            env = os.environ.copy()
            env["ETW2_ARTILLERY_VIDEO_CONFIG"] = str(job)
            run([sys.executable, ROOT/"Tools/unity_editor.py", "--graphics", "--self-exit",
                 "-executeMethod", "ArtilleryEffectsViewer.Capture", "-logFile", output/f"{ammo.lower()}.log"], cwd=ROOT, env=env)
            receipt = json.loads((frames/"receipt.json").read_text())
            count = round(config["seconds"]*config["fps"])
            assert receipt["frames"] == count and receipt["shots"] == 1 and receipt["impacts"] > 0, receipt
            assert len(list(frames.glob("frame-*.png"))) == count
            receipt["config"]["output"] = "temporary frames removed after successful encode"
            (output/f"{ammo.lower()}-receipt.json").write_text(json.dumps(receipt, indent=2)+"\n")
            label = temp/"label.png"
            overlay = Image.new("RGBA", (config["width"], config["height"]), (0, 0, 0, 0))
            drawing = ImageDraw.Draw(overlay)
            drawing.rectangle((0, 0, config["width"], 57), fill=(0, 0, 0, 184))
            drawing.text((18, 14), f'{NAMES[ammo]} | {config["range"]} m | {config["soldiers"]} infantry | seed {config["seed"]}',
                         font=ImageFont.truetype(str(font), 24), fill="white")
            drawing.text((18, config["height"]-29), "Actual game renderer",
                         font=ImageFont.truetype(str(font), 16), fill="white", stroke_width=1, stroke_fill="black")
            overlay.save(label)
            video = output/f"{ammo.lower()}.mp4"
            run(["ffmpeg", "-hide_banner", "-loglevel", "warning", "-framerate", config["fps"],
                 "-i", frames/"frame-%05d.png", "-loop", "1", "-framerate", config["fps"], "-i", label,
                 "-filter_complex", "[0:v][1:v]overlay=0:0:shortest=1", "-frames:v", count,
                 "-c:v", "libx264", "-preset", "medium", "-crf", "19", "-pix_fmt", "yuv420p",
                 "-movflags", "+faststart", video])
            metadata = validate_video(video, config, count)
            # Review contact sheet contains setup, flash, approach, impact and aftermath.
            sample_times = [0, config["fireAt"] + 1/config["fps"], config["closeAt"],
                receipt["firstImpact"], receipt["firstImpact"] + .4,
                config["seconds"]*.4, config["seconds"]*.65, config["seconds"] - .5]
            samples = sorted(set(max(0, min(count-1, round(t*config["fps"]))) for t in sample_times))
            select = "+".join(f"eq(n,{n})" for n in samples)
            run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", video,
                 "-vf", f"select='{select}',scale=480:270,tile=4x2:nb_frames={len(samples)}",
                 "-frames:v", "1", output/f"{ammo.lower()}-review.png"])
            evidence["videos"].append({"file": video.name, "media": metadata,
                "sha256": hashlib.sha256(video.read_bytes()).hexdigest(), "simulationDigest": receipt["simulationDigest"]})
            if args.keep_frames:
                shutil.move(str(frames), str(output/f"{ammo.lower()}-frames"))
    concatenation = output/"montage-inputs.txt"
    concatenation.write_text("".join(f"file '{ammo.lower()}.mp4'\n" for ammo in ammunition))
    montage = output/"artillery-comparison.mp4"
    run(["ffmpeg", "-hide_banner", "-loglevel", "warning", "-f", "concat", "-safe", "0",
         "-i", concatenation, "-c", "copy", "-movflags", "+faststart", montage])
    evidence["montage"] = validate_video(montage, config, round(config["seconds"]*config["fps"])*len(ammunition))
    evidence["free_bytes_after_cleanup"] = shutil.disk_usage(output).free
    evidence["raw_frames_retained"] = args.keep_frames
    (output/"validation.json").write_text(json.dumps(evidence, indent=2)+"\n")
    print(f"Verified labeled individual videos and montage: {output}", flush=True)


if __name__ == "__main__":
    main()
