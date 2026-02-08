using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Domain
{
    [Serializable]
    public class VazdusniProstor
    {
        public int Sirina { get; set; }
        public int Visina { get; set; }
        public int NivoiVisine { get; set; } = 3; // 1-3 nivoa visine
        public Dictionary<string, Sektor> Sektori { get; set; } = new Dictionary<string, Sektor>();
        
        public VazdusniProstor(int sirina, int visina)
        {
            Sirina = sirina;
            Visina = visina;
            InitializeSektori();
        }
        
        private void InitializeSektori()
        {
            var random = new Random();
            
            for (int x = 0; x < Sirina; x++)
            {
                for (int y = 0; y < Visina; y++)
                {
                    for (int z = 1; z <= NivoiVisine; z++)
                    {
                        var sektor = new Sektor
                        {
                            X = x,
                            Y = y,
                            Z = z,
                            AbnormalniVremenskiUslovi = random.Next(0, 10) == 0 // 10% šanse za abnormalne uslove
                        };
                        
                        Sektori[$"{x},{y},{z}"] = sektor;
                    }
                }
            }
        }
        
        public Sektor GetSektor(int x, int y, int z)
        {
            var key = $"{x},{y},{z}";
            return Sektori.ContainsKey(key) ? Sektori[key] : null;
        }
        
        public bool IsSektorValid(int x, int y, int z)
        {
            return x >= 0 && x < Sirina && y >= 0 && y < Visina && z >= 1 && z <= NivoiVisine;
        }
        
        public List<Sektor> GetSusedniSektori(int x, int y, int z)
        {
            var susedni = new List<Sektor>();
            
            // 3x3 okolina u horizontalnoj ravni
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue; // preskačemo trenutni sektor
                    
                    int newX = x + dx;
                    int newY = y + dy;
                    
                    if (IsSektorValid(newX, newY, z))
                    {
                        susedni.Add(GetSektor(newX, newY, z));
                    }
                }
            }
            
            return susedni;
        }
        
        public string GetMapVisualization(int nivo = 1)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Mapa vazdušnog prostora - Nivo {nivo}:");
            sb.AppendLine(new string('-', Sirina * 3 + 1));

            for (int y = Visina - 1; y >= 0; y--)
            {
                sb.Append("|");
                for (int x = 0; x < Sirina; x++)
                {
                    var sektor = GetSektor(x, y, nivo);
                    var display = sektor?.GetDisplayChar() ?? "??";
                    sb.Append(display.PadRight(3));
                }
                sb.AppendLine();
            }
            sb.AppendLine(new string('-', Sirina * 3 + 1));

            return sb.ToString();
        }
    }
}
