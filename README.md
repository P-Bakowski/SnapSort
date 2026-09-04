# SnapSort

SnapSort to aplikacja dla Windows do wygodnego przeglądania, porządkowania i selekcjonowania zdjęć oraz filmów. Analiza i organizacja multimediów odbywa się lokalnie na komputerze.

## Funkcje

- przeglądanie zdjęć i filmów w spójnej siatce miniaturek,
- generowanie i lokalne buforowanie miniaturek,
- odtwarzanie filmów oraz pełny i szybki podgląd zdjęć,
- porównywanie zdjęć,
- wykrywanie duplikatów i podobnych ujęć,
- kolekcja zdjęć obróconych bokiem,
- zaznaczanie wielu plików,
- przenoszenie niepotrzebnych plików do lokalnego folderu `[nazwa folderu]_Kosz`,
- motyw ciemny i jasny.

## Instalacja

Pobierz `SnapSort_Setup_v1.0.1.exe` z sekcji [Releases](https://github.com/P-Bakowski/SnapSort/releases) i uruchom instalator. Szczegółowy opis znajduje się w [INSTALL.md](INSTALL.md).

## Wymagania

- Windows 10 w wersji 1809 lub nowszy,
- komputer z procesorem x64.

Wydanie jest self-contained i nie wymaga osobnej instalacji środowiska .NET ani Pythona.

## Uruchomienie

Po instalacji uruchom SnapSort ze skrótu w menu Start, opcjonalnego skrótu na pulpicie albo bezpośrednio z wybranego katalogu instalacji.

Budowanie ze źródeł wymaga .NET 8 SDK, Python 3.12 z pakietami z `requirements.txt`, PyInstaller oraz Inno Setup 6:

```powershell
./build/build-release.ps1 -BuildWorker
```

## Wersja

Aktualne wydanie: **v1.0.1**

## Autor

**Patryk Bąkowski**
