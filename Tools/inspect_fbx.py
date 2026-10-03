#!/usr/bin/env python3
"""Read FBX metadata without running Blender/Unity or executing source content."""
import argparse, collections, json, pathlib, struct, zlib, re

class Reader:
    def __init__(self, path):
        self.f = open(path, 'rb')
        if self.f.read(23) != b'Kaydara FBX Binary  \x00\x1a\x00':
            raise ValueError('Not binary FBX')
        self.version = struct.unpack('<I', self.f.read(4))[0]
        self.wide = self.version >= 7500
    def prop(self):
        kind = self.f.read(1).decode('ascii')
        scalar = {'Y': ('h', 2), 'C': ('?', 1), 'I': ('i', 4), 'F': ('f', 4), 'D': ('d', 8), 'L': ('q', 8)}
        if kind in scalar:
            fmt, size = scalar[kind]
            return struct.unpack('<' + fmt, self.f.read(size))[0]
        if kind in 'SR':
            length = struct.unpack('<I', self.f.read(4))[0]
            if kind == 'R':
                self.f.seek(length, 1); return {'raw_bytes': length}
            return self.f.read(length).decode('utf8', errors='replace')
        if kind in 'fdilbc':
            count, encoding, length = struct.unpack('<III', self.f.read(12))
            # Decode only useful numerical arrays; polygon topology and animation timing need values.
            data = self.f.read(length)
            return {'kind': kind, 'count': count, 'encoding': encoding, 'bytes': data}
        raise ValueError('Unknown property ' + repr(kind))
    def node(self):
        header = self.f.read(25 if self.wide else 13)
        if len(header) < (25 if self.wide else 13): return None
        end, count, prop_bytes, name_bytes = struct.unpack('<QQQB' if self.wide else '<IIIB', header)
        if end == 0: return None
        name = self.f.read(name_bytes).decode('utf8', errors='replace')
        props = [self.prop() for _ in range(count)]
        children = []
        while self.f.tell() < end:
            child = self.node()
            if child is None: break
            children.append(child)
        self.f.seek(end)
        return {'name': name, 'props': props, 'children': children}
    def roots(self):
        nodes = []
        while True:
            node = self.node()
            if node is None: return nodes
            nodes.append(node)

def values(array):
    raw = array['bytes']
    if array['encoding'] == 1: raw = zlib.decompress(raw)
    fmt = {'f':'f', 'd':'d', 'i':'i', 'l':'q', 'b':'?', 'c':'B'}[array['kind']]
    return [v[0] for v in struct.iter_unpack('<' + fmt, raw)]

def walk(nodes):
    for n in nodes:
        yield n
        yield from walk(n['children'])

def clean(value): return value.split('\x00')[0] if isinstance(value, str) else value

def inspect_ascii(path):
    content = path.read_text(errors='replace')
    models = [{'id': int(i), 'name': name, 'type': kind} for i, name, kind in re.findall(r'Model:\s*(\d+),\s*"Model::([^"]*)",\s*"([^"]*)"', content)]
    polygons = re.findall(r'PolygonVertexIndex:\s*\*\d+\s*\{\s*a:\s*([^}]+)', content)
    meshes = []
    for poly in polygons:
        corners = 0; triangles = 0; count = 0
        for index in map(int, poly.replace('\n','').replace('\t','').split(',')):
            corners += 1
            if index < 0: triangles += max(0, corners-2); count += 1; corners = 0
        meshes.append({'triangles': triangles, 'polygons': count})
    return {'path': str(path), 'bytes': path.stat().st_size, 'format': 'ASCII',
            'fbx_version': int(re.search(r'FBXVersion:\s*(\d+)', content).group(1)),
            'models': models, 'bones': [m['name'] for m in models if m['type'] == 'LimbNode'],
            'bone_count': sum(m['type']=='LimbNode' for m in models), 'meshes': meshes,
            'triangles': sum(m['triangles'] for m in meshes),
            'materials': re.findall(r'Material:\s*\d+,\s*"Material::([^"]*)"', content),
            'cluster_count': len(re.findall(r'Deformer:\s*\d+,[^\n]+"Cluster"',content)),
            'note': 'ASCII summary only; mesh arrays and transforms omitted'}

def inspect(path):
    with path.open('rb') as probe:
        if probe.read(23) != b'Kaydara FBX Binary  \x00\x1a\x00': return inspect_ascii(path)
    reader = Reader(path)
    try: roots = reader.roots()
    finally: reader.f.close()
    nodes = list(walk(roots))
    result = {'path': str(path), 'bytes': path.stat().st_size, 'fbx_version': reader.version,
              'meshes': [], 'bones': [], 'models': [], 'materials': [], 'textures': [], 'animation_stacks': [], 'animation_curve_count': 0,
              'skin_count': 0, 'cluster_count': 0, 'weighted_vertices': 0}
    times = []
    for n in nodes:
        name, p = n['name'], n['props']
        if name == 'Geometry' and len(p) >= 3 and p[2] == 'Mesh':
            mesh = {'id': p[0], 'name': clean(p[1]), 'vertices': 0, 'triangles': 0, 'polygons': 0}
            for child in n['children']:
                if child['name'] == 'Vertices' and child['props']:
                    xyz = values(child['props'][0]); mesh['vertices'] = len(xyz) // 3
                    if xyz: mesh['bounds'] = [[min(xyz[i::3]), max(xyz[i::3])] for i in range(3)]
                if child['name'] == 'PolygonVertexIndex' and child['props']:
                    corners = 0
                    for index in values(child['props'][0]):
                        corners += 1
                        if index < 0:
                            mesh['polygons'] += 1; mesh['triangles'] += max(0, corners - 2); corners = 0
            result['meshes'].append(mesh)
        elif name == 'Model' and len(p) >= 3:
            model = {'id': p[0], 'name': clean(p[1]), 'type': p[2]}
            for prop in walk(n['children']):
                if prop['name'] == 'P' and prop['props'] and prop['props'][0] in ('Lcl Scaling', 'Lcl Translation', 'Lcl Rotation'):
                    model[prop['props'][0]] = prop['props'][-3:]
            result['models'].append(model)
            if p[2] in ('LimbNode', 'Root'): result['bones'].append(clean(p[1]))
        elif name == 'Material' and len(p) >= 2: result['materials'].append(clean(p[1]))
        elif name in ('RelativeFilename', 'FileName') and p and isinstance(p[0], str): result['textures'].append(p[0])
        elif name == 'Deformer' and len(p) >= 3:
            if p[2] == 'Skin': result['skin_count'] += 1
            if p[2] == 'Cluster':
                result['cluster_count'] += 1
                for child in n['children']:
                    if child['name'] == 'Indexes' and child['props']: result['weighted_vertices'] += child['props'][0]['count']
        elif name == 'AnimationStack' and len(p) >= 2: result['animation_stacks'].append(clean(p[1]))
        elif name == 'AnimationCurve': result['animation_curve_count'] += 1
        elif name == 'KeyTime' and p:
            keys = values(p[0])
            if keys: times.extend([min(keys), max(keys)])
        elif name == 'P' and p and p[0] in ('UnitScaleFactor', 'OriginalUnitScaleFactor', 'UpAxis', 'UpAxisSign', 'FrontAxis', 'FrontAxisSign', 'CoordAxis', 'CoordAxisSign'):
            result[p[0]] = p[-1]
    model_map = {m['id']: m for m in result['models']}
    geometry_map = {m['id']: m for m in result['meshes']}
    for node in nodes:
        p = node['props']
        if node['name'] == 'C' and len(p) >= 3 and p[0] == 'OO' and p[1] in geometry_map and p[2] in model_map:
            geometry_map[p[1]]['model'] = model_map[p[2]]
            geometry_map[p[1]]['name'] = model_map[p[2]]['name']
    result['bone_count'] = len(result['bones'])
    result['vertices'] = sum(m['vertices'] for m in result['meshes'])
    result['triangles'] = sum(m['triangles'] for m in result['meshes'])
    result['textures'] = sorted(set(result['textures']))
    if times: result['animation_seconds'] = [min(times) / 46186158000, max(times) / 46186158000]
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('source'); parser.add_argument('--output', required=True)
    args = parser.parse_args(); root = pathlib.Path(args.source)
    paths = sorted(p for p in root.rglob('*') if p.suffix.lower() == '.fbx') if root.is_dir() else [root]
    reports = []
    for index, path in enumerate(paths):
        try: report = inspect(path)
        except Exception as error: report = {'path': str(path), 'error': str(error)}
        reports.append(report)
        if index % 25 == 0: print(f'{index+1}/{len(paths)} inspected', flush=True)
    pathlib.Path(args.output).write_text(json.dumps(reports, indent=2))
    print(f'{len(reports)} FBX reports, {sum("error" in r for r in reports)} errors')
