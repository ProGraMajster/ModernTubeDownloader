"""Review aid: emit public parser option identities/arity, never execute upstream Python.

Apply the reviewed stdout to Resources/YtDlpOptions.json; production needs no Python.
The guard deliberately rejects unknown future options until this catalog is reviewed.
"""
import ast
import hashlib
import json
import urllib.request


def fetch(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "MTD-parser-review"}), timeout=30).read()


commit = json.loads(fetch("https://api.github.com/repos/yt-dlp/yt-dlp/commits/master"))["sha"]
source = fetch(f"https://raw.githubusercontent.com/yt-dlp/yt-dlp/{commit}/yt_dlp/options.py")
groups = []
for call in ast.walk(ast.parse(source)):
    if not isinstance(call, ast.Call) or not isinstance(call.func, ast.Attribute) or call.func.attr != "add_option":
        continue
    names = [arg.value for arg in call.args if isinstance(arg, ast.Constant) and isinstance(arg.value, str) and arg.value.startswith("-")]
    if not names:
        continue  # Dynamic aliases are themselves forbidden by the application guard.
    keywords = {k.arg: k.value for k in call.keywords}
    action = keywords.get("action")
    action = action.value if isinstance(action, ast.Constant) else "store"
    arity = 0 if action in ("store_true", "store_false", "store_const", "count", "help", "version") else 1
    if action == "callback" and "type" not in keywords and not any(k.arg is None for k in call.keywords):
        arity = 0
    if "nargs" in keywords:
        arity = ast.literal_eval(keywords["nargs"])
    groups.append({"options": names, "arity": arity})
print(json.dumps({"upstreamCommit": commit, "sourceSha256": hashlib.sha256(source).hexdigest(), "groups": groups}, indent=2))
