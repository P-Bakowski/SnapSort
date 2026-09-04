# PhotoAnalysis.Worker

Lokalny worker JSON Lines dla SnapSort.

```powershell
python .\python\PhotoAnalysis.Worker\main.py --self-check
```

Request:

```json
{"action":"analyze_photo","path":"D:\\Zdjecia\\IMG_0001.JPG","features":{"embedding":true,"blur":true,"orientation":true}}
```

Response zawiera SHA-256, perceptual hash, ostrosc, quality score, rozmiar, date z EXIF,
embedding `mobilenetv3_small_100` oraz wynik klasyfikacji orientacji.

Orientacja zawartosci jest klasyfikowana po zastosowaniu EXIF przez lokalny model
`LH-Tech-AI/GyroScope` (ResNet-18, 224x224, 4 klasy, Apache-2.0, okolo 44,8 MB jako ONNX).
SnapSort oznacza zdjecie jako bokiem tylko przy confidence >= 0,90 i przewadze nad druga
klasa >= 0,60. Zrodlo: https://huggingface.co/LH-Tech-AI/GyroScope
