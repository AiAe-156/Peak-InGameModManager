import re, sys, json
sys.stdout.reconfigure(encoding="utf-8")
data = open(sys.argv[1], "rb").read()
# InputActionAsset stores m_ActionMaps JSON-ish? Actually serialized; find readable strings around Gamepad paths
for m in re.finditer(rb'"name"\s*:\s*"([^"]+)"', data[:0]): pass
idx = data.find(b'<Gamepad>/buttonSouth')
print("first at", idx)
# dump printable strings in a window covering all <Gamepad> occurrences
occ = [m.start() for m in re.finditer(rb'<Gamepad>/', data)]
print("count", len(occ), "range", occ[0], occ[-1])
lo, hi = max(0, occ[0]-20000), occ[-1]+2000
seg = data[lo:hi]
strings = re.findall(rb'[\x20-\x7e]{3,}', seg)
open("raw_strings.txt","w",encoding="utf-8").write("\n".join(s.decode() for s in strings))
print(len(strings))
