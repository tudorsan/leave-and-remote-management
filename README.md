# Ritm — calendar de concedii și lucru remote

Aplicație web în care angajații își înregistrează concediile și zilele de lucru remote pe un calendar comun al echipei, iar managerii le aprobă. Regulile (limite, preaviz, sărbători, procentul maxim de absențe din echipă) sunt configurabile de Admin și sunt aplicate de backend.

**Stack:** .NET 10 (ASP.NET Core Web API, EF Core) · Vue 3 + Vuetify (TypeScript) · SQL Server · xUnit și Vitest · Docker Compose.

Proiectul implementează **toate cerințele din specificație, inclusiv cele opționale** (3, 6 și 16), și are **196 de teste automate** (171 backend, 25 frontend).

---

## Cuprins
1. [Pornire rapidă](#1-pornire-rapidă)
2. [Conturi de test](#2-conturi-de-test)
3. [Ghid de evaluare pas cu pas](#3-ghid-de-evaluare-pas-cu-pas)
4. [Acoperirea specificației](#4-acoperirea-specificației)
5. [Decizii și presupuneri](#5-decizii-și-presupuneri)
6. [Teste automate](#6-teste-automate)
7. [Arhitectură](#7-arhitectură)
8. [API](#8-api)
9. [Configurare](#9-configurare)

---

## 1. Pornire rapidă

### Varianta A — Docker (recomandată, orice sistem de operare)
Necesită doar [Docker Desktop](https://www.docker.com/products/docker-desktop/) (și Git).

```bash
git clone https://github.com/tudorsan/leave-and-remote-management.git
cd leave-and-remote-management
docker compose up --build -d
```

| Ce | Unde |
|---|---|
| Aplicația | http://localhost:8080 |
| Contractul API (OpenAPI) | http://localhost:8081/openapi/v1.json |
| Emailurile simulate și log-ul backend-ului | `docker compose logs -f api` |

Prima pornire durează câteva minute (se descarcă imaginile). Baza de date, migrările, datele de test și sărbătorile legale se creează automat.

```bash
docker compose down      # oprește aplicația (datele rămân)
docker compose down -v   # oprește și șterge baza de date (pornire de la zero)
```

### Varianta B — fără Docker (Windows, SQL Server LocalDB)
Necesită [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js](https://nodejs.org/) 22.18+ sau 24.12+ și SQL Server LocalDB (inclus în Visual Studio).

Terminalul 1 — backend (http://localhost:5130, baza `LeaveRemoteDb`, creată automat prin migrări):
```bash
dotnet run --project backend/LeaveRemote.Api
```

Terminalul 2 — frontend (http://localhost:5173, cererile `/api` sunt trimise către backend):
```bash
cd frontend
npm ci
npm run dev
```

> Pe Windows, dacă PowerShell blochează scriptul `npm.ps1`, folosește `npm.cmd` în loc de `npm`.

---

## 2. Conturi de test

Pe pagina de autentificare se alege întâi **rolul** (cardul), apoi se introduc datele contului. Dacă rolul ales nu corespunde contului, aplicația cere alegerea rolului corect („Schimbă rolul”).

| Rol | Email | Parolă | Cine este |
|---|---|---|---|
| Admin | `admin@example.com` | `Admin123!` | Alex Admin |
| Manager | `manager@example.com` | `Manager123!` | Maria Manager — echipa Development, superior: Admin |
| Angajat | `employee@example.com` | `Employee123!` | Andrei — echipa Development, manager: Maria |
| Angajat | `employee2@example.com` | `Employee123!` | Ioana — echipa Development, manager: Maria |

Setările globale pornesc cu valorile implicite din specificație, iar sărbătorile legale ale României sunt generate pentru anul curent și pentru anii în care se pot face cereri.

---

## 3. Ghid de evaluare pas cu pas

Un parcurs de aproximativ 15 minute care trece prin fiecare cerință. Ține deschis un terminal cu `docker compose logs -f api` (sau terminalul backend-ului, în varianta B): acolo apar emailurile simulate.

### 3.1 Angajatul — cereri de concediu (cerințele 7–10, regulile 1–3)
Autentificare: **Angajat**, `employee@example.com`.
1. **Cerere nouă → Solicită concediu**: o perioadă de câteva zile, care începe la cel puțin 3 zile de azi, tip „Concediu de odihnă”, cu un comentariu.
   - confirmarea afișează **numărul de zile lucrătoare** (fără weekend și sărbători);
   - cererea apare **imediat în calendar**, cu culoarea deschisă = „În așteptare”;
   - în log apare emailul **„Cerere nouă de aprobat”** către Maria.
2. Încearcă o perioadă care **începe peste mai puțin de 3 zile** → refuzată (preaviz). Același lucru ca **„Concediu medical”** → acceptat (preavizul se aplică doar odihnei).
3. Încearcă o perioadă **suprapusă** cu prima cerere → refuzată (regula 1).
4. **Soldul meu de concediu**: total 21, zilele cerute apar ca „blocate în cereri în așteptare” (cerința 18).
5. **Cererile mele**: cererea în așteptare se poate **edita** și **anula**.

### 3.2 Angajatul — lucru remote (cerințele 11–13, regulile 4–5)
1. **Cerere nouă → Solicită lucru remote** și selectează **mai multe zile direct în calendar**.
   - zilele sunt aprobate automat (`remote.requires_approval = false`) și apar cu culoarea plină;
   - în log apare emailul de confirmare.
2. Selectează **o zi de weekend** sau **o sărbătoare** (ex. 1 decembrie) → refuzată, cu mesaj explicit.
3. Selectează **3 zile din aceeași săptămână** → refuzată (maximum 2 pe săptămâna luni–duminică).
4. Selectează o zi **mai departe de 30 de zile** → refuzată (orizontul remote).

### 3.3 Calendarul (cerințele 4–6)
1. Navighează între luni; filtrează după **tipul de absență**.
2. Comută pe **Săptămână**: membrii echipei pe rânduri, zilele pe coloane, aceleași culori.
3. **Cererile mele → Calendar (.ics)** și **Excel (.csv)**: exportul conține **doar zilele proprii aprobate**. Fișierul `.ics` se importă în Google Calendar sau Outlook.

### 3.4 Managerul — aprobări (cerințele 14–15, regulile 6–8)
Autentificare: **Manager**, `manager@example.com`.
1. **Cereri în așteptare**: cererea lui Andrei. **Respinge** cu un motiv scurt (sub 10 caractere) → refuzat; apoi cu un motiv valid → în log apare emailul către Andrei, cu motivul.
2. Fă o cerere nouă ca Andrei și **aprob-o** ca Maria → soldul lui Andrei arată zilele consumate.
3. Ca Maria, cere un concediu și verifică faptul că **nu îl poți aproba singur** (cererea nu apare în lista ta; o aprobă Adminul — regula 7).
4. **Limita echipei (regula 6):** cu concediul lui Andrei aprobat, cere ca Ioana un concediu de odihnă în aceleași zile → refuzat (2 din 3 membri = 67% > 50%).
5. **Solduri de concediu**: Maria vede soldurile echipei sale.
6. Ca Andrei, anulează concediul aprobat (înainte de început) → zilele revin în sold, iar Maria primește email.

### 3.5 Adminul — administrare și setări (cerințele 2, 17, setările)
Autentificare: **Admin**, `admin@example.com`.
1. **Utilizatori**: creează un utilizator nou cu rol, echipă și manager; **dezactivează-l** — nu se mai poate autentifica, iar cererile lui în așteptare se anulează. Poate fi reactivat.
2. **Utilizatori → Editează → alocare anuală**: setează alt număr de zile pentru un angajat (cerința 17).
3. **Setări globale**: activează `remote.requires_approval` → o zi remote nouă rămâne „În așteptare” și trebuie aprobată. Cererile deja existente nu se revalidează.
4. **Setări globale → sărbători**: adaugă o sărbătoare → zilele lucrătoare ale cererilor **noi** o exclud.
5. **Calendar**: Adminul vede toate echipele și poate filtra pe echipă.

### 3.6 Resetarea parolei (cerința 3, opțională)
1. Pe login: **Ai uitat parola?** → `employee2@example.com`.
2. Copiază linkul din emailul apărut în log și deschide-l → alege o parolă nouă.
3. Autentifică-te cu parola nouă; sesiunile vechi ale contului au fost închise.

### 3.7 Permisiuni verificate în backend
Încercările de a ocoli interfața sunt refuzate de API. Exemple (cu tokenul unui angajat): `GET /api/settings` → 403, `PATCH /api/leave-requests/{id}/approve` → 403, `GET /api/leave-balances/{alt utilizator}` → 403. Toată matricea este acoperită de testele din `PermissionMatrixTests`.

---

## 4. Acoperirea specificației

### Roluri și permisiuni
| Acțiune | Angajat | Manager | Admin | Teste |
|---|---|---|---|---|
| Vede calendarul echipei sale | ✔ | ✔ | ✔ | `PermissionMatrixTests` |
| Vede calendarul tuturor echipelor | ✘ | ✘ | ✔ | `PermissionMatrixTests` |
| Creează / editează / anulează propriile cereri | ✔ | ✔ | ✔ | `PermissionMatrixTests` |
| Aprobă / respinge cereri din echipa sa | ✘ | ✔ | — | `PermissionMatrixTests`, `ReviewPolicyTests` |
| Aprobă / respinge orice cerere | ✘ | ✘ | ✔ | `PermissionMatrixTests` |
| Vede soldul altora | ✘ | echipa sa | toți | `PermissionMatrixTests` |
| Administrează utilizatori, echipe, roluri, setări, solduri | ✘ | ✘ | ✔ | `PermissionMatrixTests`, `AdministrationTests` |

Permisiunile se verifică în backend la fiecare request: rolul și starea contului sunt recitite din baza de date, deci un cont dezactivat sau cu rol schimbat este tratat corect chiar dacă are un token emis anterior.

### Cerințe funcționale
| # | Cerința | Implementare | Teste |
|---|---|---|---|
| 1 | Login email + parolă, JWT, parole bcrypt | `AuthService` | `AuthAndErrorTests` |
| 2 | Admin creează utilizatori (rol, echipă, manager); dezactivare fără ștergere | `UserService` | `AdministrationTests` |
| 3 *(Op)* | Resetare parolă prin link (simulat în log) | `PasswordResetService` | `PasswordResetTests` |
| 4 | Calendar lunar al echipei, culori per tip și stare | `CalendarService`, `CalendarGrid.vue` | `LeaveRequestTests`, `HomeCalendar.spec` |
| 5 | Navigare între luni, filtru echipă (Admin) și tip | `CalendarService` | `PermissionMatrixTests`, `RemoteRequestTests` |
| 6 *(Op)* | Vedere săptămânală și export al zilelor aprobate | `CalendarService`, `ExportService` | `CalendarRangeTests`, `ExportTests` |
| 7 | Cerere de concediu: interval, tip, comentariu | `LeaveRequestService` | `LeaveRequestTests` |
| 8 | Zile lucrătoare fără weekend și sărbători | `WorkingDays` | `WorkingDaysTests`, `LeaveRequestTests` |
| 9 | Cererea apare imediat în calendar, „În așteptare” | `CalendarService` | `LeaveRequestTests` |
| 10 | Anulare: oricând dacă e în așteptare, înainte de start dacă e aprobată | `LeaveRequestService` | `LeaveRequestTests` |
| 11 | Mai multe zile remote într-un formular, selectate în calendar | `RemoteRequestService` | `RemoteRequestTests` |
| 12 | Orizont remote configurat, refuz cu mesaj explicit | `RemoteRequestService` | `RemoteRequestTests` |
| 13 | Remote cu aprobare sau aprobat automat | `RemoteRequestService` | `SettingsTests` |
| 14 | Lista de cereri în așteptare, motiv obligatoriu la respingere | `ReviewPolicy` | `BusinessRuleTests` |
| 15 | Solicitantul vede starea și motivul | `LeaveRequestService` | `BusinessRuleTests` |
| 16 *(Op)* | Notificare prin email la schimbarea stării (simulat în log) | `NotificationService` | `NotificationTests` |
| 17 | Sold anual per angajat, implicit la creare; aprobat consumă, anulat restituie | `LeaveBalanceService` | `AdministrationTests`, `BusinessRuleTests` |
| 18 | Sold rămas, consumat și blocat în cereri în așteptare | `LeaveBalanceService` | `AdministrationTests` |

### Reguli de business și validări
| # | Regula | Teste |
|---|---|---|
| 1 | O zi — cel mult o cerere activă, indiferent de tip (și la cereri simultane) | `BusinessRuleTests` |
| 2 | Concediul nu începe în trecut; sfârșit ≥ început | `LeaveRequestTests` |
| 3 | Odihna nu depășește soldul disponibil | `BusinessRuleTests` |
| 4 | Remote nu în weekend sau sărbătoare | `RemoteRequestTests` |
| 5 | Maximum de zile remote pe săptămâna luni–duminică, cu zilele aprobate | `RemoteRequestTests`, `SettingsTests` |
| 6 | Procentul maxim din echipă absent, pe fiecare zi, doar concedii aprobate | `BusinessRuleTests` |
| 7 | Nimeni nu își aprobă propria cerere | `BusinessRuleTests`, `ReviewPolicyTests` |
| 8 | Motiv de respingere de minimum 10 caractere | `BusinessRuleTests`, `AppClockAndInputTests` |
| 9 | Date calendaristice, fără oră și fus orar | `AppClockAndInputTests` |

Toate erorile de validare au același format, cu cod stabil și mesaj lizibil: `{ "code": "INSUFFICIENT_LEAVE_BALANCE", "message": "Sold insuficient pentru 2026. …" }`, cu statusul 400 (date invalide), 409 (conflict de stare sau de reguli), 401, 403 sau 404.

### Setări configurabile
Toate cele 9 setări din specificație (`remote.max_days_ahead`, `remote.min_days_ahead`, `remote.max_days_per_week`, `remote.requires_approval`, `leave.max_days_ahead`, `leave.min_days_ahead`, `leave.default_annual_days`, `leave.max_team_absent_pct`, `holidays`) sunt editabile de Admin, persistate în baza de date (tabel cheie–valoare) și citite la fiecare validare. Testele: `SettingsTests`.

### Stările unei cereri
`În așteptare` → `Aprobată` / `Respinsă` / `Anulată`, iar `Aprobată` → `Anulată` înainte de data de început; zilele remote aprobate automat intră direct în `Aprobată`. Orice altă tranziție este refuzată cu 409 (`BusinessRuleTests`).

---

## 5. Decizii și presupuneri

Acolo unde specificația lasă loc de interpretare, am ales varianta de mai jos.

**Aprobări**
- **Cine aprobă:** cererea unui angajat — managerii echipei sale; cererea unui Manager (sau Admin) — superiorul său direct; Adminul aprobă orice cerere. Lista „Cereri în așteptare” conține exact cererile pe care utilizatorul le poate decide.
- **Regula 7 se aplică tuturor:** nici Adminul nu își poate aproba sau respinge propria cerere (o decide alt Admin).
- **Managerul vede soldurile** echipei sale și ale subordonaților direcți, chiar dacă sunt în altă echipă (le aprobă cererile).

**Concedii și sold**
- **Preavizul** (`leave.min_days_ahead`) se aplică doar concediului de odihnă; niciun concediu nu poate începe în trecut.
- **Doar concediul de odihnă consumă sold.** Soldul este pe an calendaristic: un concediu peste Anul Nou consumă din fiecare an zilele lui.
- **Alocarea setată de Admin** pentru anul curent sau unul viitor devine și maximul moștenit de anii următori (este o proprietate a angajatului). Nu poate fi mai mică decât zilele deja consumate și rezervate.
- **Editarea** este permisă doar cererilor în așteptare. O cerere aprobată se anulează și se refac.
- **„Înainte de start”** înseamnă cel târziu cu o zi înainte de data de început.

**Limita de absențe a echipei (regula 6)**
- Se calculează pentru fiecare zi lucrătoare: (colegi cu concediu aprobat + solicitantul) / membrii activi ai echipei; cererea este blocată dacă procentul **depășește** limita. Se verifică la creare **și din nou la aprobare**.
- **Concediul medical nu este blocat** de limită (nu poate fi planificat), dar odată aprobat contează ca absență pentru ceilalți. Lucrul remote nu este absență.
- **Echipă cu un singur membru:** cu limita implicită de 50%, singurul membru nu poate lua concediu de odihnă (100% > 50%) — aplicarea literală a regulii. Adminul poate ridica limita din setări.

**Lucru remote**
- **Fiecare zi este o cerere separată** (start = end, cum sugerează specificația): mai multe zile se trimit dintr-un singur formular și se validează împreună (tot sau nimic), dar fiecare zi poate fi aprobată, respinsă sau anulată independent.
- **Limita săptămânală** numără zilele deja aprobate (regula 5) și se verifică din nou la aprobare, ca mai multe zile în așteptare să nu o poată ocoli.

**Setări și timp**
- **Setările nu revalidează cererile existente:** fiecare cerere păstrează regulile și sărbătorile în vigoare la creare; editarea și aprobarea o folosesc pe acestea.
- **„Azi”** este data calendaristică din România (Europe/Bucharest), indiferent de fusul orar al serverului. Datele sunt `DateOnly` în .NET și `date` în SQL Server.
- **Sărbătorile legale** (inclusiv Paștele ortodox, Vinerea Mare și Rusaliile) sunt generate automat pentru toți anii în care se pot face cereri, la pornire și apoi în fiecare oră; un an este generat o singură dată, deci modificările Adminului sunt respectate.

**Întreținere automată**
- **Cererile rămase „În așteptare” după ultima zi a perioadei** sunt anulate automat (cu motiv), soldul rezervat se eliberează, iar solicitantul primește email. Cât timp perioada nu s-a încheiat, cererea poate fi încă decisă (ex. un concediu medical anunțat după ce a început). Se folosește starea „Anulată”, păstrând cele 4 stări din specificație.

**Conturi și sesiuni**
- **Dezactivarea** blochează autentificarea (și tokenurile emise deja), anulează cererile în așteptare și scoate utilizatorul din calendar și din calculul limitei echipei. Contul poate fi reactivat.
- **Sesiunea** durează 8 ore. Schimbarea parolei închide toate sesiunile contului.
- **Resetarea parolei:** tokenul are 32 de octeți aleatori, în baza de date se păstrează doar amprenta lui SHA-256, expiră în 30 de minute și poate fi folosit o singură dată; răspunsul este același dacă adresa există sau nu.

**Notificări**
- Emailurile sunt simulate în log prin `LogEmailSender` (înlocuibil cu un expeditor SMTP fără alte modificări). Sunt trimise doar după ce operația a fost salvată; o operație eșuată nu trimite nimic.

**Concurență**
- Scrierile asupra cererilor sunt serializate cu un lock de aplicație SQL Server în aceeași tranzacție, ca regulile de suprapunere, sold și limite să rămână corecte și la cereri simultane.

---

## 6. Teste automate

### Backend — 171 de teste (xUnit)
```bash
dotnet test backend/backend.slnx
```
- **Teste unitare** (`LeaveRemote.Api.Tests/Unit`): zile lucrătoare, politica de aprobare, Paștele ortodox, ceasul în ora României, motivul de respingere.
- **Teste de integrare** (`LeaveRemote.Api.Tests/Integration`): pornesc API-ul real (`WebApplicationFactory`) pe o bază SQL Server LocalDB nouă pentru fiecare clasă de teste, creată prin migrări și ștearsă la final, cu „azi” fixat la 13.11.2026. Acoperă matricea de permisiuni, cerințele, regulile de business, setările, notificările, resetarea parolei, exportul, expirarea automată și formatul erorilor.

> Testele de integrare folosesc SQL Server LocalDB, deci rulează pe Windows.

### Frontend — 25 de teste (Vitest)
```bash
cd frontend
npm run test:unit -- --run
npm run build        # verificarea tipurilor (vue-tsc) și build-ul de producție
```

---

## 7. Arhitectură

```
├── docker-compose.yml               SQL Server + API + frontend
├── backend/
│   ├── LeaveRemote.Api/
│   │   ├── Controllers/             strat HTTP subțire: autentificare, apel de serviciu, răspuns
│   │   ├── Services/                regulile de business (concedii, remote, sold, setări, calendar,
│   │   │                            utilizatori, echipe, export, resetare parolă, sărbători, expirare)
│   │   │   └── Notifications/       NotificationService, coada de emailuri, LogEmailSender
│   │   ├── Models/  Data/           entități EF Core, AppDbContext, seed, lock de scriere
│   │   ├── Migrations/              schema bazei de date (aplicată automat la pornire)
│   │   └── DTOs/                    corpurile cererilor și ale răspunsurilor
│   └── LeaveRemote.Api.Tests/       teste unitare și de integrare
└── frontend/
    └── src/
        ├── views/                   Start, Login, calendarul (Home), resetarea parolei
        ├── components/              calendar lunar și săptămânal, formulare, aprobări,
        │                            solduri, administrare, export
        └── stores/                  sesiunea de autentificare (Pinia)
```

- **Controllerele nu conțin reguli**: încarcă utilizatorul curent și traduc rezultatul serviciului (`Result<T>` / `ServiceError`) în HTTP.
- **Serviciile conțin regulile**, ușor de testat: „azi” vine dintr-un ceas injectabil (`TimeProvider`), iar emailurile printr-o interfață (`IEmailSender`).
- **Frontend-ul doar afișează erorile** primite de la backend; validările din interfață sunt doar pentru confort.

---

## 8. API

Toate rutele, cu excepția autentificării, cer header-ul `Authorization: Bearer <token>`. Contractul complet: `/openapi/v1.json`.

| Zona | Rute |
|---|---|
| Autentificare | `POST /api/auth/login` · `GET /api/auth/me` · `POST /api/auth/forgot-password` · `POST /api/auth/reset-password` |
| Concedii | `GET/POST /api/leave-requests` · `PUT /api/leave-requests/{id}` · `GET /api/leave-requests/pending` · `PATCH /api/leave-requests/{id}/approve \| reject \| cancel` |
| Remote | `GET/POST /api/remote-requests` · `PUT /api/remote-requests/{id}` · `GET /api/remote-requests/pending` · `PATCH /api/remote-requests/{id}/approve \| reject \| cancel` |
| Calendar | `GET /api/calendar?year&month&teamId&type` · `GET /api/calendar/range?from&to&teamId&type` |
| Sold | `GET /api/leave-balances/me` · `GET /api/leave-balances/{userId}` · `PUT /api/leave-balances/{userId}/{year}` (Admin) |
| Export | `GET /api/exports/my-approved-days.ics` · `GET /api/exports/my-approved-days.csv` |
| Administrare (Admin) | `/api/users` (+ `deactivate`, `reactivate`) · `/api/teams` (+ `deactivate`) · `/api/settings` · `PUT /api/settings/holidays` |

---

## 9. Configurare

| Cheie | Implicit | Rol |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | LocalDB, baza `LeaveRemoteDb` | conexiunea SQL Server |
| `Jwt:Key` | o cheie de dezvoltare în `appsettings.Development.json` | cheia de semnare a tokenurilor — **în producție se setează prin variabilă de mediu** (`Jwt__Key`) |
| `Jwt:ExpirationMinutes` | 480 | durata sesiunii |
| `Frontend:BaseUrl` | `http://localhost:5173` | adresa folosită în linkurile de resetare a parolei |
| `PasswordReset:TokenLifetimeMinutes` | 30 | valabilitatea linkului de resetare |
| `Maintenance:IntervalMinutes` | 60 | cât de des rulează generarea sărbătorilor și expirarea cererilor |

În mediul `Development`, aplicația populează datele de test și publică contractul OpenAPI. Docker Compose folosește valori demonstrative pentru parola SQL Server și cheia JWT, care se pot suprascrie prin variabilele `MSSQL_SA_PASSWORD` și `JWT_KEY`.
