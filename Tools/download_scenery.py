#!/usr/bin/env python3
"""Fetch verified CC0 scenery; preserve sources and convert selected static OBJ meshes.

Run from anywhere. Existing archives are reused; delete one to refresh it.
Output meshes contain geometry and diffuse material colors, not textures or animation.
"""
import hashlib
import json
import pathlib
import shutil
import subprocess
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT / "AssetSources/Scenery"
OUTPUT = ROOT / "Assets/Scripts/Battle/Environment/Resources/RuralScenery"
PACKS = [
    ("kenney_nature-kit.zip", "KenneyNature", "Kenney", "https://kenney.nl/assets/nature-kit",
     "https://kenney.nl/media/pages/assets/nature-kit/37ac38a37b-1677698939/kenney_nature-kit.zip"),
    ("kenney_survival-kit.zip", "KenneySurvival", "Kenney", "https://kenney.nl/assets/survival-kit",
     "https://kenney.nl/media/pages/assets/survival-kit/4065a8185b-1712149243/kenney_survival-kit.zip"),
    ("quaternius_medieval-village.zip", "QuaterniusVillageArchive", "Quaternius",
     "https://opengameart.org/content/lowpoly-medieval-village-pack",
     "https://opengameart.org/sites/default/files/medieval_village_pack_-_dec_2020.zip"),
]


def fetch(url, target):
    if not target.exists():
        subprocess.run(["curl", "-L", "--fail", "--max-time", "90", "-sS", "-o", str(target), url], check=True)


def colors_from_mtl(path):
    colors, current = {}, ""
    for line in path.read_text(errors="replace").splitlines():
        words = line.split()
        if not words:
            continue
        if words[0] == "newmtl":
            current = words[1]
        elif words[0] == "Kd":
            color = [float(v) for v in words[1:4]]
            # Preserve source geometry, adapt Kenney's bright palette to rural greens.
            lower = current.lower()
            if "leaf" in lower or "grass" in lower:
                color = [0.23, 0.37, 0.13] if "dark" not in lower else [0.15, 0.27, 0.10]
            elif "bark" in lower:
                color = [0.31, 0.23, 0.15]
            colors[current] = color
    return colors


def convert(obj, name):
    source_positions, source_normals = [], []
    vertices, normals, colors, triangles = [], [], [], []
    palette = colors_from_mtl(obj.with_suffix(".mtl"))
    current_color = [0.6, 0.55, 0.45]
    for line in obj.read_text(errors="replace").splitlines():
        words = line.split()
        if not words:
            continue
        if words[0] == "v":
            source_positions.append([float(x) for x in words[1:4]])
        elif words[0] == "vn":
            source_normals.append([float(x) for x in words[1:4]])
        elif words[0] == "usemtl":
            current_color = palette.get(words[1], [0.6, 0.55, 0.45])
        elif words[0] == "f":
            face = words[1:]
            for k in range(1, len(face)-1):
                # Reflect Z from OBJ coordinates; reverse winding to match Unity.
                for item in (face[0], face[k+1], face[k]):
                    parts = item.split("/")
                    pos_index = int(parts[0])
                    pos = source_positions[pos_index-1 if pos_index > 0 else pos_index]
                    normal = [0, 1, 0]
                    if len(parts) > 2 and parts[2]:
                        normal_index = int(parts[2])
                        normal = source_normals[normal_index-1 if normal_index > 0 else normal_index]
                    triangles.append(len(vertices)//3)
                    vertices.extend([pos[0], pos[1], -pos[2]])
                    normals.extend([normal[0], normal[1], -normal[2]])
                    colors.extend(current_color + [1.0])
    if not vertices:
        raise ValueError(f"No geometry in {obj}")
    # Center X/Z and put the lowest vertex at ground level.
    center_x = (min(vertices[0::3])+max(vertices[0::3]))/2
    center_z = (min(vertices[2::3])+max(vertices[2::3]))/2
    minimum_y = min(vertices[1::3])
    for i in range(0, len(vertices), 3):
        vertices[i] -= center_x
        vertices[i+1] -= minimum_y
        vertices[i+2] -= center_z
    output = {"vertices": vertices, "normals": normals, "colors": colors, "triangles": triangles}
    (OUTPUT / f"{name}.json").write_text(json.dumps(output, separators=(",", ":")))
    return {"name": name, "source": str(obj.relative_to(ROOT)), "triangles": len(triangles)//3}


def main():
    SOURCE.mkdir(parents=True, exist_ok=True)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    inventory = []
    for filename, folder, author, page, url in PACKS:
        target = SOURCE / filename
        fetch(url, target)
        with zipfile.ZipFile(target) as archive:
            for entry in archive.infolist():
                destination = (SOURCE / folder / entry.filename).resolve()
                if not destination.is_relative_to((SOURCE / folder).resolve()):
                    raise ValueError("Unsafe archive path")
            archive.extractall(SOURCE / folder)
        inventory.append({"archive": filename, "author": author, "license": "CC0-1.0", "page": page,
                          "download": url, "bytes": target.stat().st_size,
                          "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
    native = SOURCE / "KenneyNature/Models/OBJ format"
    village = SOURCE / "QuaterniusVillageArchive/Medieval Village Pack - Dec 2020/Buildings/OBJ"
    assets = []
    for name in ["tree_oak", "tree_oak_dark", "tree_default", "fence_simple", "grass_large", "rock_largeA", "crops_wheatStageB"]:
        obj = native / f"{name}.obj"
        if obj.exists():
            assets.append(convert(obj, name))
    for source_name, name in [("House_1", "farmhouse"), ("House_2", "farmhouse_b"), ("Stable", "stable")]:
        assets.append(convert(village / f"{source_name}.obj", name))
    diffuse = SOURCE / "leafy_grass_diff_1k.jpg"
    texture_url = "https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/leafy_grass/leafy_grass_diff_1k.jpg"
    fetch(texture_url, diffuse)
    if hashlib.md5(diffuse.read_bytes()).hexdigest() != "0dbc071e91d6905edfcfbe8eb785a1ab":
        raise ValueError("Poly Haven diffuse checksum differs from verified API response")
    shutil.copy2(diffuse, OUTPUT / diffuse.name)
    inventory.append({"file": diffuse.name, "author": "Charlotte Baglioni / Poly Haven", "license": "CC0-1.0",
                      "page": "https://polyhaven.com/a/leafy_grass", "download": texture_url,
                      "bytes": diffuse.stat().st_size, "sha256": hashlib.sha256(diffuse.read_bytes()).hexdigest()})
    bark = SOURCE / "bark_brown_02_diff_1k.jpg"
    bark_url = "https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/bark_brown_02/bark_brown_02_diff_1k.jpg"
    fetch(bark_url, bark)
    if hashlib.md5(bark.read_bytes()).hexdigest() != "6669cca520594bdca19f94084984ab72":
        raise ValueError("Poly Haven bark checksum differs from verified API response")
    shutil.copy2(bark, OUTPUT / bark.name)
    inventory.append({"file": bark.name, "author": "Rob Tuytel / Poly Haven", "license": "CC0-1.0",
                      "page": "https://polyhaven.com/a/bark_brown_02", "download": bark_url,
                      "bytes": bark.stat().st_size, "sha256": hashlib.sha256(bark.read_bytes()).hexdigest()})
    (SOURCE / "manifest.json").write_text(json.dumps({"sources": inventory, "converted_meshes": assets}, indent=2))
    print(json.dumps({"converted_meshes": assets, "downloaded_bytes": sum(x["bytes"] for x in inventory)}, indent=2))


if __name__ == "__main__":
    main()
