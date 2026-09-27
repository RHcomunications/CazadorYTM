namespace CazadorYTM.Core.Models;

using System.Collections.Generic;

public class ResultadoArchivo
{
    public string Ruta { get; set; } = string.Empty;
    public string Nombre => System.IO.Path.GetFileName(Ruta);
    public List<string> Errores { get; set; } = new();
    public List<string> Advertencias { get; set; } = new();
    public Dictionary<string, string> Info { get; set; } = new();

    public ResultadoArchivo() { }

    public ResultadoArchivo(string ruta)
    {
        Ruta = ruta;
    }

    public bool Ok => Errores.Count == 0;
    public string Estado => Errores.Count > 0 ? "❌ ERROR" : Advertencias.Count > 0 ? "⚠️ AVISO" : "✅ OK";

    public override string ToString()
    {
        return $"{Estado} - {Nombre}";
    }
}