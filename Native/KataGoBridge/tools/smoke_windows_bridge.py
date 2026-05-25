import ctypes
import json
import sys
from pathlib import Path


ERROR_BUFFER_SIZE = 4096


def encode_path(path: Path) -> bytes:
    return str(path).encode("utf-8")


def main() -> int:
    repo_root = Path(__file__).resolve().parents[3]
    engine_dir = repo_root / "KataGo" / "engines" / "win-x64" / "native-eigen"
    dll_path = engine_dir / "katago_bridge.dll"
    config_path = engine_dir / "analysis_nowrite.cfg"
    model_path = repo_root / "KataGo" / "models" / "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz"

    for required_path in (dll_path, config_path, model_path):
        if not required_path.exists():
            print(f"missing: {required_path}", file=sys.stderr)
            return 1

    bridge = ctypes.CDLL(str(dll_path))
    bridge.kg_create_engine.argtypes = [
        ctypes.c_char_p,
        ctypes.c_char_p,
        ctypes.c_char_p,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.c_char_p,
        ctypes.c_int,
    ]
    bridge.kg_create_engine.restype = ctypes.c_int
    bridge.kg_analyze.argtypes = [
        ctypes.c_void_p,
        ctypes.c_char_p,
        ctypes.c_int,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.c_char_p,
        ctypes.c_int,
    ]
    bridge.kg_analyze.restype = ctypes.c_int
    bridge.kg_free_string.argtypes = [ctypes.c_void_p]
    bridge.kg_destroy_engine.argtypes = [ctypes.c_void_p]

    engine = ctypes.c_void_p()
    error_buffer = ctypes.create_string_buffer(ERROR_BUFFER_SIZE)
    created = bridge.kg_create_engine(
        encode_path(config_path),
        encode_path(model_path),
        encode_path(engine_dir),
        ctypes.byref(engine),
        error_buffer,
        ERROR_BUFFER_SIZE,
    )
    if created == 0 or not engine.value:
        print(error_buffer.value.decode("utf-8", errors="replace"), file=sys.stderr)
        return 1

    try:
        query = {
            "id": "native-smoke-9",
            "initialStones": [],
            "moves": [],
            "rules": "chinese",
            "komi": 7.5,
            "boardXSize": 9,
            "boardYSize": 9,
            "maxVisits": 1,
            "includeOwnership": True,
            "includePolicy": False,
        }
        response_ptr = ctypes.c_void_p()
        error_buffer = ctypes.create_string_buffer(ERROR_BUFFER_SIZE)
        analyzed = bridge.kg_analyze(
            engine,
            json.dumps(query, separators=(",", ":")).encode("utf-8"),
            45000,
            ctypes.byref(response_ptr),
            error_buffer,
            ERROR_BUFFER_SIZE,
        )
        if analyzed == 0 or not response_ptr.value:
            print(error_buffer.value.decode("utf-8", errors="replace"), file=sys.stderr)
            return 1

        try:
            response_json = ctypes.string_at(response_ptr).decode("utf-8")
            response = json.loads(response_json)
        finally:
            bridge.kg_free_string(response_ptr)

        ownership = response.get("ownership")
        if response.get("id") != query["id"] or not isinstance(ownership, list):
            print(response_json, file=sys.stderr)
            return 1

        print(f"id={response['id']} ownershipLength={len(ownership)}")
        return 0
    finally:
        bridge.kg_destroy_engine(engine)


if __name__ == "__main__":
    raise SystemExit(main())
