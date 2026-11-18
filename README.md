# Ritm — calendar de concedii și remote

Proiectul meu pentru specificația „Calendar concedii & remote”. Angajații își trec concediile și zilele de remote într-un calendar comun al echipei, managerii le aprobă, iar adminul configurează regulile (preaviz, limite, sărbători, cât din echipă poate lipsi în aceeași zi).

Backend în .NET 10 (Web API + EF Core), frontend în Vue 3 cu Vuetify, bază de date SQL Server. Am făcut toate cerințele obligatorii și pe cele trei opționale (resetarea parolei, vederea săptămânală cu export, notificările pe email).

## Cum îl pornești

### Cu Docker (cel mai simplu, merge pe orice sistem)

Ai nevoie doar de Git și Docker Desktop.

```bash
git clone https://github.com/tudorsan/leave-and-remote-management.git
cd leave-and-remote-management
docker compose up --build -d
```

Prima pornire durează câteva minute, cât se descarcă imaginile. După aceea aplicația e pe **http://localhost:8080**. Baza de date se creează singură prin migrări și primește la prima pornire niște utilizatori de test și sărbătorile legale.

Emailurile nu pleacă de fapt nicăieri (specificația permite să fie simulate), ci apar în log-ul backend-ului, așa că merită ținut deschis:

```bash
docker compose logs -f api
```

Oprire: `docker compose down`. Dacă vrei să o iei de la zero cu baza goală: `docker compose down -v`.

### Fără Docker (Windows, cu LocalDB)

Așa am lucrat eu în timpul dezvoltării. Trebuie .NET 10 SDK, Node.js (22.18+ sau 24.12+) și SQL Server LocalDB, care vine cu Visual Studio.

Backend-ul, într-un terminal (pornește pe http://localhost:5130 și își creează baza `LeaveRemoteDb`):

```bash
dotnet run --project backend/LeaveRemote.Api
```

Frontend-ul, în alt terminal:

```bash
cd frontend
npm ci
npm run dev
```

și apoi http://localhost:5173. Dacă PowerShell refuză să ruleze `npm` din cauza politicii de scripturi, `npm.cmd` în loc de `npm` rezolvă problema.

## Conturi de test

| Rol | Email | Parolă | |
|---|---|---|---|
| Admin | admin@example.com | Admin123! | Alex |
| Manager | manager@example.com | Manager123! | Maria, echipa Development (șeful ei e adminul) |
| Angajat | employee@example.com | Employee123! | Andrei, în echipa Mariei |
| Angajat | employee2@example.com | Employee123! | Ioana, tot în echipa Mariei |

La login alegi mai întâi rolul și abia apoi scrii datele contului. Dacă alegi alt rol decât are contul, aplicația îți spune și te lasă să-l schimbi.

## Ce merită încercat

Am pus mai jos un traseu prin aplicație care atinge toate cerințele din specificație. Durează cam un sfert de oră. Numerele din paranteze sunt cele din PDF.

**Ca Andrei (angajat)**

- Cere un concediu de odihnă de câteva zile, care să înceapă peste cel puțin 3 zile. La confirmare vezi câte zile lucrătoare are (fără weekend și sărbători), iar cererea apare imediat în calendar, în culoarea deschisă, „în așteptare” (7, 8, 9). În log apare emailul către Maria.
- Dacă încerci un concediu de odihnă care începe mai devreme de 3 zile, e refuzat. Același interval ca medical e acceptat, pentru că preavizul l-am aplicat doar la odihnă.
- O cerere peste una existentă e refuzată (regula 1), la fel una care depășește soldul (regula 3).
- „Soldul meu de concediu” arată totalul, ce ai consumat și ce e blocat în cereri în așteptare (18).
- Pentru remote, alegi mai multe zile direct din calendar, într-un singur formular (11). Implicit sunt aprobate automat (13). Weekendul, sărbătorile (de exemplu 1 decembrie), a treia zi din aceeași săptămână și zilele mai departe de 30 de zile sunt refuzate, cu mesaj (12, regulile 4 și 5).
- În „Cererile mele” poți edita sau anula cererile în așteptare și le poți anula pe cele aprobate, dar numai înainte să înceapă (10). Tot de acolo descarci zilele aprobate în format `.ics` (se importă în Google Calendar sau Outlook) sau `.csv` pentru Excel (6).
- Calendarul se poate vedea pe lună sau pe săptămână și se poate filtra după tipul absenței (4, 5, 6).

**Ca Maria (manager)**

- În „Cereri în așteptare” apar cererile echipei ei. Respingerea cere un motiv de cel puțin 10 caractere (14, regula 8), iar Andrei vede motivul în aplicație și îl primește pe email (15, 16).
- Dacă Maria își face singură o cerere, nu și-o poate aproba (regula 7). Cererea ajunge la superiorul ei, adică la admin.
- Limita de absențe din echipă (regula 6): cu un concediu aprobat al lui Andrei, Ioana nu mai poate lua concediu de odihnă în aceleași zile, pentru că ar lipsi 2 din 3 oameni, adică peste 50%.
- „Solduri de concediu” îi arată soldurile echipei.

**Ca admin**

- Creează utilizatori cu rol, echipă și manager, și îi poate dezactiva fără să-i șteargă (2). Un cont dezactivat nu se mai poate loga, iar cererile lui în așteptare se anulează. Contul se poate și reactiva.
- Din fișa unui angajat schimbă numărul de zile de concediu pe an (17).
- Din „Setări globale” modifică toate setările din specificație și lista de sărbători. De exemplu, dacă pornești aprobarea pentru remote, următoarea zi remote rămâne în așteptare. Cererile deja făcute nu se revalidează.
- Vede calendarul tuturor echipelor și poate filtra pe echipă.

**Resetarea parolei** (3), **vederea săptămânală și exportul** (6) și **notificările pe email** (16) au mai jos o secțiune separată, cu pași mai detaliați.

**Permisiunile** sunt verificate în backend, nu doar ascunse în interfață. Cu tokenul unui angajat, de exemplu, `GET /api/settings` sau aprobarea unei cereri întorc 403. Rolul și starea contului sunt recitite din baza de date la fiecare request, deci schimbarea rolului sau dezactivarea au efect imediat, chiar dacă omul era deja logat.

## Cerințele opționale

Le-am făcut pe toate trei. Mai jos e cum le poți verifica pe fiecare.

Emailurile sunt simulate, deci nu ajung într-o căsuță, ci apar în log-ul backend-ului, fiecare cu „EMAIL SIMULAT”, destinatarul, subiectul și textul. În varianta Docker le vezi cu `docker compose logs -f api`, iar în varianta fără Docker, direct în terminalul în care rulează backend-ul. Pentru toată secțiunea asta merită să ai log-ul deschis lângă browser.

### Notificări pe email la schimbarea stării (16)

Fiecare schimbare a unei cereri trimite un email:

| Ce se întâmplă | Cine primește emailul |
|---|---|
| un angajat cere concediu | managerii echipei lui (pentru un manager, șeful lui direct) |
| cererea e aprobată | cel care a cerut-o |
| cererea e respinsă | cel care a cerut-o, cu motivul respingerii |
| cel care a cerut-o o anulează | cei care trebuiau să o aprobe |
| zile remote aprobate automat | cel care le-a cerut (un singur email pentru toate zilele din formular) |
| zile remote care așteaptă aprobare | managerul |
| o cerere rămâne nedecisă după ultima ei zi și e anulată automat | cel care a cerut-o |

Un test rapid: ca Andrei, cere un concediu; în log apare emailul către Maria. Ca Maria, respinge-l cu un motiv; apare emailul către Andrei, cu motivul. Dacă încerci o respingere cu motiv prea scurt, aplicația o refuză și în log nu apare nimic, pentru că emailurile pleacă doar după ce operația s-a salvat.

### Vederea săptămânală și exportul zilelor aprobate (6)

**Vederea săptămânală.** Deasupra calendarului sunt butoanele **Lună** și **Săptămână**. Pe săptămână, membrii echipei sunt pe rânduri, iar zilele de luni până duminică pe coloane. Culorile sunt aceleași ca la lună (deschis = în așteptare, plin = aprobat), iar weekendul și sărbătorile sunt marcate. Cu „Săptămâna anterioară” și „Săptămâna următoare” te muți prin calendar, iar filtrul pe tipul absenței (și, pentru admin, pe echipă) merge la fel. Cel mai bine se vede ca Maria, care are toată echipa pe rânduri. Când deschizi un formular de cerere, calendarul trece singur pe lună, pentru că zilele remote se aleg din grila lunară.

**Exportul.** În „Cererile mele” sunt două butoane:

- **Calendar (.ics)**: un fișier cu zilele tale aprobate, câte un eveniment pe toată ziua. Pe Windows se deschide direct în Outlook. În Google Calendar îl imporți din Setări → Import și export. Ca să nu amesteci testul cu calendarul tău, poți crea întâi un calendar separat și să-l imporți acolo.
- **Excel (.csv)**: același conținut, ca tabel cu tipul, data de început, data de sfârșit, zilele lucrătoare și comentariul. Dacă datele apar ca `#####`, coloana e doar prea îngustă: un dublu-click pe marginea ei o lărgește.

Exportul conține **doar zilele proprii aprobate**. Cererile în așteptare, cele respinse și cele ale colegilor nu apar. Ca să ai ce exporta, ai nevoie de ceva aprobat: zilele remote se aprobă automat, iar un concediu îl aprobă Maria.

### Resetarea parolei prin link pe email (3)

1. Pe pagina de login alege un rol și apasă **Ai uitat parola?**.
2. Scrie `employee2@example.com` și trimite. Mesajul de pe ecran e același și pentru o adresă care nu există, ca să nu se poată afla ce conturi sunt (atunci nu apare niciun email în log).
3. În log apare emailul „Resetarea parolei”, cu un link de forma `http://localhost:8080/reset-password?token=...` (sau pe portul 5173 în varianta fără Docker). Deschide-l în browser.
4. Alege o parolă nouă de cel puțin 8 caractere, scrisă de două ori. În log apare și emailul de confirmare „Parola ta a fost schimbată”.
5. Autentifică-te cu parola nouă. Cea veche nu mai merge.

Câteva lucruri pe care le poți verifica tot aici:

- **Linkul merge o singură dată.** Dacă îl deschizi din nou după ce ai schimbat parola, primești „Linkul de resetare este invalid, a fost deja folosit sau a expirat”.
- **Un link nou îl anulează pe cel vechi.** Dacă ceri resetarea de două ori, doar ultimul link funcționează.
- **Sesiunile vechi se închid.** Dacă ești logat ca Ioana într-un tab și schimbi parola din altul, la următoarea acțiune în primul tab ești trimis la login, cu mesajul că sesiunea nu mai e valabilă.
- **Linkul expiră după 30 de minute.**

După test, parola Ioanei rămâne cea nouă. În Docker o readuci la `Employee123!` cu încă o resetare sau pornind de la zero cu `docker compose down -v`.

## Cum am interpretat unele lucruri

Specificația lasă câteva întrebări deschise. Am ales așa și cred că merită spus de ce.

**Cine aprobă ce.** Cererea unui angajat o aprobă managerii echipei lui. Cererea unui manager o aprobă șeful lui direct, cum zice PDF-ul. Adminul poate aproba orice. Regula „managerul nu își aprobă propriile cereri” am aplicat-o la toată lumea, inclusiv la admin: mi s-a părut ciudat ca tocmai el să fie excepția. În lista de aprobat apar doar cererile pe care chiar le poți decide.

**Soldul managerului.** Managerul vede soldurile echipei lui și pe ale subordonaților direcți, chiar dacă sunt în altă echipă. Altfel ar fi aprobat cereri fără să vadă soldul omului.

**Preavizul** (`leave.min_days_ahead`) l-am aplicat doar concediului de odihnă, cum scrie în tabelul de setări. Niciun tip de concediu nu poate începe în trecut.

**Soldul pe an.** Doar concediul de odihnă consumă din sold. Soldul e pe an calendaristic, deci un concediu peste Revelion ia din fiecare an zilele care îi revin. Când adminul schimbă numărul de zile al cuiva pentru anul curent, valoarea rămâne și pentru anii următori, pentru că o consider o proprietate a angajatului, nu a unui an anume. Nu poate fi mai mică decât ce a consumat și a rezervat deja omul.

**Limita de absențe din echipă.** O calculez pe fiecare zi lucrătoare: colegii cu concediu aprobat plus cel care cere, împărțit la membrii activi ai echipei. Dacă procentul depășește limita, cererea e blocată. Verificarea se face la creare și încă o dată la aprobare, pentru că între timp se pot aproba alte concedii. Concediul medical nu e blocat de limită (nu-l poți programa), dar odată aprobat contează la ceilalți. Remote-ul nu e absență. Un caz de știut: într-o echipă cu un singur om, cu limita implicită de 50%, omul acela nu poate lua concediu de odihnă, pentru că ar însemna 100% absent. E aplicarea literală a regulii, iar adminul poate ridica limita.

**Remote, câte o cerere pe zi.** PDF-ul sugerează asta (start = end) și am mers pe varianta asta: poți trimite mai multe zile odată, sunt validate împreună (dacă una nu e bună, nu se salvează nimic), dar apoi fiecare zi se aprobă sau se anulează separat. Limita pe săptămână numără zilele deja aprobate, cum zice regula 5, și o verific din nou la aprobare, ca să nu poată fi ocolită cu mai multe cereri în așteptare.

**Setările nu schimbă cererile vechi.** Fiecare cerere ține minte regulile și sărbătorile de la momentul creării, iar editarea sau aprobarea ei se fac după acelea.

**Editare și anulare.** Se pot edita doar cererile în așteptare. Una aprobată se anulează și se face alta. „Înainte de start” înseamnă cel târziu cu o zi înainte de prima zi. Orice trecere care nu e în diagrama de stări din PDF (de exemplu aprobarea unei cereri anulate sau respingerea uneia deja aprobate) e refuzată cu 409.

**Ce e „azi”.** Toate datele sunt date calendaristice, fără oră. „Azi” e data din România (Europe/Bucharest), indiferent unde rulează serverul, ca să nu apară decalaje în jurul miezului nopții.

**Cereri uitate.** O cerere care a rămas „în așteptare” după ultima ei zi nu mai are cum să fie aprobată, dar ar ține blocat soldul. Aplicația o anulează singură (verifică la fiecare oră), notează motivul și îi trimite omului un email. Am ales să aștept finalul perioadei, nu începutul ei, ca un concediu medical anunțat cu întârziere să poată fi totuși aprobat. N-am introdus o stare nouă, ca să rămân la cele patru din diagramă.

**Sărbătorile** (inclusiv Paștele ortodox, Vinerea Mare și Rusaliile) se generează singure pentru toți anii în care se pot face cereri, deci și la trecerea în anul nou, fără repornire. Un an se generează o singură dată, ca să nu suprascriu modificările adminului.

**Conturi.** Parolele sunt salvate cu bcrypt, iar sesiunea e un token JWT care ține 8 ore. Un cont dezactivat e scos și din calendar, și din calculul limitei echipei. La resetarea parolei, linkul e valabil 30 de minute și merge o singură dată. În baza de date țin doar amprenta (SHA-256) tokenului, nu tokenul în sine. Răspunsul e același și pentru o adresă care nu există, ca să nu se poată afla ce conturi sunt.

**Emailurile** trec printr-o interfață (`IEmailSender`). Acum implementarea lor e o scriere în log, dar poate fi înlocuită cu una SMTP fără alte schimbări. Pleacă doar după ce operația s-a salvat, deci o aprobare eșuată nu trimite nimic.

**Cereri simultane.** Scrierile pe cereri sunt serializate cu un lock de aplicație în SQL Server, în aceeași tranzacție, ca două cereri trimise în același moment să nu poată trece amândouă de verificarea de sold sau de suprapunere.

## Teste

Am scris teste pentru aproape fiecare regulă din PDF: 171 în backend și 25 în frontend.

```bash
dotnet test backend/backend.slnx
```

Cele de integrare pornesc API-ul real (`WebApplicationFactory`) pe câte o bază LocalDB nouă pentru fiecare clasă de teste, cu data de „azi” fixată la 13 noiembrie 2026, și o șterg la final. Din cauza LocalDB, rulează doar pe Windows. Le-am grupat după subiect, iar numele lor se citesc ca specificația: `PermissionMatrixTests` urmează tabelul de roluri rând cu rând, `BusinessRuleTests` acoperă regulile de business, iar `SettingsTests`, `NotificationTests`, `PasswordResetTests` și celelalte au fiecare zona lor. Pe lângă ele sunt teste unitare pentru calculul zilelor lucrătoare, data Paștelui, regulile de aprobare și altele.

Pentru frontend:

```bash
cd frontend
npm run test:unit -- --run
npm run build
```

(`build` verifică și tipurile TypeScript.)

## Cum e organizat codul

```
docker-compose.yml          baza de date + API + frontend
backend/
  LeaveRemote.Api/
    Controllers/            doar HTTP: cine e utilizatorul, ce serviciu se apelează, ce răspuns se dă
    Services/               regulile propriu-zise (concedii, remote, sold, calendar, setări, utilizatori,
                            echipe, export, resetare parolă, sărbători, expirarea cererilor)
      Notifications/        emailurile: ce se scrie, coada, trimiterea (în log)
    Models/, Data/          entitățile, DbContext, datele de test
    Migrations/             schema bazei, aplicată automat la pornire
    DTOs/                   ce primește și ce întoarce API-ul
  LeaveRemote.Api.Tests/    testele (unitare și de integrare)
frontend/src/
  views/                    pagina de start, login, aplicația propriu-zisă, resetarea parolei
  components/               calendarul lunar și săptămânal, formularele, aprobările, soldurile,
                            administrarea, exportul
  stores/                   sesiunea (Pinia)
```

Am încercat să țin controllerele subțiri și toată logica în servicii, ca să poată fi testată ușor. Data curentă vine dintr-un `TimeProvider` injectat, așa că în teste pot „muta” ziua. Erorile au peste tot aceeași formă, `{ "code": "...", "message": "..." }`, cu 400 pentru date greșite, 409 pentru conflicte cu regulile sau cu starea cererii, plus 401, 403 și 404. Frontend-ul doar afișează mesajul primit.

## API

Rutele cer `Authorization: Bearer <token>`, în afară de login și resetarea parolei. Contractul complet e la `/openapi/v1.json` (în varianta Docker: http://localhost:8081/openapi/v1.json).

- **Autentificare:** `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`
- **Concedii:** `GET` și `POST /api/leave-requests`, `PUT /api/leave-requests/{id}`, `GET /api/leave-requests/pending`, `PATCH /api/leave-requests/{id}/approve`, `/reject`, `/cancel`
- **Remote:** aceleași rute, sub `/api/remote-requests`
- **Calendar:** `GET /api/calendar?year=&month=&teamId=&type=` (lunar) și `GET /api/calendar/range?from=&to=` (săptămânal)
- **Sold:** `GET /api/leave-balances/me`, `GET /api/leave-balances/{userId}`, `PUT /api/leave-balances/{userId}/{year}` (admin)
- **Export:** `GET /api/exports/my-approved-days.ics` și `.csv`
- **Administrare** (admin): `/api/users` (cu `deactivate` și `reactivate`), `/api/teams`, `/api/settings`, `PUT /api/settings/holidays`

## Configurare

Setările din specificație (orizonturile, limitele, aprobarea remote, procentul de absențe, zilele implicite de concediu și sărbătorile) se modifică din aplicație, de către admin, și stau în baza de date. În `appsettings.json` rămân doar cele tehnice:

- `ConnectionStrings:DefaultConnection`: implicit LocalDB, baza `LeaveRemoteDb`
- `Jwt:Key`: în `appsettings.Development.json` e o cheie doar pentru dezvoltare. În producție se dă prin variabila de mediu `Jwt__Key`.
- `Jwt:ExpirationMinutes`: 480 (o zi de lucru)
- `Frontend:BaseUrl`: adresa din linkurile de resetare a parolei
- `PasswordReset:TokenLifetimeMinutes`: 30
- `Maintenance:IntervalMinutes`: cât de des se verifică cererile uitate și sărbătorile (60)

În Docker, parola SQL Server și cheia JWT sunt valori de demo, care se pot schimba cu variabilele `MSSQL_SA_PASSWORD` și `JWT_KEY`.
