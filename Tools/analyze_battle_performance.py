#!/usr/bin/env python3
"""Summarize completed battle captures. Nested stage timings are not additive."""
import argparse
import csv
import json
import statistics
from pathlib import Path


def summarize(folder):
    folder = Path(folder)
    report = json.loads((folder / 'report.json').read_text())
    run = json.loads((folder / 'run.json').read_text())
    with (folder / 'frames.csv').open() as handle:
        rows = list(csv.DictReader(handle))
    if not rows:
        raise ValueError(f'No measured frames in {folder}')
    stages = []
    for key in rows[0]:
        if not key.endswith('Ms') or key[:-2] + 'Calls' not in rows[0]:
            continue
        total = sum(float(row[key]) for row in rows)
        calls = sum(float(row[key[:-2] + 'Calls']) for row in rows)
        stages.append({'stage': key[:-2], 'meanMsPerFrame': total / len(rows),
                       'meanMsPerCall': total / calls if calls else 0,
                       'calls': int(calls)})
    gpu = [float(row['gpuMs']) for row in rows if float(row['gpuMs']) > 0]
    return {'capture': str(folder), 'frames': len(rows),
            'assemblySha256': run['assembly_sha256'], 'command': run['command'],
            'medianFrameMs': report['medianFrameMs'], 'p95FrameMs': report['p95FrameMs'],
            'medianFps': 1000 / report['medianFrameMs'],
            'medianGpuMs': statistics.median(gpu) if gpu else None,
            'medianSimulationMs': report['medianSimulationMs'],
            'survivors': report['survivors'],
            'meanCrowdDrawCalls': statistics.mean(float(row['crowdDrawCalls']) for row in rows),
            'stages': sorted(stages, key=lambda stage: -stage['meanMsPerFrame'])}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('captures', nargs='+', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    result = {'note': 'Nested timings overlap; do not sum stages. Compare identical scenarios and build hashes.',
              'captures': [summarize(folder) for folder in args.captures]}
    encoded = json.dumps(result, indent=2) + '\n'
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(encoded)
    for capture in result['captures']:
        print(f"{Path(capture['capture']).name}: {capture['medianFps']:.1f} FPS, "
              f"median {capture['medianFrameMs']:.2f} ms, p95 {capture['p95FrameMs']:.2f} ms, "
              f"GPU {capture['medianGpuMs']} ms")
        for stage in capture['stages'][:10]:
            print(f"  {stage['stage']}: {stage['meanMsPerFrame']:.3f} ms/frame, "
                  f"{stage['meanMsPerCall']:.3f} ms/call")


if __name__ == '__main__':
    main()
