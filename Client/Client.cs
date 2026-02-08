using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using Domain;

namespace Client
{
    public class Client
    {
        private const int UdpPort = 15001;
        private static Socket _tcpSocket;
        private static int _flightId;
        private static Let _currentFlight;
        private static readonly object _lockObject = new object();
        private static bool _flightActive = false;

        private static void Main(string[] args)
        {
            Console.WriteLine("=== AVION - KLIJENT ===");
            Console.WriteLine("Pokretanje aviona...");

            try
            {
                while (true)
                {
                    Console.WriteLine("\nOpcije:");
                    Console.WriteLine("1. Zahtev za ulazak u vazdušni prostor");
                    Console.WriteLine("2. Izlaz");
                    Console.Write("Izaberite opciju: ");

                    var choice = Console.ReadLine();

                    switch (choice)
                    {
                        case "1":
                            RequestFlightEntry();
                            break;
                        case "2":
                            return;
                        default:
                            Console.WriteLine("Nevalidna opcija.");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greška: {ex.Message}");
            }
            finally
            {
                if (_tcpSocket != null && _tcpSocket.Connected)
                {
                    _tcpSocket.Close();
                }
            }
        }

        private static void RequestFlightEntry()
        {
            try
            {
                Console.WriteLine("\n=== KREIRANJE ZAHTEVA ZA LET ===");
                
                // Kreiranje letelice
                var letelica = new Letelica();
                Console.Write("Naziv aviokompanije: ");
                letelica.NazivAviokompanije = Console.ReadLine();
                
                Console.Write("Naziv glavnog pilota: ");
                letelica.NazivGlavnogPilota = Console.ReadLine();
                
                Console.Write("Registracija aviona: ");
                letelica.RegistracijaAviona = Console.ReadLine();
                
                Console.Write("Maksimalan broj putnika: ");
                letelica.MaksimalanBrojPutnika = int.Parse(Console.ReadLine() ?? "0");
                
                Console.Write("Trenutni broj putnika: ");
                letelica.TrenutanBrojPutnika = int.Parse(Console.ReadLine() ?? "0");

                // Kreiranje leta
                var let = new Let { Letelica = letelica };
                
                Console.Write("Početna X koordinata: ");
                let.PocetnaX = int.Parse(Console.ReadLine() ?? "0");
                
                Console.Write("Početna Y koordinata: ");
                let.PocetnaY = int.Parse(Console.ReadLine() ?? "0");
                
                Console.Write("Početna Z koordinata (1-3): ");
                let.PocetnaZ = int.Parse(Console.ReadLine() ?? "1");
                
                Console.Write("Krajnja X koordinata: ");
                let.KrajnjaX = int.Parse(Console.ReadLine() ?? "0");
                
                Console.Write("Krajnja Y koordinata: ");
                let.KrajnjaY = int.Parse(Console.ReadLine() ?? "0");
                
                Console.Write("Krajnja Z koordinata (1-3): ");
                let.KrajnjaZ = int.Parse(Console.ReadLine() ?? "1");

                // Postavi trenutnu poziciju na početnu
                let.TrenutnaX = let.PocetnaX;
                let.TrenutnaY = let.PocetnaY;
                let.TrenutnaZ = let.PocetnaZ;

                // Kreiraj zahtev
                var zahtev = new ZahtevZaUlazak { Let = let };

                // Pošalji UDP zahtev
                var odgovor = SendUdpRequest(zahtev);
                
                if (odgovor != null)
                {
                    Console.WriteLine($"\nOdgovor servera: {odgovor}");
                    
                    if (odgovor.Odobren)
                    {
                        _flightId = odgovor.RequestId;
                        _currentFlight = let;
                        Console.WriteLine($"Let odobren! ID leta: {_flightId}");
                        
                        // Uspostavi TCP konekciju i počni let
                        if (EstablishTcpConnection())
                        {
                            StartFlight();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Let odbačen: {odgovor.Poruka}");
                        if (odgovor.NovaX != -1 && odgovor.NovaY != -1 && odgovor.NovaZ != -1)
                        {
                            Console.WriteLine($"Predlog korekcije: ({odgovor.NovaX},{odgovor.NovaY},{odgovor.NovaZ})");
                            Console.Write("Da li želite da prihvatite korekciju? (da/ne): ");
                            var response = Console.ReadLine()?.ToLower();
                            
                            if (response == "da" || response == "d")
                            {
                                let.PocetnaX = odgovor.NovaX;
                                let.PocetnaY = odgovor.NovaY;
                                let.PocetnaZ = odgovor.NovaZ;
                                let.TrenutnaX = let.PocetnaX;
                                let.TrenutnaY = let.PocetnaY;
                                let.TrenutnaZ = let.PocetnaZ;
                                
                                // Pošalji novi zahtev
                                zahtev.Let = let;
                                var noviOdgovor = SendUdpRequest(zahtev);
                                
                                if (noviOdgovor != null && noviOdgovor.Odobren)
                                {
                                    _flightId = noviOdgovor.RequestId;
                                    _currentFlight = let;
                                    Console.WriteLine($"Let odobren sa korekcijom! ID leta: {_flightId}");
                                    
                                    if (EstablishTcpConnection())
                                    {
                                        StartFlight();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greška pri zahtevanju leta: {ex.Message}");
            }
        }

        private static OdgovorServera SendUdpRequest(ZahtevZaUlazak zahtev)
        {
            using (var udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                try
                {
                    var serverEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), UdpPort);
                    
                    var requestData = SerializeObject(zahtev);
                    udpSocket.SendTo(requestData, serverEndPoint);
                    
                    var buffer = new byte[4096];
                    EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    var received = udpSocket.ReceiveFrom(buffer, ref remoteEp);
                    
                    if (received > 0)
                    {
                        return DeserializeObject<OdgovorServera>(buffer);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UDP greška: {ex.Message}");
                }
                
                return null;
            }
        }

        private static bool EstablishTcpConnection()
        {
            try
            {
                // Prvo dobij server info
                var serverInfo = GetServerInfo();
                if (serverInfo == null)
                {
                    Console.WriteLine("Nije moguće dobiti informacije o serveru.");
                    return false;
                }

                _tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _tcpSocket.Connect(IPAddress.Parse(serverInfo.IpAddress), serverInfo.Port);
                
                // Pošalji flight ID
                var flightIdData = Encoding.UTF8.GetBytes(_flightId.ToString());
                _tcpSocket.Send(flightIdData);
                
                // Čekaj potvrdu
                var buffer = new byte[1024];
                var received = _tcpSocket.Receive(buffer);
                var response = Encoding.UTF8.GetString(buffer, 0, received);
                
                if (response == "TCP_CONNECTED")
                {
                    Console.WriteLine("TCP konekcija uspešno uspostavljena.");
                    return true;
                }
                else
                {
                    Console.WriteLine("Neočekivana TCP potvrda.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greška pri uspostavljanju TCP konekcije: {ex.Message}");
                return false;
            }
        }

        private static ServerInfo GetServerInfo()
        {
            using (var udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                try
                {
                    var serverEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), UdpPort);

                    var requestData = Encoding.UTF8.GetBytes("SERVER_INFO");
                    udpSocket.SendTo(requestData, serverEndPoint);
                  
                    var buffer = new byte[1024];
                    EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);
                    var received = udpSocket.ReceiveFrom(buffer, ref remoteEp);

                    if (received > 0)
                    {
                        return DeserializeObject<ServerInfo>(buffer);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Greška pri dobijanju server info: {ex.Message}");
                }

                return null;
            }
        }

        private static void StartFlight()
        {
            _flightActive = true;
            Console.WriteLine("\n=== LET POČINJE ===");
            Console.WriteLine($"Avion {_currentFlight.Letelica.RegistracijaAviona} kreće sa pozicije " +
                            $"({_currentFlight.TrenutnaX},{_currentFlight.TrenutnaY},{_currentFlight.TrenutnaZ}) " +
                            $"ka odredištu ({_currentFlight.KrajnjaX},{_currentFlight.KrajnjaY},{_currentFlight.KrajnjaZ})");

            // Pokreni thread za praćenje TCP poruka
            var tcpThread = new Thread(HandleTcpMessages) { IsBackground = true };
            tcpThread.Start();

            // Glavna petlja leta
            while (_flightActive)
            {
                try
                {
                    DisplayFlightStatus();
                    
                    if (HasReachedDestination())
                    {
                        CompleteFlight();
                        break;
                    }

                    // Izračunaj sledeći potez
                    var nextMove = CalculateNextMove();
                    
                    // Pošalji zahtev za ažuriranje pozicije
                    var message = $"POSITION_UPDATE:{nextMove.Item1},{nextMove.Item2},{nextMove.Item3}";
                    _tcpSocket.Send(Encoding.UTF8.GetBytes(message));

                    Thread.Sleep(3000); // Čekaj 3 sekunde između poteza
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Greška tokom leta: {ex.Message}");
                    break;
                }
            }
        }

        private static void HandleTcpMessages()
        {
            var buffer = new byte[4096];
            
            while (_flightActive)
            {
                try
                {
                    if (_tcpSocket.Poll(1000000, SelectMode.SelectRead)) // 1 sekunda timeout
                    {
                var received = _tcpSocket.Receive(buffer);
                        if (received > 0)
                        {
                            var message = Encoding.UTF8.GetString(buffer, 0, received);
                            
                            if (message == "POSITION_OK")
                            {
                                // Pozicija ažurirana uspešno
                                lock (_lockObject)
                                {
                                    _currentFlight.BrojProdjeniSektora++;
                                    var sectorMessage = $"SECTOR_ENTERED:{_currentFlight.TrenutnaX},{_currentFlight.TrenutnaY},{_currentFlight.TrenutnaZ}";
                                    _tcpSocket.Send(Encoding.UTF8.GetBytes(sectorMessage));
                                }
                            }
                            else if (message.StartsWith("REDIRECT:"))
                            {
                                // Server traži preusmeravanje
                                var parts = message.Split(':')[1].Split(',');
                                if (parts.Length == 3 && 
                                    int.TryParse(parts[0], out var newX) && 
                                    int.TryParse(parts[1], out var newY) && 
                                    int.TryParse(parts[2], out var newZ))
                                {
                                    lock (_lockObject)
                                    {
                                        _currentFlight.TrenutnaX = newX;
                                        _currentFlight.TrenutnaY = newY;
                                        _currentFlight.TrenutnaZ = newZ;
                                        _currentFlight.BrojIzmenaputanje++;
                                    }
                                    
                                    Console.WriteLine($"\n*** PREUSMERAVANJE: Nova pozicija ({newX},{newY},{newZ}) ***");
                                }
                            }
                            else if (message == "DESTINATION_REACHED")
                            {
                                Console.WriteLine("\n*** SERVER POTVRĐUJE: ODREDIŠTE DOSTIGNUTO ***");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Greška pri obradi TCP poruke: {ex.Message}");
                    break;
                }
            }
        }

        private static void DisplayFlightStatus()
        {
            lock (_lockObject)
            {
                Console.Clear();
                Console.WriteLine("=== STATUS LETA ===");
                Console.WriteLine($"Avion: {_currentFlight.Letelica.RegistracijaAviona}");
                Console.WriteLine($"Pilot: {_currentFlight.Letelica.NazivGlavnogPilota}");
                Console.WriteLine($"Avioompanija: {_currentFlight.Letelica.NazivAviokompanije}");
                Console.WriteLine($"Putnici: {_currentFlight.Letelica.TrenutanBrojPutnika}/{_currentFlight.Letelica.MaksimalanBrojPutnika}");
                Console.WriteLine();
                Console.WriteLine($"Trenutna pozicija: ({_currentFlight.TrenutnaX},{_currentFlight.TrenutnaY},{_currentFlight.TrenutnaZ})");
                Console.WriteLine($"Odredište: ({_currentFlight.KrajnjaX},{_currentFlight.KrajnjaY},{_currentFlight.KrajnjaZ})");
                Console.WriteLine($"Broj pređenih sektora: {_currentFlight.BrojProdjeniSektora}");
                Console.WriteLine($"Broj preostalih sektora: {_currentFlight.BrojPreostalihSektora()}");
                Console.WriteLine($"Broj izmena putanje: {_currentFlight.BrojIzmenaputanje}");
                Console.WriteLine($"Vreme početka: {_currentFlight.VremePocetka:HH:mm:ss}");
                Console.WriteLine($"Trajanje leta: {DateTime.Now - _currentFlight.VremePocetka:mm\\:ss}");
                Console.WriteLine();
            }
        }

        private static bool HasReachedDestination()
        {
            lock (_lockObject)
            {
                return _currentFlight.TrenutnaX == _currentFlight.KrajnjaX &&
                       _currentFlight.TrenutnaY == _currentFlight.KrajnjaY &&
                       _currentFlight.TrenutnaZ == _currentFlight.KrajnjaZ;
            }
        }

        private static (int, int, int) CalculateNextMove()
        {
            lock (_lockObject)
            {
                var currentX = _currentFlight.TrenutnaX;
                var currentY = _currentFlight.TrenutnaY;
                var currentZ = _currentFlight.TrenutnaZ;
                
                var targetX = _currentFlight.KrajnjaX;
                var targetY = _currentFlight.KrajnjaY;
                var targetZ = _currentFlight.KrajnjaZ;

                // Jednostavan algoritam: pomeri se za jedan korak u pravcu cilja
                var nextX = currentX;
                var nextY = currentY;
                var nextZ = currentZ;

                if (currentX < targetX) nextX++;
                else if (currentX > targetX) nextX--;
                else if (currentY < targetY) nextY++;
                else if (currentY > targetY) nextY--;
                else if (currentZ < targetZ) nextZ++;
                else if (currentZ > targetZ) nextZ--;

                // Ažuriraj trenutnu poziciju
                _currentFlight.TrenutnaX = nextX;
                _currentFlight.TrenutnaY = nextY;
                _currentFlight.TrenutnaZ = nextZ;

                return (nextX, nextY, nextZ);
            }
        }

        private static void CompleteFlight()
        {
            try
            {
                Console.WriteLine("\n=== LET ZAVRŠEN ===");
                Console.WriteLine($"Avion {_currentFlight.Letelica.RegistracijaAviona} je stigao do odredišta!");
                Console.WriteLine($"Ukupno pređenih sektora: {_currentFlight.BrojProdjeniSektora}");
                Console.WriteLine($"Ukupno izmena putanje: {_currentFlight.BrojIzmenaputanje}");
                Console.WriteLine($"Ukupno vreme leta: {DateTime.Now - _currentFlight.VremePocetka:mm\\:ss}");

                // Obavesti server da je let završen
                _tcpSocket.Send(Encoding.UTF8.GetBytes("FLIGHT_COMPLETED"));
                
                _flightActive = false;
                
                Console.WriteLine("\nPritisnite bilo koji taster za povratak na glavni meni...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Greška pri završavanju leta: {ex.Message}");
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
