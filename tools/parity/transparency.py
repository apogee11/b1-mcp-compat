"""Transparency (P2): connector with SimulateLegacy=false must answer like the native Service Layer.

  python -I -X utf8 tools/parity/transparency.py [--no-start]

Same requests to the native SL and to the connector (127.0.0.1:15311); compares status and body
(upstream host/port and volatile ids normalised) and the response headers that matter. Read-only.
"""
import http.cookiejar, json, os, re, ssl, subprocess, sys, time, urllib.error, urllib.request
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import importlib.util
ROOT = Path(__file__).resolve().parents[2]
PORT = 15311; CONN = f'http://127.0.0.1:{PORT}'

def load_env():
    env = dict(os.environ); p = ROOT / 'rea' / 'creds.local.env'
    for line in p.read_text(encoding='utf-8').splitlines() if p.exists() else []:
        if '=' in line and not line.lstrip().startswith('#'):
            k, v = line.split('=', 1); env.setdefault(k.strip(), v.strip())
    return env

ctx = ssl._create_unverified_context()
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k): return None

def client(base, env):
    jar = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ctx), urllib.request.HTTPCookieProcessor(jar), NoRedirect)
    def call(method, path, body=None, headers=None):
        h = {'Content-Type': 'application/json'}; h.update(headers or {})
        data = body if isinstance(body, (bytes, type(None))) else json.dumps(body).encode()
        try: r = op.open(urllib.request.Request(base + path, data=data, method=method, headers=h), timeout=120)
        except urllib.error.HTTPError as e: r = e
        return r.status if hasattr(r, 'status') else r.code, dict(r.headers), r.read().decode('utf-8', 'ignore')
    s, _, b = call('POST', '/b1s/v2/Login', {'CompanyDB': env['B1_COMPANY'], 'UserName': env['B1_USER'], 'Password': env['B1_PASS']})
    assert s == 200, 'login failed %s' % s
    return call

def norm(t, bases):
    for b in bases: t = t.replace(b, 'BASE')
    t = re.sub(r'"SessionId"\s*:\s*"[^"]*"', '"SessionId":"X"', t)
    return re.sub(r'"SessionTimeout"\s*:\s*\d+', '"SessionTimeout":0', t)

BOUNDARY = 'batch_x'
BATCH = (f'--{BOUNDARY}\r\nContent-Type: application/http\r\nContent-Transfer-Encoding: binary\r\n\r\n'
         f'GET /b1s/v2/Currencies?$top=2&$select=Code\r\n\r\n--{BOUNDARY}--\r\n').encode()
CASES = [
    ('GET', '/b1s/v2/Currencies?$top=3&$select=Code,Name&$orderby=Code', None, None),
    ('GET', '/b1s/v2/Orders?$top=2&$select=DocEntry,CardCode&$expand=DocumentLines($select=ItemCode)&$orderby=DocEntry', None, None),
    ('GET', '/b1s/v2/BusinessPartners?$top=5&$select=CardCode&$count=true&$orderby=CardCode', None, None),
    ('GET', '/b1s/v2/Items?$top=1100&$select=ItemCode&$orderby=ItemCode', None, None),  # paging cut
    ('GET', '/b1s/v2/Orders(999999999)', None, None),                                    # 404 error body
    ('GET', '/b1s/v2/NoSuchEntity', None, None),
    ('POST', '/b1s/v2/Currencies', {'Code': ''}, None),                                  # validation error, no write
    ('GET', '/b1s/v2/$metadata', None, None),
    ('GET', '/b1s/v2/$metadata?scope=entityset&entityset=Orders&dependency=true', None, None),
    ('POST', '/b1s/v2/$batch', BATCH, {'Content-Type': f'multipart/mixed;boundary={BOUNDARY}'}),
    ('GET', '/b1s/v2/Currencies?$top=1', None, {'Accept': 'application/json;odata=minimalmetadata'}),
]
KEEP = ('content-type', 'odata-version')

def main():
    env = load_env(); sl = env['B1_SL_URL'].rstrip('/'); proc = None
    if '--no-start' not in sys.argv:
        exe = next(iter(sorted((ROOT / 'compat/src/B1.Mcp.Compat.Host/bin/Release').rglob('B1.Mcp.Compat.Host.exe'), key=lambda p: p.stat().st_mtime, reverse=True)), None)
        e = dict(os.environ, B1McpCompat__Listen=CONN, B1McpCompat__SimulateLegacy='false', B1McpCompat__ServiceLayer__Url=sl, B1McpCompat__Release='2608', B1McpCompat__Build='10.0.340.0-d02489c511c7-7e3e810900f1-fe64a1a2b194',
                 B1McpCompat__Database__User=env['B1_SQL_USER'], B1McpCompat__Database__Password=env['B1_SQL_PASS'])
        proc = subprocess.Popen([str(exe)], cwd=exe.parent, env=e, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(60):
            try: urllib.request.urlopen(CONN + '/', timeout=3); break
            except urllib.error.HTTPError: break
            except Exception: time.sleep(1)
        nat, con = client(sl, env), client(CONN, env); bad = 0
        for m, p, b, h in CASES:
            sn, hn, bn = nat(m, p, b, h); sc, hc, bc = con(m, p, b, h)
            bn, bc = norm(bn, [sl, 'https://localhost:50000']), norm(bc, [CONN, sl])
            dh = [k for k in KEEP if hn.get(k) != hc.get(k)]
            ok = sn == sc and bn == bc and not dh
            bad += not ok
            print('OK  ' if ok else 'DIFF', m, p[:70], sn, sc, len(bn), len(bc), dh if dh else '')
        print('TRANSPARENCY OK' if not bad else f'TRANSPARENCY FAIL ({bad})'); return 1 if bad else 0
    finally:
        if proc: subprocess.run(['taskkill', '/T', '/F', '/PID', str(proc.pid)], capture_output=True)
sys.exit(main())
