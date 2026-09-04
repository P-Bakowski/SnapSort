import hashlib
import io
import json
import os
import sys
import time
import warnings
from datetime import datetime
from pathlib import Path

MODEL_NAME = "mobilenetv3_small_100"
ORIENTATION_MODEL_NAME = "LH-Tech-AI/GyroScope"
_model = None
_orientation_session = None
_last_ai_error = None
os.environ.setdefault("HF_HUB_DISABLE_SYMLINKS_WARNING", "1")
warnings.filterwarnings("ignore", category=DeprecationWarning)
if hasattr(sys.stdin, "reconfigure"):
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")


def base_dir():
    return Path(getattr(sys, "_MEIPASS", Path(__file__).parent))


def get_model():
    global _model
    if _model is None:
        import timm
        import timm.models.mobilenetv3
        from safetensors.torch import load_file

        weights = base_dir() / "models" / "model.safetensors"
        model = timm.create_model(MODEL_NAME, pretrained=False, num_classes=0)
        if weights.exists():
            state = load_file(str(weights))
            model.load_state_dict(state, strict=False)
        else:
            model = timm.create_model(MODEL_NAME, pretrained=True, num_classes=0)
        model.eval()
        _model = model
    return _model


def get_orientation_session():
    global _orientation_session
    if _orientation_session is None:
        import onnxruntime as ort

        options = ort.SessionOptions()
        options.intra_op_num_threads = max(1, min(4, os.cpu_count() or 1))
        _orientation_session = ort.InferenceSession(
            str(base_dir() / "models" / "orientation.onnx"),
            sess_options=options,
            providers=["CPUExecutionProvider"],
        )
    return _orientation_session


def array_for(image):
    import numpy as np

    image = image.convert("RGB").resize((224, 224))
    arr = np.asarray(image).astype("float32") / 255.0
    arr = (arr - np.array([0.485, 0.456, 0.406], dtype="float32")) / np.array([0.229, 0.224, 0.225], dtype="float32")
    return arr.transpose(2, 0, 1)[None, ...]


def orientation_array_for(image):
    import numpy as np
    from PIL import Image

    image = image.convert("RGB")
    scale = 256 / min(image.size)
    resized = image.resize(tuple(round(side * scale) for side in image.size), Image.Resampling.BILINEAR)
    left = (resized.width - 224) // 2
    top = (resized.height - 224) // 2
    arr = np.asarray(resized.crop((left, top, left + 224, top + 224))).astype("float32") / 255.0
    arr = (arr - np.array([0.485, 0.456, 0.406], dtype="float32")) / np.array([0.229, 0.224, 0.225], dtype="float32")
    return arr.transpose(2, 0, 1)[None, ...]


def tensor_for(image):
    import torch

    return torch.from_numpy(array_for(image))


def embedding_for(image):
    global _last_ai_error
    try:
        import torch

        tensor = tensor_for(image)
        with torch.no_grad():
            vector = get_model()(tensor)[0]
            vector = vector / vector.norm().clamp(min=1e-12)
        return [round(float(x), 6) for x in vector.tolist()]
    except Exception as exc:
        _last_ai_error = str(exc)
        return None


def orientation_for(image):
    try:
        import numpy as np

        logits = get_orientation_session().run(None, {"pixel_values": orientation_array_for(image)})[0][0]
        probabilities = np.exp(logits - logits.max())
        probabilities /= probabilities.sum()
        order = np.argsort(probabilities)[::-1]
        best, second = int(order[0]), int(order[1])
        correction = {0: 0, 1: 90, 2: 180, 3: 270}[best]
        return correction, round(float(probabilities[best]), 6), round(float(probabilities[second]), 6), [round(float(value), 6) for value in probabilities]
    except Exception as exc:
        global _last_ai_error
        _last_ai_error = str(exc)
        return 0, 0.0, 0.0, []


def analyze_image(path, use_ai=True, features=None):
    started = time.perf_counter()
    features = features or {"embedding": True, "blur": True, "orientation": True}
    data = Path(path).read_bytes()
    sha = hashlib.sha256(data).hexdigest().upper()
    width = height = 0
    phash = sha[:16]
    sharpness = 0.0
    date_taken = None
    orientation = 1
    suggested_rotation = 0
    orientation_confidence = 0.0
    second_best_orientation_confidence = 0.0
    orientation_probabilities = []
    embedding = None

    try:
        from PIL import Image, ImageFilter, ImageOps, ImageStat

        with Image.open(io.BytesIO(data)) as source:
            exif = source.getexif()
            orientation = int(exif.get(274) or 1)
            img = ImageOps.exif_transpose(source)
            img.load()
            width, height = img.size
            raw_date = exif.get(36867) or exif.get(306)
            if raw_date:
                try:
                    date_taken = datetime.strptime(str(raw_date), "%Y:%m:%d %H:%M:%S").isoformat()
                except ValueError:
                    date_taken = str(raw_date)

            gray = img.convert("L").resize((9, 8))
            pixels = list(gray.getdata())
            bits = []
            for y in range(8):
                row = pixels[y * 9 : y * 9 + 9]
                bits.extend(1 if row[x] > row[x + 1] else 0 for x in range(8))
            phash = f"{int(''.join(map(str, bits)), 2):016X}"

            if features.get("blur", True):
                edges = img.convert("L").resize((256, 256)).filter(ImageFilter.FIND_EDGES)
                variance = ImageStat.Stat(edges).var[0]
                sharpness = min(1.0, variance / 1600.0)
            if use_ai and features.get("embedding", True):
                embedding = embedding_for(img)
            if use_ai and features.get("orientation", True):
                suggested_rotation, orientation_confidence, second_best_orientation_confidence, orientation_probabilities = orientation_for(img)
    except Exception:
        pass

    quality = max(1, min(100, round(35 + sharpness * 65)))
    modified = datetime.fromtimestamp(Path(path).stat().st_mtime).isoformat()
    return {
        "success": True,
        "sha256": sha,
        "perceptualHash": phash,
        "sharpness": sharpness,
        "qualityScore": quality,
        "width": width,
        "height": height,
        "dateTaken": date_taken or modified,
        "embedding": embedding,
        "orientation": orientation,
        "orientationConfidence": orientation_confidence,
        "secondBestOrientationConfidence": second_best_orientation_confidence,
        "orientationProbabilities": orientation_probabilities,
        "suggestedRotation": suggested_rotation,
        "model": MODEL_NAME if embedding else "phash-fallback",
        "orientationModel": ORIENTATION_MODEL_NAME,
        "elapsedMs": round((time.perf_counter() - started) * 1000, 1),
        "aiError": _last_ai_error,
    }


def handle(request):
    if request.get("action") not in {"analyze_image", "analyze_photo"}:
        return {"success": False, "error": "unknown action"}
    return analyze_image(request["path"], request.get("useAi", True), request.get("features"))


def self_check():
    from tempfile import NamedTemporaryFile
    from PIL import Image

    assert handle({"action": "nope"})["success"] is False
    assert len(hashlib.sha256(b"x").hexdigest()) == 64
    with NamedTemporaryFile(suffix=".jpg", delete=False) as temp:
        temp_path = temp.name
    try:
        Image.new("RGB", (16, 16), (120, 30, 40)).save(temp_path)
        assert analyze_image(temp_path, use_ai=False)["success"] is True
    finally:
        Path(temp_path).unlink(missing_ok=True)


def main():
    if "--self-check" in sys.argv:
        self_check()
        return

    for line in sys.stdin:
        try:
            print(json.dumps(handle(json.loads(line.lstrip("\ufeff")))), flush=True)
        except Exception as exc:
            print(json.dumps({"success": False, "error": str(exc)}), flush=True)


if __name__ == "__main__":
    main()
