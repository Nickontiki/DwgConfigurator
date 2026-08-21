using System.Diagnostics;
using Microsoft.Data.SqlClient;
using DwgConfigurator.Shared.Config;
using DwgConfigurator.Shared.Models;

namespace DwgConfigurator.Shared.Data;

/// <summary>
/// Accede al database SAP (SQL Server) per recuperare i dati delle commesse.
/// </summary>
public class CommessaRepository
{
    /// <summary>
    /// Crea una connection string compatibile con i vecchi comportamenti di System.Data.SqlClient.
    /// Microsoft.Data.SqlClient può forzare Encrypt=True in alcuni casi: qui lo rendiamo esplicito.
    /// </summary>
    private static string GetSapConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(AppSettings.SapConnectionString)
        {
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Cerca commesse il cui OrderCode inizia con il testo digitato.
    /// Restituisce max 20 risultati con OrderCode + POST1 per la tendina.
    /// </summary>
    public List<CommessaInfo> SearchCommesse(string partialOrderCode)
    {
        var results = new List<CommessaInfo>();
        if (string.IsNullOrWhiteSpace(partialOrderCode) || partialOrderCode.Length < 2)
            return results;

        try
        {
            using var conn = new SqlConnection(GetSapConnectionString());
            conn.Open();

            const string query = @"
                SELECT DISTINCT TOP 20
                    la.OrderCode,
                    (SELECT TOP 1 s.POST1 FROM dbo.SAP s WHERE s.PSPID = la.OrderCode) AS POST1
                FROM qy_SAP_Logistics_Address la
                WHERE la.OrderCode LIKE @Search + '%'
                ORDER BY la.OrderCode";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Search", partialOrderCode);
            cmd.CommandTimeout = 5;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new CommessaInfo
                {
                    OrderCode = reader["OrderCode"]?.ToString() ?? "",
                    Post1 = reader["POST1"]?.ToString() ?? ""
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[CommessaRepository.SearchCommesse] " + ex);
        }

        return results;
    }

    /// <summary>
    /// Recupera tutti i dati di una commessa specifica.
    /// </summary>
    public CommessaInfo? GetCommessa(string orderCode)
    {
        if (string.IsNullOrWhiteSpace(orderCode)) return null;

        try
        {
            using var conn = new SqlConnection(GetSapConnectionString());
            conn.Open();

            const string query = @"
                SELECT
                    cc.Company,
                    (SELECT TOP 1 s.POST1 FROM dbo.SAP s WHERE s.PSPID = la.OrderCode) AS POST1,
                    la.STREET,
                    la.ORT01,
                    la.PSTLZ,
                    la.REGIO,
                    la.LAND1
                FROM qy_SAP_Logistics_Address la
                LEFT JOIN qy_Company_Countries cc
                    ON cc.Code COLLATE SQL_Latin1_General_CP1_CI_AS = la.LAND1
                WHERE la.OrderCode = @OrderCode";

            using var cmd = new SqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@OrderCode", orderCode);
            cmd.CommandTimeout = 5;

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new CommessaInfo
                {
                    OrderCode = orderCode,
                    Company = reader["Company"]?.ToString() ?? "",
                    Post1 = reader["POST1"]?.ToString() ?? "",
                    Street = reader["STREET"]?.ToString() ?? "",
                    Ort01 = reader["ORT01"]?.ToString() ?? "",
                    Pstlz = reader["PSTLZ"]?.ToString() ?? "",
                    Regio = reader["REGIO"]?.ToString() ?? "",
                    Land1 = reader["LAND1"]?.ToString() ?? ""
                };
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[CommessaRepository.GetCommessa] " + ex);
        }

        return null;
    }
}
