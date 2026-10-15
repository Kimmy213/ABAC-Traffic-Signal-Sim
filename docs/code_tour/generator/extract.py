"""Reads the Unity C# sources and SampleScene.unity and returns the facts the code tour quotes.

Nothing here is typed in by hand: field names, types, defaults, line numbers and scene values all
come from the files themselves, so a page can never show a variable that does not exist.
"""
import os
import re

import yaml

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SCRIPT_DIRS = ["Assets/Scripts", "Assets/Editor"]
SCENE = "Assets/Scenes/SampleScene.unity"

CLASS_RE = re.compile(r"^(\s*)(?:\[[^\]]*\]\s*)*(?:public\s+|internal\s+|private\s+|protected\s+)?(?:static\s+)?(?:sealed\s+)?(class|struct|enum)\s+(\w+)")
FIELD_RE = re.compile(
    r"^(\s*)((?:\[[^\]]*\]\s*)*)public\s+((?:static\s+readonly|const|static|readonly)\s+)?"
    r"([\w\.]+(?:<[\w\.,\s<>]+>)?(?:\[,?\])?)\s+(\w+)\s*(?:=\s*(.+?))?\s*;\s*(?://\s*(.*))?$"
)
STRING_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')


def rel(path):
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def source_files():
    out = []
    for d in SCRIPT_DIRS:
        full = os.path.join(ROOT, d)
        for name in sorted(os.listdir(full)):
            if name.endswith(".cs"):
                out.append(os.path.join(full, name))
    return out


def read_lines(path):
    with open(path, encoding="utf-8") as f:
        return f.read().split("\n")


def _attribute_block(lines, i):
    """Collects the [Attribute(...)] lines directly above line i (stopping at code or blank)."""
    attrs = []
    j = i - 1
    buf = []
    while j >= 0:
        s = lines[j].strip()
        if s.startswith("//") or s.startswith("///"):
            j -= 1
            continue
        if not s:
            break
        buf.insert(0, s)
        joined = " ".join(buf)
        if joined.startswith("["):
            attrs.insert(0, joined)
            buf = []
        elif not (s.endswith(")]") or s.endswith('" +') or s.endswith('",') or s.startswith('"')):
            break
        j -= 1
    return " ".join(attrs)


def _tooltip(attr_text):
    m = re.search(r"\[Tooltip\((.*?)\)\]", attr_text, re.S)
    if not m:
        return ""
    parts = STRING_RE.findall(m.group(1))
    return "".join(parts).replace("\\n", "\n").replace('\\"', '"')


def _header_above(lines, i):
    for j in range(i, -1, -1):
        m = re.search(r'\[Header\("([^"]*)"\)\]', lines[j])
        if m:
            return m.group(1)
        if CLASS_RE.match(lines[j]):
            return ""
    return ""


def _range(attr_text):
    m = re.search(r"\[Range\(([^,]+),\s*([^)]+)\)\]", attr_text)
    return (m.group(1).strip().rstrip("f"), m.group(2).strip().rstrip("f")) if m else None


def extract_fields():
    """Returns {class_name: {"file": rel, "line": n, "kind": "class", "fields": {name: info}}}."""
    classes = {}
    for path in source_files():
        lines = read_lines(path)
        stack = []  # (indent, name)
        for i, line in enumerate(lines):
            cm = CLASS_RE.match(line)
            if cm:
                indent = len(cm.group(1))
                while stack and stack[-1][0] >= indent:
                    stack.pop()
                stack.append((indent, cm.group(3)))
                classes.setdefault(cm.group(3), {
                    "file": rel(path), "line": i + 1, "kind": cm.group(2), "fields": {}})
                continue
            fm = FIELD_RE.match(line)
            if not fm or "=>" in line or "{ get" in line or "(" in fm.group(4):
                continue
            indent = len(fm.group(1))
            owners = [n for (ind, n) in stack if ind < indent]
            if not owners:
                continue
            owner = owners[-1]
            mod = (fm.group(3) or "").strip()
            attrs = _attribute_block(lines, i) + " " + (fm.group(2) or "")
            default = fm.group(6)
            if default is not None:
                default = default.strip()
            classes[owner]["fields"][fm.group(5)] = {
                "name": fm.group(5),
                "type": fm.group(4),
                "modifier": mod,
                "default": default,
                "line": i + 1,
                "file": rel(path),
                "header": _header_above(lines, i),
                "tooltip": _tooltip(attrs),
                "range": _range(attrs),
                "hidden": "HideInInspector" in attrs,
                "comment": (fm.group(7) or "").strip(),
            }
    return classes


def script_guids():
    out = {}
    for d in SCRIPT_DIRS:
        full = os.path.join(ROOT, d)
        for name in os.listdir(full):
            if name.endswith(".cs.meta"):
                with open(os.path.join(full, name), encoding="utf-8") as f:
                    m = re.search(r"guid:\s*([0-9a-f]+)", f.read())
                if m:
                    out[m.group(1)] = name[:-len(".cs.meta")]
    return out


def extract_scene():
    """Returns {class_name: [{"object": game_object_name, "values": {field: value}}]}."""
    guids = script_guids()
    with open(os.path.join(ROOT, SCENE), encoding="utf-8") as f:
        text = f.read()
    docs = re.split(r"^--- !u!(\d+) &(\d+).*$", text, flags=re.M)
    objects, behaviours = {}, []
    for k in range(1, len(docs) - 2, 3):
        type_id, file_id, body = docs[k], docs[k + 1], docs[k + 2]
        if type_id not in ("1", "114"):
            continue
        try:
            data = yaml.safe_load(body)
        except yaml.YAMLError:
            continue
        if not isinstance(data, dict):
            continue
        if type_id == "1" and "GameObject" in data:
            objects[file_id] = data["GameObject"].get("m_Name", "")
        elif type_id == "114" and "MonoBehaviour" in data:
            behaviours.append(data["MonoBehaviour"])
    out = {}
    for mb in behaviours:
        guid = (mb.get("m_Script") or {}).get("guid")
        cls = guids.get(guid)
        if not cls:
            continue
        go = str((mb.get("m_GameObject") or {}).get("fileID"))
        values = {k: v for k, v in mb.items() if not k.startswith("m_")}
        out.setdefault(cls, []).append({"object": objects.get(go, "?"), "values": values})
    for cls in out:
        out[cls].sort(key=lambda r: r["object"])
    return out


def line_count(rel_path):
    return len(read_lines(os.path.join(ROOT, rel_path))) - 1


if __name__ == "__main__":
    import json
    c = extract_fields()
    s = extract_scene()
    print(json.dumps({k: list(v["fields"]) for k, v in c.items()}, indent=1))
    print({k: [r["object"] for r in v] for k, v in s.items()})
