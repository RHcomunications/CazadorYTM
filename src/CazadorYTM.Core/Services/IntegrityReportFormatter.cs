namespace CazadorYTM.Core.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using CazadorYTM.Core.Models;

public static class IntegrityReportFormatter
{
    public static string Build(List<ResultadoArchivo> resultados, string carpeta)
    {
        var lineas = new List<string>();
        var fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var (okCnt, warnCnt, errCnt) = Helpers.SummarizeResults(resultados);

        lineas.Add("========================================================================");
        lineas.Add($"  REPORTE DE INTEGRIDAD DE AUDIO — CazadorYTM v{Constants.AppVersion}");
        lineas.Add($"  Fecha: {fecha}");
        lineas.Add($"  Carpeta analizada: {carpeta}");
        lineas.Add("========================================================================");
        lineas.Add("");
        lineas.Add("RESUMEN GENERAL:");
        lineas.Add($"  • Total de archivos analizados: {resultados.Count}");
        lineas.Add($"  • Archivos correctos (OK):     {okCnt}");
        lineas.Add($"  • Archivos con advertencias:   {warnCnt}");
        lineas.Add($"  • Archivos con errores/fallos:  {errCnt}");
        lineas.Add("");
        lineas.Add("------------------------------------------------------------------------");
        lineas.Add("DETALLE POR ARCHIVO:");
        lineas.Add("------------------------------------------------------------------------");

        foreach (var r in resultados)
        {
            lineas.Add($"Archivo: {r.Nombre}");
            lineas.Add($"Estado:  {r.Estado}");
            if (r.Info.Count > 0)
            {
                var infoStr = string.Join(" | ", r.Info.Select(kv => $"{kv.Key}: {kv.Value}"));
                lineas.Add($"Info:    {infoStr}");
            }
            if (r.Errores.Count > 0)
            {
                foreach (var err in r.Errores)
                    lineas.Add($"  [Error] {err}");
            }
            if (r.Advertencias.Count > 0)
            {
                foreach (var adv in r.Advertencias)
                    lineas.Add($"  [Aviso] {adv}");
            }
            lineas.Add("");
        }

        return string.Join("\n", lineas);
    }
}
