"""Lists survivors' skills straight from the game's Addressables bundles, to find skills the Skills tab hides.

Usage (UnityPy in a scratch venv; run with -I -X utf8):
    python -I -X utf8 skill_census.py <StreamingAssets dir> <group> [<group> ...]

A group is a bundle name prefix such as ror2-dlc4-acolyte; its _assets_ and _static_assets_ bundles are
loaded. Prints:
    FAMILY  a skill family and its variants (what the Loadout tab offers)
    SLOT    a GenericSkill on a body prefab, with hideInCharacterSelect and loadoutTitleToken
    REF     any other serialized reference to a SkillDef: an entity state configuration's field, or a
            component's field on a prefab. Hidden skills are the REF targets that are not FAMILY variants.
Each skill shows its name and description tokens with the English text, and its activation state.
References can point into bundles of other groups; pass those groups too, or the target shows unresolved.
"""
import glob
import json
import os
import re
import sys

import UnityPy


def load_english(streaming_assets):
    strings = {}
    for path in glob.glob(os.path.join(streaming_assets, "Language", "en", "*.json")):
        try:
            with open(path, encoding="utf-8-sig") as f:
                data = json.load(f)
        except (OSError, ValueError):
            continue
        for value in data.values():
            if isinstance(value, dict):
                strings.update({k: v for k, v in value.items() if isinstance(v, str)})
    return strings


def main(streaming_assets, groups):
    english = load_english(streaming_assets)
    bundle_dir = os.path.join(streaming_assets, "aa", "StandaloneWindows64")
    paths = []
    for group in groups:
        paths += glob.glob(os.path.join(bundle_dir, group + "_assets_all_*.bundle"))
        paths += glob.glob(os.path.join(bundle_dir, group + "_static_assets_all_*.bundle"))

    # (serialized file name, path id) -> what the object is, for every MonoBehaviour and GameObject.
    objects = {}
    for path in paths:
        env = UnityPy.load(path)
        for bundle in env.files.values():
            for inner in getattr(bundle, "files", {}).values():
                if not hasattr(inner, "objects"):
                    continue
                cab = os.path.basename(inner.name).lower()
                externals = [os.path.basename(e.path).lower() for e in inner.externals]
                for path_id, obj in inner.objects.items():
                    if obj.type.name not in ("MonoBehaviour", "GameObject"):
                        continue
                    try:
                        tree = obj.read_typetree()
                    except Exception:  # noqa: BLE001 - some scripts have no type tree; skip them
                        continue
                    group = os.path.basename(path).split("_")[0]
                    objects[(cab, path_id)] = {"group": group, "type": obj.type.name, "tree": tree, "externals": externals}

    def resolve(owner, externals, pptr):
        file_id, path_id = pptr.get("m_FileID", 0), pptr.get("m_PathID", 0)
        if path_id == 0:
            return None, None
        cab = owner if file_id == 0 else (externals[file_id - 1] if file_id <= len(externals) else "?")
        return (cab, path_id), objects.get((cab, path_id))

    def is_skill(info):
        return info and "skillNameToken" in info["tree"] and "activationState" in info["tree"]

    def describe(key, info):
        if info is None:
            return f"<unresolved {key}>"
        t = info["tree"]
        name, desc = t.get("skillNameToken", ""), t.get("skillDescriptionToken", "")
        text = re.sub(r"<[^>]+>", "", english.get(desc, "?"))[:110]
        state = t.get("activationState", {}).get("_typeName", "")
        return f"{t.get('m_Name')} ({name} = {english.get(name, '?')!r}, {desc} = {text!r}, state={state})"

    def pointers(node, trail):
        if isinstance(node, dict):
            if {"m_FileID", "m_PathID"} <= set(node):
                yield trail, node
                return
            for k, v in node.items():
                yield from pointers(v, trail + [k])
        elif isinstance(node, list):
            for i, v in enumerate(node):
                yield from pointers(v, trail + [str(i)])

    report = set()
    for key, info in objects.items():
        t = info["tree"]
        if info["type"] != "MonoBehaviour" or is_skill(info):
            continue
        prefix = f"[{info['group']}]"
        if "variants" in t and "defaultVariantIndex" in t:
            variants = [describe(*resolve(key[0], info["externals"], v.get("skillDef", {}))) for v in t["variants"]]
            report.add(f"{prefix} FAMILY {t.get('m_Name')}: " + " | ".join(variants))
            continue

        owner = t.get("m_Name") or ""
        go = t.get("m_GameObject", {})
        if go.get("m_PathID") and go.get("m_FileID", 0) == 0:
            owner_info = objects.get((key[0], go["m_PathID"]))
            owner = owner_info["tree"].get("m_Name") if owner_info else owner

        if "_skillFamily" in t and "hideInCharacterSelect" in t:
            _, family = resolve(key[0], info["externals"], t["_skillFamily"])
            family_name = family["tree"].get("m_Name") if family else "<unresolved>"
            report.add(f"{prefix} SLOT on {owner}: {t.get('skillName')!r} family={family_name} "
                       f"hideInCharacterSelect={t.get('hideInCharacterSelect')} loadoutTitleToken={t.get('loadoutTitleToken')!r}")
            continue

        for trail, pptr in pointers(t, []):
            if trail and trail[0] in ("m_GameObject", "m_Script"):
                continue
            target_key, target = resolve(key[0], info["externals"], pptr)
            if not is_skill(target):
                continue
            if "serializedFieldsCollection" in t:
                label = t.get("targetType", {}).get("assemblyQualifiedName", "").split(",")[0]
                field = t["serializedFieldsCollection"]["serializedFields"][int(trail[2])]["fieldName"]
            else:
                label, field = f"component on {owner}", ".".join(trail)
            report.add(f"{prefix} REF {label} .{field} -> {describe(target_key, target)}")

    for line in sorted(report):
        print(line)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2:])
