# Kwalifikacja Windows x64 dla etapu 2

Lokalna paczka powstaje przez `scripts/Build-Release.ps1 -DevelopmentPackage -ArtifactsDirectory artifacts/stage2-local-package`. Skrypt przyjmuje tę opcję dla wersji `-dev.N` i nowego katalogu. Weryfikację testów przeprowadza się osobno. Do maszyny testowej przenieś gotowy ZIP, jego SHA-256 i poniższe skrypty z tego samego commitu. Wystarczy Windows PowerShell 5.1. Paczka zawiera .NET Runtime 11 dla GUI, NativeHost i Updatera. GUI zawiera też ASP.NET Core 11. Okno instalacji uruchamia ten sam EXE Updatera z tego samego katalogu.

## Maszyny i konta

| Środowisko | Przygotowanie |
| --- | --- |
| Bez współczesnego .NET | Świeża VM, zwykłe konto lokalne. Sprawdź systemowe i per-user katalogi `dotnet\shared`, także `%LOCALAPPDATA%\Microsoft\dotnet`. Windows .NET Framework 4.x może zostać. |
| Tylko .NET 10 | Migawka tej samej VM z .NET Runtime 10. |
| Tylko .NET 11 | Migawka z .NET Runtime 11. |
| Kilka wersji | Zapisz inwentarz wersji i wykonaj ten sam test. |
| Offline | Po skopiowaniu paczki odłącz kartę sieciową VM i zachowaj działanie loopback. |
| Windows 11 22631 | Użyj migawki Windows 11 23H2 i zapisz edycję oraz kompilację. |
| Minimalny system | Windows 10 Enterprise LTSC 2019, build 17763, aktualne poprawki. Sprawdź aplikację na sprzęcie albo profilu CPU spełniającym x86-64-v2 bez v3. |

Rozpakuj paczkę do zapisywalnego katalogu zwykłego użytkownika. Zapisz SHA-256 i inwentarz środowiska przed uruchomieniem. Użyj osobnych migawek lub osobnych VM. Testy tworzą syntetyczny profil w TEMP i działają bez rejestracji przeglądarki, powiadomień oraz protokołu na koncie testowym.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Stage2Runtime.ps1 `
    -PackageDirectory C:\Stage2\unpacked -EvidenceDirectory C:\Stage2\runtime-evidence
```

Skrypt wymaga widocznego pulpitu. Rejestruje lokalizację naprawdę załadowanych `coreclr.dll` i `hostpolicy.dll` dla wszystkich czterech ról. Weryfikuje Web Remote przez zapytanie na 127.0.0.1 z aktywnym uwierzytelnianiem. Updater działa na osobnej instalacji z plikami syntetycznymi. `-RefuseHttpProxy` wymusza odmowę ruchu HTTP aplikacji podczas lokalnej próby. Odłączona karta w VM daje osobny dowód pracy bez sieci.

`Test-Stage2InstallerFrames.ps1` odtwarza wszystkie stany prezentacji przez lokalny kanał testowy. Folder zrzutów opisuje je jako dane syntetyczne. `Test-Stage2StartupWindow.ps1` rejestruje stany faktycznie osiągnięte przez GUI w bieżącej sesji, z ustawieniami Light lub Dark oraz Classic lub Modern. `Test-Stage2Migration.ps1` potrzebuje archiwalnego ZIP i znanej sumy. Rozpakowuje kopię, uruchamia updater z tej wersji, przechowuje wynik restartu i sprawdza skróty plików występujących wyłącznie w źródłowym ZIP-ie.

## Wymagania CPU

.NET 11 dla Windows x64 wymaga x86-64-v2. Instrukcje obejmują między innymi SSE3, SSSE3, SSE4.1, SSE4.2, POPCNT i CMPXCHG16B. Kod ReadyToRun może używać v3, z przejściem na JIT na CPU spełniającym v2. Własne publikacje aplikacji wyłączają ReadyToRun. Przykładowa rodzina poniżej progu to Intel Core 2, w tym Penryn bez SSE4.2. [Microsoft](https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/11/minimum-hardware-requirements) · [Intel](https://www.intel.com/content/dam/develop/external/us/en/documents/d9156103-705230.pdf)

Manifest i importy PE dają informację o platformie i zależnościach bibliotek natywnych. Kwalifikacja rzeczywistego progu dla Skia, HarfBuzz i renderowania wymaga uruchomienia na CPU v2 bez v3. Do weryfikacji minimalnego systemu użyj wspieranej edycji LTSC zgodnej z macierzą .NET. [Wersje Windows wspierane przez .NET](https://learn.microsoft.com/en-us/dotnet/core/install/windows) · [Minimalna wersja systemu dla Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/get-started/versioning-overview)

Komunikat na CPU bez x86-64-v2 można wyświetlić przed uruchomieniem CoreCLR w małym programie Win32 kompilowanym dla bazowego x64. Taki program sprawdza flagi CPUID i może użyć systemowego okna komunikatu. Wymaga ustalenia punktów wejścia dla skrótów, restartu aktualizatora i NativeHost oraz testu na CPU poniżej v2. Etap 2 obejmuje ocenę tej opcji.
