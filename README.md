# Leave & Remote Management

**Aplicație full-stack pentru gestionarea concediilor și a zilelor de lucru remote**

## Despre proiect

Leave & Remote Management este o aplicație web destinată gestionării cererilor de concediu și a zilelor de lucru remote într-o organizație.

Aplicația urmărește să ofere angajaților o modalitate simplă de a trimite și urmări cereri, managerilor instrumentele necesare pentru aprobarea acestora, iar administratorilor control asupra utilizatorilor, echipelor și regulilor organizaționale.

Proiectul este dezvoltat pe baza unei specificații tehnice, cu accent pe corectitudinea regulilor de business, securitate, organizarea codului și experiența utilizatorului.

## Tehnologii

| Componentă | Tehnologie 
| Frontend   | Vue.js 
| Backend    | .NET Web API (ASP.NET Core) 
| BD         | Microsoft SQL Server 
| Acces date | Entity Framework Core 
| Comunicare | REST API 
| Versionare | Git & GitHub 

## Funcționalități planificate

### Autentificare și utilizatori
- Autentificare securizată prin email și parolă.
- Gestionarea utilizatorilor, rolurilor și echipelor.
- Trei roluri: Admin, Manager și Employee.
- Controlul accesului în funcție de rol și echipă.
- Dezactivarea conturilor fără ștergerea istoricului.

### Calendar
- Calendar lunar pentru vizualizarea concediilor și a zilelor remote.
- Diferențiere vizuală a tipurilor de cereri și a statusurilor.
- Navigare între luni și filtrare.

### Concedii și lucru remote
- Crearea, modificarea și anularea cererilor conform regulilor aplicației.
- Calcularea zilelor lucrătoare, ținând cont de weekenduri și sărbători legale.
- Solicitarea mai multor zile remote dintr-un singur formular.
- Validarea suprapunerilor și a limitelor configurate.

### Aprobări și solduri
- Aprobarea sau respingerea cererilor de către utilizatorii autorizați.
- Urmărirea statusului și a motivelor de respingere.
- Evidența zilelor de concediu disponibile, consumate și blocate de cererile în așteptare.

### Administrare și configurare
- Administrarea utilizatorilor și echipelor.
- Configurarea regulilor pentru concedii și remote.
- Gestionarea soldurilor individuale de concediu.
- Aplicarea regulilor și validărilor în backend.

## Arhitectură

Aplicația va utiliza o arhitectură formată din trei componente principale:

- **Frontend (Vue.js):** interfața prin care utilizatorii interacționează cu aplicația.
- **Backend (.NET Web API):** autentificare, autorizare, procesarea cererilor și aplicarea regulilor de business.
- **Bază de date (SQL Server):** stocarea persistentă a utilizatorilor, echipelor, cererilor și configurărilor.

Comunicarea dintre frontend și backend se va realiza prin API-uri REST.

## Instalare și rulare

Backendul necesită SDK-ul .NET 10 și SQL Server. Configurația locală implicită folosește `(localdb)\MSSQLLocalDB` și baza `LeaveRemoteDb`. Pentru alt server, setează variabila `ConnectionStrings__DefaultConnection`.

Din rădăcina repository-ului, în PowerShell:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.0
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$jwtKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set 'Jwt:Key' $jwtKey --project backend/LeaveRemote.Api
dotnet ef database update --project backend/LeaveRemote.Api
dotnet run --project backend/LeaveRemote.Api
```

Dacă `dotnet-ef` este instalat deja, sari peste prima comandă. Folosește o cheie JWT nouă doar la configurarea inițială sau când vrei să invalidezi tokenurile existente.

În mediul `Development`, o bază nouă primește utilizatori demonstrativi, echipe, setări și sărbătorile anului curent. Conturile de test sunt `admin@example.com` / `Admin123!`, `manager@example.com` / `Manager123!` și `employee@example.com` / `Employee123!`.

Backendul ascultă implicit pe `http://localhost:5128`. Tokenul obținut de la `POST /api/auth/login` se trimite în antetul `Authorization: Bearer <token>`. Contractul API este disponibil în development la `/openapi/v1.json`.

Pentru o demonstrație cu datele inițiale ale proiectului, folosește baza separată `LeaveRemoteDemoDb`. Prima comandă creează schema prin migrații, iar prima pornire populează datele demonstrative.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project backend/LeaveRemote.Api --configuration Release --connection 'Server=(localdb)\MSSQLLocalDB;Database=LeaveRemoteDemoDb;Trusted_Connection=True;TrustServerCertificate=True;'
dotnet run --project backend/LeaveRemote.Api --configuration Release --launch-profile demo
```

Profilul `demo` ascultă pe `http://localhost:5130`. Al doilea angajat demonstrativ este `employee2@example.com` / `Employee123!`.

Pentru interfața Vue, lasă backendul pornit cu profilul `demo`. Ai nevoie de Node.js 22.18 sau mai nou din seria 22, ori Node.js 24.12 sau mai nou. Din rădăcina proiectului, deschide un al doilea terminal PowerShell și rulează:

```powershell
Set-Location frontend
npm.cmd ci
npm.cmd run dev
```

Deschide adresa afișată de Vite, implicit `http://localhost:5173`. Vite trimite cererile către API pe portul `5130`. Alege rolul contului de test înainte de autentificare.

## Testare

Verifică compilarea backendului cu `dotnet build backend/backend.slnx`. Poți folosi contractul OpenAPI pentru a verifica fluxurile cu cele trei conturi demonstrative.

Din folderul `frontend`, rulează `npm.cmd run build` pentru verificarea tipurilor și compilare, apoi `npm.cmd run test:unit -- --run` pentru testele frontend.

## Stadiul proiectului

Aplicația include autentificare pe roluri, calendar lunar, cereri de concediu și lucru remote, aprobări, solduri și administrarea utilizatorilor, echipelor și setărilor globale.
