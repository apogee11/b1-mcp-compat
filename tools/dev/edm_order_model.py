"""Modelo del orden de salida del $metadata plano 2608 (ServiceLayer.dll, std::unordered_map<std::string,..> MSVC + FNV-1a).
Estado 2026-10-27: EntityType 425/425, EntitySet 458/458, ComplexType 942/942 (2608; ver rea/01-findings.md, sesion 4).
Uso: python tools/dev/edm_order_model.py   (lee rea/kbgen/out/csmd_B11000_2608.json, setname_params.json y edm-2608.xml.gz)
DEPENDENCIA DE EMPRESA (BD de prueba): COMPANY_* (UDT/UDO U_SBOCTXT, SBOUDT, U_SBOBULK e InvokeParams) y su posicion relativa no salen del instalador."""
import gzip, json, re, pathlib
ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / 'rea' / 'kbgen' / 'out'

import functools
@functools.lru_cache(maxsize=None)
def fnv(s):
    h = 14695981039346656037
    for b in s.encode('utf8'):
        h = ((h ^ b) * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return h

class MsvcUnorderedMap:
    """Orden de lista: clave nueva al FRENTE de su cubo; cubo vacio -> al final; crecimiento ANTES de insertar cuando
    size+1 > cubos (max_load 1.0): 8 -> 64 -> 512 -> (x2); el rehash recorre la lista y agrupa por cubo nuevo
    en orden de primera aparicion, INVIRTIENDO el orden dentro de cada cubo."""
    def __init__(self):
        self.lst, self.nb = [], 8
    def insert(self, k):
        if k in self.lst: return
        if len(self.lst) + 1 > self.nb: self._grow()
        b = fnv(k) & (self.nb - 1)
        idx = [i for i, n in enumerate(self.lst) if fnv(n) & (self.nb - 1) == b]
        if idx: self.lst.insert(idx[0], k)
        else: self.lst.append(k)
    def _grow(self):
        self.nb = self.nb * 8 if self.nb < 512 else self.nb * 2
        groups, order = {}, []
        for n in self.lst:
            b = fnv(n) & (self.nb - 1)
            if b not in groups: groups[b] = []; order.append(b)
            groups[b].append(n)
        self.lst = [n for b in order for n in reversed(groups[b])]

def measured():
    x = gzip.open(OUT / 'edm-2608.xml.gz', 'rt', encoding='utf8').read()
    return re.findall(r'<EntityType (?:[^>]*? )?Name="([^"]+)"', x)

def entity_type_order():
    c = json.load(open(OUT / 'csmd_B11000_2608.json', encoding='utf8'))
    m = measured(); ms = set(m)
    keys, seen = [], set()
    # orden de insercion = servicios del catalogo por CODIGO-CADENA (std::map ordenado por cadena); ScheduleJob se inserta pero no se emite
    for s in c['services']:
        for d in s['datas']:
            t = d['type']
            if (t in ms or t == 'ScheduleJob') and t not in seen: seen.add(t); keys.append(t)
    keys += ['U_SBOCTXT', 'SBOUDT', 'U_SBOBULK']   # servicios de empresa (UDT del DB de prueba), al final
    u = MsvcUnorderedMap()
    for k in keys: u.insert(k)
    return [x for x in u.lst if x in ms], m


def measured_of(tag):
    x = gzip.open(OUT / 'edm-2608.xml.gz', 'rt', encoding='utf8').read()
    return re.findall(r'<%s (?:[^>]*? )?Name="([^"]+)"' % tag, x)

# ---- EntitySet: AddEntitSets (0x18001e060) itera vt[0x90] = ServiceParams del std::map +0x28 (orden por codigo-cadena), una clave por
# ServiceParams = nombre expuesto (setname_params.json 'exp', = FUN_180018dd0/GetExposedNameOfEntitySet); se insertan TODOS (tambien ScheduleJobs, SBOMailers, no emitidos),
# luego los de empresa. Fuera del mapa, al final: B1Sessions, ItemImages, EmployeeImages, Pictures.
COMPANY_ES = ['U_SBOCTXT', 'SBOUDT', 'U_SBOBULK']   # cualquier permutacion da lo mismo
def entity_set_order():
    sp = json.load(open(OUT / 'setname_params.json', encoding='utf8'))
    m = measured_of('EntitySet'); ms = set(m)
    u = MsvcUnorderedMap()
    for r in sp.values(): u.insert(r['exp'])
    for k in COMPANY_ES: u.insert(k)
    return [x for x in u.lst if x in ms] + ['B1Sessions', 'ItemImages', 'EmployeeImages', 'Picture' + 's'], m

# ---- ComplexType: FUN_18001d890 -> AddDeclareType (FUN_180026aa0). Bucle 1 = servicios CON ServiceParams (vt[0x90], codigo-cadena, luego empresa);
# bucle 2 = vt[0x98] (servicios sin ServiceParams; [V] sesion 5: IServicesMD::GetServiceMDList -> FUN_18124d5f0 = recorrido en orden de clave de un std::map, comunes y luego claves de empresa no numericas; comparador de SBOString no decompilado). Por data: si NO es principal (tipo no-ET) inserta su tipo; luego inserta TODOS los tipos de sus hijos
# directos (pasada A) y despues recursa en cada hijo (pasada B). JournalVoucher se salta. ScheduleJob es principal (ET oculto), ScheduleJobParams y
# BackendSchedulingSubTasks se insertan pero no se emiten. Tipos ET nunca son CT. Fuera del mapa, al final: CompanySummary, Event, EventCatagory.
# EMPRESA: orden empirico (todas las posiciones 1..4 de InvokeParams dan 100 %); InvokeParams viene de CompanyServicesMeteData.cpp (UDO Invoke), no del catalogo comun. [V] es un CDataMD creado en FUN_1804fddf0/FUN_1804fea10, no una clave de servicio; [H] posicion y orden relativo del bloque siguen siendo empiricos (ver rea/01-findings.md sesion 5).
COMPANY_CT = ['SBOUDTParams', 'SBOUDTL', 'InvokeParams', 'U_SBOCTXTParams', 'U_SBOBULKParams']
def complex_type_order():
    c = json.load(open(OUT / 'csmd_B11000_2608.json', encoding='utf8'))
    sp = json.load(open(OUT / 'setname_params.json', encoding='utf8'))
    et = set(measured()) | {'ScheduleJob'}
    out = []
    def add(d, main):
        if d['type'] == 'JournalVoucher': return
        if not main: out.append(d['type'])
        for ch in d['children']: out.append(ch['type'])
        for ch in d['children']: add(ch, False)
    S = c['services']
    for grp in ([s for s in S if s['code'] in sp], None, [s for s in S if s['code'] not in sp]):
        if grp is None: out.extend(COMPANY_CT); continue
        for s in grp:
            for d in s['datas']: add(d, d['type'] in et)
    m = measured_of('ComplexType'); ms = set(m)
    u = MsvcUnorderedMap()
    for k in out:
        if k not in et: u.insert(k)
    return [x for x in u.lst if x in ms] + ['CompanySummary', 'Event', 'EventCatagory'], m

if __name__ == '__main__':
    r, m = entity_type_order()
    tail = ['B1Session', 'ItemImage', 'EmployeeImage', 'Picture']   # anadidos fuera del mapa (final)
    print('EntityType', sum(a == b for a, b in zip(r + tail, m)), '/', len(m))
    r, m = entity_set_order()
    print('EntitySet', sum(a == b for a, b in zip(r, m)), '/', len(m))
    r, m = complex_type_order()
    print('ComplexType', sum(a == b for a, b in zip(r, m)), '/', len(m))
