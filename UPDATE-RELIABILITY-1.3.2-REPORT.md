# ModernTubeDownloader 1.3.2 — niezawodność aktualizacji i odzyskiwanie

Stan na 26 listopada 2026. Zmiany należą do istniejącego hotfixa z bazy v1.3.1. Prace ograniczono do transakcji aktualizacji, jej wyniku i diagnostyki tej operacji. Integracja aktualizatora używa kodu PR #27 jako punktu wyjścia. Końcowy kandydat z 1661cd5acb25e669f9f9fc777a8d2ec8aa18bdf2 zachowuje przypięty ModernFormsNext 7bf58d06d795d7298c86d9bf9471bbfb46852bab. [PR #30](https://github.com/ProGraMajster/ModernTubeDownloader/pull/30) · [Historia diagnostyki](ADVANCED-DIAGNOSTICS-1.3.2-REPORT.md)

## Co ustalono w #23

Oba nieudane przebiegi 1.3.1 → 1.3.2 z poprzedniej kwalifikacji zostawiły wynik success=false. W obu zapisano UnauthorizedAccessException ze stosem System.IO.FileSystem.MoveFile → UpdateTransaction.SaveJournal. Diagnostyka 1.3.1 nie zbierała właściciela uchwytu ani surowego kodu Win32. Po pierwszej awarii plik EXE nadal miał wersję 1.3.1, a dziennik miał Completed=true. Pole Completed w starym kontrakcie oznaczało również zakończony rollback. [Issue #23](https://github.com/ProGraMajster/ModernTubeDownloader/issues/23)

Na C: i D: wykonałem ten sam kontrolowany test NTFS z File.Move(temporary, target, overwrite: true). Po odmowie powtórzyłem operację bezpośrednio przez MoveFileExW i odczytałem GetLastError. Próby używały wyłącznie nowych plików w izolowanych katalogach, przy zwykłych uprawnieniach.

| Warunek | C: | D: |
| --- | --- | --- |
| Brak obciążenia | Sukces | Sukces |
| Czytelnik otworzył docelowy plik z FileShare.Read | 5 / 0x80070005 | 5 / 0x80070005 |
| Czytelnik otworzył docelowy plik z FileShare.Read i FileShare.Delete | 5 / 0x80070005 | 5 / 0x80070005 |
| Czytelnik pliku tymczasowego z FileShare.Read | 32 / 0x80070020 | 32 / 0x80070020 |
| Czytelnik pliku tymczasowego z FileShare.Read i FileShare.Delete | Sukces | Sukces |
| Docelowy plik tylko do odczytu | 5 / 0x80070005 | 5 / 0x80070005 |
| Kontrolowana reguła odmowy Delete na pliku w dziedziczącym katalogu | Sukces | 5 / 0x80070005 |
| Odmowa Delete na pliku i DeleteChild w jego katalogu | 5 / 0x80070005 | 5 / 0x80070005 |

Wyniki wspierają hipotezę chwilowego otwarcia docelowego pliku. Reguła tylko na pliku może zachować się różnie zależnie od praw katalogu nadrzędnego, więc odczyt atrybutów i porównanie samych ACL pliku nie wykluczają odmowy. Błąd 5 samodzielnie nie identyfikuje procesu, reguły ACL ani programu zabezpieczającego. Na podstawie starych rezultatów nie da się odtworzyć uchwytu, pochodzenia równoległego odczytu ani rozpoznać udziału indeksowania lub oprogramowania antywirusowego. Dokumentacja Windows odróżnia uprawnienie DELETE od zasad zastępowania pliku z otwartymi uchwytami. [Win32 MoveFileExW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw) · [Reguły FILE_RENAME_INFORMATION](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/content/ntifs/ns-ntifs-_file_rename_information)

W kodzie 1.3.1 strumień pliku tymczasowego kończy zapis oraz Flush(true) i jest zamykany przed File.Move. Odczyt poprzedniego dziennika też zamyka własny uchwyt przed instalacją. Ten mechanizm nie wyklucza drugiego procesu lub czytelnika zewnętrznego między kolejnymi zapisami. Kod wykonywał podmianę po każdej pozycji oraz przy zatwierdzeniu. W starych przebiegach nie zachowano śladu ETW ani aktualnej listy uchwytów z momentu wyjątku. Usługi Defendera i Windows Search działały w laboratorium. WPR był dostępny, ale nie nagrywał poprzednich incydentów. Próby nie wyłączały ochrony ani indeksowania. Zmiany ACL ograniczono do plików i katalogów utworzonych dla próby, a pierwotne reguły przywrócono w jej trakcie.

## Integracja PR #27

- Zachowano oddzielenie GUI i zewnętrznego EXE. Instalator nadal czeka na proces nadrzędny o zapisanym PID i czasie startu, weryfikuje SHA-256 i sprawdza marker zgody przy aktualizacji startowej. Ograniczenia ZIP-a, ścieżek aplikacji, punktów ponownej analizy i danych profilu nadal obowiązują.
- Właściciela instalacji rozróżnia muteks wyprowadzony ze znormalizowanej ścieżki. Po jej przejęciu instalator weryfikuje i odzyskuje niedokończoną transakcję nowego formatu. Błędny lub nieweryfikowalny dziennik zostaje na dysku. Zakończony dziennik jest przenoszony przed sprzątaniem kopii, aby przerwane sprzątanie nie naruszyło aktywnej transakcji.
- Plan i kopie powstają przed zmianą docelowych plików. SHA-256 pochodzi z oryginalnego strumienia, a kopia jest sprawdzana osobno. Przed każdą zmianą tworzony i opróżniany jest znacznik zapisu. Istniejący plik jest weryfikowany i zapisywany tym samym wyłącznym uchwytem. Nowy plik jest publikowany po sprawdzeniu skrótu, bez nadpisania istniejącej pozycji. Dziennik jest publikowany przed pierwszą zmianą i podmieniany dopiero przy zatwierdzeniu lub po rollbacku.
- Rollback weryfikuje oryginalny skrót, zanim użyje kopii. Plik dodany jest usuwany, jeśli jego skrót zgadza się z planem. Nieautoryzowana zmiana wpisu bez znacznika kończy się błędem weryfikacji. Test zmodyfikowanej kopii potwierdził zachowanie jej do analizy i odmowę zgłoszenia pełnego przywrócenia. Nieukończony dziennik starego formatu bez niezależnego dowodu zachowuje się do ręcznej naprawy wraz z plikami. Zakończone starsze dzienniki pozostają zgodne.
- Podmiana dziennika lub wyniku ma trzy dodatkowe próby po 40, 120 i 300 ms wyłącznie dla kodów 32 i 33. Kod 5 jest zgłaszany bez ponawiania. Test współdzielenia źródła potwierdza zarówno udaną próbę po zwolnieniu blokady, jak i zatrzymanie po wyczerpaniu limitu. W tym badaniu nie wywoływano natywnego kodu 33 podczas zmiany nazwy.
- Wynik nadal zawiera kompatybilne pole success. GUI potrafi odczytać stary wynik. Nowy wynik rozróżnia sukces, przywrócenie zweryfikowane, częściowe, nieznane oraz sukces instalacji z osobnym niepotwierdzonym restartem. Instalator publikuje wynik zatwierdzenia przed uruchomieniem procesu. Błąd zapisu wyniku lub błąd transakcji blokuje automatyczny start następnej aplikacji. Błąd startu lub zakończenie procesu w pierwszej sekundzie nie zmienia zatwierdzonej instalacji w nieudany rollback. Instalator zapisuje odrębny wynik restartu i, jeśli dostępny, kod wyjścia uruchomionego procesu. Pełne uruchomienie aplikacji pozostawia osobny wpis w GUI.
- Odczyt wyniku w GUI następuje przed sprawdzaniem dostępności staged ZIP-a i po zakończeniu rozpoznanego procesu pomocniczego. Dzięki temu brak archiwum lub kolejne ręczne sprawdzanie aktualizacji nie zasłaniają komunikatu o odzyskiwaniu. Trwały wynik zachowuje się po restarcie. [PR #27](https://github.com/ProGraMajster/ModernTubeDownloader/pull/27)

Rozróżnienie etapów przyczynowych wykorzystuje istniejący JSON Lines. Pole operationId łączy żądanie w GUI, niezależny zapis instalatora i odczyt wyniku po uruchomieniu programu. Przy odmowie publikacji są dostępne operacja, HRESULT, typ pierwotnego wyjątku, liczba dodatkowych błędów oraz do 16 bezpiecznych rekordów rollbacku. Kod wyjątku wewnętrznego z kontrolowanej otoczki publikacji jest rozwiązany przed zapisem. Dowolne stosy i komunikaty wyjątków nie są utrwalane. Gdy etap i operacja są znane, nazwy metod ze stosu nie wnoszą dodatkowej wartości przy tym incydencie. Nie dodano mechanizmu logowania dowolnego tekstu ani ścieżek użytkownika.

## Migracja istniejących instalacji

| Instalacja | Dostarczenie |
| --- | --- |
| 1.3.0, działająca usługa | Automatyczna instalacja, jeśli przejście zostanie zakwalifikowane dla starego helpera. Przy odmowie zapisu użyć pełnego ZIP-a w nowym folderze. |
| 1.3.1, działająca usługa | Przejście w istniejącym katalogu nadal uruchamia helper 1.3.1 ze starą publikacją dziennika. Bezpieczna ścieżka do pierwszej weryfikacji to pełny ZIP w nowym folderze. |
| 1.3.1, błąd wiązania DLL lub niepewny rollback | Zamknąć 1.3.1 i przejść na pełny ZIP w nowym folderze. Stara aplikacja z błędem wiązania nie może sama pobrać i uruchomić poprawki. |

1. Zamknij aplikację i poczekaj na zakończenie widocznego procesu instalatora. Jeśli awaria jest nieznana lub częściowa, zachowaj stary folder i dziennik transakcji.
2. Zrób kopię całego bieżącego profilu. Domyślnie jest w %LOCALAPPDATA%\ModernTubeDownloader. Przy niestandardowej ścieżce użyj aktualnego MODERNTUBEDOWNLOADER_DATA_ROOT. Pozostaw nagrania w ich obecnych folderach.
3. Zweryfikuj SHA-256 wybranego kandydata. Wypakuj wszystkie pliki do nowego zapisywalnego katalogu. Uruchom nowy ModernTubeDownloader.exe z tym samym profilem.
4. Otwórz Ustawienia → Aktualizacje, sprawdź dostępność aktualizacji i zapis ustawienia. Historyczny komunikat o nieznanym stanie może pozostać widoczny. Użyj Diagnostyki, aby zebrać nowy przebieg. Pierwszą próbę Organizatora wykonaj bez przenoszenia.
5. Po udanej weryfikacji można zaktualizować własne skróty do nowego położenia. Integracje wskazujące stary program mogą wymagać ponownego ustawienia za pomocą istniejących opcji programu. Zachowaj kopię profilu i starego folderu do zakończenia weryfikacji.

Niezależne narzędzie naprawcze wymagałoby ponownej kwalifikacji procedury piszącej do istniejącej instalacji. Obecna naprawa korzysta z kompletnej, zweryfikowanej paczki i osobnego folderu. Publiczne ZIP-y 1.3.0 i 1.3.1 nie zmieniły się. [Issue #29](https://github.com/ProGraMajster/ModernTubeDownloader/issues/29)

## Kwalifikacja

Testy ukierunkowane przeszły 112/112 lokalnie. Obejmują prawdziwy proces pomocniczy, ochronę danych, restart uszkodzonego EXE, odczyt nieudanego rezultatu bez archiwum, odmowę zastępowania dziennika, niezmienność wyniku po restarcie, konkurencyjny instalator oraz weryfikację kopii przy odtwarzaniu. Pierwszy przebieg wydania przeszedł 1 663/1 663 w Debug, 1 663/1 663 w Release i 93/93 w JavaScript. Dalsza kontrola natywna ujawniła potrzebę dwóch zmian przed kwalifikacją końcową.

### Ustalenia z pierwszej macierzy

Odświeżenie panelu aktualizacji cofało zaznaczenie instalacji przy zamykaniu, zanim akcja odczytywała wybór. Panel zachowuje teraz wybór przed odświeżeniem. Test regresji sprawdza jego zapis i odznaczenie przez kontrolkę.

Pierwsza sztuczna paczka 1.3.3 miała bibliotekę Authentication z inną wersją niż wymagana przez GUI. Instalacja bajtów zakończyła się sukcesem, a uruchomiony proces zaraz się zakończył. Naprawiłem skład syntetycznego ZIP-a i dodałem wykrywanie natychmiastowego zakończenia uruchamianej aplikacji. Trzy testy ukierunkowane przeszły po tych dwóch poprawkach. Przypadki obejmują wybór przy zamykaniu, odmowę uruchomienia pliku wykonywalnego oraz uruchomiony proces, który natychmiast wychodzi.

Pełny przebieg na końcowym commicie przeszedł 1 665/1 665 w Debug, 1 665/1 665 w Release i 93/93 w JavaScript. Obie konfiguracje skompilowały się bez ostrzeżeń.

### Migracje z opublikowanych wersji

Próby odbyły się na Windows 11 build 26300 ze zwykłymi uprawnieniami. C: używało NVMe, a D: SATA. Oba woluminy były NTFS. Każda próba używała nowego katalogu oraz osobnego syntetycznego profilu. ZIP-y źródłowe wzięto z opublikowanych artefaktów i zweryfikowano ich oficjalne sumy. Wykorzystano normalny interfejs i pakiet wstępnie zapisany do profilu z syntetycznymi metadanymi wydania. Tą ścieżką sprawdzono UI, weryfikację pakietu i działanie starego helpera bez użycia publicznego feedu dla nieopublikowanej wersji.

| Przejście | C: do wyniku instalatora | D: do wyniku instalatora | Sukcesy |
| --- | ---: | ---: | ---: |
| 1.2.1 → 1.3.2 | 11015 ms | 46830 ms | 2/2 |
| 1.3.0 → 1.3.2 | 8936 ms | 46580 ms | 2/2 |
| 1.3.1 → 1.3.2 | 11401 ms | 45943 ms | 2/2 |

W sześciu przebiegach na pierwszej paczce aktualizatora z 40b249a aplikacja uruchomiła się ponownie automatycznie, wszystkie 815 zainstalowanych wpisów miały skróty jej ZIP-a, a dwa pliki kontrolne profilu zachowały skróty. Czasy obejmują oczekiwanie od uruchomienia starego GUI do odczytu wyniku. Pełne porównanie plików i dalsza obserwacja nowego GUI odbyły się później.

Przy naprawie #29 odtworzono niedziałający aktualizator oficjalnej 1.3.1 z kontrolowaną podmianą DLL w starym katalogu. Następnie uruchomiono 1.3.2 z osobnego katalogu na tym samym profilu. Sprawdzenie dostępności i zapis ustawienia działały przed restartem i po restarcie. Zweryfikowano wszystkie 815 wpisów nowego pakietu. Skróty starego pliku wykonywalnego oraz plików kontrolnych profilu były zgodne.

### Awarie i odzyskiwanie w pierwszej macierzy

- Przeszło sześć prób programów pomocniczych. Obejmowały NativeHost bez argumentów, oczekiwanie na rodzica, blokadę DLL, błędną sumę, aktualizację startową bez zgody i wariant z prawidłową zgodą.
- Oddzielnie utrzymano uchwyt do docelowego dziennika w drugim procesie. Instalator zapisał dwa odrębne błędy, pierwotne `replace-journal` z `0x80070005` oraz błąd publikacji zakończenia rollbacku. Bajty oryginalnych plików przywrócono, ale brak trwałego zapisu końcowego zachował wynik `unknown` i otwarty dziennik. Po zwolnieniu uchwytu ponowienie najpierw odczytało i zweryfikowało rollback, a następnie zainstalowało pakiet.
- W trakcie rzeczywistej zmiany pliku przerwano proces po zaobserwowaniu pierwszego znacznika. Niezakończony dziennik pozostał czytelny, a skrót zmienianej biblioteki różnił się od oryginału. Kolejny proces zarejestrował zakończenie odzyskiwania pod własnym identyfikatorem i zainstalował prawidłowe bajty. Plik kontrolny profilu zachował SHA-256 w obu próbach.
- W zautomatyzowanej próbie nieuruchamialny plik wykonywalny dał `success=true` oraz odrębny `restartError`. Nieprawidłowy ZIP i próby odzyskiwania nieprawidłowych danych nie powodowały automatycznego uruchomienia.

### Powtórzenie migracji na końcowym ZIP-ie

Na C: wykonano po dwie nowe instalacje każdej wersji źródłowej, za każdym razem z nowym katalogiem i profilem. Wszystkie sześć przejść zakończyło się automatycznym restartem. Każda próba potwierdziła skróty 815 wpisów końcowego ZIP-a i niezmienione dwa pliki kontrolne profilu.

| Przejście | Przebieg 1 | Przebieg 2 | Sukcesy |
| --- | ---: | ---: | ---: |
| 1.2.1 → 1.3.2 | 8817 ms | 11627 ms | 2/2 |
| 1.3.0 → 1.3.2 | 9059 ms | 10803 ms | 2/2 |
| 1.3.1 → 1.3.2 | 11414 ms | 12572 ms | 2/2 |

Naprawa uszkodzonego 1.3.1 w osobnym folderze przeszła również na końcowym ZIP-ie. Ręczne sprawdzenie aktualizacji i zapis polityki działały podczas pierwszego uruchomienia i po restarcie. Wszystkie wpisy nowej dystrybucji, stary EXE i pliki kontrolne profilu zachowały oczekiwane skróty.

### Następne wydanie i powtórzenie awarii na końcowym ZIP-ie

W każdym z trzech przebiegów końcowy 1.3.2 zainstalował spójną syntetyczną 1.3.3. Zweryfikowano komplet 815 wpisów i te same dwa skróty profilu. Niezależne dzienniki starego GUI, procesu instalatora i nowego GUI zawierały ten sam identyfikator operacji.

| Sposób uruchomienia aktualizacji | Wolumin | Czas do wyniku | Uruchomienie 1.3.3 |
| --- | --- | ---: | --- |
| Przy starcie z ważnym markerem zgody | C: | 27 069 ms | Automatyczny restart |
| Przy zamknięciu po zaznaczeniu w ustawieniach | D: | 143 321 ms | Ręczny start po instalacji |
| Z przycisku instalacji | C: | 20 239 ms | Automatyczny restart |

Na D: etap wstępnego zapisania i zweryfikowania kopii trwał około 67 sekund. Porównanie czasów obejmuje różne woluminy, pakiety i tryby. Potwierdza, że około dwuminutowe oczekiwanie na tym woluminie zakończyło się instalacją.

Powtórzone próby z końcowym instalatorem znów wykazały osobny błąd publikacji dziennika przy zablokowanym celu, zachowanie bajtów oryginalnych oraz odzyskanie po przerwaniu faktycznej zmiany biblioteki. Po zwolnieniu obciążenia obie ponowione instalacje zakończyły się powodzeniem. Wszystkie sześć podstawowych prób programów pomocniczych przeszło również z końcowego ZIP-a.

### Dwie zachowane paczki lokalne

| Przeznaczenie | Katalog lokalny w worktree hotfixa | SHA-256 |
| --- | --- | --- |
| Dotychczasowa weryfikacja diagnostyki u zgłaszającego | `artifacts/advanced-candidate/release` | `834849d87c625339b6dae2cc336b24cb9b296de6fa6054176ff131a4a77780b9` |
| Końcowy kandydat z poprawkami aktualizatora | `artifacts/update-reliability-final/release` | `55c3719f0f25c92ebda5928cd62e88b02aab7f783f28897e98333453c03a8c3e` |

W obu katalogach znajduje się `ModernTubeDownloader-1.3.2-win-x64.zip` i odpowiadający mu plik `.sha256`. Nowy ZIP ma 145 049 892 bajty, 815 wpisów i własne środowisko Windows x64. Z wypakowanego EXE i DLL odczytano wersję 1.3.2, commit źródeł i pin frameworka. Pierwsza paczka aktualizatora z 40b249a jest zachowana jako lokalny materiał porównawczy. Przy kwalifikacji ponownie sprawdzono sumę dotychczasowej paczki. Syntetyczna wersja 1.3.3 służy wyłącznie do prób kontrolowanych.

## Ryzyko publikacji i pozostałe ograniczenia

Nowy helper zmniejsza liczbę podmian aktywnego dziennika i pozwala odróżnić potwierdzony rollback od nieznanej lub częściowej instalacji. Przejście ze starszego wydania zaczyna się w starym helperze. Sześć udanych prób końcowego ZIP-a oraz dodatkowe pomiary C: i D: nie usuwają ryzyka sporadycznej odmowy. Wcześniej w kwalifikacji dwukrotnie wystąpiła w działającym helperze 1.3.1. Dla zgłaszającego i dla uszkodzonego 1.3.1 pozostała droga z pełnego ZIP-a w nowym folderze. Decyzja o publicznym wydaniu pozostaje wstrzymana do weryfikacji terenowej i akceptacji ryzyka migracji.

- Dostępne środowisko miało Windows 11 build 26300. Próba na buildzie 22631 i na rzeczywistym profilu zgłaszającego pozostaje otwarta.
- Mechanizm kodu 5 odtworzono na NTFS. Źródło odmowy w dwóch historycznych awariach, w tym ewentualny proces trzymający uchwyt, pozostaje nieustalone.
- Test przerwania kończył proces. Skutki utraty zasilania i innych systemów plików pozostają poza zakwalifikowaną macierzą.
- Nowe instalatory współdzielą blokadę przy tej samej znormalizowanej ścieżce. Starsze nie używają tej blokady. Dowolne aliasy wskazujące ten sam katalog i równoległa praca starszego helpera nie były kwalifikowane.
- Sprawdzenie restartu obejmuje błąd utworzenia procesu oraz jego trwanie przez pierwszą sekundę. Pełny start potwierdza log nowego GUI. Późniejsze awarie wymagają odczytu diagnostyki aplikacji.
- Nieukończony stary dziennik bez zapisanych niezależnie skrótów oryginałów wymaga zachowania danych i użycia osobnego folderu. Instalator nie przepisuje zweryfikowanego nowego formatu na starszy.
- Brak publikacji, tagu i scalenia. PR #30 pozostaje szkicem.
