"""Convert the offline probe's explicit floor connections into an A* test graph.

This is a data importer, not a client PAK/geometry decoder. It preserves directed
connections; it never infers visibility from a map picture or joins separate floors.
"""
import argparse
import collections
import gzip
import json
import math
from pathlib import Path


def convert(floors):
    cells = collections.defaultdict(list)
    for i, node in enumerate(floors):
        if not all(math.isfinite(node[k]) for k in ('x', 'y', 'z')):
            raise ValueError('Nonfinite position')
        cells[(node['graph'], node['bx'], node['by'])].append(i)
    directions = [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)]
    result = []
    for node in floors:
        links = []
        for d, (dx, dy) in enumerate(directions):
            if node['flags'] & (1 << d):
                continue
            matches = [j for j in cells[(node['graph'], node['bx'] + dx, node['by'] + dy)]
                       if abs(node['z'] - floors[j]['z']) <= .9001
                       and not floors[j]['flags'] & (1 << ((d + 4) % 8))]
            if len(matches) != 1:
                raise ValueError('Missing or ambiguous floor connection')
            links.append(matches[0])
        result.append({'position': {k.upper(): node[k] for k in ('x', 'y', 'z')}, 'neighbors': links})
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--floors', type=Path, required=True)
    parser.add_argument('--map-id', type=int, required=True)
    parser.add_argument('--name', required=True)
    parser.add_argument('--description', default='Offline experimental navigation data; validate in game before use.')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists():
        parser.error('Output already exists; choose a new file.')
    nodes = convert(json.loads(args.floors.read_text(encoding='utf-8')))
    document = {'version': 1, 'mapId': args.map_id, 'name': args.name, 'description': args.description, 'nodes': nodes}
    encoded = json.dumps(document, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(gzip.compress(encoded, mtime=0) if str(args.output).endswith('.gz') else encoded)
    print(f'Wrote {len(nodes)} nodes, {sum(len(n["neighbors"]) for n in nodes)} directed edges, {args.output.stat().st_size} bytes')
