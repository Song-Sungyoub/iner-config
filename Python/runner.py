"""InerConfig private runner. Local user code, NOT a security sandbox."""
import json
import math
import sys
import traceback
from pathlib import Path

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")
# The host assigns the Job Object BEFORE sending this request. No user code
# executes until the complete request has been received.
request = json.load(sys.stdin)
run_id = request["runId"]
response = {"protocolVersion": 1, "runId": run_id, "success": False}
try:
    code, value = request["code"], request["value"]
    if not isinstance(code, str) or len(code.encode("utf-8")) > 65536:
        raise ValueError("코드는 UTF-8 64 KiB 이하여야 합니다.")
    if not isinstance(value, str) or len(value.encode("utf-8")) > 1048576:
        raise ValueError("입력은 UTF-8 1 MiB 이하여야 합니다.")
    scope = {"value": value, "__name__": "__main__"}
    exec(compile(code, "user_script.py", "exec"), scope)
    if "result" not in scope:
        raise ValueError("result에 최종값을 할당해주세요. print 출력은 결과가 아닙니다.")
    result = scope["result"]
    if type(result) is str:
        text, kind = result, "String"
    elif type(result) is bool:
        text, kind = ("true" if result else "false"), "Boolean"
    elif type(result) is int:
        text, kind = str(result), "Number"
    elif type(result) is float and math.isfinite(result):
        text, kind = json.dumps(result, allow_nan=False), "Number"
    else:
        raise TypeError("result는 문자열, 정수, 유한한 실수 또는 bool이어야 합니다.")
    if len(text.encode("utf-8")) > 1048576:
        raise ValueError("결과가 UTF-8 1 MiB를 초과했습니다.")
    response.update(success=True, resultText=text, resultKind=kind)
except BaseException as error:
    line = getattr(error, "lineno", 0) or 0
    column = getattr(error, "offset", 0) or 0
    for frame in traceback.extract_tb(error.__traceback__):
        if frame.filename == "user_script.py":
            line = frame.lineno
    response.update(errorKind=type(error).__name__, message=str(error)[:4096], line=line, column=column)
Path("result.json").write_text(json.dumps(response, ensure_ascii=False), encoding="utf-8")
