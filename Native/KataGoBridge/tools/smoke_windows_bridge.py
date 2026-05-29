import ctypes
import json
import sys
from pathlib import Path


ERROR_BUFFER_SIZE = 4096
DEFAULT_ENGINE_NAME = "native-eigen"
DEFAULT_TIMEOUT_MS = 45000


def encode_path(path: Path) -> bytes:
    return str(path).encode("utf-8")


def main() -> int:
    repo_root = Path(__file__).resolve().parents[3]
    engine_name = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_ENGINE_NAME
    timeout_ms = int(sys.argv[2]) if len(sys.argv) > 2 else DEFAULT_TIMEOUT_MS
    config_name = "analysis_example.cfg" if "opencl" in engine_name.lower() else "analysis_nowrite.cfg"
    engine_dir = repo_root / "KataGo" / "engines" / "win-x64" / engine_name
    dll_path = engine_dir / "katago_bridge.dll"
    config_path = engine_dir / config_name
    model_path = repo_root / "KataGo" / "models" / "kata1-b18c384nbt-s9996604416-d4316597426.bin.gz"
    human_model_path = repo_root / "KataGo" / "models" / "b18c384nbt-humanv0.bin.gz"

    for required_path in (dll_path, config_path, model_path, human_model_path):
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
    bridge.kg_create_engine_with_human_model.argtypes = [
        ctypes.c_char_p,
        ctypes.c_char_p,
        ctypes.c_char_p,
        ctypes.c_char_p,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.c_char_p,
        ctypes.c_int,
    ]
    bridge.kg_create_engine_with_human_model.restype = ctypes.c_int
    bridge.kg_analyze.argtypes = [
        ctypes.c_void_p,
        ctypes.c_char_p,
        ctypes.c_int,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.c_char_p,
        ctypes.c_int,
    ]
    bridge.kg_analyze.restype = ctypes.c_int
    bridge.kg_analyze_many.argtypes = [
        ctypes.c_void_p,
        ctypes.c_char_p,
        ctypes.c_int,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.c_char_p,
        ctypes.c_int,
    ]
    bridge.kg_analyze_many.restype = ctypes.c_int
    bridge.kg_free_string.argtypes = [ctypes.c_void_p]
    bridge.kg_destroy_engine.argtypes = [ctypes.c_void_p]
    bridge.kg_get_bridge_backend.argtypes = []
    bridge.kg_get_bridge_backend.restype = ctypes.c_char_p
    bridge.kg_supports_analyze_many.argtypes = []
    bridge.kg_supports_analyze_many.restype = ctypes.c_int

    bridge_backend = bridge.kg_get_bridge_backend().decode("utf-8")
    expected_backend = "opencl" if "opencl" in engine_name.lower() else "eigen"
    if bridge_backend != expected_backend:
        print(f"bridge backend mismatch: expected={expected_backend} actual={bridge_backend}", file=sys.stderr)
        return 1
    if bridge.kg_supports_analyze_many() == 0:
        print("bridge reports kg_analyze_many unsupported", file=sys.stderr)
        return 1

    engine = ctypes.c_void_p()
    error_buffer = ctypes.create_string_buffer(ERROR_BUFFER_SIZE)
    created = bridge.kg_create_engine_with_human_model(
        encode_path(config_path),
        encode_path(model_path),
        encode_path(human_model_path),
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
            "includePolicy": True,
            "overrideSettings": {"humanSLProfile": "rank_12k"},
        }
        response_ptr = ctypes.c_void_p()
        error_buffer = ctypes.create_string_buffer(ERROR_BUFFER_SIZE)
        analyzed = bridge.kg_analyze(
            engine,
            json.dumps(query, separators=(",", ":")).encode("utf-8"),
            timeout_ms,
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
        human_policy = response.get("humanPolicy")
        if response.get("id") != query["id"] or not isinstance(ownership, list) or not isinstance(human_policy, list):
            print(response_json, file=sys.stderr)
            return 1

        many_query = {
            "id": "native-smoke-many-9",
            "initialStones": [],
            "moves": [["B", "D4"], ["W", "E4"], ["B", "D5"]],
            "rules": "chinese",
            "komi": 7.5,
            "boardXSize": 9,
            "boardYSize": 9,
            "maxVisits": 1,
            "includeOwnership": False,
            "includePolicy": False,
            "analyzeTurns": [1, 3],
        }
        response_ptr = ctypes.c_void_p()
        error_buffer = ctypes.create_string_buffer(ERROR_BUFFER_SIZE)
        analyzed = bridge.kg_analyze_many(
            engine,
            json.dumps(many_query, separators=(",", ":")).encode("utf-8"),
            timeout_ms,
            ctypes.byref(response_ptr),
            error_buffer,
            ERROR_BUFFER_SIZE,
        )
        if analyzed == 0 or not response_ptr.value:
            print(error_buffer.value.decode("utf-8", errors="replace"), file=sys.stderr)
            return 1

        try:
            many_response_json = ctypes.string_at(response_ptr).decode("utf-8")
            many_response = json.loads(many_response_json)
        finally:
            bridge.kg_free_string(response_ptr)

        turn_numbers = sorted(result.get("turnNumber") for result in many_response)
        if len(many_response) != 2 or turn_numbers != many_query["analyzeTurns"]:
            print(many_response_json, file=sys.stderr)
            return 1

        print(
            f"id={response['id']} ownershipLength={len(ownership)} "
            f"humanPolicyLength={len(human_policy)} analyzeManyTurns={turn_numbers}"
        )
        return 0
    finally:
        bridge.kg_destroy_engine(engine)


if __name__ == "__main__":
    raise SystemExit(main())
