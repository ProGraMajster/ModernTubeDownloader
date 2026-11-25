# ModernTubeDownloader 1.3.2 — analiza incydentu produkcyjnego

Stan dochodzenia: 25 listopada 2026. Gałąź `work/production-hotfix-1.3.2` powstała z publicznego `v1.3.1` (`f5990176cd8955a5d11671d534b1fb52054d92b6`). Kod do kwalifikacji to `d613b46`. Pin ModernFormsNext pozostaje `7bf58d06d795d7298c86d9bf9471bbfb46852bab`.

## Chronologia i klasyfikacja

Przeanalizowano 88 968 linii siedmiu plików, w tym kontynuacje stosów i zdarzenia z kolejnych dni zawarte w jednym pliku. Liczby poniżej dotyczą linii lub wskazanych zdarzeń, bez utożsamiania powtórzeń stderr, stosu i logu zadania z osobnymi awariami. Oryginalne pliki pozostały lokalnie.

| Plik od | Linie | Zakres ustaleń |
| --- | ---: | --- |
| 19.10 | 26 | Jedna awaria odczytu stanu powiadomień WinRT, `0x80070490`. Sześć procesów narzędziowych zakończonych kodem 0. Próby wersji FFmpeg i FFprobe trwały do 6,03 s i 10,49 s. |
| 20.10 | 26 | Ten sam błąd WinRT. Sześć procesów narzędziowych zakończonych kodem 0. Najdłuższe próby FFmpeg/FFprobe trwały 5,32 s i 5,08 s. |
| 21.10–24.10 | 72 260 | Udokumentowany start 1.2.1 na 22631. Po początkowych błędach powiadomień działał wybór Shell. 1 281 startów pobierania, 1 209 ukończeń i 72 końcowe błędy: 70 `HttpForbidden`, jeden `Network`, jeden `Disk`. Jest też osiem błędów osobnego sprawdzania metadanych URL i komunikaty o niedostępności materiału. Wiele wystąpień słowa `timeout` to część argumentów procesu. |
| 06.11–09.11 | 15 646 | Siedem startów 1.2.1 z wyborem Shell. 851 startów pobierania, 830 ukończeń, 20 `Unknown` i jeden `Timeout`. Log zawiera ukończone zapisy na osobnym woluminie D:. Przy 3 450 zadaniach projekcja utworzyła 40 kart w 397,5 ms. Najwolniejsza zmierzona projekcja miała 833 zadań i trwała 746,2 ms. |
| 16.11 | 711 | Dwa starty 1.3.0-candidate.1, osiem ostrzeżeń odzyskiwania Batch. Pierwszy start jest podniesiony i natywne backendy powiadomień pomija polityka. Drugi używa Shell po próbach backendów. Projekcja 830 zadań i 40 kart trwała 526 ms. Jedno pobieranie przerwało się z `Unknown` po 1 240 s. |
| 23.11 | 132 | Starty 1.2.1 i 1.3.0, dwie próby przekazania sterowania instalatorowi. Trzykrotne odrzucenie pola `interfaceMode` w starszym czytniku. Cztery ostrzeżenia Batch przy starcie 1.3.0. |
| 24.11 | 167 | 20:26 start 1.3.0. 20:29 pierwsze uruchomienie 1.3.1 po instalacji, potem awaria monitora i sześć błędów akcji ustawień. 20:32 ponowny start 1.3.1 i ponowna awaria monitora. Cztery błędy Batch na każde uruchomienie. 20:42:26 błąd sugestii celu, 20:42:34 błąd źródła analizy. |

### Fragmenty zanonimizowane

```text
20:29 [ERR] The application update monitor failed.
System.IO.FileNotFoundException: Could not load file or assembly
  'ModernTubeDownloader.Updater, Version=1.3.1.0'
  at ApplicationUpdateService.InitializeCoreAsync(...)
  at ApplicationUpdateService.InitializeAsync(...)
  at ApplicationUpdateService.MonitorAsync(...)

20:42:26 [WRN] Organizer SuggestDestination failed.
  Context=Organizer.Error.OutputFolder Reason=Organizer.Error.DriveUnavailable
  Type=IOException HResult=0x80131620 Origin=OrganizerPathSafety.DirectoryPath
20:42:34 [WRN] Organizer BuildPlanAsync failed.
  Context=Organizer.Source Reason=Organizer.Error.DriveUnavailable
  Type=IOException HResult=0x80131620 Origin=OrganizerPathSafety.DirectoryPath

[ERR] Batch session recovery requires attention; original data was retained.
System.IO.InvalidDataException: Batch.Error.Session
  at BatchSessionStore.UnfinishedSessionsAsync(...)
```

## P0 — aktualizacje

### Pakiety i ładowanie aplikacji

Pobrano opublikowane ZIP-y Windows x64 i porównano ich SHA-256 z plikami sum kontrolnych oraz metadanymi zasobów GitHub.

| Wersja | Wpisy | SHA-256 | `.NETCore.App` |
| --- | ---: | --- | --- |
| [1.2.1](https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.2.1) | 810 | `705f48799797ce3d15ad0eba9eff20587971daabe4b79b15299bd17d3cd62cbb` | 10.0.13 |
| [1.3.0](https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.3.0) | 816 | `bf323df6514b64539fc295cf5ed4e6bfc814c82bb367b9e327ee80e6e075862a` | 10.0.15 |
| [1.3.1](https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.3.1) | 816 | `8901835fb0c8168ed72dba92b8f248f7a86e2d088bb5581d859a2eb9ff2e1a00` | 10.0.15 |

W każdej paczce główna aplikacja i obie kopie `ModernTubeDownloader.Updater.dll` mają tożsamość odpowiadającą wydaniu. Kopia w katalogu głównym i kopia w `updater/` są identyczne bajtowo w ramach danej wersji. Manifest RID GUI deklaruje ładowaną w procesie bibliotekę aktualizatora przez `ProjectReference`. Manifest i konfiguracja runtime w `updater/` dotyczą osobnego procesu korzystającego z dołączonego .NET. W wydaniu GUI nie ma niestandardowego `AssemblyLoadContext` kierującego żądania do podkatalogu. Samo istnienie drugiej kopii w `updater/` nie spełnia zależności GUI.

Wadliwe sprzężenie jest w projekcie GUI i w `ApplicationUpdateService.InitializeCoreAsync`. Kod odwołuje się do typów z EXE instalatora podczas walidacji stanu, archiwum i wyniku poprzedniej instalacji. Błąd wiązania może pojawić się przy kompilacji metody asynchronicznej, zanim wewnętrzny `try/catch` zacznie wykonywać kod. Wtedy inicjalizacja pozostaje nieukończona, monitor przestaje działać, a przyciski sprawdzania i zapisu zasad ponownie trafiają w tę samą zależność.

Na izolowanej instalacji opublikowany instalator 1.3.0 zainstalował oficjalną paczkę 1.3.1 i sam uruchomił nowy GUI. Monitor ustawił `LastCheck`; natywny ekran Ustawień wykonał kolejne ręczne sprawdzenie, pokazał „Zainstalowana wersja jest aktualna” i zapisał wyłączenie automatycznych sprawdzeń. Czysta aktualizacja nie odtwarza błędu z produkcji. Starszy raport 1.3.1 używał `Restart=false`, osobnego uruchomienia i poprawnego zestawu plików. Nie sprawdzał odporności funkcji aktualizacji na uszkodzenie wiązania biblioteki, w szczególności po restarcie z zapisanym wynikiem instalacji. Sumy 816 nowo zainstalowanych plików potwierdzały zawartość kontrolowanej kopii.

### Instalacja i zmiany

Aplikacja kopiuje samodzielny instalator do własnego katalogu `Updates/runner-…` w profilu. Instalator sprawdza zgodność procesu nadrzędnego, po jego zamknięciu weryfikuje SHA-256, wypakowuje ZIP do izolowanego katalogu i zapisuje dziennik przed zastąpieniem każdego pliku. Restart wskazuje pełną ścieżkę EXE i ustawia katalog instalacji jako katalog roboczy. Instalator działa poza nadpisywanym drzewem. Transakcja nadpisuje wpisy z nowego ZIP-a i nie sprząta starych plików. Może zatem pozostać główna kopia starej biblioteki po aktualizacji do 1.3.2. Nowy manifest GUI jej nie używa, a czysta paczka jej nie zawiera.

Kompilator dołącza wspólne typy żądania, potwierdzenia i walidacji pakietu bezpośrednio do GUI i do zewnętrznego instalatora. Zachowane są format żądania, sumy, reguły ścieżek i wymaganie zamknięcia właściwego procesu. W logu aplikacji są wersje i stan plików oraz początek i wynik inicjalizacji. Instalator zapisuje etapy od odczytu żądania do uruchomienia nowego procesu, rozróżniając błąd restartu od zatwierdzonej instalacji. Test pakietu sprawdza oba manifesty, samodzielne runtime i brak zależności GUI od DLL instalatora.

Dostęp zwykłego użytkownika do katalogu z ZIP-em i do profilu jest wymagany do aktualizacji. Zewnętrzna blokada lub kwarantanna pliku to hipotezy. Log produkcyjny nie zawiera nazw plików sprawdzonych na dysku, wersji metadanych, śladu hosta, wpisu antywirusa ani błędu zapisu dziennika. Issue #23 dotyczy odrębnego `UnauthorizedAccessException` przy `File.Move` dziennika transakcji i ma własny PR #27. Brak podstaw do połączenia obu zdarzeń w jedną przyczynę. [Issue #23](https://github.com/ProGraMajster/ModernTubeDownloader/issues/23) · [PR #27](https://github.com/ProGraMajster/ModernTubeDownloader/pull/27)

## P0 — Organizator plików

Użytkownik potwierdził, że D: był początkowo niedostępny albo niezainicjalizowany, a następnie przywrócono go bez usunięcia objawu. Pierwsze `DriveUnavailable` jest zgodne ze stanem systemu. W 1.3.1 ten zaraportowany stos i zarządzany HRESULT wskazują gałąź `DriveType.Unknown/NoRootDirectory`. Plik kończy się przed kolejnym kliknięciem po przywróceniu D:, więc nie rozstrzyga przebiegu tej próby.

### Reprodukcja na opublikowanej 1.3.1

Test użył polskiego interfejsu Release, osobnego profilu Windows o zwykłych uprawnieniach i `SUBST` dla wolnej litery Q: wskazującej testowy katalog na fizycznym D:. Nie odłączano żadnego rzeczywistego dysku. Z absent Q: import TXT zachował listę, ale pozostawił pusty cel. Próba analizy zwróciła `DriveUnavailable` dla źródła. Po przywróceniu Q: w tym samym oknie źródło przechodziło walidację, lecz pusty cel zwracał `PathType`, ponieważ import zachowuje pusty cel, a `BuildPlanCoreAsync` w 1.3.1 nie ponawia sugestii. Ręczne wpisanie celu w tym samym oknie dało 10 wierszy. Kolejne odłączenie z istniejącym planem pozostawiało 10 poprzednich wierszy na ekranie mimo zakończenia nowej operacji błędem. Drugie przywrócenie, ponowne otwarcie okna oraz ponowne uruchomienie aplikacji przy prawidłowym celu umożliwiły analizę.

Przy 3 500 linkach na D: oficjalna 1.3.1 zbudowała 40 widocznych wierszy i uaktywniła przycisk Analizuj, ale UI przez 90 s pozostawało na „Skanowanie folderu źródłowego · 7000 / — · 0 B / 0 B”. To osobna usterka prezentacji sukcesu. Końcowe zdarzenie postępu było odraczane przez `RunOnUIThread`. `finally` mogło wcześniej wyczyścić `operation`, a delegat pomijał wówczas końcowy status. W odtworzonych przypadkach analiza działała lub kończyła się błędem, a aplikacja przyjmowała następne operacje. Nie znaleziono zakleszczenia ModernFormsNext ani podstaw do zgłoszenia frameworka dla tego przebiegu.

### Stan i poprawki

- Kod wykonuje `Path.GetFullPath` i `Path.GetPathRoot` po wymaganej składni lokalnej bez zmiany pisowni Unicode na NTFS. `DriveType.Network` jest odrzucany, typ nieokreślony nadal nie jest dopuszczany. `File.GetAttributes` zachowuje natywny błąd korzenia, w tym odmowę, brak dysku i brak gotowości. `Directory.Exists` i `DriveInfo.IsReady` spłaszczają część tych błędów do `false`; nie są kryterium dopuszczenia. Katalog docelowy może nie istnieć przy dostępnym korzeniu i bezpiecznych przodkach. Zasady blokujące dowiązania, junctions, ścieżki systemowe, UNC, urządzenia i obejście zakresu pozostają aktywne. Katalog istniejący w Eksploratorze z innym tokenem logowania nie dowodzi jego dostępności w kontekście GUI.
- Walidacja nie buforuje dostępności woluminu ani rozstrzygnięcia ścieżki pomiędzy analizami. Przy braku elementu w hierarchii sprawdza korzeń, a przed zwrotem planu sprawdza ponownie powierzchnie źródła i celu. Test odłącza mapowanie po skanowaniu i przywraca je w tej samej instancji usługi. Zniknięcie korzenia nie kończy się pozornym sukcesem.
- Po błędzie niedostępności automatyczna sugestia zostaje oznaczona do ponowienia. Po skutecznej analizie uzupełniany jest widoczny cel. Edycja ręczna wycofuje oznaczenie. Brakujący wpis Historii może zostać niedopasowany, ale nie wyznacza automatycznie korzenia skanowania. Ścieżki na D:, lokalnych dyskach i mapowaniu typu Fixed przechodzą tę samą walidację.
- Formularz rejestruje obsłużone kliknięcie, odrzucenie przez stan zajętości lub brak listy, wejście do usługi, etapy i końcowy wynik UI z jednym `OperationId`. Fizyczny klik w wyłączoną kontrolkę nie generuje zdarzenia aplikacji. Brak wpisu wejścia nie pozwala wtedy wnioskować o zawieszeniu. Dziennik zawiera bezpieczne liczby i kody bez nazw list, ścieżek i adresów.
- Bramkę `SemaphoreSlim` zwalnia `finally`; każde uruchomienie ma nowy token formularza połączony z czasem życia usługi. Windows może trzymać pojedyncze synchroniczne wywołanie systemu plików do jego powrotu. Niewchodzące w to wywołania UI działają niezależnie. Wyniki, stan anulowania i odblokowanie kontrolek trafiają jawnie na dispatcher. Stary podgląd jest czyszczony przed próbą. Zamykanie w trakcie operacji zleca anulowanie, po którym okno może się zamknąć.
- Weryfikacja folderów, odczyt list, skanowanie, dopasowanie, budowa i prezentacja podglądu mają osobne etapy. Liczba list i wierszy ma rzeczywisty mianownik; skanowanie i dopasowanie o nieznanym zakresie używają wskaźnika nieokreślonego. Aktualizacja UI jest ograniczona do około ośmiu razy na sekundę. Część etapów przy małej próbce mieści się między odświeżeniami.
- Analiza czyta lokalne pliki z identyfikatorami w Historii, kolejce i JSON. Skan drzewa odbywa się raz na analizę. Pamięć podręczna analizy ponownie używa już odczytanego dowodu z sidecara i odcisku pliku. Podgląd pokazuje do 40 wierszy na stronę. Istniejące środki ochrony przenoszenia, ponowna weryfikacja tożsamości, SHA-256 i dziennik odzyskiwania pozostają częścią ścieżki wykonania.

## Audyt P1 i pozostałych odchyleń

- **Batch.** 24 wpisy to sześć grup po cztery, bez identyfikatorów wystarczających do ustalenia liczby różnych sesji. `UnfinishedSessionsAsync` używa tego samego wyjątku dla braku znacznika `session.json`, obcego schematu, niezgodnego GUID lub niezdefiniowanej fazy. Kandydat i stable obsługują schemat 1. Kandydat zapisywał wiersze przed zatwierdzeniem metadanych. Stabilna wersja rozpoznaje dodatkowy znacznik importu jawnie przerwanego lub odrzuconego. Starszy katalog mógł pozostać bez obu markerów. Taki sam błąd daje uszkodzona albo obca sesja. Rozpoznanie na podstawie samego stosu nie daje podstaw do odbudowania nieznanych powiązań z kolejką ani pominięcia blokady aktualizacji przy starcie. Oddzielne issue obejmuje diagnostykę, deduplikację i odzyskiwanie na podstawie dowodów. Organizator korzystający z bezpośredniego importu TXT nie wchodzi w ścieżkę odzyskiwania Batch. [Issue #28](https://github.com/ProGraMajster/ModernTubeDownloader/issues/28)
- **AppearanceProfile.** Starszy czytnik 1.2.1 ma `UnmappedMemberHandling.Disallow` i nie ma właściwości `InterfaceMode`. Profil nowszego kandydata lub stabilnego 1.3.x zawiera ją, więc zakres zgodności jest jednostronny. 1.3.2 akceptuje schematy 1 i 2 oraz poprawny `interfaceMode`. Log 23.11 pokazuje fallback po odczycie starszą wersją i brak tego wyjątku po udokumentowanym uruchomieniu 1.3.0. Zmiana serializatora nie jest potrzebna w tym hotfixie.
- **Powiadomienia.** `ToastNotifier.Setting` może zgłosić `0x80070490` dla niesprawnej tożsamości WinRT. Framework w używanym pinie ma już próbę podczas inicjalizacji i kontrolowany fallback oraz cykl życia identyfikatorów Shell. Wpis `AppSdk … 0x80670016` potwierdza niezdatność ścieżki App SDK w tym uruchomieniu. Dokładny etap inicjalizacji na komputerze użytkownika pozostaje nieustalony. App SDK wymaga dopasowanego pakietu runtime, a wybór Shell zachowuje podstawowe powiadomienia i mniejszy zestaw możliwości. Log zgłoszenia powiadomienia nie potwierdza fizycznego wyświetlenia bannera. Powiązane defekty mają istniejące zgłoszenia i poprawkę. [MFN #269](https://github.com/ProGraMajster/ModernFormsNext/issues/269) · [MFN #270](https://github.com/ProGraMajster/ModernFormsNext/issues/270) · [MFN PR #271](https://github.com/ProGraMajster/ModernFormsNext/pull/271) · [Microsoft — TryInitialize](https://learn.microsoft.com/en-us/windows/apps/api-reference/cs-bootstrapper-apis/microsoft.windows.applicationmodel.dynamicdependency/microsoft.windows.applicationmodel.dynamicdependency.bootstrap)
- **Pobieranie.** Seria 403 i komunikaty o prywatnych/usuniętych lub niedostępnych materiałach pochodzą z zewnętrznych prób yt-dlp. Log nie dowodzi przyczyny odmów CDN. 6–9 listopada `Unknown` obejmuje jawne „empty file” i „no data blocks” z kodem 1, nawet po ok. 32 minutach. To utrata danych wejściowych lub niewystarczająca klasyfikacja błędu procesu, bez dowodu awarii GUI. Jednostkowy timeout sieciowy zakończył zadanie po 848,9 s. Ostrzeżenie MPEG-TS/AAC wystąpiło 830 razy w dużym logu listopadowym i 11 razy 16.11. Są też zakończone transfery i wywołania ffprobe. Samo ostrzeżenie nie przesądza o uszkodzeniu pliku. Jednostkowy błąd dyskowy z października jest odrębny od listopadowej walidacji woluminu. Powolne próby narzędzi zakończyły się też kodami 0; czas uruchomienia, operacji i odpowiedzi serwera nie jest rozdzielony w tych wpisach. Nie zmieniono silnika pobierania ani kolejek w hotfixie. Większe prace nad wydajnością śledzi issue #9. [Issue #9](https://github.com/ProGraMajster/ModernTubeDownloader/issues/9)

## Kwalifikacja na Windows x64

Środowisko lokalne: Windows `10.0.26300`, aplikacja x64, konto bez podniesienia uprawnień i polski interfejs. Kontrolowana biblioteka fizycznego NTFS D: była oddzielona od danych właściciela. Zanik litery odtwarzało mapowanie DOS wyłącznie do testowego katalogu. Produkcyjne 22631 pozostaje osobnym warunkiem. Sesje automatyzacji używały własnych profili pod `TEMP`.

| Regresja na commicie kodu | Wynik |
| --- | --- |
| Ukierunkowany przebieg Release przed uzupełnieniem scenariusza odzyskiwania | 188/188 |
| Pełny Debug | 1 640/1 640 |
| Pełny Release | 1 640/1 640 |
| Testy JavaScript | 93/93 |
| ZIP i manifesty | 815 wpisów. GUI bez wpisu Updater w `deps.json`. Samodzielny instalator w `updater/`. |
| Rzeczywiste pomocnicze EXE z ZIP-a | 6/6. W tym oczekiwanie na rodzica, blokada pliku, zła suma, brak i obecność potwierdzenia instalacji przy starcie. |
| Regresja ścieżek i przenoszenia | Utrata mapowania po skanowaniu i skuteczna analiza po przywróceniu na tej samej usłudze. Test przeniesienia i cofnięcia między fizycznymi woluminami C: i D: z porównaniem pięciu plików. |

Pierwszy pełny przebieg ujawnił osiem błędów kwalifikacji. Plikom językowym brakowało nowych kluczy, a siedem testów zdalnych not wydania wciąż odpowiadało na poprzedni tag. Po uzupełnieniu 16 katalogów i aktualizacji źródła testowych not oba pełne przebiegi przeszły. Wcześniejsza próba ogólnego smoke 1.3.1 zatrzymała się na niezależnym kroku Batch, który potrzebował syntetycznych narzędzi. Wyniki aktualizacji i Organizatora poniżej pochodzą z osobnych natywnych scenariuszy, które nie zależą od tego kroku.

### Natywny Organizator

Po przywróceniu mapowania w tym samym oknie 1.3.2 obliczyła i pokazała sugerowany cel, przygotowała 10 wyników i pokazała „Plan gotowy”. Powtórne odłączenie dało 0 wierszy, komunikat źródła i aktywny przycisk, a przywrócenie pozwoliło powtórzyć analizę. Osobno otworzono nowe okno i uruchomiono cały proces ponownie. Brak podfolderu źródłowego i fizyczny plik na ścieżce celu pokazały odrębne błędy. SHA-256 pliku kontrolnego w bibliotece zachowano i plan nie utworzył folderu docelowego. Przy źródle na D: i niedostępnym osobnym celu UI prawidłowo wskazało cel. Po przywróceniu jego mapowania analiza przeszła w tym samym oknie. Test natywny anulował analizę zestawu z plikiem o długości 1 GiB w 67 ms, potem ponowił ją z wynikiem po 1 081 ms. Przycisk Zamknij w trakcie analizy również zakończył operację i zamknął okno. Wspólny komunikat anulowania zawiera informację o dzienniku przenoszenia, chociaż suchy przebieg go nie tworzy. Próba z plikiem 1 GiB sprawdza dodatkowy koszt odczytu i haszowania, a fizycznie wolny nośnik pozostaje poza pomiarem.

Benchmark pochodzi z jednej sesji natywnego UIA na fizycznym D:. To 3 500 plików syntetycznych po 64 KiB i 3 500 odpowiadających plików JSON, rozłożonych w podfolderach. Saldo pamięci to szczyt obserwowanego working set minus stan przy kliknięciu. Opóźnienie próby UIA obejmuje odczyt statusu i stanu klawisza, więc nie jest pomiarem klatek ani fizycznego wejścia. Czas podglądu pochodzi z logu wokół renderowania i przypisania stanu końcowego.

| Linki | Kliknięcie → gotowy | Przyrost working set | Najwolniejszy odczyt UIA | Render i status podglądu | Widoczne wiersze |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | 460 ms | 15,51 MiB | 16 ms | 11 ms | 10 |
| 100 | 471 ms | 1,49 MiB | 19 ms | 19 ms | 40 |
| 1 000 | 608 ms | 3,44 MiB | 16 ms | 20 ms | 40 |
| 3 500 | 1 037 ms | 13,08 MiB | 13 ms | 18 ms | 40 |
| 10 000 | 1 054 ms | 15,70 MiB | 20 ms | 18 ms | 40 |

Przy 10 000 linków 6 500 pozycji nie miało lokalnego pliku. Te dane mierzą obsługę braków oraz koszt pierwszej strony UI. Nie przewidują czasu haszowania 10 000 pełnowymiarowych materiałów. Wyniki przekrojowe dotyczą syntetycznej biblioteki, a nie plików produkcyjnych.

### Natywna aktualizacja i stan profilu

1. Czysta kopia oficjalnej 1.3.1 z osadzoną główną DLL 1.3.0.0 odtworzyła log produkcyjny. DLL fizycznie istniała. Kopia w `updater/` miała nadal 1.3.1.0. Monitor, Check i zapis polityki zgłosiły dokładne żądanie wersji 1.3.1.0, stan UI wskazał błąd i zmiana polityki nie zapisała się. Ten wariant ustala mechanizm awarii. Nie ustanawia stanu pliku na komputerze użytkownika.
2. Oficjalny instalator 1.2.1 zainstalował 1.3.0 i automatycznie uruchomił GUI. Początkowy ręcznie zbudowany test zapisał w dzienniku ścieżkę bez separatora, podczas gdy GUI później przekazało `AppContext.BaseDirectory` z separatorem. Stary instalator 1.3.0 odrzucił ten równoważny zapis konkretnego testu. Znormalizowano wejście do postaci produkcyjnej w powtórzeniu. PR #27 zawiera już normalizację porównania tych ścieżek.
3. W powtórzeniu z produkcyjną składnią podczas aktualizacji 1.3.0 → 1.3.1 rzeczywisty instalator zgłosił odmowę zastąpienia dziennika z #23, bez wstrzykiwania blokady. Po zakończeniu odczytano `Completed=true`, 308 wpisów i EXE 1.3.0. Ponowienie na tej samej instalacji przeszło. Dokładna przyczyna odmowy pozostaje otwarta.
4. Z GUI 1.3.0 oraz 1.3.1 wybrano instalację wcześniej przygotowanych i zweryfikowanych ZIP-ów. Obie operacje zakończyły rzeczywisty proces nadrzędny i automatycznie uruchomiły nowe PID-y 1.3.1 i 1.3.2. Po migracji 815 wpisów było zgodnych z ZIP-em 1.3.2, a profilowy plik kontrolny zachował hash. Stara DLL przy EXE pozostała po aktualizacji, a nowa usługa zakończyła inicjalizację z `StateAvailable=True`. Dwa uruchomienia nowej wersji potwierdziły Check, polski komunikat o aktualnej wersji i trwałą zmianę ustawień w obu kierunkach.
5. W czystej 1.3.0 kliknięcia Check i Pobierz pobrały rzeczywisty oficjalny ZIP 1.3.1 i jego sumę z GitHub. GUI pokazało „Aktualizacja zweryfikowana i gotowa do instalacji”, a hash pliku w stagingu był identyczny z opublikowaną sumą. Dla 1.3.2 dostarczenie ZIP-a do stagingu było kontrolowane lokalnie. Brak publicznego następnego wydania do kwalifikacji pobrania po 1.3.2.
6. W ręcznym teście program 1.3.2 uruchomiono z nowego folderu na tym samym syntetycznym profilu, który wcześniej odtwarzał zepsuty monitor 1.3.1. Ręczne sprawdzanie i zapis opcji przeszły. Zachowano bajty starej DLL, profilu wyglądu w formacie kandydata oraz czterech niedokończonych sesji Batch. Były to odtworzone dane o zgodnym kształcie, bez publicznego binarnego kandydata do odtworzenia pełnej migracji.

### Pakiet do weryfikacji

Paczka użyta w powyższych natywnych scenariuszach to `ModernTubeDownloader-1.3.2-win-x64.zip`, 144 975 122 bajtów i SHA-256 `c43d0579b9e98c1d58e04adf84176593f8de57458b5cff302c17e249bb115e73`. Zbudowano ją w osobnym checkoutcie kodu `d613b46` podczas pełnej kwalifikacji, tym samym mechanizmem publikacji i pakowania. Pełny przebieg w drugim checkoutcie wytworzył drugi ZIP z tym samym zestawem 815 nazw i inną sumą. Porównanie treści wykazało 804 identyczne pliki i 11 różnic w generowanych bibliotekach .NET. Natywne wyniki i suma podana użytkownikowi odnoszą się do pierwszej paczki.

## Ręczne odzyskanie po awarii aktualizatora 1.3.1

1. Zamknij aplikację przez Wyjdź, także jeśli jest w zasobniku. Jeżeli trwa instalacja, zaczekaj na zakończenie instalatora. Zachowaj dotychczasowy folder aplikacji i profil. Typowy profil to `%LOCALAPPDATA%\ModernTubeDownloader`. Instalacja z niestandardowym `MODERNTUBEDOWNLOADER_DATA_ROOT` używa nadal swojego dotychczasowego katalogu.
2. Pobierz zatwierdzony ZIP Windows x64 oraz plik `.sha256` z tego samego źródła. Dla lokalnej paczki użyj sumy podanej powyżej. W PowerShell sprawdź `Get-FileHash -Algorithm SHA256 -LiteralPath '<ścieżka-do-ZIP>'`. Przy różnicy sum przerwij i pobierz poprawną paczkę.
3. Wypakuj cały ZIP do nowego, pustego folderu zapisywalnego na koncie użytkownika. Zachowaj strukturę podkatalogów. Uruchom `ModernTubeDownloader.exe` z nowego folderu przy tym samym profilu. Przenoszenie pojedynczej DLL pomiędzy wersjami odtwarza problem mieszanych plików.
4. Sprawdź numer wersji i sprawdzanie aktualizacji w Ustawieniach. Otwórz Historię, kolejkę i używane listy. Zachowaj profile, pliki pobrań i wstrzymane sesje. Po potwierdzeniu uruchomienia z nowego folderu zaktualizuj skrót aplikacji. Jeśli profil zawiera nieczytelną sesję Batch, zabezpiecz jej kopię na potrzeby #28.
5. Jeżeli usługa aktualizacji nadal zgłasza problem, zbierz zestaw diagnostyczny przed zmianą starej instalacji. Jeżeli 1.3.2 działa, pracuj z nowej kopii. Powrót do starszego pliku EXE z tym samym profilem może skutkować fallbackiem wyglądu, dlatego zachowaj kopię profilu sprzed kolejnych zmian ustawień.

## Diagnostyka na komputerze źródłowym

`scripts/Get-ProductionIncident132Evidence.ps1` działa tylko do odczytu. Podaj katalog starej instalacji oraz, opcjonalnie, wartości dwóch pól Organizatora bez publikowania samej komendy. Przykład:

```powershell
pwsh -NoProfile -File .\scripts\Get-ProductionIncident132Evidence.ps1 -Installation '<folder 1.3.1>' -Source '<źródło>' -Destination '<cel>'
```

Wynik zawiera wersje i sumy znanych plików programu, wpisy manifestu, typ woluminu i kod błędu korzenia lub przodka oraz zbiorcze wyniki dla Batch. Nazwy folderów, nazwy użytkowników, sesje, URL-e i pliki diagnostyczne użytkownika pozostają poza wynikiem. Przed udostępnieniem przejrzyj JSON. Przy ponownej próbie w 1.3.2 zanotuj czas kliknięcia, końcowy status i powiązane bezpieczne pola `OperationId`, `Stage`, `Outcome`, `Type` i `HResult`.

## GitHub i gotowość

- [#24 Organizator](https://github.com/ProGraMajster/ModernTubeDownloader/issues/24) ponownie otwarte z reprodukcją na oficjalnym 1.3.1.
- [#29 wiązanie biblioteki](https://github.com/ProGraMajster/ModernTubeDownloader/issues/29) śledzi awarię usługi aktualizacji. [#23](https://github.com/ProGraMajster/ModernTubeDownloader/issues/23) i [PR #27](https://github.com/ProGraMajster/ModernTubeDownloader/pull/27) śledzą odmowę zapisu dziennika i wiarygodność rollbacku.
- [#28 Batch](https://github.com/ProGraMajster/ModernTubeDownloader/issues/28) śledzi niejednoznaczność metadanych i odzyskiwanie.
- Przeniesienie do 1.4.0 powinno uwzględnić dwa commity implementacji bez podmiany numeru wersji, na docelowej bazie po integracji zmian Organizatora i odzyskiwania. Obejmuje zachowanie nowych lokalizacji, logowania startu procesu i sprawdzenia manifestów w obecności zmian #27. Osobne gałęzie i PR-y rozwojowe zachowały dotychczasowy stan. [PR #21](https://github.com/ProGraMajster/ModernTubeDownloader/pull/21) · [PR #26](https://github.com/ProGraMajster/ModernTubeDownloader/pull/26)

**Kandydat do lokalnej weryfikacji. Ocena publikacji: wstrzymana.** Brak natywnego przebiegu na dokładnym Windows 11 22631 i na fizycznie odłączanym nośniku. Wystąpiła też odrębna, spontaniczna odmowa zapisu dziennika na starej wersji aktualizatora. Następne uruchomienie instalacji było skuteczne, a naprawa w nowym folderze omija zależność od uszkodzonego GUI 1.3.1. Przyczyna stanu plików na komputerze zgłaszającego nadal wymaga danych z kolektora.
