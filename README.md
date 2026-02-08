# Sistem Kontrole Leta

Simulacija rada sistema kontrole leta za nadzor i koordinaciju aviona unutar pravougaone sektorske mape vazdušnog prostora.

## Opis Sistema

Centralni server upravlja svim informacijama o sektorima vazdušnog prostora, uključujući stanje sektora i trenutnu zauzetost, dok avioni (klijenti) komuniciraju sa serverom putem Socket klase (UDP za zahteve za ulazak, TCP za koordinaciju tokom kretanja).

## Komponente

### 1. Server (Server.exe)
- Upravlja pravougaonom sektorskom mapom vazdušnog prostora
- Prima zahteve aviona za ulazak putem UDP soketa
- Koordinira kretanje aviona putem TCP soketa
- Detektuje i rešava konflikte između aviona (na osnovu broja putnika)
- Prikazuje vizuelnu reprezentaciju stanja vazdušnog prostora

### 2. Client/Avion (Client.exe)
- Šalje zahtev za ulazak u vazdušni prostor putem UDP soketa
- Uspostavlja TCP soket vezu sa serverom za koordinaciju leta
- Prima instrukcije od servera za kretanje i preusmeravanje
- Prikazuje status leta u realnom vremenu

### 3. Domain
Sadrži klase:
- **Letelica**: Informacije o avionu (aviokompanija, pilot, registracija, putnici)
- **Let**: Koordinate početka i kraja leta, trenutna pozicija
- **Sektor**: Koordinate sektora, stanje zauzetosti, vremenski uslovi
- **VazdusniProstor**: Mapa sektora sa 3 nivoa visine
- **ZahtevZaUlazak**: Zahtev aviona za ulazak u vazdušni prostor
- **OdgovorServera**: Odgovor servera na zahtev (odobren/odbačen)

## Komunikacija - Socket klasa

Komunikacija se ostvaruje isključivo putem `System.Net.Sockets.Socket` klase (ne koriste se TcpClient/UdpClient wrapperi):

### UDP Socket (Port 15001)
- Klijent kreira `Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)`
- Koristi `SendTo()` i `ReceiveFrom()` za razmenu zahteva/odgovora
- Zahtev: serijalizovan `ZahtevZaUlazak` objekat
- Odgovor: serijalizovan `OdgovorServera` objekat

### TCP Socket (Port 15002)
- Server kreira `Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)`
- Koristi `Bind()`, `Listen()`, `Accept()` za prihvatanje konekcija
- Klijent koristi `Connect()`, `Send()`, `Receive()` za komunikaciju
- Multipleksiranje: `Socket.Poll()` za proveru više konekcija

### Protokol poruka (TCP):
1. Klijent -> Server: Flight ID (UTF8 string, handshake)
2. Server -> Klijent: "TCP_CONNECTED" (potvrda)
3. Klijent -> Server: "POSITION_UPDATE:x,y,z"
4. Server -> Klijent: "POSITION_OK" ili "REDIRECT:x,y,z"
5. Klijent -> Server: "SECTOR_ENTERED:x,y,z"
6. Server -> Klijent: "DESTINATION_REACHED"
7. Klijent -> Server: "FLIGHT_COMPLETED"

## Serijalizacija

Koristi se BinaryFormatter za serijalizaciju objekata (`[Serializable]` atribut na svim Domain klasama).

## Pokretanje Sistema

### 1. Pokretanje Servera
```bash
cd Server/bin/Debug
Server.exe
```

Pri pokretanju servera:
- Unesite širinu vazdušnog prostora (broj sektora)
- Unesite visinu vazdušnog prostora (broj sektora)
- Server će kreirati 3D mapu sa 3 nivoa visine (Z=1,2,3)

### 2. Pokretanje Aviona (Klijenta)
```bash
cd Client/bin/Debug
Client.exe
```

Pri pokretanju klijenta:
1. Izaberite opciju "1" za zahtev za ulazak
2. Unesite podatke o letelici
3. Unesite koordinate leta (početne i krajnje)

## Algoritmi

### Detekcija Konflikata i Razrešavanje:
1. Server proverava da li je ciljani sektor zauzet ili ima abnormalne uslove
2. Kod kolizije: avion sa **manjim brojem putnika** se preusmerava
3. Prvo se pokušava alternativna visina (Z koordinata)
4. Ako su sve visine zauzete, traži se najbliži slobodni susedni sektor u smeru cilja
5. Server šalje REDIRECT instrukciju preusmerenom avionu

### Kretanje Aviona:
1. Avion se kreće korak po korak ka cilju (1 sektor po ciklusu u 3x3 okolini)
2. Prioritet: X koordinata -> Y koordinata -> Z koordinata
3. Server potvrđuje svaki potez ili šalje preusmeravanje
4. Let se završava kada avion stigne do odredišta

## Vizualizacija

### Mapa Sektora:
- `[]` - Slobodan sektor
- `A1`, `A2`... - Sektor zauzet avionom (redni broj iz liste aktivnih letova)
- `XX` - Sektor sa abnormalnim vremenskim uslovima

### Server Prikaz:
- Lista aktivnih letova sa trenutnim pozicijama i statusom
- Lista zahteva za ulazak
- 2D mapa za svaki nivo visine (1, 2, 3)
- Legenda simbola

### Client Prikaz:
- Informacije o avionu i pilotu
- Trenutna pozicija i odredište
- Broj pređenih sektora
- Broj preostalih sektora do cilja
- Broj izmena putanje zbog konflikata
- Trajanje leta

## Tehnički Detalji

- **Komunikacija**: Socket klasa (`System.Net.Sockets.Socket`)
- **Protokoli**: UDP za zahteve, TCP za koordinaciju
- **Serijalizacija**: BinaryFormatter za razmenu objekata
- **Threading**: Background thread-ovi za ažuriranje stanja i komunikaciju
- **Sinhronizacija**: `lock()` za thread-safe pristup deljenim resursima
- **Port**: UDP 15001, TCP 15002
- **Framework**: .NET Framework 4.7.2
