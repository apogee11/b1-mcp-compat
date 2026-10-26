"""JSON parity of the extended read (Core/Read) against the native FP2608 Service Layer, per entity set.

  python -I -X utf8 tools/parity/read_parity.py [--only REGEX] [--package B11000SP_2408] [--out DIR]

For each of the 140 sets of the package (131 servable + the unavailable ones) the same /b1s/v2 GET is asked of
  - the native 2608 Service Layer (reference; B1_SL_URL, session from rea/creds.local.env), and
  - Core/Read (ReadService) over the company database, through tools/parity/ReadDump (one process, all requests).
Both JSON bodies are parsed keeping property order and the lexical form of numbers, and compared (property order,
JSON types, nulls, child collections, paging). Result per set: OK / DIFF / NO-DATA / UNAVAILABLE-OK / UNAVAILABLE-DIFF / ERROR.
Exit 0 only when no set is DIFF/ERROR/UNAVAILABLE-DIFF. NO-DATA (native table empty) is NOT a pass: it cannot show types or child order.

Why not through the Host proxy: the Host serves the package only for sets the Service Layer does NOT expose (ReadRoute.Decide) and for the
exact declared build; the local 2608 exposes all of them, so the proxy would pass everything through. Core/Read is exercised directly instead,
with the same package, planner, SQL and serializer the proxy calls (ProxyEndpoint.TryServeReadAsync -> ReadService.GetAsync).
Credentials: rea/creds.local.env (git-ignored) or environment; never printed. Report: rea/kbgen/out/read-parity-report.{md,json} (git-ignored).
"""
import argparse, gzip, http.cookiejar, json, os, re, ssl, subprocess, sys, tempfile, urllib.error, urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HELPER = Path(__file__).parent / 'ReadDump'
PKG_DIR = ROOT / 'compat/src/B1.Mcp.Compat.Host/Resources'
ctx = ssl._create_unverified_context()
MAXDIFF = 8


def load_env():
    env = dict(os.environ)
    p = ROOT / 'rea' / 'creds.local.env'
    if p.exists():
        for line in p.read_text(encoding='utf-8').splitlines():
            if '=' in line and not line.lstrip().startswith('#'):
                k, v = line.split('=', 1)
                env.setdefault(k.strip(), v.strip())
    miss = [k for k in ['B1_SQL_USER', 'B1_SQL_PASS', 'B1_SL_URL', 'B1_COMPANY', 'B1_USER', 'B1_PASS'] if not env.get(k)]
    if miss:
        sys.exit('missing ' + ', '.join(miss) + ' (rea/creds.local.env or environment)')
    return env


class Native:
    def __init__(self, env):
        self.base = env['B1_SL_URL'].rstrip('/')
        self.op = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ctx), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        body = json.dumps({'CompanyDB': env['B1_COMPANY'], 'UserName': env['B1_USER'], 'Password': env['B1_PASS']}).encode()
        self.op.open(urllib.request.Request(self.base + '/b1s/v2/Login', data=body, headers={'Content-Type': 'application/json'}), timeout=60).read()

    def get(self, path, page_size):
        h = {'Prefer': 'odata.maxpagesize=%d' % page_size, 'Accept': 'application/json'}
        try:
            r = self.op.open(urllib.request.Request(self.base + '/b1s/v2/' + path.replace(' ', '%20'), headers=h), timeout=120)
            return r.status, r.read().decode('utf-8')
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode('utf-8', 'replace')
        except Exception as e:
            return -1, repr(e)


# ---- JSON comparison: property order, types, lexical numbers ----
def parse(text):
    return json.loads(text, object_pairs_hook=lambda p: ('obj', p), parse_float=lambda s: ('num', s), parse_int=lambda s: ('num', s))


def kind(v):
    if v is None:
        return 'null'
    if isinstance(v, tuple):
        return 'object' if v[0] == 'obj' else 'number'
    return {bool: 'bool', str: 'string', list: 'array'}.get(type(v), type(v).__name__)


def plain(v):
    if isinstance(v, tuple):
        return dict((k, plain(x)) for k, x in v[1]) if v[0] == 'obj' else v[1]
    if isinstance(v, list):
        return [plain(x) for x in v]
    return v


def short(v):
    s = json.dumps(plain(v), ensure_ascii=False)
    return s if len(s) < 90 else s[:87] + '...'


def diff(a, b, path, out):
    """a = native, b = Core/Read."""
    if len(out) >= MAXDIFF:
        return
    ka, kb = kind(a), kind(b)
    if ka != kb:
        out.append(f'{path}: type native={ka} core={kb} (native {short(a)} / core {short(b)})')
        return
    if ka == 'object':
        da, db_ = dict(a[1]), dict(b[1])
        ia, ib = [k for k, _ in a[1]], [k for k, _ in b[1]]
        for k in ia:
            if k not in db_:
                out.append(f'{path}.{k}: only in native ({short(da[k])})')
        for k in ib:
            if k not in da:
                out.append(f'{path}.{k}: only in core ({short(db_[k])})')
        ca = [k for k in ia if k in db_]
        cb = [k for k in ib if k in da]
        if ca != cb:
            i = next(i for i, (x, y) in enumerate(zip(ca, cb)) if x != y)
            out.append(f'{path}: property ORDER differs at #{i}: native {ca[i:i + 3]} core {cb[i:i + 3]}')
        for k in ca:
            diff(da[k], db_[k], f'{path}.{k}', out)
    elif ka == 'array':
        if len(a) != len(b):
            out.append(f'{path}: array length native={len(a)} core={len(b)}')
        for i, (x, y) in enumerate(zip(a, b)):
            diff(x, y, f'{path}[{i}]', out)
    elif a != b:
        out.append(f'{path}: value native={short(a)} core={short(b)}')


def has_rows(doc):
    return isinstance(doc, tuple) and doc[0] == 'obj' and any(k == 'value' and isinstance(v, list) and len(v) > 0 for k, v in doc[1])


def first_row(doc):
    for k, v in doc[1]:
        if k == 'value' and isinstance(v, list) and v and isinstance(v[0], tuple):
            return dict(v[0][1])
    return None


def key_literal(pkg_set, row):
    """(key) of the first row, or None when a key part cannot be written without guessing (enum / date / missing)."""
    cols = {c['name']: c for c in pkg_set['columns']}
    parts = []
    for pk in pkg_set['primaryKey']:
        c = cols.get(pk)
        j = c and c.get('json')
        if not j or j['property'] not in row or j.get('enum'):
            return None
        v = row[j['property']]
        et = (j.get('edmType') or '').split('.')[-1]
        if isinstance(v, tuple):
            lit = v[1]
        elif isinstance(v, str) and et not in ('DateTimeOffset', 'Date', 'TimeOfDay'):
            lit = "'" + v.replace("'", "''") + "'"
        else:
            return None
        parts.append((j['property'], lit))
    if not parts:
        return None
    return '(' + parts[0][1] + ')' if len(parts) == 1 else '(' + ','.join(f'{p}={l}' for p, l in parts) + ')'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only')
    ap.add_argument('--package', default='B11000SP_2408')
    ap.add_argument('--out', default=str(ROOT / 'rea/kbgen/out'))
    a = ap.parse_args()
    env = load_env()
    pkg_path = PKG_DIR / f'read-{a.package}.json.gz'
    pkg = json.loads(gzip.open(pkg_path).read().decode('utf-8'))
    sets, unavailable = pkg['sets'], pkg['unavailable']
    names = sorted(list(sets) + list(unavailable))
    if a.only:
        names = [n for n in names if re.search(a.only, n)]
    tmp = Path(tempfile.mkdtemp(prefix='readdump-'))
    r = subprocess.run(['dotnet', 'build', str(HELPER / 'ReadDump.csproj'), '-c', 'Release', '-o', str(tmp / 'bin'), '-nologo', '-v', 'q'], capture_output=True, text=True)
    if r.returncode:
        sys.exit('ReadDump build failed:\n' + r.stdout[-1500:])
    nat = Native(env)
    core_base = 'http://core.invalid/b1s/v2'
    nbase = nat.base + '/b1s/v2'

    def norm(text):
        return text.replace(nbase, '<SL>').replace(core_base, '<SL>')

    def run_core(reqs):
        e = dict(os.environ, DB_USER=env['B1_SQL_USER'], DB_PASS=env['B1_SQL_PASS'])
        p = subprocess.run(['dotnet', str(tmp / 'bin' / 'ReadDump.dll'), str(pkg_path), env['B1_COMPANY'], core_base, env.get('B1_SQL_SERVER', 'localhost')],
                           input='\n'.join(json.dumps(x) for x in reqs), capture_output=True, text=True, encoding='utf-8', env=e)
        if p.returncode:
            sys.exit('ReadDump failed: ' + p.stderr[-800:])
        return {j['id']: j for j in (json.loads(l) for l in p.stdout.splitlines() if l.strip())}

    first = {n: nat.get(n + '?$top=3', 20) for n in names}
    reqs = []   # (set, label, path, pageSize)
    for n in names:
        st, body = first[n]
        reqs.append((n, 'top3', n + '?$top=3', 20))
        if n in unavailable:
            continue
        s = sets[n]
        props = s['propertyOrder']
        reqs.append((n, 'count', n + '/$count', 20))
        reqs.append((n, 'skip', n + '?$top=2&$skip=1', 20))
        reqs.append((n, 'paged2', n, 2))
        if props:
            reqs.append((n, 'select2', n + '?$top=2&$select=' + ','.join(props[:2]), 20))
            reqs.append((n, 'orderdesc', n + '?$top=2&$orderby=' + props[0] + ' desc', 20))
        if st == 200:
            try:
                row = first_row(parse(body))
            except Exception:
                row = None
            if row:
                k = key_literal(s, row)
                if k:
                    reqs.append((n, 'bykey', n + k, 20))
    ids = {str(i): r_ for i, r_ in enumerate(reqs)}
    core = run_core([{'id': i, 'path': r_[2], 'pageSize': r_[3]} for i, r_ in ids.items()])

    results = {}
    for i, (n, label, path, page) in ids.items():
        nst, nbody = first[n] if label == 'top3' else nat.get(path, page)
        c = core[i]
        cst, cbody = c['status'], c['body']
        nbody, cbody = norm(nbody), norm(cbody)
        d = []
        if nst != cst:
            d.append(f'HTTP status native={nst} core={cst}; native body {nbody[:140]!r}; core body {cbody[:140]!r}')
        elif label == 'count' or nst != 200:
            if nbody.strip() != cbody.strip():
                d.append(f'body native={nbody[:120]!r} core={cbody[:120]!r}')
        else:
            try:
                diff(parse(nbody), parse(cbody), '$', d)
            except Exception as ex:
                d.append(f'unparseable JSON: {ex}')
        nodata = False
        if label == 'top3' and nst == 200:
            try:
                nodata = not has_rows(parse(nbody))
            except Exception:
                pass
        results.setdefault(n, []).append({'req': label, 'path': path, 'native': nst, 'core': cst, 'diffs': d, 'nodata': nodata})

    summary = {}
    for n in names:
        rs = results[n]
        top = rs[0]
        if n in unavailable:
            # the native has the set; the package says this build lacks a table: the right answer is the package's own error (400), never data
            summary[n] = 'UNAVAILABLE-OK' if top['core'] == 400 else 'UNAVAILABLE-DIFF'
        elif any(x['core'] == -1 for x in rs):
            summary[n] = 'ERROR'
        elif any(x['diffs'] for x in rs):
            summary[n] = 'DIFF'
        elif top['nodata']:
            summary[n] = 'NO-DATA'
        else:
            summary[n] = 'OK'
    outdir = Path(a.out)
    outdir.mkdir(parents=True, exist_ok=True)
    (outdir / 'read-parity-report.json').write_text(json.dumps({'package': a.package, 'summary': summary, 'results': results}, indent=1, ensure_ascii=False), encoding='utf-8')
    cnt = {}
    for v in summary.values():
        cnt[v] = cnt.get(v, 0) + 1
    L = [f'# Read JSON parity ({a.package}) vs native 2608\n', 'Totals: ' + ', '.join(f'{k}={v}' for k, v in sorted(cnt.items())) + f' (sets {len(names)})\n',
         '| set | result | requests with differences |', '|---|---|---|']
    for n in names:
        L.append(f'| {n} | {summary[n]} | {", ".join(x["req"] for x in results[n] if x["diffs"])} |')
    L.append('\n## Differences\n')
    for n in names:
        if summary[n] != 'OK' and summary[n] != 'NO-DATA' or any(x['diffs'] for x in results[n]):
            L.append(f'### {n} - {summary[n]}')
            for x in results[n]:
                for d in x['diffs']:
                    L.append(f'- `{x["path"]}`: {d}')
            L.append('')
    (outdir / 'read-parity-report.md').write_text('\n'.join(L), encoding='utf-8')
    print('Totals:', cnt)
    for k in ('DIFF', 'ERROR', 'UNAVAILABLE-DIFF', 'NO-DATA'):
        print(k, [n for n in names if summary[n] == k])
    print('report:', outdir / 'read-parity-report.md')
    return 1 if any(v in ('DIFF', 'ERROR', 'UNAVAILABLE-DIFF') for v in summary.values()) else 0


if __name__ == '__main__':
    sys.exit(main())
