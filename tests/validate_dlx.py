"""Deployment asset validation; does not replace opening DLX in NX Block UI Styler."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
data = (repo / "ClassLibrary1.dlx").read_bytes()
assert data.startswith(b'<?xml'), "XML declaration must start the DLX"
root = ET.fromstring(data)
items = root.findall(".//item")
ids = [item.attrib["id"] for item in items]
assert len(ids) == len(set(ids)), "Duplicate block IDs"
expected = {"filePath", "namePatterns", "IDPattern", "group", "flagImageOn", "viewList", "group0", "flagXYZOn", "coordinateSelection"}
assert set(ids) == expected, "Unexpected/missing blocks"
nav = root.find("./PropertyList/Property[@id='NavigationStyle']")
assert nav.attrib["selected"] == "0", "Only OK/Cancel is supported"
assert nav.find("./Option[@value='0']").attrib["name"] == "OK Cancel"
views = root.find(".//item[@id='viewList']/PropertyList/Property[@sname='ListItems']")
actual = [entry.attrib["value"] for entry in views]
source = (repo / "ImageExporter.cs").read_text(encoding="utf-8")
match = re.search(r"enum SnapViewType\s*\{([^}]+)\}", source)
assert actual == [value.strip() for value in match[1].split(",")], "DLX/C# view mismatch"
coordinates = root.find(".//item[@id='coordinateSelection']/PropertyList/Property[@sname='Value']")
assert [entry.attrib["name"] for entry in coordinates] == ["绝对坐标系", "工作坐标系"]
project = ET.parse(repo / "NX_Traverser.csproj")
ns = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}
for name in ("NXOpen", "NXOpen.Utilities", "NXOpenUI", "NXOpen.UF"):
    reference = project.find(f".//m:Reference[@Include='{name}']", ns)
    assert reference is not None, f"Missing SDK reference: {name}"
    assert reference.find("m:HintPath", ns).text == f"$(NXManagedDir)\\{name}.dll"
    assert reference.find("m:Private", ns).text == "false", "SDK assemblies must not be deployed"
assembly = project.find(".//m:AssemblyName", ns).text
controller = (repo / "UIscripts.cs").read_text(encoding="utf-8")
assert re.search(rf"public sealed class {re.escape(assembly)}\s*:", controller), "Entry class/assembly mismatch"
assert "public static void Main()" in controller, "Missing plugin entry"
assert "public static int GetUnloadOption(string arg)" in controller, "Missing unload hook"
print("PASS: DLX XML/mappings, project SDK references and entry-name consistency (not SDK compilation)")
