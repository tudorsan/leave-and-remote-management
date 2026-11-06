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

Instrucțiunile complete pentru instalare, configurarea bazei de date, aplicarea migrațiilor și pornirea aplicației vor fi documentate pe parcursul dezvoltării.

## Testare

Proiectul va include verificări pentru funcționalitățile principale, permisiunile utilizatorilor și regulile de business, inclusiv situațiile-limită relevante.

## Stadiul proiectului

**În dezvoltare — etapă de inițializare.**

Funcționalitățile prezentate în acest document descriu obiectivele proiectului și nu indică faptul că toate au fost deja implementate.
