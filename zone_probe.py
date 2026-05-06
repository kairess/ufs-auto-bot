"""One-shot UDP sender for plugin discovery commands.

Usage (with the game running and BepInEx plugin loaded):
    .venv\\Scripts\\python zone_probe.py probe   # type/field discovery
    .venv\\Scripts\\python zone_probe.py dump    # live data dump

Plugin output goes to BepInEx\\LogOutput.log — tail it in another terminal.
"""

from __future__ import annotations
import socket
import sys

CMD_PORT = 18501


def main() -> int:
    arg = sys.argv[1] if len(sys.argv) > 1 else "probe"
    if arg in ("probe", "dump"):
        msg = f"zone:{arg}".encode("utf-8")
    elif arg == "fish":
        msg = b"fish:probe"
    elif arg == "fields":
        msg = b"fish:fields"
    elif arg == "terrain":
        msg = b"fish:terrain"
    elif arg == "bait":
        msg = b"bait:probe"
    elif arg == "likes":
        msg = b"bait:likes"
    elif arg == "interests":
        msg = b"bait:interests"
    elif arg == "species":
        msg = b"species:enum"
    elif arg == "reco":
        msg = b"zone:reco"
    elif arg == "loc":
        msg = b"loc:test"
    elif arg == "locbait":
        msg = b"loc:bait"
    elif arg == "terms":
        msg = b"loc:terms"
    elif arg == "ball":
        msg = b"bait:all"
    elif arg == "equip":
        msg = b"equip:probe"
    elif arg == "mgr":
        msg = b"equip:mgr"
    elif arg == "catalog":
        msg = b"equip:catalog"
    elif arg == "natural":
        msg = b"equip:natural"
    elif arg == "natcheck":
        msg = b"nat:check"
    elif arg == "flikes":
        msg = b"fish:likes"
    elif arg == "ilbait":
        msg = b"il:Fish.LikesBait"
    elif arg == "ilboilie":
        msg = b"il:Fish.LikesBoilie"
    elif arg == "iltaste":
        msg = b"il:Bait.CheckTaste"
    elif arg == "ilboiliefi":
        msg = b"il:Boilie.GetFishInterest"
    elif arg == "boilie":
        msg = b"boilie:probe"
    elif arg == "baitpart":
        msg = b"baitpart:probe"
    elif arg == "full":
        msg = b"equip:full"
    elif arg == "hook":
        msg = b"hook:cm"
    elif arg == "params":
        msg = b"equip:params"
    else:
        msg = arg.encode("utf-8")
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.sendto(msg, ("127.0.0.1", CMD_PORT))
    print(f"sent {msg!r} -> 127.0.0.1:{CMD_PORT}")
    print("now check the BepInEx LogOutput.log for [zone:probe] lines.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
