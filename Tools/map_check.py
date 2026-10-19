"""Level-design checks for Arena maps (no Unity needed).

Usage:
  python Tools/map_check.py Tools/maps/snow_valley_lb_quarter.txt --quarter [--fog] [--print]
  python Tools/map_check.py <full rows file>               (team maps: rows top first)
  python Tools/map_check.py Tools/maps/snow_valley_acorns_half.txt --half [--decor <decor half file>] [--print]

Input: text rows in the DataSeeder / MapConfig legend (MapConverter):
  . floor  # wall  % breakable wall  " bush  ~ water  $ berry box  S shared spawn  A/B team spawn  H hollow (floor)
  T hollow on a wall cell (proposed P1, not in MapConverter yet)
--quarter: the file is the top-left quarter (N x N, last column and last row are the mirror axes), mirrored like
DataSeeder.MirrorQuarter (left<->right, then top<->bottom) into a (2N-1) x (2N-1) map.
--half: the file is the bottom half (team 0, text rows top first) and, as its last line, the middle row, mirrored like
DataSeeder.MirrorBottomHalf (the top half = the bottom half rotated by 180 degrees, A -> B).
--decor: decor layer (Arena.Data.MapDecor legend) in the same format; checked for size and the allowed symbol per cell.
Lines starting with ';' are comments.

Team maps (A/B spawns, Acorns): central symmetry, hollow in the centre, acorn scatter (radius 1.5, 8 attempts) on
blocked cells, spawn -> pickup zone / enemy spawn paths, cover within 3, dead ends, shot lines in the middle band (with
and without breakables = after ults), exposure of the pickup zone and of own-half cells to the enemy side, bush clusters
(distance to the enemy spawns and to the pickup zone), decor validity.

Checks: legend, connectivity, reachability of every walkable cell by the largest body (radius 0.55) on a 0.1-cell grid,
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

BLOCK_MOVE = set('#%~$T')
BLOCK_SHOT = set('#%$T')
WALKABLE = set('."SABH')
LEGEND = set('.#%"~$SABHT')   # T = proposed P1 (Map-SnowValley.md 1.3): a wall cell that is the hollow


def mirror_quarter(q):
    rows = [r + r[-2::-1] for r in q]
    return rows + rows[-2::-1]


def mirror_half(lines, swap=True):
    """DataSeeder.MirrorBottomHalf: lines = bottom half (top first) + the middle row as the last line."""
    half, middle = lines[:-1], lines[-1]
    top = [r[::-1].replace('A', 'B') if swap else r[::-1] for r in reversed(half)]
    return top + [middle] + half


def read_lines(path):
    with open(path, encoding='utf-8') as f:
        return [ln.rstrip('\n') for ln in f if ln.strip() and not ln.startswith(';')]


def load(path, quarter, half=False):
    rows = read_lines(path)
    if half:
        return mirror_half(rows)
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


def big_body_reach(m, start, res=10):
    """Grid points (`res` per cell) where a body of BIG_RADIUS fits, flood-filled from the points of the start cell where it fits; a cell is
    reached if a reached point lies inside it. A half-cell grid (res 2) misses diagonal squeezes, hence 10."""
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
        v = ok.get(p)
        if v is None:
            v = ok[p] = 0 <= p[0] <= res * m.w and 0 <= p[1] <= res * m.h and fits(p[0] / res, p[1] / res)
        return v

    seen = set(p for p in ((start[0] * res + i, start[1] * res + j) for i in range(res + 1) for j in range(res + 1)) if valid(p))
    q = deque(seen)
    while q:
        x, y = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            n = (x + dx, y + dy)
            if n not in seen and valid(n):
                seen.add(n)
                q.append(n)
    reach = set()
    for (px, py) in seen:
        reach.add((min(px // res, m.w - 1), min(py // res, m.h - 1)))
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
    path = [a for i, a in enumerate(args) if not a.startswith('--') and (i == 0 or args[i - 1] != '--decor')][0]
    m = Map(load(path, '--quarter' in args, '--half' in args))
    if '--print' in args:
        print('\n'.join(('t%02d r%02d  ' % (i, m.h - 1 - i)) + r for i, r in enumerate(m.rows)))
    if m.cells('AB'):
        decor = None
        if '--decor' in args:
            dp = args[args.index('--decor') + 1]
            decor = mirror_half(read_lines(dp), swap=False) if '--half' in args else read_lines(dp)
        team_report(m, decor, '--print' in args)
        return
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
    big = big_body_reach(m, spawns[0] if spawns else start)   # a spawn: the body surely fits there
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


# ---------------------------------------------------------------- team maps (Acorns, 3x3)
SPEEDS = (2.2, 2.5, 2.8)    # Slow / Normal / Fast
ACORN_SCATTER, ACORN_ATTEMPTS = 1.5, 8
TEAM_COVER = 3              # cover/bush within this Chebyshev distance (3x3 rule)
BASE_ROWS = 4               # text rows this close to the top/bottom edge are the bases (exempt from the cover rule)
LONG_SHOT = 10              # Nova's range: open lines longer than this are "sniper lines"
EXPOSE_RANGE = 8.0          # Pix's range: exposure is counted from cells this close
DECOR_ALLOWED = {'#': set('TfLds'), '%': set('mfg'), '$': set('G')}   # L = lantern (D-142), g = gift on '%' (proposed, D-141)


def map_xy(m, x, y):
    """Text cell (column, text row) -> map-space centre (x, y) with row 0 at the bottom (like MapDef)."""
    return x + 0.5, (m.h - 1 - y) + 0.5


def cell_at_point(m, px, py):
    """Map-space point -> text cell."""
    return int(math.floor(px)), m.h - 1 - int(math.floor(py))


def clear_shot(m, a, b, stop):
    """True if no `stop` cell lies on the segment between the centres of text cells a and b (sampled every 0.05)."""
    ax, ay = a[0] + 0.5, a[1] + 0.5
    bx, by = b[0] + 0.5, b[1] + 0.5
    n = max(1, int(math.hypot(bx - ax, by - ay) / 0.05))
    for i in range(1, n):
        t = i / n
        cx, cy = int(math.floor(ax + (bx - ax) * t)), int(math.floor(ay + (by - ay) * t))
        if (cx, cy) != a and (cx, cy) != b and m.at(cx, cy) in stop:
            return False
    return True


def exposure(m, cell, sources, stop=BLOCK_SHOT, rng=EXPOSE_RANGE):
    return sum(1 for s in sources if s != cell and math.dist(s, cell) <= rng and clear_shot(m, cell, s, stop))


def band_runs(m, stop, lo, hi):
    """Open runs along rows lo..hi and along columns clipped to rows lo..hi (text rows)."""
    out = []
    for y in range(lo, hi + 1):
        run = 0
        for x in range(m.w + 1):
            ch = m.at(x, y) if x < m.w else '#'
            if ch not in stop:
                run += 1
            else:
                if run:
                    out.append((run, 'row t%02d' % y, x - run, x - 1))
                run = 0
    for x in range(m.w):
        run = 0
        for y in range(lo, hi + 2):
            ch = m.at(x, y) if y <= hi else '#'
            if ch not in stop:
                run += 1
            else:
                if run:
                    out.append((run, 'col %d' % x, 't%02d' % (y - run), 't%02d' % (y - 1)))
                run = 0
    out.sort(reverse=True)
    return out


def team_report(m, decor, verbose):
    W, H = m.w, m.h
    print('== size %dx%d = %d cells (limit 2048)' % (W, H, W * H))
    counts = {ch: sum(r.count(ch) for r in m.rows) for ch in LEGEND}
    print('== counts: floor %d, wall # %d, breakable %% %d, bush " %d, water ~ %d, box $ %d, spawns A %d / B %d, hollow %d'
          % (counts['.'], counts['#'], counts['%'], counts['"'], counts['~'], counts['$'], counts['A'], counts['B'], counts['H'] + counts['T']))
    swap = {'A': 'B', 'B': 'A'}
    bad = [(x, y) for y in range(H) for x in range(W) if swap.get(m.rows[y][x], m.rows[y][x]) != m.rows[H - 1 - y][W - 1 - x]]
    print('== central symmetry (A<->B): %s' % ('ok' if not bad else 'BROKEN at %s' % bad[:8]))
    hol = m.cells('HT')
    centre = (W // 2, H // 2)
    print('== hollow: %s (centre %s) %s' % (hol, centre, 'ok' if hol == [centre] else 'NOT IN THE CENTRE'))
    hollow = hol[0] if hol else centre

    walk = m.cells(WALKABLE)
    start = m.cells('A')[0]
    seen = bfs4(m, start)
    print('== connectivity: %d / %d walkable cells reachable (4-neighbour)' % (len(seen & set(walk)), len(walk)))
    big = big_body_reach(m, m.cells("A")[0])   # a spawn: the body surely fits there
    lost = sorted(set(walk) - big)
    print('== big body r=%.2f: %d walkable cells it cannot reach%s' % (BIG_RADIUS, len(lost), (': ' + str(lost[:20])) if lost else ''))
    p = pinches(m)
    print('== 1-cell pinches (walkable cell between two blockers): %d%s' % (len(p), (': ' + str(p[:20])) if p else ''))
    dead = [(x, y) for (x, y) in walk if sum(1 for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)) if not m.walk(x + dx, y + dy)) >= 3]
    print('== dead ends (3+ closed sides, edge counts): %d%s' % (len(dead), (': ' + str(dead[:12])) if dead else ''))

    # acorn scatter: uniform disc of radius 1.5 around the hollow centre, 8 attempts, else the centre itself
    hx, hy = map_xy(m, *hollow)
    n = blocked = 0
    step = 0.02
    k = int(ACORN_SCATTER / step)
    zone = set()
    for i in range(-k, k):
        for j in range(-k, k):
            px, py = hx + (i + 0.5) * step, hy + (j + 0.5) * step   # cell-centred samples: none on a cell border
            if (px - hx) ** 2 + (py - hy) ** 2 > ACORN_SCATTER ** 2:
                continue
            n += 1
            c = cell_at_point(m, px, py)
            if not m.walk(*c):
                blocked += 1
            else:
                zone.add(c)
    pf = blocked / n
    print('== acorn scatter r=%.1f: %.1f %% of the disc on blocked cells, all %d attempts fail: %.4f %% (acorn on the hollow centre)'
          % (ACORN_SCATTER, 100 * pf, ACORN_ATTEMPTS, 100 * pf ** ACORN_ATTEMPTS))
    print('   pickup zone (walkable cells the disc touches): %d cells %s' % (len(zone), sorted(zone)))

    team0 = sorted(m.cells('A'))
    team1 = sorted(m.cells('B'))
    print('== spawns (col,text row): path to the pickup zone (s at %s cells/s) / to the nearest enemy spawn (path, straight)'
          % '/'.join(str(v) for v in SPEEDS))
    for s in team0 + team1:
        d = dijkstra8(m, s)
        dz = min(d.get(c, 1e9) for c in zone)
        enemies = team1 if s in team0 else team0
        de = min(d.get(e, 1e9) for e in enemies)
        st = min(math.dist(s, e) for e in enemies)
        print('   %s %-8s zone %4.1f (%s s) / enemy spawn %4.1f, %4.1f'
              % ('A' if s in team0 else 'B', str(s), dz, '/'.join('%.1f' % (dz / v) for v in SPEEDS), de, st))

    lo, hi = BASE_ROWS, H - 1 - BASE_ROWS
    cover = set(m.cells('#%$~"'))
    bare = [(x, y) for (x, y) in walk if lo <= y <= hi and not any(
        (x + dx, y + dy) in cover for dx in range(-TEAM_COVER, TEAM_COVER + 1) for dy in range(-TEAM_COVER, TEAM_COVER + 1))]
    print('== open cells with no cover/bush within %d (text rows %d..%d, bases exempt): %d%s'
          % (TEAM_COVER, lo, hi, len(bare), (': ' + str(bare[:12])) if bare else ''))

    no_break = Map([r.replace('%', '.') for r in m.rows])
    for title, mm in (('now', m), ('after ults (all % broken)', no_break)):
        runs = band_runs(mm, BLOCK_SHOT, lo, hi)
        bare_runs = band_runs(mm, BLOCK_SHOT | {'"'}, lo, hi)
        print('== shot lines in text rows %d..%d, %s: longest %s; > %d: %d' % (lo, hi, title, runs[:4], LONG_SHOT,
              sum(1 for r in runs if r[0] > LONG_SHOT)))
        print('   bare (bushes stop it too): longest %s; > %d: %d' % (bare_runs[:4], LONG_SHOT, sum(1 for r in bare_runs if r[0] > LONG_SHOT)))

    # exposure of the pickup zone: walkable cells within 8 with a clear shot, split by half
    north = [c for c in walk if c[1] < H // 2]   # team 1 half (text rows above the middle)
    south = [c for c in walk if c[1] > H // 2]
    for title, mm in (('now', m), ('after ults', no_break)):
        ez = sorted((exposure(mm, c, north), exposure(mm, c, south), c) for c in zone)
        print('== pickup zone exposure, %s (cells within %.0f with a clear shot from the team 1 half / team 0 half): '
              'min %s, max %s, mean %.1f / %.1f'
              % (title, EXPOSE_RANGE, ez[0][:2], max(e[:2] for e in ez), sum(e[0] for e in ez) / len(ez), sum(e[1] for e in ez) / len(ez)))
        if verbose:
            print('   ' + ', '.join('%s %d/%d' % (str(c), a, b) for a, b, c in ez))

    # safe pockets of team 0: own-half cells hidden from the enemy half
    own = [c for c in walk if H // 2 < c[1] <= H - 1 - BASE_ROWS]
    exp_own = {c: exposure(m, c, north, rng=10.0) for c in own}
    safe = sorted(c for c, v in exp_own.items() if v == 0)
    low = sorted(c for c, v in exp_own.items() if 0 < v <= 3)
    print('== team 0 own half (text rows %d..%d): cells with no clear shot from the team 1 half within 10: %d, with 1-3: %d (of %d)'
          % (H // 2 + 1, H - 1 - BASE_ROWS, len(safe), len(low), len(own)))
    print('   none: %s' % safe)
    print('   1-3:  %s' % low)

    # bushes
    bush = set(m.cells('"'))
    clusters = []
    while bush:
        c = bush.pop()
        q, cl = [c], [c]
        while q:
            x, y = q.pop()
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                k2 = (x + dx, y + dy)
                if k2 in bush:
                    bush.remove(k2)
                    q.append(k2)
                    cl.append(k2)
        clusters.append(sorted(cl))
    print('== bush clusters: %d; cells, bbox, min dist to the spawns of that half (an enemy ambush there), to the pickup zone (Euclid, cell centres)' % len(clusters))
    for cl in sorted(clusters, key=lambda c: (min(p[1] for p in c), min(p[0] for p in c))):
        xs, ys = [c[0] for c in cl], [c[1] for c in cl]
        owners = team1 if max(ys) < H // 2 else team0 if min(ys) > H // 2 else team0 + team1   # spawns of this half
        de = min(math.dist(c, e) for c in cl for e in owners)
        dz = min(math.dist(c, z) for c in cl for z in zone)
        print('   %2d cells  cols %2d-%-2d text rows %2d-%-2d  half spawns %4.1f  zone %4.1f'
              % (len(cl), min(xs), max(xs), min(ys), max(ys), de, dz))

    if decor is not None:
        problems = []
        if len(decor) != H:
            problems.append('decor has %d rows, map %d' % (len(decor), H))
        for y in range(min(H, len(decor))):
            if len(decor[y]) != W:
                problems.append('decor text row %d length %d' % (y, len(decor[y])))
                continue
            for x in range(W):
                d, t = decor[y][x], m.rows[y][x]
                if d != '.' and d not in DECOR_ALLOWED.get(t, set()):
                    problems.append('%r on %r at (%d, t%02d)' % (d, t, x, y))
        marks = {}
        for y in range(min(H, len(decor))):
            for ch in decor[y]:
                if ch != '.':
                    marks[ch] = marks.get(ch, 0) + 1
        print('== decor: %s; marks %s' % ('ok' if not problems else 'PROBLEMS %s' % problems[:8], dict(sorted(marks.items()))))
        if verbose:
            print('\n'.join('   t%02d  %s' % (i, r) for i, r in enumerate(decor)))


if __name__ == '__main__':
    main()
