#!/usr/bin/env python3
"""Prepare and launch isolated macOS instances using the game's native fastmp mode."""
import argparse
from datetime import datetime
import hashlib
import json
from pathlib import Path
import platform
import shutil
import subprocess


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_GAME = Path.home() / "Library/Application Support/Steam/steamapps/common/Slay the Spire 2"
DEFAULT_SESSION = ROOT / "output/multiplayer/beta-test"
PLAYERS = {"host": 1, "client": 1001}


def prepare(session, game, progress):
    if platform.system() != "Darwin":
        raise SystemExit("This launcher currently supports macOS only.")
    if session.exists():
        raise SystemExit("Session already exists. Reuse it, or choose a new --session directory.")
    app = game / "SlayTheSpire2.app"
    dll = ROOT / "bin/Release/net9.0/Gravity.dll"
    if not app.is_dir() or not dll.is_file():
        raise SystemExit("Game or built Gravity.dll missing. Run the documented build first.")
    if progress is not None and not progress.is_file():
        raise SystemExit("Progress file does not exist.")
    # A unique custom user directory isolates even user://gravity.cfg, which is
    # shared by all native player IDs within one Godot user directory.
    token = hashlib.sha256(str(session).encode()).hexdigest()[:12]
    user_base = Path.home() / "Library/Application Support/GravityMultiplayerTests" / token
    if user_base.exists():
        raise SystemExit(f"Existing test data at {user_base}; choose a new --session.")
    session.mkdir(parents=True)
    manifest = {"source_game": str(game), "players": {},
                "gravity_sha256": hashlib.sha256(dll.read_bytes()).hexdigest()}
    for role, player_id in PLAYERS.items():
        role_dir = session / role
        role_dir.mkdir()
        copied_app = role_dir / app.name
        subprocess.run(["cp", "-cR", str(app), str(copied_app)], check=True)
        executable_dir = copied_app / "Contents/MacOS"
        # These are newly created disposable copies, never the source game.
        for name in ("mods", "mods_STEAMTEST"):
            copied_mods = executable_dir / name
            if copied_mods.exists():
                shutil.rmtree(copied_mods)
        mod_dir = executable_dir / "mods/Gravity"
        mod_dir.mkdir(parents=True)
        shutil.copy2(dll, mod_dir / "Gravity.dll")
        shutil.copy2(ROOT / "Gravity.json", mod_dir / "Gravity.json")
        user_dir = user_base / role
        user_dir.mkdir(parents=True)
        (executable_dir / "override.cfg").write_text(
            '[application]\nconfig/use_custom_user_dir=true\n'
            f'config/custom_user_dir_name="GravityMultiplayerTests/{token}/{role}"\n')
        is_host = role == "host"
        (user_dir / "gravity.cfg").write_text(
            '[settings]\nhue=43\npulse_percent=25\n'
            f'encounter_mode={3 if is_host else 1}\n'
            f'custom_encounters={2 if is_host else 15}\n'
            f'encounters={2 if is_host else 15}\n'
            f'lock_encounters_after_boss_unlock={"false" if is_host else "true"}\n')
        account = user_dir / "default" / str(player_id)
        account.mkdir(parents=True)
        settings = {
            "schema_version": 8, "language": "eng", "fullscreen": False,
            "window_size": {"X": 960, "Y": 540}, "fps_limit": 30,
            "limit_fps_in_background": False, "volume_master": 0.0,
            "skip_intro_logo": True, "seen_ea_disclaimer": True,
            "mod_settings": {"mods_enabled": True, "mod_list": [
                {"id": "Gravity", "is_enabled": True, "source": "mods_directory"}]},
        }
        (account / "settings.save").write_text(json.dumps(settings, indent=2))
        if progress:
            saves = account / "modded/profile1/saves"
            saves.mkdir(parents=True)
            shutil.copy2(progress, saves / "progress.save")
            (account / "modded/profile.save").write_text(
                json.dumps({"last_profile_id": 1, "schema_version": 2}))
        manifest["players"][role] = {
            "id": player_id, "executable": str(executable_dir / "Slay the Spire 2"),
            "userdata": str(user_dir),
        }
    (session / "session.json").write_text(json.dumps(manifest, indent=2))
    print(f"Prepared {session}\nUser data: {user_base}")


def command(session, role, mode):
    manifest = json.loads((session / "session.json").read_text())
    player = manifest["players"][role]
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
    log = session / role / f"{mode}-{stamp}.log"
    return [player["executable"], "--force-steam", "off", "--fastmp", mode,
            "--clientId", str(player["id"]), "--log-file", str(log),
            "--windowed", "--resolution", "960x540", "--max-fps", "30",
            "-wpos", "20" if role == "host" else "1000", "80"], log


def launch(session, action):
    role = "client" if action == "client" else "host"
    mode = {"host": "host_standard", "client": "join", "load-host": "load"}[action]
    args, log = command(session, role, mode)
    # Refuse a second copy of the same player; rejoining requires the original ID.
    processes = subprocess.run(["ps", "-ax", "-o", "command="],
                               check=True, text=True, capture_output=True).stdout
    if args[0] in processes:
        raise SystemExit(f"{role} is already running. Close that test window first.")
    with log.with_suffix(".stdout.log").open("w") as output:
        process = subprocess.Popen(args, cwd=session / role, stdout=output,
                                   stderr=subprocess.STDOUT, start_new_session=True)
    print(f"Launched {role}: PID {process.pid}\nLog: {log}")


def status(session):
    manifest = json.loads((session / "session.json").read_text())
    for role, player in manifest["players"].items():
        user_dir = Path(player["userdata"])
        print(f"\n{role} (ID {player['id']}): {user_dir}")
        print((user_dir / "gravity.cfg").read_text().strip())
        cache = user_dir / "gravity_run_settings.json"
        print("Remembered host settings: " + (cache.read_text() if cache.exists() else "not received yet"))
        for save in user_dir.glob("default/*/modded/profile*/saves/current_run_mp.save"):
            data = json.loads(save.read_text())
            print(f"Host save: {save}")
            print("Run extra fields: " + json.dumps(data.get("extra_fields", {})))
        print("A cache/save inspection does not establish live UI or reconnect success.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "host", "client", "load-host", "status"))
    parser.add_argument("--session", type=Path, default=DEFAULT_SESSION)
    parser.add_argument("--game-path", type=Path, default=DEFAULT_GAME)
    parser.add_argument("--progress", type=Path, help="Copy unlock progress only; no active run is copied")
    args = parser.parse_args()
    session = args.session.expanduser().resolve()
    if args.action == "prepare":
        prepare(session, args.game_path.expanduser().resolve(), args.progress)
    elif args.action == "status":
        status(session)
    else:
        launch(session, args.action)


if __name__ == "__main__":
    main()
