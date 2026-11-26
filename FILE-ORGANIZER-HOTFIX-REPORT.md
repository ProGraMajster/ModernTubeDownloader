# File Organizer — raport hotfixa 1.3.1

## Zgłoszenie i baza wydania

Użytkownik po aktualizacji do 1.3.0 otrzymał ogólny komunikat Organizatora mimo podania poprawnych bezwzględnych folderów lokalnych. Odtworzyliśmy ten sam komunikat w trzech scenariuszach na opublikowanej binarce. Oryginalne wartości obu pól i nazwa listy ze zgłoszenia są nadal nieznane, więc scenariusz tego konkretnego użytkownika pozostaje niepotwierdzony. [Zgłoszenie MTD #24](https://github.com/ProGraMajster/ModernTubeDownloader/issues/24)

Bazą hotfixa jest publiczny tag `v1.3.0` wskazujący `4e1eb868219162cece7903ffbd702a5ce5b55e44`. Paczki 1.2.1 i 1.3.0 zweryfikowano na podstawie opublikowanych SHA-256. W chwili rozpoczęcia `origin/master` wskazywał ten sam commit co tag, a PR #21 zawierał identyczny kod Organizatora na bazie integracji ModernFormsNext 1.12.0. Zmiany dla 1.3.1 i przeniesienie poprawki mają osobne PR-y. [Wydanie 1.3.0](https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.3.0) · [PR #25](https://github.com/ProGraMajster/ModernTubeDownloader/pull/25) · [Integracja 1.4.0, PR #26](https://github.com/ProGraMajster/ModernTubeDownloader/pull/26)

## Przyczyny i warunki reprodukcji

| Wartość w 1.3.0 | Odrzucony element i warunek | Zachowanie w 1.3.1 |
| --- | --- | --- |
| `D:\` jako źródło | `DirectoryPath` dzieli pustą część po korzeniu na jedną pustą nazwę. `ValidateName` odrzuca ją przed kontrolą korzenia. | Można analizować wyłącznie poziom główny. Przy włączonym skanowaniu podfolderów komunikat wskazuje przełącznik. |
| Istniejący folder `Cafe` + U+0301 | `DirectoryPath` zamienia nazwę na NFC. `Directory.Exists` sprawdza inną nazwę na NTFS. Gdy wariant NFC istnieje osobno, 1.3.0 może wskazać ten drugi katalog. | Sprawdzenie składni Windows, separatorów i pełnej ścieżki zachowuje zapis Unicode faktycznego folderu. |
| Import `Muzyka ..txt`, proponowany folder dla nazwy `Muzyka .` | `ListFolder` wykonuje `Trim().TrimEnd('.')`, odsłaniając końcową spację. Walidacja odrzuca nazwę, a dodanie podkreślenia na początku pozostawia spację na końcu. Błąd powstaje przed analizą i dotyczy folderu wewnętrznego. | Ciąg końcowych kropek i spacji jest usuwany wspólnie. Przy skracaniu nazwy zachowywana jest cała para UTF-16. |
| Istniejący plik o nazwie sugerowanego folderu | `DirectoryPath` używa tego samego klucza dla pliku w miejscu folderu i nieistniejącego wymaganego katalogu. | Komunikat identyfikuje sugestię i nazwę listy. Lista zostaje dostępna, a jej pole docelowe można uzupełnić ręcznie lub przez selektor. |

Pierwsze trzy scenariusze zwróciły `Organizer.Error.Path` przy wywołaniu właściwych metod w opublikowanym `ModernTubeDownloader.dll` 1.3.0. Zwykły katalog kontrolny przeszedł. Stary klucz był zgłaszany w `DirectoryPath` dla niedozwolonej postaci ścieżki i nieprawidłowego stanu katalogu, w `ValidateName` dla składni i nazw urządzeń oraz w `CheckFile` dla katalogu na ścieżce pliku. Wszystkie 16 katalogów językowych mapowało go na ogólny tekst. [Kod opublikowanej walidacji](https://github.com/ProGraMajster/ModernTubeDownloader/blob/4e1eb868219162cece7903ffbd702a5ce5b55e44/ModernTubeDownloader/Organizer/OrganizerPathSafety.cs#L8)

Starsze testy sprawdzały niedozwolone nazwy, NFC w zwykłych podfolderach i przenoszenie danych, lecz nie łączyły istniejącej nazwy NTFS w zapisie rozłożonym z obowiązkowym testem istnienia. Nie obejmowały też pustej listy segmentów korzenia, spacji ujawnionej po usunięciu końcowej kropki ani otwierania selektora po wyczyszczeniu pola.

## Przepływ danych i zmiany

1. Organizator wypełnia pole źródła katalogiem pobierania z ustawień. Formularz przekazuje bieżący tekst źródła oraz zapamiętane pola docelowe każdej listy. Wybór folderu zapisuje ścieżkę zwróconą przez selektor do tej samej kontrolki co ręczna edycja.
2. Import TXT, CSV i JSON oraz otwarcie sesji Batch używają istniejącego celu lub proponują podfolder z nazwy listy. Błąd sugestii pozostawia listę i jej edytowalne pole docelowe. Program pokazuje kontekst ustawień, sugestii lub konkretnej listy.
3. Analiza waliduje źródło i wszystkie cele przed skanowaniem. Podgląd używa ścieżek z Historii i kolejki oraz weryfikuje dowody tożsamości z JSON. Format ustawień i istniejących kolekcji pozostaje zgodny z 1.3.0. W kodzie 1.2.1 nie było Organizatora. Odczyt przez 1.3.0 zachował rozłożony zapis folderu z 1.2.1, a błąd wystąpił dopiero podczas analizy Organizatora. Osobny świeży profil 1.3.0 bez zapisanych ustawień także odrzucił rozłożoną nazwę. Jego zwykły katalog kontrolny przeszedł analizę.
4. Plan przypisuje plik i pliki towarzyszące do listy. Przy wykonaniu i odzyskiwaniu dziennika serwis ponownie sprawdza korzeń, położenie plików, kolizje, aktywność zadania oraz zweryfikowaną tożsamość.
5. UI rozróżnia nieprawidłowe źródło, cel, ustawiony folder pobierania, sugerowany folder i plik listy. W zależności od błędu wskazuje brak folderu, dostęp, dysk, typ ścieżki, składnię nazwy, przejście do innego katalogu, błąd normalizacji lub inny błąd operacji plikowej. Log lokalny zapisuje etap, klucz przyczyny, typ wyjątku i HRESULT. Nowe logi pomijają prywatne ścieżki i nazwy list. Wszystkie 16 katalogów otrzymało tłumaczenia.

### Selektor ModernFormsNext

Oddzielnie od `Organizer.Error.Path` odtworzono `ArgumentException` przy `FolderBrowserDialog.InitialDirectory = ""`. Fabrycznie pusta wartość `null` jest obsługiwana. Problem występuje przed wyświetleniem systemowego okna. Wskazano go na dokładnym frameworku spakowanym w 1.3.0 oraz w implementacji 1.12.0. Hotfix ustawia katalog początkowy tylko wtedy, gdy pole wskazuje istniejący bezwzględny katalog. Gałąź 1.3.1 została zbudowana z dotychczasowego pinu ModernFormsNext `7bf58d06d795d7298c86d9bf9471bbfb46852bab`. [ModernFormsNext #549, minimalna reprodukcja](https://github.com/ProGraMajster/ModernFormsNext/issues/549)

## Bezpieczeństwo danych

- Wymagana jest bezwzględna ścieżka lokalnego dysku. Kontrole blokują UNC, mapowane dyski sieciowe, przestrzenie nazw urządzeń, nazwy urządzeń, alternatywne strumienie danych, `.`, `..`, aliasy ze spacją lub kropką na końcu oraz katalogi systemowe, dowiązania i junctions. Atrybuty przodków są sprawdzane przed odczytem potomków. Cel przenoszenia musi być poniżej korzenia dysku.
- Bezpieczny podgląd pozostaje operacją tylko do odczytu. Istniejąca kolizja jest sygnalizowana w planie i powtórnie sprawdzana przed przeniesieniem. Pomiędzy dyskami operacja weryfikuje SHA-256 całej kopii, publikuje ją bez nadpisywania oraz usuwa źródło po potwierdzeniu kopii. Historia i kolejka zachowują tożsamości rekordów.
- Dziennik obsługuje przerwanie i jawne odzyskiwanie. Testy obejmują awarię w trakcie kopiowania, publikację przed zapisem następnego kroku, niezapisane referencje i przerwane cofanie. Weryfikacja SHA-256 obejmuje wideo, audio, JSON i miniaturki. Sprawdzanie ścieżek pozostaje kontrolą na granicach operacji. Jednoczesna podmiana folderów przez zewnętrzny proces może nastąpić między sprawdzeniem i użyciem. [Opis bezpieczeństwa Organizatora](FILE_ORGANIZER_SAFETY.md)

## Wyniki kwalifikacji

| Zakres | Wynik |
| --- | --- |
| Pełna kwalifikacja 1.3.1 w Windows | Debug **1639/1639**, Release **1639/1639**, JavaScript **93/93**. Kompilacje bez ostrzeżeń. Test archiwum obejmował 816 wpisów. |
| Walidacja Windows | Testy składni obejmują przykłady z C, D i E, separatory mieszane i końcowe, spacje, nawiasy, polskie znaki, zapis rozłożony, brakujący dysk, niedozwolone ścieżki i kolizje. Test integracyjny sprawdził katalog docelowy o długości ponad 260 znaków oraz jeszcze nieistniejące foldery. Korzeń sprawdzono na izolowanym mapowaniu. |
| Organizacja i bezpieczeństwo | Podgląd, przeniesienie i cofnięcie na mieszanych danych wideo/audio, wielu listach, metadanych, miniaturkach, brakujących pozycjach i powtarzających się nazwach. Zestaw obejmuje plany 2 750 i 10 000 pozycji, kolizje, przerwania i blokadę junction dodanego po podglądzie. Faktyczne przeniesienie między woluminami NTFS C i D oraz cofnięcie zachowały SHA-256 wszystkich pięciu wybranych plików, kolejkę i Historię. |
| UI Organizatora | Test formularza przez ModernFormsNext TestHost oraz smoke natywnego okna na syntetycznych plikach. Win32 otworzył selektor z pustego pola, anulowanie zachowało wartość, a wybrany istniejący folder wrócił z dokładnym zapisem Unicode. Natywne scenariusze pokazały podgląd, komunikat celu, przeniesienie i cofnięcie. Odrębny test pakietu Release przez Windows UIA potwierdził obie powłoki, Batch i brak oferty starszej aktualizacji. |
| Migracja 1.2.1 → 1.3.0 → 1.3.1 | Usługi oryginalnego 1.2.1 zapisały syntetyczny profil. Opublikowane aktualizatory zainstalowały kolejno dwie paczki z kontrolą SHA-256, po 816 plików zapisanych w dzienniku każdej instalacji. Hash plików profilu pozostał identyczny w obu przejściach. Odczyt 1.3.0 odtworzył błąd. Ten sam profil w 1.3.1 zaplanował, przeniósł i przywrócił wideo, JSON i miniaturkę, zachowując SHA-256 plików, identyfikator rekordu, indeks playlisty i oba odwołania kanoniczne. |
| Aktualizacja automatyczna | Oryginalna usługa 1.3.0 użyła symulowanych odpowiedzi GitHub z rzeczywistymi bajtami lokalnego ZIP-a. Zachowany opt-out wykonał zero żądań. Po włączeniu instalacji przy wyjściu usługa pobrała ZIP i SHA-256, zakończyła walidację i przygotowała aktualizację do instalacji. Zweryfikowana kopia trafiła do testu opublikowanego instalatora. Żądanie instalatora użyło ustawienia `Restart=false`. Uruchomienie pakietu 1.3.1 zweryfikował osobny test Windows UIA. |
| Narzędzia z paczki | **6/6** testów rzeczywistych plików wykonywalnych NativeHost i Updater, m.in. oczekiwanie na proces nadrzędny, odrzucenie sumy, odtwarzanie po blokadzie i dopuszczenie lub odmowa instalacji przy starcie. |
| Gałąź rozwojowa | **92/92** testów Organizatora na końcowym commicie gałęzi integracji z ModernFormsNext 1.12.0. Poprawkę wraz z testami przeniesiono do PR #26 na bazie PR #21. |

Pierwszy pełny przebieg ujawnił siedem niepowodzeń związanych z odpowiedzią symulowanego GitHub. Odpowiedź dla 1.3.1 nadal zwracała tag 1.3.0. Po poprawieniu odpowiedzi kolejne pełne przebiegi Debug i Release przeszły. Osobny początkowy test selektora z pustym polem odtworzył błąd frameworka i przeszedł po poprawce po stronie aplikacji.

Lokalne logi i TRX znajdują się pod `artifacts/hotfix/final-qualification`, `artifacts/hotfix/final-qualification.log` oraz `artifacts/hotfix/published-helpers`. Próbę migracji zachowano pod `artifacts/hotfix/migration-final`, a skrypty testowe pod `artifacts/hotfix/migration-tools`. Wszystkie dane w tej kwalifikacji były testowe.

## Ręczna weryfikacja na kopii testowej

1. Rozpakuj 1.2.1, 1.3.0 i lokalną paczkę 1.3.1 do osobnych testowych folderów. Zweryfikuj ich sumy SHA-256. Wykonaj próbę w testowym koncie Windows lub w maszynie wirtualnej. Uruchamiaj po jednej instancji z tym samym profilem wskazanym przez `MODERNTUBEDOWNLOADER_DATA_ROOT`. Utwórz go jako nowy podfolder `mtd-automation-smoke-<identyfikator>` bezpośrednio w katalogu tymczasowym. W 1.3.x ustaw też `MODERNTUBEDOWNLOADER_QA_SKIP_PROTOCOL=1`. Używaj wyłącznie plików i profilu przeznaczonych do testów.
2. W 1.2.1 zapisz folder pobierania z nazwą w zapisie rozłożonym, np. w PowerShell utwórz nazwę przez `'Pobrane Cafe' + [char]0x0301`. Zachowaj jedno nagranie, odpowiadający mu JSON z `id` i `webpage_url`, miniaturkę oraz wpis Historii. Utwórz testowy TXT z jego adresem i brakującym adresem.
3. Otwórz ten sam profil w 1.3.0. W Organizatorze ustaw poprawny folder docelowy. Analiza zapisanego źródła ma pokazać stary błąd. Osobno odtwórz generowanie folderu z `Muzyka ..txt` przy zwykłej bazie pobierania.
4. Otwórz profil w 1.3.1. Wybierz katalog przez selektor, a potem wpisz ten sam katalog ręcznie z końcowym separatorem. Poprawna analiza ma pokazać dopasowany i brakujący wpis, bez przenoszenia plików. Sprawdź też błędny cel, katalog zajęty przez plik oraz katalog główny dysku z włączonym i wyłączonym skanowaniem podfolderów. Test korzenia wykonaj na izolowanym dysku testowym.
5. Potwierdź przeniesienie dopasowanego wpisu, porównaj SHA-256 plików oraz wpisy Historii i kolejki, uruchom ponownie i użyj Cofnij. Zweryfikuj pliki oraz ścieżki źródłowe po cofnięciu.

## Gotowość do publikacji

**Kandydat 1.3.1 przeszedł lokalną kwalifikację i jest gotowy do przeglądu.** Wydanie przygotowano z commitu kodu `f3fd725dfb4101146901d8b0d1abde7633bdc881`. Paczka `ModernTubeDownloader-1.3.1-win-x64.zip` jest lokalna, samodzielna i ma 144 974 263 bajty. Jej SHA-256 to `6683b7b6bd3adbf577d60395201dcfa84b9a4c6f2670d86670009940c2fb79ee`.

PR #25 i PR #26 pozostają w trybie Draft. Publikacja i scalenie czekają na akceptację właściciela. Dokładny przypadek zgłaszającego wymaga oryginalnych danych wejściowych w zanonimizowanej postaci. Oddzielne zgłoszenie #23 nadal śledzi odmowę dostępu podczas wymiany dziennika aktualizatora. [Status PR #25](https://github.com/ProGraMajster/ModernTubeDownloader/pull/25/checks) · [Status PR #26](https://github.com/ProGraMajster/ModernTubeDownloader/pull/26/checks) · [Updater #23](https://github.com/ProGraMajster/ModernTubeDownloader/issues/23)
