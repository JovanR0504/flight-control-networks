using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using Domain;

namespace Server
{
    public class Server
    {
        private const int UdpPort = 15001;
        private const int TcpPort = 15002;
        
        private static VazdusniProstor _vazdusniProstor;
        private static readonly Dictionary<int, Let> _aktivniLetovi = new Dictionary<int, Let>();
        private static readonly Dictionary<int, Socket> _avionSockets = new Dictionary<int, Socket>();
        private static readonly List<ZahtevZaUlazak> _zahteviZaUlazak = new List<ZahtevZaUlazak>();
        private static int _nextFlightId = 1;
        private static readonly object _lockObject = new object();

        private static void Main(string[] args)
        {
            Console.WriteLine("=== SISTEM KONTROLE LETA ===");
            Console.WriteLine("Server pokretanje...");

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\nGašenje servera...");
                Environment.Exit(0);
            };

            InitializeAirspace();

            var udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            var tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
       
            var udpEndPoint = new IPEndPoint(IPAddress.Any, UdpPort);
            var tcpEndPoint = new IPEndPoint(IPAddress.Any, TcpPort);

            udpSocket.Bind(udpEndPoint);
            tcpSocket.Bind(tcpEndPoint);
            tcpSocket.Listen(10);

            Console.WriteLine($"Server sluša na UDP portu {UdpPort} i TCP portu {TcpPort}");
            Console.WriteLine("Vazdušni prostor je inicijalizovan i spreman za rad.\n");

            // Pokretanje thread-a za ažuriranje stanja
            var updateThread = new Thread(UpdateFlightsLoop) { IsBackground = true };
            updateThread.Start();

            while (true)
            {
                if (udpSocket.Poll(1000, SelectMode.SelectRead))
                {
                    HandleUdpClient(udpSocket);
                }

                if (tcpSocket.Poll(1000, SelectMode.SelectRead))
                {
                    var clientSocket = tcpSocket.Accept();
                    Console.WriteLine($"Nova TCP konekcija od {clientSocket.RemoteEndPoint}");
                    HandleNewTcpConnection(clientSocket);
                }

                // Proveri postojeće TCP konekcije
                var socketsToRemove = new List<int>();
                lock (_lockObject)
                {
                    foreach (var kvp in _avionSockets.ToList())
                    {
                        try
                        {
                            if (kvp.Value.Poll(1000, SelectMode.SelectRead))
                            {
                                if (!HandleTcpClient(kvp.Key, kvp.Value))
                                {
                                    socketsToRemove.Add(kvp.Key);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Greška sa avionom {kvp.Key}: {ex.Message}");
                            socketsToRemove.Add(kvp.Key);
                        }
                    }

                    // Ukloni neispravne konekcije
                    foreach (var flightId in socketsToRemove)
                    {
                        RemoveFlight(flightId);
                    }
                }

                DisplayCurrentStatus();
                Thread.Sleep(100); // Kratka pauza
            }
        }

        private static void InitializeAirspace()
        {
            Console.Write("Unesite širinu vazdušnog prostora (broj sektora): ");
            var sirina = int.Parse(Console.ReadLine() ?? "10");
            
            Console.Write("Unesite visinu vazdušnog prostora (broj sektora): ");
            var visina = int.Parse(Console.ReadLine() ?? "10");
            
            _vazdusniProstor = new VazdusniProstor(sirina, visina);
            
            Console.WriteLine($"Vazdušni prostor kreiran: {sirina}x{visina} sa {_vazdusniProstor.NivoiVisine} nivoa visine");
            Console.WriteLine($"Ukupno sektora: {sirina * visina * _vazdusniProstor.NivoiVisine}");
        }

        private static void HandleUdpClient(Socket udpSocket)
        {
            var buffer = new byte[4096];
            EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

            var received = udpSocket.ReceiveFrom(buffer, ref remoteEp);
            if (received <= 0) return;

            try
            {
                var zahtev = DeserializeObject<ZahtevZaUlazak>(buffer);
                Console.WriteLine($"\nPrimljen UDP zahtev od {remoteEp}: {zahtev.Let?.Letelica?.RegistracijaAviona}");
                
                lock (_lockObject)
                {
                    _zahteviZaUlazak.Add(zahtev);
                }

                var odgovor = ProcessFlightRequest(zahtev);
                var response = SerializeObject(odgovor);
                udpSocket.SendTo(response, remoteEp);
                
                Console.WriteLine($"Poslat odgovor: {odgovor}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greška pri obradi UDP zahteva: {ex.Message}");
                
                // Pošalji server info ako je osnovni zahtev
                var serverInfo = new ServerInfo
                {
                    IpAddress = "127.0.0.1",
                    Port = TcpPort
                };
                var response = SerializeObject(serverInfo);
                udpSocket.SendTo(response, remoteEp);
            }
        }

        private static void HandleNewTcpConnection(Socket clientSocket)
        {
            // Čekaj da klijent pošalje svoj flight ID
            try
            {
                var buffer = new byte[4096];
                var received = clientSocket.Receive(buffer);
                
                if (received > 0)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, received);
                    if (int.TryParse(message, out var flightId))
                    {
                        lock (_lockObject)
                        {
                            _avionSockets[flightId] = clientSocket;
                        }
                        Console.WriteLine($"Avion {flightId} povezan preko TCP-a");
                        
                        // Pošalji potvrdu
                        var response = Encoding.UTF8.GetBytes("TCP_CONNECTED");
                        clientSocket.Send(response);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greška pri povezivanju TCP klijenta: {ex.Message}");
                clientSocket.Close();
            }
        }

        private static bool HandleTcpClient(int flightId, Socket clientSocket)
        {
            try
            {
                var buffer = new byte[4096];
                var received = clientSocket.Receive(buffer);

                if (received == 0)
                {
                    return false; // Konekcija zatvorena
                }

                var message = Encoding.UTF8.GetString(buffer, 0, received);
                
                if (message.StartsWith("POSITION_UPDATE:"))
                {
                    HandlePositionUpdate(flightId, message, clientSocket);
                }
                else if (message.StartsWith("SECTOR_ENTERED:"))
                {
                    HandleSectorEntered(flightId, message);
                }
                else if (message.StartsWith("FLIGHT_COMPLETED"))
                {
                    HandleFlightCompleted(flightId);
                    return false; // Ukloni let
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greška pri obradi TCP poruke od aviona {flightId}: {ex.Message}");
                return false;
            }
        }

        private static OdgovorServera ProcessFlightRequest(ZahtevZaUlazak zahtev)
        {
            var let = zahtev.Let;
            
            // Proveri validnost koordinata
            if (!_vazdusniProstor.IsSektorValid(let.PocetnaX, let.PocetnaY, let.PocetnaZ) ||
                !_vazdusniProstor.IsSektorValid(let.KrajnjaX, let.KrajnjaY, let.KrajnjaZ))
            {
                return new OdgovorServera
                {
                    Odobren = false,
                    Poruka = "Nevalidne koordinate početka ili kraja leta"
                };
            }

            // Proveri početni sektor
            var pocetniSektor = _vazdusniProstor.GetSektor(let.PocetnaX, let.PocetnaY, let.PocetnaZ);
            
            if (pocetniSektor.Zauzet)
            {
                // Pokušaj da nađeš alternativnu visinu
                var alternativa = FindAlternativeAltitude(let.PocetnaX, let.PocetnaY, let.PocetnaZ);
                if (alternativa != null)
                {
                    return new OdgovorServera
                    {
                        Odobren = false,
                        Poruka = "Početni sektor zauzet",
                        NovaX = let.PocetnaX,
                        NovaY = let.PocetnaY,
                        NovaZ = alternativa.Z
                    };
                }
                else
                {
                    return new OdgovorServera
                    {
                        Odobren = false,
                        Poruka = "Početni sektor zauzet, nema slobodnih alternativa"
                    };
                }
            }

            if (pocetniSektor.AbnormalniVremenskiUslovi)
            {
                return new OdgovorServera
                {
                    Odobren = false,
                    Poruka = "Abnormalni vremenski uslovi u početnom sektoru"
                };
            }

            // Odobri let
            var flightId = _nextFlightId++;
            let.TrenutnaX = let.PocetnaX;
            let.TrenutnaY = let.PocetnaY;
            let.TrenutnaZ = let.PocetnaZ;
            
            lock (_lockObject)
            {
                _aktivniLetovi[flightId] = let;
                pocetniSektor.Zauzet = true;
                pocetniSektor.ZauzetOdAviona = let.Letelica.RegistracijaAviona;
                pocetniSektor.ZauzetOdAvionaId = flightId;
            }

            zahtev.StatusZahteva = "Odobren";

            return new OdgovorServera
            {
                Odobren = true,
                Poruka = "Let odobren",
                RequestId = flightId
            };
        }

        private static Sektor FindAlternativeAltitude(int x, int y, int currentZ)
        {
            for (int z = 1; z <= _vazdusniProstor.NivoiVisine; z++)
            {
                if (z == currentZ) continue;
                
                var sektor = _vazdusniProstor.GetSektor(x, y, z);
                if (sektor != null && !sektor.Zauzet && !sektor.AbnormalniVremenskiUslovi)
                {
                    return sektor;
                }
            }
            return null;
        }

        private static void HandlePositionUpdate(int flightId, string message, Socket clientSocket)
        {
            // Format: "POSITION_UPDATE:x,y,z"
            var parts = message.Split(':')[1].Split(',');
            if (parts.Length == 3 && 
                int.TryParse(parts[0], out var x) && 
                int.TryParse(parts[1], out var y) && 
                int.TryParse(parts[2], out var z))
            {
                lock (_lockObject)
                {
                    if (_aktivniLetovi.TryGetValue(flightId, out var let))
                    {
                        // Oslobodi stari sektor
                        var stariSektor = _vazdusniProstor.GetSektor(let.TrenutnaX, let.TrenutnaY, let.TrenutnaZ);
                        if (stariSektor != null)
                        {
                            stariSektor.Zauzet = false;
                            stariSektor.ZauzetOdAviona = "";
                            stariSektor.ZauzetOdAvionaId = 0;
                        }

                        // Proveri novi sektor
                        var noviSektor = _vazdusniProstor.GetSektor(x, y, z);
                        if (noviSektor != null)
                        {
                            if (noviSektor.AbnormalniVremenskiUslovi)
                            {
                                // Abnormalni uslovi - preusmeravanje
                                var alternativa = ResolveConflict(let, x, y, z);
                                var instrukcija = $"REDIRECT:{alternativa.X},{alternativa.Y},{alternativa.Z}";
                                clientSocket.Send(Encoding.UTF8.GetBytes(instrukcija));
                                let.BrojIzmenaputanje++;
                                return;
                            }

                            if (noviSektor.Zauzet)
                            {
                                // Kolizija - poredi broj putnika
                                var occupyingFlightId = noviSektor.ZauzetOdAvionaId;
                                if (_aktivniLetovi.TryGetValue(occupyingFlightId, out var occupyingLet))
                                {
                                    if (let.Letelica.TrenutanBrojPutnika >= occupyingLet.Letelica.TrenutanBrojPutnika)
                                    {
                                        // Zahtevajući avion ima više putnika - preusmeriti postojećeg
                                        var alternativa = ResolveConflict(occupyingLet, occupyingLet.TrenutnaX, occupyingLet.TrenutnaY, occupyingLet.TrenutnaZ);

                                        // Premesti postojećeg na alternativu
                                        noviSektor.Zauzet = false;
                                        noviSektor.ZauzetOdAviona = "";
                                        noviSektor.ZauzetOdAvionaId = 0;

                                        var altSektor = _vazdusniProstor.GetSektor(alternativa.X, alternativa.Y, alternativa.Z);
                                        if (altSektor != null)
                                        {
                                            altSektor.Zauzet = true;
                                            altSektor.ZauzetOdAviona = occupyingLet.Letelica.RegistracijaAviona;
                                            altSektor.ZauzetOdAvionaId = occupyingFlightId;
                                        }
                                        occupyingLet.TrenutnaX = alternativa.X;
                                        occupyingLet.TrenutnaY = alternativa.Y;
                                        occupyingLet.TrenutnaZ = alternativa.Z;
                                        occupyingLet.BrojIzmenaputanje++;

                                        // Obavesti postojećeg o preusmeravanju
                                        if (_avionSockets.TryGetValue(occupyingFlightId, out var occupyingSocket))
                                        {
                                            try
                                            {
                                                var redirectMsg = $"REDIRECT:{alternativa.X},{alternativa.Y},{alternativa.Z}";
                                                occupyingSocket.Send(Encoding.UTF8.GetBytes(redirectMsg));
                                            }
                                            catch { }
                                        }

                                        // Daj sektor zahtevajućem avionu
                                        noviSektor.Zauzet = true;
                                        noviSektor.ZauzetOdAviona = let.Letelica.RegistracijaAviona;
                                        noviSektor.ZauzetOdAvionaId = flightId;
                                        let.TrenutnaX = x;
                                        let.TrenutnaY = y;
                                        let.TrenutnaZ = z;
                                    }
                                    else
                                    {
                                        // Zahtevajući avion ima manje putnika - preusmeriti njega
                                        var alternativa = ResolveConflict(let, x, y, z);
                                        var instrukcija = $"REDIRECT:{alternativa.X},{alternativa.Y},{alternativa.Z}";
                                        clientSocket.Send(Encoding.UTF8.GetBytes(instrukcija));
                                        let.BrojIzmenaputanje++;
                                        return;
                                    }
                                }
                                else
                                {
                                    // Nije pronađen postojeći let - preusmeravanje
                                    var alternativa = ResolveConflict(let, x, y, z);
                                    var instrukcija = $"REDIRECT:{alternativa.X},{alternativa.Y},{alternativa.Z}";
                                    clientSocket.Send(Encoding.UTF8.GetBytes(instrukcija));
                                    let.BrojIzmenaputanje++;
                                    return;
                                }
                            }
                            else
                            {
                                // Zauzmi novi sektor
                                noviSektor.Zauzet = true;
                                noviSektor.ZauzetOdAviona = let.Letelica.RegistracijaAviona;
                                noviSektor.ZauzetOdAvionaId = flightId;

                                let.TrenutnaX = x;
                                let.TrenutnaY = y;
                                let.TrenutnaZ = z;
                            }
                        }
                    }
                }

                // Pošalji potvrdu
                clientSocket.Send(Encoding.UTF8.GetBytes("POSITION_OK"));
            }
        }

        private static Sektor ResolveConflict(Let let, int targetX, int targetY, int targetZ)
        {
            // Prvo pokušaj alternativnu visinu
            var alternativaVisina = FindAlternativeAltitude(targetX, targetY, targetZ);
            if (alternativaVisina != null)
            {
                return alternativaVisina;
            }

            // Zatim pokušaj susedne sektore
            var susedni = _vazdusniProstor.GetSusedniSektori(targetX, targetY, targetZ);
            foreach (var sektor in susedni)
            {
                if (!sektor.Zauzet && !sektor.AbnormalniVremenskiUslovi)
                {
                    return sektor;
                }
            }

            // Ako nema alternativa, vrati trenutnu poziciju
            return _vazdusniProstor.GetSektor(let.TrenutnaX, let.TrenutnaY, let.TrenutnaZ);
        }

        private static void HandleSectorEntered(int flightId, string message)
        {
            lock (_lockObject)
            {
                if (_aktivniLetovi.TryGetValue(flightId, out var let))
                {
                    let.BrojProdjeniSektora++;
                }
            }
        }

        private static void HandleFlightCompleted(int flightId)
        {
            lock (_lockObject)
            {
                if (_aktivniLetovi.TryGetValue(flightId, out var let))
                {
                    // Oslobodi poslednji sektor
                    var sektor = _vazdusniProstor.GetSektor(let.TrenutnaX, let.TrenutnaY, let.TrenutnaZ);
                    if (sektor != null)
                    {
                        sektor.Zauzet = false;
                        sektor.ZauzetOdAviona = "";
                        sektor.ZauzetOdAvionaId = 0;
                    }

                    _aktivniLetovi.Remove(flightId);
                    Console.WriteLine($"Let {let.Letelica.RegistracijaAviona} završen uspešno");
                }
            }
        }

        private static void RemoveFlight(int flightId)
        {
            lock (_lockObject)
            {
                if (_aktivniLetovi.TryGetValue(flightId, out var let))
                {
                    // Oslobodi sektor
                    var sektor = _vazdusniProstor.GetSektor(let.TrenutnaX, let.TrenutnaY, let.TrenutnaZ);
                    if (sektor != null)
                    {
                        sektor.Zauzet = false;
                        sektor.ZauzetOdAviona = "";
                        sektor.ZauzetOdAvionaId = 0;
                    }

                    _aktivniLetovi.Remove(flightId);
                }

                if (_avionSockets.TryGetValue(flightId, out var socket))
                {
                    socket.Close();
                    _avionSockets.Remove(flightId);
                }
            }
        }

        private static void UpdateFlightsLoop()
        {
            while (true)
            {
                try
                {
                    Thread.Sleep(2000); // Ažuriraj svakih 2 sekunde
                    
                    lock (_lockObject)
                    {
                        foreach (var kvp in _aktivniLetovi.ToList())
                        {
                            var flightId = kvp.Key;
                            var let = kvp.Value;
                            
                            // Proveri da li je avion stigao do odredišta
                            if (let.TrenutnaX == let.KrajnjaX && 
                                let.TrenutnaY == let.KrajnjaY && 
                                let.TrenutnaZ == let.KrajnjaZ)
                            {
                                // Obavesti avion da je stigao
                                if (_avionSockets.TryGetValue(flightId, out var socket))
                                {
                                    try
                                    {
                                        socket.Send(Encoding.UTF8.GetBytes("DESTINATION_REACHED"));
                                    }
                                    catch
                                    {
                                        // Ignoriši greške
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Greška u update loop-u: {ex.Message}");
                }
            }
        }

        private static void DisplayCurrentStatus()
        {
            // Ova metoda se poziva često, pa ćemo ograničiti ispis
            if (DateTime.Now.Second % 5 == 0) // Svakih 5 sekundi
            {
                Console.Clear();
                Console.WriteLine("=== SISTEM KONTROLE LETA - STATUS ===");
                Console.WriteLine($"Vreme: {DateTime.Now:HH:mm:ss}");
                Console.WriteLine();

                lock (_lockObject)
                {
                    Console.WriteLine($"Aktivni letovi ({_aktivniLetovi.Count}):");
                    foreach (var kvp in _aktivniLetovi)
                    {
                        var let = kvp.Value;
                        Console.WriteLine($"  {kvp.Key}: {let.Letelica.RegistracijaAviona} - " +
                                        $"({let.TrenutnaX},{let.TrenutnaY},{let.TrenutnaZ}) -> " +
                                        $"({let.KrajnjaX},{let.KrajnjaY},{let.KrajnjaZ}) " +
                                        $"[Preostalo: {let.BrojPreostalihSektora()}]");
                    }

                    Console.WriteLine();
                    Console.WriteLine($"Zahtevi za ulazak ({_zahteviZaUlazak.Count}):");
                    var recentRequests = _zahteviZaUlazak.Skip(Math.Max(0, _zahteviZaUlazak.Count - 5)).Take(5);
                    foreach (var zahtev in recentRequests)
                    {
                        Console.WriteLine($"  {zahtev.Let.Letelica.RegistracijaAviona} - {zahtev.StatusZahteva}");
                    }
                }

                Console.WriteLine();
                Console.WriteLine(_vazdusniProstor.GetMapVisualization(1));
                
                if (_vazdusniProstor.NivoiVisine > 1)
                {
                    Console.WriteLine(_vazdusniProstor.GetMapVisualization(2));
                }
                
                if (_vazdusniProstor.NivoiVisine > 2)
                {
                    Console.WriteLine(_vazdusniProstor.GetMapVisualization(3));
                }

                Console.WriteLine("Legenda: [] = Slobodan sektor, A1/A2... = Avion, XX = Abnormalni vremenski uslovi");
            }
        }

        private static byte[] SerializeObject(object obj)
        {
            using (var ms = new MemoryStream())
            {
                var bf = new BinaryFormatter();
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        private static T DeserializeObject<T>(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                var bf = new BinaryFormatter();
                return (T)bf.Deserialize(ms);
            }
        }
    }
}
