"""Level-design checks for Arena maps (no Unity needed).

Usage:
  python Tools/map_check.py Tools/maps/snow_valley_lb_quarter.txt --quarter [--fog] [--print]
  python Tools/map_check.py <full rows file>               (team maps: rows top first)

Input: text rows in the DataSeeder / MapConfig legend (MapConverter):
  . floor  # wall  % breakable wall  " bush  ~ water  $ berry box  S shared spawn  A/B team spawn  H hollow (floor)
--quarter: the file is the top-left quarter (N x N, last column and last row are the mirror axes), mirrored like
DataSeeder.MirrorQuarter (left<->right, then top<->bottom) into a (2N-1) x (2N-1) map.
Lines starting with ';' are comments.

Checks: legend, connectivity, reachability of every walkable cell by the largest body (radius 0.55) on a half-cell grid,
1-cell pinches, spawn -> loot / spawn -> spawn / spawn -> centre path lengths (8 neighbours, no corner cutting, like the
bots' A*), boxes per spawn (nearest spawn by path), longest open shot lines, cover density, rings by fog step, final
safe square, bush clusters. Parameters below mirror the DataSeeder values (speed 2.5, fog 40 s / 20 s / 2 cells / 2.5).
"""
import heapq
import math
import sys
from collections import deque

BIG_RADIUS = 0.55          # RadiusLarge (Bruno)
SPEED = 2.5                # NormalSpeed, cells/s
FOG_START, FOG_STEP, FOG_SHRINK, FOG_MIN_HALF = 40.0, 20.0, 2.0, 2.5
BOX_HP, PIX_BURST, PIX_RELOAD = 3600, 4 * 380, 1.4
COVER_RADIUS = 4           # every non-edge walkable cell should have cover/bush within this Chebyshev distance
EDGE_BAND = 2              # rows/cols this close to the border are exempt (fog takes them first)

BLOCK_MOVE = set('#%~$')
BLOCK_SHOT = set('#%$')
WALKABLE = set('."SABH')
LEGEND = set('.#%"~$SABH')


def mirror_quarter(q):
    rows = [r + r[-2::-1] for r in q]
    return rows + rows[-2::-1]


def load(path, quarter):
    with open(path, encoding='utf-8') as f:
        rows = [ln.rstrip('\n') for ln in f if ln.strip() and not ln.startswith(';')]
    return mirror_quarter(rows) if quarter else rows


class Map:
    def __init__(self, rows):
        self.rows = rows
        self.h = len(rows)
        self.w = len(rows[0])
        for i, r in enumerate(rows):
            assert len(r) == self.w, 'row %d length %d != %d' % (i, len(r), self.w)
            for ch in r:
                assert ch in LEGEND, 'unknown char %r in row %d' % (ch, i)

    def at(self, x, y):  # x = column, y = text row (0 top); outside = wall
        if 0 <= x < self.w and 0 <= y < self.h:
            return self.rows[y][x]
        return '#'

    def cells(self, chars):
        return [(x, y) for y in range(self.h) for x in range(self.w) if self.rows[y][x] in chars]

    def walk(self, x, y):
        return self.at(x, y) not in BLOCK_MOVE


def bfs4(m, start):
    seen = {start}
    q = deque([start])
    while q:
        x, y = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            n = (x + dx, y + dy)
            if n not in seen and m.walk(*n) and 0 <= n[0] < m.w and 0 <= n[1] < m.h:
                seen.add(n)
                q.append(n)
    return seen


def dijkstra8(m, start):
    """Octile distances like GridPathfinder: diagonal only if both orthogonal neighbours are free."""
    dist = {start: 0.0}
    pq = [(0.0, start)]
    while pq:
        d, (x, y) = heapq.heappop(pq)
        if d > dist[(x, y)]:
            continue
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                nx, ny = x + dx, y + dy
                if not (0 <= nx < m.w and 0 <= ny < m.h) or not m.walk(nx, ny):
                    continue
                if dx and dy and (not m.walk(x + dx, y) or not m.walk(x, y + dy)):
                    continue
                nd = d + (1.4142 if dx and dy else 1.0)
                if nd < dist.get((nx, ny), 1e9):
                    dist[(nx, ny)] = nd
                    heapq.heappush(pq, (nd, (nx, ny)))
    return dist


def big_body_reach(m, start):
    """Half-cell grid points where a body of BIG_RADIUS fits, flood-filled from the start cell centre."""
    r2 = BIG_RADIUS * BIG_RADIUS

    def fits(px, py):
        for cy in range(int(math.floor(py - BIG_RADIUS)), int(math.floor(py + BIG_RADIUS)) + 1):
            for cx in range(int(math.floor(px - BIG_RADIUS)), int(math.floor(px + BIG_RADIUS)) + 1):
                if m.walk(cx, cy) and 0 <= cx < m.w and 0 <= cy < m.h:
                    continue
                nx = min(max(px, cx), cx + 1)
                ny = min(max(py, cy), cy + 1)
                if (px - nx) ** 2 + (py - ny) ** 2 < r2:
                    return False
        return True

    ok = {}

    def valid(p):
        if p not in ok:
            ok[p] = 0 <= p[0] <= 2 * m.w and 0 <= p[1] <= 2 * m.h and fits(p[0] / 2, p[1] / 2)
        return ok[p]

    seeds = [(start[0] * 2 + i, start[1] * 2 + j) for i in range(3) for j in range(3)]
    seen = set(p for p in seeds if valid(p))
    q = deque(seen)
    while q:
        x, y = q.popleft()
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                n = (x + dx, y + dy)
                if n in seen or not valid(n):
                    continue
                if dx and dy and not (valid((x + dx, y)) and valid((x, y + dy))):
                    continue
                seen.add(n)
                q.append(n)
    reach = set()
    for (px, py) in seen:
        for cx in ((px - 1) // 2, px // 2):
            for cy in ((py - 1) // 2, py // 2):
                if 0 <= cx < m.w and 0 <= cy < m.h and cx * 2 <= px <= cx * 2 + 2 and cy * 2 <= py <= cy * 2 + 2:
                    reach.add((cx, cy))
    return reach


def pinches(m):
    """Walkable cells between two movement blockers 2 cells apart (a gap of exactly 1 cell, incl. the map edge)."""
    out = set()
    for y in range(m.h):
        for x in range(m.w):
            if not m.walk(x, y):
                continue
            for (ax, ay), (bx, by) in (((-1, 0), (1, 0)), ((0, -1), (0, 1))):
                if not m.walk(x + ax, y + ay) and not m.walk(x + bx, y + by):
                    out.add((x, y))
    return sorted(out)


def longest_runs(m, inset, stop=BLOCK_SHOT):
    """Open shot runs along rows / columns, clipped to the square `inset` cells inside the border."""
    best = []
    for horizontal in (True, False):
        n_lines, n_len = (m.h, m.w) if horizontal else (m.w, m.h)
        for i in range(inset, n_lines - inset):
            run, start = 0, 0
            for j in range(inset, n_len - inset + 1):
                ch = (m.at(j, i) if horizontal else m.at(i, j)) if j < n_len - inset else '#'
                if ch not in stop:
                    if run == 0:
                        start = j
                    run += 1
                else:
                    if run:
                        best.append((run, 'row' if horizontal else 'col', i, start, start + run - 1))
                    run = 0
    best.sort(reverse=True)
    return best


def cheb_center(m, x, y):
    return max(abs(x + 0.5 - m.w / 2), abs(y + 0.5 - m.h / 2))



def main():
    args = sys.argv[1:]
    path = [a for a in args if not a.startswith('--')][0]
    m = Map(load(path, '--quarter' in args))
    if '--print' in args:
        print('\n'.join(m.rows))
    W, H = m.w, m.h
    print('== size %dx%d = %d cells (limit 2048)' % (W, H, W * H))
    counts = {ch: sum(r.count(ch) for r in m.rows) for ch in LEGEND}
    print('== counts: floor %d, wall # %d, breakable %% %d, bush " %d, water ~ %d, boxes $ %d, spawns S %d'
          % (counts['.'], counts['#'], counts['%'], counts['"'], counts['~'], counts['$'], counts['S']))
    spawns = m.cells('S') or m.cells('AB')
    boxes = m.cells('$')
    walk = [c for c in m.cells(WALKABLE)]
    centre = (W // 2, H // 2)
    start = centre if m.walk(*centre) else min(walk, key=lambda c: abs(c[0] - centre[0]) + abs(c[1] - centre[1]))

    seen = bfs4(m, start)
    print('== connectivity: %d / %d walkable cells reachable (4-neighbour)' % (len(seen & set(walk)), len(walk)))
    big = big_body_reach(m, start)
    lost = sorted(set(walk) - big)
    print('== big body r=%.2f: %d walkable cells it cannot reach%s' % (BIG_RADIUS, len(lost), (': ' + str(lost[:20])) if lost else ''))
    p = pinches(m)
    print('== 1-cell pinches (walkable cell between two blockers): %d%s' % (len(p), (': ' + str(p[:20])) if p else ''))

    half = FOG_MIN_HALF
    lo, hi = int(math.floor(W / 2 - half)), int(math.ceil(W / 2 + half)) - 1
    fin = [(x, y) for y in range(lo, hi + 1) for x in range(lo, hi + 1)]
    print('== final safe square cols/rows %d..%d: %d cells, walkable %d' % (lo, hi, len(fin), sum(m.walk(*c) for c in fin)))

    # path metrics per spawn
    dists = {s: dijkstra8(m, s) for s in spawns}

    def to_box(d, b):
        best = 1e9
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                n = (b[0] + dx, b[1] + dy)
                if n in d:
                    best = min(best, d[n])
        return best

    owner = {}
    for b in boxes:
        ds = sorted((to_box(dists[s], b), s) for s in spawns)
        owner[b] = ds[0][1] if ds[0][0] + 1.0 < ds[1][0] else None  # None = contested (within 1 cell)
    print('== spawns (col,row_text): nearest box path / 2nd box / boxes within 12 / own boxes / to centre ring / nearest spawn (path, straight) / fogged at')
    ring = [c for c in walk if 2.5 < cheb_center(m, *c) <= 4.5]
    for s in spawns:
        d = dists[s]
        bd = sorted(to_box(d, b) for b in boxes)
        near12 = sum(1 for v in bd if v <= 12)
        own = sum(1 for b in boxes if owner[b] == s)
        dc = min(d.get(c, 1e9) for c in ring)
        others = [o for o in spawns if o != s]
        sp = min(d.get(o, 1e9) for o in others)
        st = min(math.dist(s, o) for o in others)
        print('   %-8s box %4.1f (%.1fs) / %4.1f / %2d / own %d / centre %4.1f (%.1fs) / spawn %4.1f, %4.1f / fog %ss'
              % (str(s), bd[0], bd[0] / SPEED, bd[1], near12, own, dc, dc / SPEED, sp, st,
                 fog_time_cell(m, s)))
    contested = sum(1 for b in boxes if owner[b] is None)
    print('   contested boxes (no spawn closer by >1 cell): %d of %d' % (contested, len(boxes)))
    t_box = BOX_HP / PIX_BURST
    print('   box %d HP = %.1f Pix bursts (~%.1f s with reload %.1f s); 2 berries each -> %d berries on the map'
          % (BOX_HP, t_box, (math.ceil(t_box) - 1) * PIX_RELOAD + 0.4, PIX_RELOAD, 2 * len(boxes)))

    for inset in (0, EDGE_BAND + 1, 6, 10):
        runs = longest_runs(m, inset)
        print('== open shot lines inside the square inset %2d (fogged from %ss): longest %s; > 12: %d, > 16: %d'
              % (inset, fog_time_dist(m, m.w / 2 - inset - 0.01), runs[:3], sum(1 for r in runs if r[0] > 12),
                 sum(1 for r in runs if r[0] > 16)))
        bare = longest_runs(m, inset, BLOCK_SHOT | {'"'})
        print('   bare (no bush either): longest %s; > 12: %d, > 16: %d'
              % (bare[:4], sum(1 for r in bare if r[0] > 12), sum(1 for r in bare if r[0] > 16)))

    cover = set(m.cells('#%$~"'))
    bare = []
    for (x, y) in walk:
        if min(x, y, W - 1 - x, H - 1 - y) < EDGE_BAND:
            continue
        if not any((x + dx, y + dy) in cover for dx in range(-COVER_RADIUS, COVER_RADIUS + 1) for dy in range(-COVER_RADIUS, COVER_RADIUS + 1)):
            bare.append((x, y))
    print('== open cells with no cover/bush within %d (outside the edge band): %d%s' % (COVER_RADIUS, len(bare), (': ' + str(bare[:12])) if bare else ''))

    print('== rings by Chebyshev distance from centre (fogged at): boxes / bushes / walls+breakables / water')
    bands = [(0, 4.5), (4.5, 8.5), (8.5, 12.5), (12.5, 16.5), (16.5, 23)]
    for a, b in bands:
        inb = lambda chs: sum(1 for c in m.cells(chs) if a < cheb_center(m, *c) <= b)
        print('   %4.1f..%4.1f  fog %5ss: $ %2d / " %3d / #%% %2d / ~ %2d'
              % (a, b, fog_time_dist(m, b - 0.01 if b < 23 else 22), inb('$'), inb('"'), inb('#%'), inb('~')))

    # bush clusters
    bush = set(m.cells('"'))
    sizes = []
    while bush:
        c = bush.pop()
        q, n = [c], 1
        while q:
            x, y = q.pop()
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                k = (x + dx, y + dy)
                if k in bush:
                    bush.remove(k)
                    q.append(k)
                    n += 1
        sizes.append(n)
    print('== bush clusters: %d, sizes %s' % (len(sizes), sorted(sizes, reverse=True)))
    # quadrant balance
    q = {}
    for name, chs in (('$', '$'), ('"', '"'), ('#%', '#%'), ('~', '~'), ('S', 'S')):
        q[name] = [sum(1 for (x, y) in m.cells(chs) if (x < W // 2) == left and (y < H // 2) == top and x != W // 2 and y != H // 2)
                   for top in (True, False) for left in (True, False)]
    print('== quadrants TL/TR/BL/BR (axis cells excluded): %s' % q)


def fog_time_dist(m, dist):
    """Seconds until a point `dist` (Chebyshev) from the map centre is in the fog; 'never' inside the last square."""
    half0 = m.w / 2
    k = 0
    while True:
        k += 1
        half = max(FOG_MIN_HALF, half0 - FOG_SHRINK * k)
        if dist > half:
            return '%d' % (FOG_START + FOG_STEP * (k - 1))
        if half == FOG_MIN_HALF:
            return 'never'


def fog_time_cell(m, c):
    return fog_time_dist(m, cheb_center(m, *c))


if __name__ == '__main__':
    main()
