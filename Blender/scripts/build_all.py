"""Rebuild Facecast models: FBX + .blend + preview PNG.

Usage (from anywhere):
  Blender -b --factory-startup -P Blender/scripts/build_all.py -- [options] [ModelName ...]

Options:
  --no-preview   skip per-model preview renders
  --list         print model names and exit
With no model names, every model is rebuilt. Model modules register themselves in MODULES below; each
defines MODELS = {"Name": builder(root)}.
"""

import importlib
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import common  # noqa: E402

MODULES = ["models_avatar", "models_plaza", "models_boards"]


def registry():
    reg = {}
    for mod_name in MODULES:
        try:
            mod = importlib.import_module(mod_name)
        except ModuleNotFoundError as e:
            if e.name == mod_name:
                print("[build_all] (module %s not present, skipped)" % mod_name)
                continue
            raise
        for name, fn in mod.MODELS.items():
            if name in reg:
                raise RuntimeError("model %s defined twice" % name)
            reg[name] = fn
    return reg


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    flags = {a for a in argv if a.startswith("--")}
    names = [a for a in argv if not a.startswith("--")]
    reg = registry()
    if "--list" in flags:
        print("\n".join(reg))
        return
    unknown = [n for n in names if n not in reg]
    if unknown:
        raise SystemExit("unknown model(s): %s" % ", ".join(unknown))
    todo = names or list(reg)
    t0 = time.time()
    for name in todo:
        t = time.time()
        tris = common.build_model(name, reg[name], preview="--no-preview" not in flags)
        print("[build_all] %-14s %6d tris  %.1fs" % (name, tris, time.time() - t))
    print("[build_all] built %d model(s) in %.1fs" % (len(todo), time.time() - t0))


if __name__ == "__main__":
    main()
