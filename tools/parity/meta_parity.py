"""Parity of the connector (SimulateLegacy=true) against a native FP2608 Service Layer.

  python -I -X utf8 tools/parity/meta_parity.py [--only REGEX] [--skip-pf] [--no-start]

Starts the connector on 127.0.0.1:15310, compares the annotated $metadata of every entity set
and GetPersonalFieldsByTable of every table in pf-tables-2608.txt, stops it, exits 0 only if all match.
Credentials come from rea/creds.local.env (git-ignored) or the same-named environment variables.
Single entity set diff: --diff NAME
"""
import argparse, collections, http.cookiejar, json, os, re, ssl, subprocess, sys, time, urllib.error, urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PORT = 15310
CONN = f'http://127.0.0.1:{PORT}'

def load_env():
    env = dict(os.environ)
    p = ROOT / 'rea' / 'creds.local.env'
    if p.exists():
        for line in p.read_text(encoding='utf-8').splitlines():
            if '=' in line and not line.lstrip().startswith('#'):
                k, v = line.split('=', 1); env.setdefault(k.strip(), v.strip())
    need = ['B1_SQL_USER', 'B1_SQL_PASS', 'B1_SL_URL', 'B1_COMPANY', 'B1_USER', 'B1_PASS']
    miss = [k for k in need if not env.get(k)]
    if miss: sys.exit('missing ' + ', '.join(miss) + ' (rea/creds.local.env or environment)')
    return env

ctx = ssl._create_unverified_context()
def sess(base, env):
    op = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ctx), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    body = json.dumps({'CompanyDB': env['B1_COMPANY'], 'UserName': env['B1_USER'], 'Password': env['B1_PASS']}).encode()
    op.open(urllib.request.Request(base + '/b1s/v2/Login', data=body, headers={'Content-Type': 'application/json'}), timeout=60).read()
    return op

def meta(op, base, es):
    u = base + '/b1s/v2/$metadata?scope=entityset&annotation=labelWithTable&annotation=labelWithField&entityset=%s&dependency=true' % es
    try: return op.open(u, timeout=120).read().decode('utf-8')
    except urllib.error.HTTPError as e: return 'HTTP%d %s' % (e.code, e.read()[:200].decode('utf-8', 'ignore'))

def lines(x):
    try: return collections.Counter(ET.canonicalize(x, strip_text=True).replace('><', '>\n<').split('\n'))
    except Exception: return collections.Counter([x])

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--only'); ap.add_argument('--skip-pf', action='store_true'); ap.add_argument('--no-start', action='store_true'); ap.add_argument('--diff')
    a = ap.parse_args()
    env = load_env(); sl = env['B1_SL_URL'].rstrip('/')
    proc = None
    if not a.no_start:
        exe = next(iter(sorted((ROOT / 'compat/src/B1.Mcp.Compat.Host/bin/Release').rglob('B1.Mcp.Compat.Host.exe'), key=lambda p: p.stat().st_mtime, reverse=True)), None)
        if not exe: sys.exit('connector not built: dotnet build compat -c Release')
        e = dict(os.environ, B1McpCompat__Listen=CONN, B1McpCompat__SimulateLegacy='true', B1McpCompat__Upstream__Url=sl, B1McpCompat__Release='2608', B1McpCompat__Build='10.0.340.0-d02489c511c7-7e3e810900f1-fe64a1a2b194',
                 B1McpCompat__Database__User=env['B1_SQL_USER'], B1McpCompat__Database__Password=env['B1_SQL_PASS'])
        proc = subprocess.Popen([str(exe)], cwd=exe.parent, env=e, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(60):
            try: urllib.request.urlopen(CONN + '/', timeout=3); break
            except urllib.error.HTTPError: break
            except Exception: time.sleep(1)
        else: sys.exit('connector did not start')
        nat, cmp_ = sess(sl, env), sess(CONN, env)
        plain = nat.open(sl + '/b1s/v2/$metadata', timeout=120).read().decode()
        names = sorted(set(re.findall(r'<EntitySet [^>]*Name="([^"]+)"', plain)))
        if a.diff: names = [a.diff]
        elif a.only: names = [n for n in names if re.search(a.only, n)]
        diff = []
        for i, n in enumerate(names):
            A = meta(nat, sl, n); B = meta(cmp_, CONN, n).replace(CONN, sl)
            ca, cb = lines(A), lines(B)
            if ca != cb:
                diff.append(n)
                if a.diff:
                    print('only native', sum((ca - cb).values()), 'only connector', sum((cb - ca).values()))
                    for k in list((ca - cb))[:8]: print('N', k[:200])
                    for k in list((cb - ca))[:8]: print('C', k[:200])
            if i % 100 == 0: print(i, len(names), len(diff), flush=True)
        print(f'entity sets {len(names)} identical {len(names) - len(diff)} different {len(diff)} {diff[:60]}')
        bad = []
        if not a.skip_pf and not a.diff and not a.only:
            tabs = (Path(__file__).parent / 'pf-tables-2608.txt').read_text().split()
            def call(op, base, t):
                r = urllib.request.Request(base + '/b1s/v2/PersonalFieldsSetupsService_GetPersonalFieldsByTable', data=json.dumps({'PersonalFieldsSetupTableParams': {'TableName': t}}).encode(), headers={'Content-Type': 'application/json'})
                return json.loads(op.open(r, timeout=60).read())['value']
            for t in tabs:
                x, y = call(nat, sl, t), call(cmp_, CONN, t)
                # row order varies between SL nodes: compare as sets
                if sorted(map(json.dumps, x)) != sorted(map(json.dumps, y)): bad.append(t)
            print(f'personal-fields tables {len(tabs)} different {len(bad)} {bad[:10]}')
        ok = not diff and not bad
        print('PARITY OK' if ok else 'PARITY FAIL'); return 0 if ok else 1
    finally:
        if proc: subprocess.run(['taskkill', '/T', '/F', '/PID', str(proc.pid)], capture_output=True)

if __name__ == "__main__":
    sys.exit(main())
